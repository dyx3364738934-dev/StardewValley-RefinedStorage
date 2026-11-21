using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RefinedStorage.Ime;
using StardewValley;
using StardewValley.Menus;

namespace RefinedStorage.UI
{
    public sealed class InGamePinyinBox : IKeyboardSubscriber
    {
        private const int PageSize = 5;
        private const int MaxComposingLength = 32;
        private const int MaxTextLength = 64;
        private const double BackspaceInitialDelayMs = 400.0;
        private const double BackspaceRepeatIntervalMs = 45.0;

        private readonly Texture2D _textBoxTexture;
        private readonly SpriteFont _font;
        private readonly Color _textColor;
        private readonly List<CandidateHitBox> _candidateHitBoxes = new List<CandidateHitBox>();

        private string _text = string.Empty;
        private string _composing = string.Empty;
        private PinyinQueryResult? _queryResult;
        private ISet<string>? _lastPriorityWords;
        private int _page;
        private bool _selected;

        private KeyboardState _prevKb;
        private bool _shiftAloneCandidate;
        private double _backHeldMs;
        private double _backNextRepeatMs = BackspaceInitialDelayMs;
        private double _caretBlinkMs;

        private Rectangle _candidateBarBounds;
        private Rectangle _prevPageBounds;
        private Rectangle _nextPageBounds;
        private Vector2 _pageTextPos;
        private string _pageText = "1/1";
        private int _totalPages = 1;
        private int _lastLayoutX;
        private int _lastLayoutY;
        private int _lastLayoutWidth;
        private int _lastLayoutHeight;

        private readonly struct CandidateHitBox
        {
            public Rectangle Bounds { get; }
            public PinyinCandidate Candidate { get; }
            public string IndexPrefix { get; }
            public string WordText { get; }
            public int IndexInPage { get; }

            public CandidateHitBox(Rectangle bounds, PinyinCandidate candidate, string indexPrefix, string wordText, int indexInPage)
            {
                // 独立逻辑，无上文
                // 作用：缓存单条候选词的屏幕命中矩形与分段绘制文本
                Bounds = bounds;
                Candidate = candidate;
                IndexPrefix = indexPrefix;
                WordText = wordText;
                IndexInPage = indexInPage;
            }
        }

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool ChineseMode { get; set; } = true;
        public bool ConsumedEscapeThisTick { get; private set; }

        public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

        public Rectangle ModeButtonBounds => new Rectangle(X + Width + 6, Y + Math.Max(0, (Height - 40) / 2), 44, 40);

        public Rectangle CandidateBarBounds
        {
            get
            {
                // 与上文：候选浮窗仅在聚焦且正在拼写时激活
                // 作用：返回锚定在搜索框正下方的当前候选浮窗矩形，坐标变更时先同步排版
                if (!_selected || _composing.Length == 0)
                {
                    return Rectangle.Empty;
                }

                EnsurePopupLayoutCurrent();
                return _candidateBarBounds;
            }
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                // 与上文：由鼠标点击、回车失焦、菜单退出或 KeyboardDispatcher 回调触发焦点切换
                // 作用：管理键盘订阅者注册、系统输入法抑制/无条件恢复以及首帧按键快照防误吃
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                if (_selected)
                {
                    KeyboardDispatcher? dispatcher = GetDispatcherSafe();
                    if (dispatcher != null)
                    {
                        dispatcher.Subscriber = this;
                    }

                    NativeImeSuppressor.Suppress();

                    _prevKb = ReadKeyboardStateSafe();
                    _shiftAloneCandidate = false;
                    _backHeldMs = 0.0;
                    _backNextRepeatMs = BackspaceInitialDelayMs;
                    _caretBlinkMs = 0.0;
                    RebuildPopupLayout();
                }
                else
                {
                    ResetComposingState();
                    KeyboardDispatcher? dispatcher = GetDispatcherSafe();
                    if (dispatcher != null && ReferenceEquals(dispatcher.Subscriber, this))
                    {
                        dispatcher.Subscriber = null;
                    }

                    NativeImeSuppressor.Restore();
                }
            }
        }

        public string Text
        {
            get => _text;
            set => _text = value ?? string.Empty;
        }

        public string ComposingText => _composing;

        public bool IsComposing => _composing.Length > 0;

        public string EffectiveSearchQuery
        {
            get
            {
                // 独立逻辑，无上文
                // 作用：将已上屏词与未上屏拼音实时拼成空格分词查询串，驱动箱格即时过滤
                if (_composing.Length == 0)
                {
                    return _text;
                }

                return string.IsNullOrEmpty(_text) ? _composing : $"{_text} {_composing}";
            }
        }

        public InGamePinyinBox(Texture2D textBoxTexture, SpriteFont font, Color textColor)
        {
            // 独立逻辑，无上文
            // 作用：初始化原版文本框贴图、字体、颜色与默认尺寸
            _textBoxTexture = textBoxTexture;
            _font = font;
            _textColor = textColor;
            Width = textBoxTexture != null ? textBoxTexture.Width : 240;
            Height = textBoxTexture != null ? textBoxTexture.Height : 48;
        }

        public void Clear(bool keepFocus)
        {
            // 独立逻辑，无上文
            // 作用：清空已上屏文本与拼音拼写状态，并按参数保留或释放输入焦点
            _text = string.Empty;
            ResetComposingState();
            Selected = keepFocus;
        }

        public void Update(GameTime time, ISet<string>? priorityWords = null)
        {
            // 与上文：每帧在 RefinedChestMenu.update 中早于 receiveKeyPress 被调用
            // 作用：基于 KeyboardState 边沿检测处理单按 Shift 切中英、Back 连删、拼音/候选键与英文直输
            ConsumedEscapeThisTick = false;
            _lastPriorityWords = priorityWords;

            if (!_selected)
            {
                return;
            }

            double elapsedMs = Math.Clamp(time?.ElapsedGameTime.TotalMilliseconds ?? 0.0, 0.0, 100.0);
            _caretBlinkMs += elapsedMs;

            if (IsComposing && (_queryResult == null || !string.Equals(_queryResult.RawInput, _composing, StringComparison.Ordinal)))
            {
                RebuildQuery(priorityWords);
            }

            KeyboardState cur = ReadKeyboardStateSafe();
            ProcessShiftToggleEdge(cur);

            bool ctrlDown = cur.IsKeyDown(Keys.LeftControl) || cur.IsKeyDown(Keys.RightControl);
            bool altDown = cur.IsKeyDown(Keys.LeftAlt) || cur.IsKeyDown(Keys.RightAlt);
            if (!ctrlDown && !altDown)
            {
                ProcessBackspaceWithRepeat(cur, elapsedMs, priorityWords);
                if (ChineseMode)
                {
                    ProcessChineseModeKeys(cur, priorityWords);
                }
                else
                {
                    ProcessEnglishModeKeys(cur);
                }
            }
            else
            {
                _backHeldMs = 0.0;
                _backNextRepeatMs = BackspaceInitialDelayMs;
            }

            _prevKb = cur;
        }

        public bool ReceiveLeftClick(int x, int y)
        {
            // 与上文：由菜单左键分发入口优先调用，命中栏内返回 true 阻止穿透到网格
            // 作用：依次处理候选浮窗选词/翻页、中英切换按钮与搜索框聚焦，点击外部时自动失焦
            if (_selected && IsComposing && CandidateBarBounds.Contains(x, y))
            {
                HandleCandidateBarLeftClick(x, y);
                return true;
            }

            if (ModeButtonBounds.Contains(x, y))
            {
                Selected = true;
                ToggleInputMode();
                return true;
            }

            if (Bounds.Contains(x, y))
            {
                Selected = true;
                _caretBlinkMs = 0.0;
                return true;
            }

            Selected = false;
            return false;
        }

        public bool ReceiveRightClick(int x, int y)
        {
            // 与上文：由菜单右键分发入口优先调用
            // 作用：拦截候选栏内右键防穿透，支持中英切换按钮与框内右键清空搜索词，外部点击失焦
            if (_selected && IsComposing && CandidateBarBounds.Contains(x, y))
            {
                return true;
            }

            if (ModeButtonBounds.Contains(x, y))
            {
                Selected = true;
                ToggleInputMode();
                return true;
            }

            if (Bounds.Contains(x, y))
            {
                Clear(keepFocus: true);
                PlaySoundSafe("smallSelect");
                return true;
            }

            Selected = false;
            return false;
        }

        public bool ReceiveScrollWheel(int direction, int mouseX, int mouseY)
        {
            // 与上文：鼠标位于打开的候选浮窗内滚动滚轮
            // 作用：在候选浮窗内拦截滚轮实现候选词翻页，避免带动背后箱子网格滚动
            if (!_selected || !IsComposing || !CandidateBarBounds.Contains(mouseX, mouseY))
            {
                return false;
            }

            if (direction > 0 && _page > 0)
            {
                _page--;
                RebuildPopupLayout();
                PlaySoundSafe("shwip");
            }
            else if (direction < 0 && _page < _totalPages - 1)
            {
                _page++;
                RebuildPopupLayout();
                PlaySoundSafe("shwip");
            }

            return true;
        }

        public void DrawBox(SpriteBatch b)
        {
            // 与上文：在菜单顶部栏阶段绘制，先于 ItemsToGrabMenu 网格
            // 作用：三宫格拉伸绘制原版搜索木框、占位提示、已上屏词、带高亮下划线拼音音节、光标与中英切换按钮
            if (_textBoxTexture != null)
            {
                int texH = _textBoxTexture.Height;
                int texW = _textBoxTexture.Width;
                b.Draw(_textBoxTexture, new Rectangle(X, Y, 16, Height), new Rectangle(0, 0, 16, texH), Color.White);
                b.Draw(_textBoxTexture, new Rectangle(X + 16, Y, Math.Max(0, Width - 32), Height), new Rectangle(16, 0, 4, texH), Color.White);
                b.Draw(_textBoxTexture, new Rectangle(X + Width - 16, Y, 16, Height), new Rectangle(texW - 16, 0, 16, texH), Color.White);
            }
            else
            {
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), X, Y, Width, Height, Color.White, 1f, drawShadow: false);
            }

            float lineHeight = Math.Max(22f, MeasureTextSafe("木").Y);
            float textY = Y + (Height - lineHeight) / 2f + 1f;
            int innerLeft = X + 14;
            int innerMaxWidth = Math.Max(40, Width - 28);

            if (string.IsNullOrEmpty(_text) && !IsComposing && !_selected)
            {
                const string placeholder = "搜索";
                Vector2 phSize = MeasureTextSafe(placeholder);
                float phScale = phSize.X > innerMaxWidth ? innerMaxWidth / phSize.X : 1f;
                float phY = Y + (Height - phSize.Y * phScale) / 2f + 1f;
                if (_font != null)
                {
                    Utility.drawTextWithShadow(b, placeholder, _font, new Vector2(innerLeft, phY), _textColor * 0.45f, phScale);
                }
            }
            else
            {
                DrawCommittedAndComposingText(b, innerLeft, innerMaxWidth, textY);
            }

            DrawModeButton(b);
        }

        public void DrawCandidatePopup(SpriteBatch b)
        {
            // 与上文：在菜单物品网格、滚动条与飞行精灵全部绘制完成后调用
            // 作用：在搜索框正下方绘制原版羊皮纸候选词浮窗，支持悬停高亮、首选强调与页码箭头展示
            if (!_selected || !IsComposing)
            {
                return;
            }

            EnsurePopupLayoutCurrent();
            if (_candidateBarBounds.IsEmpty)
            {
                return;
            }

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                _candidateBarBounds.X,
                _candidateBarBounds.Y,
                _candidateBarBounds.Width,
                _candidateBarBounds.Height,
                Color.White,
                scale: 0.75f,
                drawShadow: true);

            if (_font == null)
            {
                return;
            }

            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            float lineHeight = Math.Max(24f, MeasureTextSafe("木").Y);

            if (_candidateHitBoxes.Count == 0)
            {
                const string emptyHint = "无候选";
                float hintY = _candidateBarBounds.Y + (_candidateBarBounds.Height - lineHeight) / 2f + 1f;
                Utility.drawTextWithShadow(b, emptyHint, _font, new Vector2(_candidateBarBounds.X + 18, hintY), _textColor * 0.6f);
            }
            else
            {
                for (int i = 0; i < _candidateHitBoxes.Count; i++)
                {
                    CandidateHitBox entry = _candidateHitBoxes[i];
                    bool hovered = entry.Bounds.Contains(mouseX, mouseY);
                    if (hovered)
                    {
                        b.Draw(Game1.staminaRect, entry.Bounds, new Color(255, 215, 110) * 0.48f);
                    }
                    else if (entry.IndexInPage == 0)
                    {
                        b.Draw(Game1.staminaRect, entry.Bounds, new Color(255, 232, 165) * 0.28f);
                    }

                    float itemTextY = entry.Bounds.Y + (entry.Bounds.Height - lineHeight) / 2f + 1f;
                    Color indexColor = entry.IndexInPage == 0 ? new Color(175, 65, 20) : _textColor * 0.65f;
                    Utility.drawTextWithShadow(b, entry.IndexPrefix, _font, new Vector2(entry.Bounds.X + 4, itemTextY), indexColor);

                    float wordX = entry.Bounds.X + 4 + MeasureTextSafe(entry.IndexPrefix).X;
                    Utility.drawTextWithShadow(b, entry.WordText, _font, new Vector2(wordX, itemTextY), _textColor);
                }
            }

            Utility.drawTextWithShadow(b, _pageText, _font, _pageTextPos, _textColor * 0.7f);
            DrawPageArrowButton(b, _prevPageBounds, "<", _page > 0, mouseX, mouseY);
            DrawPageArrowButton(b, _nextPageBounds, ">", _page < _totalPages - 1, mouseX, mouseY);
        }

        public void RecieveTextInput(char inputChar)
        {
            // 与上文：由 Stardew KeyboardDispatcher 在未被原生抑制或回退模式下分发字符事件
            // 作用：仅在未抑制且非 Update 已轮询的 ASCII 字母数字空格时接收字符，防止双倍录入
            if (!_selected || NativeImeSuppressor.IsSuppressed || char.IsControl(inputChar))
            {
                return;
            }

            if (IsPolledAsciiChar(inputChar) || inputChar == '"')
            {
                return;
            }

            if (ChineseMode && IsComposing &&
                (inputChar == '-' || inputChar == ',' || inputChar == '+' || inputChar == '=' || inputChar == '.'))
            {
                return;
            }

            if (_text.Length < MaxTextLength)
            {
                _text += inputChar;
                _caretBlinkMs = 0.0;
            }
        }

        public void RecieveTextInput(string text)
        {
            // 与上文：由 KeyboardDispatcher 在 Ctrl+V 粘贴或批量提交文本时触发
            // 作用：过滤控制符后将剪贴板或中文字符串追加进已上屏搜索文本
            if (!_selected || string.IsNullOrEmpty(text))
            {
                return;
            }

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (!char.IsControl(ch) && ch != '"')
                {
                    sb.Append(ch);
                }
            }

            if (sb.Length == 0)
            {
                return;
            }

            int remain = MaxTextLength - _text.Length;
            if (remain <= 0)
            {
                return;
            }

            _text += sb.Length > remain ? sb.ToString(0, remain) : sb.ToString();
            _caretBlinkMs = 0.0;
        }

        public void RecieveCommandInput(char command)
        {
            // 独立逻辑，无上文
            // 作用：退格与回车已统一在 Update 边沿与连删状态机中处理，此处留空防止重复触发
        }

        public void RecieveSpecialInput(Keys key)
        {
            // 独立逻辑，无上文
            // 作用：特殊功能键由 Update 边沿检测统一调度，此处留空
        }

        private void DrawCommittedAndComposingText(SpriteBatch b, int innerLeft, int innerMaxWidth, float textY)
        {
            // 与上文：DrawBox 在文本非空或处于聚焦态时进入绘制正文分支
            // 作用：裁剪超宽头部以保正文与高亮音节串不溢出木框，并在末尾绘制闪烁输入光标
            string composingDisplay = IsComposing
                ? (!string.IsNullOrEmpty(_queryResult?.SegmentedDisplay) ? _queryResult!.SegmentedDisplay : _composing)
                : string.Empty;

            while (composingDisplay.Length > 1 && MeasureTextSafe(composingDisplay).X > innerMaxWidth - 8)
            {
                composingDisplay = composingDisplay.Substring(1);
            }

            float composingWidth = composingDisplay.Length > 0 ? MeasureTextSafe(composingDisplay).X + 6f : 0f;
            string displayedText = _text;
            while (displayedText.Length > 0 && MeasureTextSafe(displayedText).X + composingWidth + 4f > innerMaxWidth)
            {
                displayedText = displayedText.Substring(1);
            }

            float cursorX = innerLeft;
            if (_font != null && displayedText.Length > 0)
            {
                Utility.drawTextWithShadow(b, displayedText, _font, new Vector2(cursorX, textY), _textColor);
                cursorX += MeasureTextSafe(displayedText).X;
            }

            if (IsComposing && composingDisplay.Length > 0)
            {
                Vector2 compSize = MeasureTextSafe(composingDisplay);
                int boxW = (int)Math.Ceiling(compSize.X) + 4;
                Rectangle highlightRect = new Rectangle((int)cursorX, Y + 8, boxW, Math.Max(16, Height - 16));
                b.Draw(Game1.staminaRect, highlightRect, new Color(255, 232, 165) * 0.55f);

                Rectangle underlineRect = new Rectangle((int)cursorX + 1, Y + Height - 11, Math.Max(4, boxW - 2), 2);
                b.Draw(Game1.staminaRect, underlineRect, new Color(165, 85, 25) * 0.85f);

                if (_font != null)
                {
                    Utility.drawTextWithShadow(b, composingDisplay, _font, new Vector2(cursorX + 2, textY), new Color(95, 45, 15));
                }

                cursorX += compSize.X + 4f;
            }
            else if (displayedText.Length > 0)
            {
                cursorX += 2f;
            }

            if (_selected && _caretBlinkMs % 1000.0 < 500.0)
            {
                int clampedCaretX = Math.Min((int)cursorX, X + Width - 12);
                b.Draw(Game1.staminaRect, new Rectangle(clampedCaretX, Y + 9, 3, Math.Max(14, Height - 18)), _textColor);
            }
        }

        private void DrawModeButton(SpriteBatch b)
        {
            // 与上文：DrawBox 已完成主搜索木框及文本内容绘制
            // 作用：紧邻右侧绘制原版纹理框样式的 [中]/[英] 状态切换按钮
            Rectangle modeRect = ModeButtonBounds;
            Color boxTint = ChineseMode ? new Color(255, 236, 188) : new Color(225, 232, 240);

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                modeRect.X,
                modeRect.Y,
                modeRect.Width,
                modeRect.Height,
                boxTint,
                scale: 0.5f,
                drawShadow: false);

            if (_font == null)
            {
                return;
            }

            string label = ChineseMode ? "中" : "英";
            Color labelColor = ChineseMode ? new Color(165, 55, 15) : new Color(45, 75, 120);

            Vector2 labelSize = MeasureTextSafe(label);
            Vector2 labelPos = new Vector2(
                modeRect.X + (modeRect.Width - labelSize.X) / 2f,
                modeRect.Y + (modeRect.Height - labelSize.Y) / 2f + 1f);
            Utility.drawTextWithShadow(b, label, _font, labelPos, labelColor);
        }

        private void DrawPageArrowButton(SpriteBatch b, Rectangle bounds, string glyph, bool enabled, int mouseX, int mouseY)
        {
            // 与上文：DrawCandidatePopup 在页码文本右侧绘制翻页控件
            // 作用：渲染候选词浮窗的上一页/下一页箭头及鼠标悬停态底纹
            if (_font == null)
            {
                return;
            }

            if (enabled && bounds.Contains(mouseX, mouseY))
            {
                b.Draw(Game1.staminaRect, bounds, new Color(255, 215, 110) * 0.45f);
            }

            Vector2 size = MeasureTextSafe(glyph);
            Vector2 pos = new Vector2(
                bounds.X + (bounds.Width - size.X) / 2f,
                bounds.Y + (bounds.Height - size.Y) / 2f + 1f);
            Utility.drawTextWithShadow(b, glyph, _font, pos, enabled ? _textColor : _textColor * 0.35f);
        }

        private void ProcessShiftToggleEdge(KeyboardState cur)
        {
            // 与上文：Update 开始读取当前帧键盘快照后最先检查 Shift 组合
            // 作用：仅当单独按下并松开 LeftShift/RightShift 且未夹带其他按键时切换中英文模式
            bool prevShift = IsShiftDown(_prevKb);
            bool curShift = IsShiftDown(cur);

            if (curShift && !prevShift)
            {
                _shiftAloneCandidate = !HasAnyNonShiftKeyDown(cur);
            }
            else if (curShift && prevShift)
            {
                if (_shiftAloneCandidate && HasAnyNonShiftKeyDown(cur))
                {
                    _shiftAloneCandidate = false;
                }
            }
            else if (!curShift && prevShift)
            {
                if (_shiftAloneCandidate)
                {
                    ToggleInputMode();
                }

                _shiftAloneCandidate = false;
            }
        }

        private void ProcessBackspaceWithRepeat(KeyboardState cur, double elapsedMs, ISet<string>? priorityWords)
        {
            // 与上文：Update 在非 Ctrl/Alt 组合下检查退格键状态
            // 作用：按下首帧立即删除一字符，持续按住超过 400ms 后每 45ms 连续删除
            bool curBack = cur.IsKeyDown(Keys.Back);
            bool prevBack = _prevKb.IsKeyDown(Keys.Back);

            if (curBack && !prevBack)
            {
                _backHeldMs = 0.0;
                _backNextRepeatMs = BackspaceInitialDelayMs;
                HandleBackspace(priorityWords);
                return;
            }

            if (curBack && prevBack)
            {
                _backHeldMs += elapsedMs;
                while (_backHeldMs >= _backNextRepeatMs)
                {
                    if (_composing.Length == 0 && _text.Length == 0)
                    {
                        break;
                    }

                    HandleBackspace(priorityWords);
                    _backNextRepeatMs += BackspaceRepeatIntervalMs;
                }
                return;
            }

            _backHeldMs = 0.0;
            _backNextRepeatMs = BackspaceInitialDelayMs;
        }

        private void ProcessChineseModeKeys(KeyboardState cur, ISet<string>? priorityWords)
        {
            // 与上文：ChineseMode 为真且未按住 Ctrl/Alt
            // 作用：采集 A-Z 拼音拼写、1-5/空格选词上屏、翻页键与回车/Esc 控制键
            for (Keys key = Keys.A; key <= Keys.Z; key++)
            {
                if (IsNewlyPressed(cur, key) && _composing.Length < MaxComposingLength)
                {
                    _composing += (char)('a' + (key - Keys.A));
                    _page = 0;
                    _caretBlinkMs = 0.0;
                    RebuildQuery(priorityWords);
                    PlaySoundSafe("cowboy_monsterhit");
                }
            }

            for (int idx = 0; idx < PageSize; idx++)
            {
                Keys mainDigit = Keys.D1 + idx;
                Keys padDigit = Keys.NumPad1 + idx;
                if (IsNewlyPressed(cur, mainDigit) || IsNewlyPressed(cur, padDigit))
                {
                    if (IsComposing)
                    {
                        TryCommitCandidateAtPageIndex(idx, priorityWords);
                    }
                    else
                    {
                        AppendToText((char)('1' + idx));
                    }
                }
            }

            if (!IsComposing)
            {
                ProcessExtraDigits(cur);
            }

            if (IsNewlyPressed(cur, Keys.Space))
            {
                if (IsComposing)
                {
                    TryCommitCandidateAtPageIndex(0, priorityWords);
                }
                else
                {
                    AppendToText(' ');
                }
            }

            if (IsComposing)
            {
                if (IsNewlyPressed(cur, Keys.OemMinus) || IsNewlyPressed(cur, Keys.OemComma) ||
                    IsNewlyPressed(cur, Keys.PageUp) || IsNewlyPressed(cur, Keys.Up))
                {
                    if (_page > 0)
                    {
                        _page--;
                        RebuildPopupLayout();
                        PlaySoundSafe("shwip");
                    }
                }

                if (IsNewlyPressed(cur, Keys.OemPlus) || IsNewlyPressed(cur, Keys.OemPeriod) ||
                    IsNewlyPressed(cur, Keys.PageDown) || IsNewlyPressed(cur, Keys.Down))
                {
                    if (_page < _totalPages - 1)
                    {
                        _page++;
                        RebuildPopupLayout();
                        PlaySoundSafe("shwip");
                    }
                }
            }

            if (IsNewlyPressed(cur, Keys.Enter))
            {
                if (IsComposing)
                {
                    CommitRawComposingToText();
                }
                else
                {
                    Selected = false;
                }
            }
            else if (IsNewlyPressed(cur, Keys.Escape))
            {
                ConsumedEscapeThisTick = true;
                if (IsComposing)
                {
                    ResetComposingState();
                }
                else
                {
                    Selected = false;
                }
            }
        }

        private void ProcessEnglishModeKeys(KeyboardState cur)
        {
            // 与上文：处于英文直输模式
            // 作用：按边沿检测直接追加字母（响应 Shift 大小写）、数字与空格到搜索文本，回车或 Esc 失焦
            bool shiftDown = IsShiftDown(cur);
            for (Keys key = Keys.A; key <= Keys.Z; key++)
            {
                if (IsNewlyPressed(cur, key))
                {
                    char ch = shiftDown ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
                    AppendToText(ch);
                }
            }

            for (int d = 0; d <= 9; d++)
            {
                if (IsNewlyPressed(cur, Keys.D0 + d) || IsNewlyPressed(cur, Keys.NumPad0 + d))
                {
                    AppendToText((char)('0' + d));
                }
            }

            if (IsNewlyPressed(cur, Keys.Space))
            {
                AppendToText(' ');
            }

            if (IsNewlyPressed(cur, Keys.Enter))
            {
                Selected = false;
            }
            else if (IsNewlyPressed(cur, Keys.Escape))
            {
                ConsumedEscapeThisTick = true;
                Selected = false;
            }
        }

        private void ProcessExtraDigits(KeyboardState cur)
        {
            // 与上文：中文模式下且当前未处于拼音拼写态
            // 作用：补充采集 0 与 6-9 数字键，避免搜索含这些数字的名称时漏输
            if (IsNewlyPressed(cur, Keys.D0) || IsNewlyPressed(cur, Keys.NumPad0))
            {
                AppendToText('0');
            }

            for (int d = 6; d <= 9; d++)
            {
                if (IsNewlyPressed(cur, Keys.D0 + d) || IsNewlyPressed(cur, Keys.NumPad0 + d))
                {
                    AppendToText((char)('0' + d));
                }
            }
        }

        private void HandleBackspace(ISet<string>? priorityWords)
        {
            // 与上文：退格键首帧触发或连删定时器到期
            // 作用：优先削减未上屏拼音末尾字符并重查候选，若拼音为空则删除已上屏末尾字符
            _caretBlinkMs = 0.0;
            if (_composing.Length > 0)
            {
                _composing = _composing.Substring(0, _composing.Length - 1);
                _page = 0;
                if (_composing.Length > 0)
                {
                    RebuildQuery(priorityWords);
                }
                else
                {
                    ResetComposingState();
                }

                PlaySoundSafe("tinyWhip");
                return;
            }

            if (_text.Length > 0)
            {
                _text = _text.Substring(0, _text.Length - 1);
                PlaySoundSafe("tinyWhip");
            }
        }

        private void HandleCandidateBarLeftClick(int x, int y)
        {
            // 与上文：ReceiveLeftClick 已确认点击落点在可见 CandidateBarBounds 内部
            // 作用：响应上一页/下一页箭头及候选词项点击上屏
            EnsurePopupLayoutCurrent();

            if (_prevPageBounds.Contains(x, y))
            {
                if (_page > 0)
                {
                    _page--;
                    RebuildPopupLayout();
                    PlaySoundSafe("shwip");
                }
                return;
            }

            if (_nextPageBounds.Contains(x, y))
            {
                if (_page < _totalPages - 1)
                {
                    _page++;
                    RebuildPopupLayout();
                    PlaySoundSafe("shwip");
                }
                return;
            }

            for (int i = 0; i < _candidateHitBoxes.Count; i++)
            {
                if (_candidateHitBoxes[i].Bounds.Contains(x, y))
                {
                    CommitCandidate(_candidateHitBoxes[i].Candidate, _lastPriorityWords);
                    return;
                }
            }
        }

        private void ToggleInputMode()
        {
            // 与上文：单按 Shift 松开或左/右键点击中英模式按钮
            // 作用：切换中英输入模式；若切回英文时仍有未上屏拼音则直接将英文原串落入 Text
            ChineseMode = !ChineseMode;
            if (!ChineseMode && IsComposing)
            {
                CommitRawComposingToText();
            }

            _caretBlinkMs = 0.0;
            PlaySoundSafe("smallSelect");
        }

        private void TryCommitCandidateAtPageIndex(int indexInPage, ISet<string>? priorityWords)
        {
            // 与上文：用户在拼写态按下数字 1-5 或空格选词
            // 作用：校验当前页内存在对应下标候选词后执行上屏
            IReadOnlyList<PinyinCandidate>? candidates = _queryResult?.Candidates;
            if (candidates == null)
            {
                return;
            }

            int globalIndex = _page * PageSize + indexInPage;
            if (globalIndex < 0 || globalIndex >= candidates.Count)
            {
                return;
            }

            CommitCandidate(candidates[globalIndex], priorityWords);
        }

        private void CommitCandidate(PinyinCandidate candidate, ISet<string>? priorityWords)
        {
            // 与上文：通过快捷键或鼠标左键选中了具体候选词
            // 作用：将词条追加到 Text，按 ConsumedInputLength 剥离已消费拼音并继续重查或收尾
            if (!string.IsNullOrEmpty(candidate.Text))
            {
                int remain = MaxTextLength - _text.Length;
                if (remain > 0)
                {
                    _text += candidate.Text.Length > remain ? candidate.Text.Substring(0, remain) : candidate.Text;
                }
            }

            int consumed = candidate.ConsumedInputLength <= 0
                ? _composing.Length
                : Math.Min(candidate.ConsumedInputLength, _composing.Length);
            _composing = _composing.Substring(consumed);
            _page = 0;
            _caretBlinkMs = 0.0;

            if (_composing.Length > 0)
            {
                RebuildQuery(priorityWords);
            }
            else
            {
                ResetComposingState();
            }

            PlaySoundSafe("smallSelect");
        }

        private void CommitRawComposingToText()
        {
            // 与上文：拼写态下按 Enter 或切为英文模式
            // 作用：把当前原始拼音英文串直接追加进 Text 并清空拼写缓冲区
            if (_composing.Length > 0)
            {
                int remain = MaxTextLength - _text.Length;
                if (remain > 0)
                {
                    _text += _composing.Length > remain ? _composing.Substring(0, remain) : _composing;
                }
            }

            ResetComposingState();
            _caretBlinkMs = 0.0;
        }

        private void AppendToText(char ch)
        {
            // 独立逻辑，无上文
            // 作用：在长度上限内向已上屏文本末尾追加单个字符并重置光标闪烁相位
            if (_text.Length < MaxTextLength)
            {
                _text += ch;
                _caretBlinkMs = 0.0;
                PlaySoundSafe("cowboy_monsterhit");
            }
        }

        private void RebuildQuery(ISet<string>? priorityWords)
        {
            // 与上文：拼写缓冲区发生变化或分步选词后仍有剩余拼音
            // 作用：调用 PinyinEngine.Query 生成切分显示与箱内优先候选列表并同步浮窗几何排版
            if (_composing.Length == 0)
            {
                ResetComposingState();
                return;
            }

            _queryResult = PinyinEngine.Query(_composing, priorityWords, 60);
            int totalCount = _queryResult.Candidates.Count;
            _totalPages = totalCount <= 0 ? 1 : (totalCount + PageSize - 1) / PageSize;
            _page = Math.Clamp(_page, 0, _totalPages - 1);
            RebuildPopupLayout();
        }

        private void ResetComposingState()
        {
            // 独立逻辑，无上文
            // 作用：重置拼音缓冲、查询结果、分页序号与候选命中框缓存
            _composing = string.Empty;
            _queryResult = null;
            _page = 0;
            _totalPages = 1;
            _candidateBarBounds = Rectangle.Empty;
            _prevPageBounds = Rectangle.Empty;
            _nextPageBounds = Rectangle.Empty;
            _candidateHitBoxes.Clear();
        }

        private void EnsurePopupLayoutCurrent()
        {
            // 与上文：绘制或命中检测访问浮窗坐标前调用
            // 作用：当窗口尺寸变化或 X/Y/Width/Height 被外部重设时及时重建候选浮窗矩形
            if (_lastLayoutX != X || _lastLayoutY != Y || _lastLayoutWidth != Width || _lastLayoutHeight != Height)
            {
                RebuildPopupLayout();
            }
        }

        private void RebuildPopupLayout()
        {
            // 与上文：RebuildQuery、翻页或输入框位置变更后调用
            // 作用：依据字体测量结果与屏幕右边界自适应计算 CandidateBarBounds 及各候选与翻页命中框
            _lastLayoutX = X;
            _lastLayoutY = Y;
            _lastLayoutWidth = Width;
            _lastLayoutHeight = Height;
            _candidateHitBoxes.Clear();

            if (!_selected || _composing.Length == 0)
            {
                _candidateBarBounds = Rectangle.Empty;
                _prevPageBounds = Rectangle.Empty;
                _nextPageBounds = Rectangle.Empty;
                return;
            }

            int popupX = X;
            int popupY = Y + Height + 4;
            int lineHeight = Math.Max(26, (int)Math.Ceiling(MeasureTextSafe("木").Y));
            int popupHeight = Math.Max(56, lineHeight + 24);
            int viewportWidth = GetViewportWidthSafe();
            int maxPopupWidth = Math.Max(Math.Max(Width, 260), viewportWidth - popupX - 16);

            IReadOnlyList<PinyinCandidate>? candidates = _queryResult?.Candidates;
            int totalCount = candidates?.Count ?? 0;
            _totalPages = totalCount <= 0 ? 1 : (totalCount + PageSize - 1) / PageSize;
            _page = Math.Clamp(_page, 0, _totalPages - 1);

            _pageText = $"{_page + 1}/{_totalPages}";
            Vector2 pageTextSize = MeasureTextSafe(_pageText);

            const int arrowSize = 28;
            const int arrowGap = 4;
            const int sidePad = 18;
            const int itemPadX = 6;
            const int itemGap = 8;
            int rightSectionWidth = (int)Math.Ceiling(pageTextSize.X) + 10 + arrowSize + arrowGap + arrowSize + sidePad;

            int popupWidth;
            if (totalCount == 0 || candidates == null)
            {
                int hintWidth = (int)Math.Ceiling(MeasureTextSafe("无候选").X);
                popupWidth = Math.Clamp(sidePad + hintWidth + 16 + rightSectionWidth, Math.Max(Width, 260), maxPopupWidth);
            }
            else
            {
                int start = _page * PageSize;
                int count = Math.Min(PageSize, totalCount - start);
                int maxItemsWidth = Math.Max(120, maxPopupWidth - sidePad - rightSectionWidth - 12);
                int cursorX = popupX + sidePad;

                for (int i = 0; i < count; i++)
                {
                    PinyinCandidate cand = candidates[start + i];
                    string indexPrefix = $"{i + 1}.";
                    string wordText = cand.Text ?? string.Empty;
                    int prefixWidth = (int)Math.Ceiling(MeasureTextSafe(indexPrefix).X);
                    int wordWidth = (int)Math.Ceiling(MeasureTextSafe(wordText).X);
                    int entryWidth = prefixWidth + wordWidth + itemPadX * 2;

                    if (i > 0 && (cursorX - (popupX + sidePad)) + entryWidth > maxItemsWidth)
                    {
                        break;
                    }

                    Rectangle itemRect = new Rectangle(cursorX, popupY + 10, entryWidth, popupHeight - 20);
                    _candidateHitBoxes.Add(new CandidateHitBox(itemRect, cand, indexPrefix, wordText, i));
                    cursorX += entryWidth + itemGap;
                }

                int usedItemsWidth = cursorX > popupX + sidePad ? (cursorX - itemGap) - popupX : sidePad;
                popupWidth = Math.Clamp(usedItemsWidth + 12 + rightSectionWidth, Math.Max(Width, 260), maxPopupWidth);
            }

            _candidateBarBounds = new Rectangle(popupX, popupY, popupWidth, popupHeight);

            int arrowY = popupY + (popupHeight - arrowSize) / 2;
            _nextPageBounds = new Rectangle(popupX + popupWidth - sidePad - arrowSize, arrowY, arrowSize, arrowSize);
            _prevPageBounds = new Rectangle(_nextPageBounds.X - arrowGap - arrowSize, arrowY, arrowSize, arrowSize);
            _pageTextPos = new Vector2(_prevPageBounds.X - 10 - pageTextSize.X, popupY + (popupHeight - pageTextSize.Y) / 2f + 1f);
        }

        private Vector2 MeasureTextSafe(string text)
        {
            // 独立逻辑，无上文
            // 作用：在字体可用时精确测量文本尺寸，无头环境字体为空时返回保守估算尺寸
            if (_font != null && !string.IsNullOrEmpty(text))
            {
                return _font.MeasureString(text);
            }

            return new Vector2((text?.Length ?? 0) * 16f, 26f);
        }

        private bool IsNewlyPressed(KeyboardState cur, Keys key)
        {
            // 独立逻辑，无上文
            // 作用：比较当前与上一帧键盘快照实现单次按键上升沿判定
            return cur.IsKeyDown(key) && !_prevKb.IsKeyDown(key);
        }

        private static bool IsShiftDown(KeyboardState state)
        {
            // 独立逻辑，无上文
            // 作用：检测左右 Shift 任一键是否处于按下状态
            return state.IsKeyDown(Keys.LeftShift) || state.IsKeyDown(Keys.RightShift);
        }

        private static bool HasAnyNonShiftKeyDown(KeyboardState state)
        {
            // 独立逻辑，无上文
            // 作用：遍历当前按下键集合判断 Shift 按下期间是否夹带了其他按键
            Keys[] keys = state.GetPressedKeys();
            for (int i = 0; i < keys.Length; i++)
            {
                Keys key = keys[i];
                if (key != Keys.None && key != Keys.LeftShift && key != Keys.RightShift)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPolledAsciiChar(char c)
        {
            // 独立逻辑，无上文
            // 作用：识别已由 Update 轮询采集的 ASCII 字母、数字与空格字符
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == ' ';
        }

        private static KeyboardDispatcher? GetDispatcherSafe()
        {
            // 独立逻辑，无上文
            // 作用：在 Game1 实例就绪时安全读取键盘分发器，防止无头测试空引用崩溃
            try
            {
                return Game1.game1 != null ? Game1.keyboardDispatcher : null;
            }
            catch
            {
                return null;
            }
        }

        private static KeyboardState ReadKeyboardStateSafe()
        {
            // 独立逻辑，无上文
            // 作用：安全读取当前游戏键盘快照，无窗口环境返回空状态
            try
            {
                return Game1.game1 != null && Game1.input != null ? Game1.input.GetKeyboardState() : default;
            }
            catch
            {
                return default;
            }
        }

        private static int GetViewportWidthSafe()
        {
            // 独立逻辑，无上文
            // 作用：安全读取当前 UI 视口宽度用于限制候选浮窗右边界
            try
            {
                return Game1.game1 != null ? Game1.uiViewport.Width : 1280;
            }
            catch
            {
                return 1280;
            }
        }

        private static void PlaySoundSafe(string cue)
        {
            // 独立逻辑，无上文
            // 作用：在音频引擎可用时播放原版界面音效，无声卡/无头测试下静默忽略
            try
            {
                if (Game1.soundBank != null)
                {
                    Game1.playSound(cue);
                }
            }
            catch
            {
            }
        }
    }
}
