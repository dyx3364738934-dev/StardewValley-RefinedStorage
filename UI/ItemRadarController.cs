using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RefinedStorage.Ime;
using RefinedStorage.Storage;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Objects;

namespace RefinedStorage.UI
{
    public sealed class RadarMatchItem
    {
        public Item Proto { get; }
        public string DisplayName { get; }
        public int TotalCount { get; set; }

        public RadarMatchItem(Item proto, string displayName, int totalCount)
        {
            // 独立逻辑，无上文
            // 作用：封装单只命中物理箱内同种匹配物品的原型、展示名称与合并总数
            Proto = proto;
            DisplayName = displayName;
            TotalCount = totalCount;
        }
    }

    public sealed class RadarChestMatch
    {
        public Chest Chest { get; }
        public Vector2 Tile { get; }
        public List<RadarMatchItem> Matches { get; }

        public RadarChestMatch(Chest chest, Vector2 tile, List<RadarMatchItem> matches)
        {
            // 独立逻辑，无上文
            // 作用：封装找物雷达命中的单只物理箱引用、世界格位坐标及其箱内全部匹配物品汇总
            Chest = chest;
            Tile = tile;
            Matches = matches;
        }
    }

    public sealed class ItemRadarController
    {
        private const int OffscreenArrowMargin = 28;
        private const int RefreshIntervalTicks = 20;

        private readonly IModHelper? _helper;
        private readonly Func<RefinedStorageConfig> _getConfig;
        private readonly HashSet<string> _locationItemNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<RadarChestMatch> _matches = new List<RadarChestMatch>();

        private InGamePinyinBox? _searchBox;
        private Rectangle _barBounds;
        private Rectangle _clearButtonBounds;
        private Rectangle _closeButtonBounds;
        private string _lastSearchQuery = string.Empty;
        private bool _searchDirty;
        private bool _namesDirty = true;
        private int _refreshTickCounter;

        public bool IsOpen { get; private set; }

        public ItemRadarController(Func<RefinedStorageConfig> getConfig)
        {
            // 独立逻辑，无上文
            // 作用：保存配置读取委托以支持无 SMAPI 事件总线的无头单元测试
            _getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
        }

        public ItemRadarController(IModHelper helper, Func<RefinedStorageConfig> getConfig)
            : this(getConfig)
        {
            // 与上文：先保存配置读取委托，再注册输入、帧循环、渲染与场景切换事件
            // 作用：监听雷达热键、屏蔽输入期角色动作、同步同场景箱子查找并在离开或切菜单时无条件解锁输入法
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
            helper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.World.ChestInventoryChanged += OnChestInventoryChanged;
            helper.Events.World.ObjectListChanged += OnObjectListChanged;
        }

        public static List<RadarChestMatch> SearchLocationChests(GameLocation? location, string? query)
        {
            // 独立逻辑，无上文
            // 作用：只读扫描当前场景全部合法玩家箱，按多词 AND 与拼音简拼匹配物品并合并同箱同款数量
            var results = new List<RadarChestMatch>();
            if (location?.objects == null || string.IsNullOrWhiteSpace(query))
                return results;

            string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return results;

            foreach (KeyValuePair<Vector2, StardewValley.Object> pair in location.objects.Pairs)
            {
                if (pair.Value is not Chest chest || !ChestFilter.IsLinkablePlayerChest(chest))
                    continue;

                Inventory? items = chest.Items;
                if (items == null)
                    continue;

                int capacity = GetChestCapacitySafe(chest);
                int limit = Math.Min(items.Count, Math.Max(0, capacity));
                List<RadarMatchItem>? chestMatches = null;

                for (int s = 0; s < limit; s++)
                {
                    Item? item = items[s];
                    if (item == null || item.Stack <= 0 || !MatchesAllTokens(item, tokens))
                        continue;

                    chestMatches ??= new List<RadarMatchItem>();
                    string displayName = ReadItemDisplayName(item);
                    RadarMatchItem? existing = FindMergeTarget(chestMatches, item, displayName);
                    if (existing != null)
                    {
                        existing.TotalCount += item.Stack;
                    }
                    else
                    {
                        Item proto = item.getOne();
                        proto.Stack = 1;
                        chestMatches.Add(new RadarMatchItem(proto, displayName, item.Stack));
                    }
                }

                if (chestMatches != null && chestMatches.Count > 0)
                    results.Add(new RadarChestMatch(chest, pair.Key, chestMatches));
            }

            results.Sort((a, b) =>
            {
                int cmpY = a.Tile.Y.CompareTo(b.Tile.Y);
                return cmpY != 0 ? cmpY : a.Tile.X.CompareTo(b.Tile.X);
            });

            return results;
        }

        public static Vector2 ComputeOffscreenArrowPosition(
            Vector2 targetUiPos,
            int uiViewportWidth,
            int uiViewportHeight,
            int margin,
            out float rotationRadians)
        {
            // 独立逻辑，无上文
            // 作用：从视口中心向屏外目标点作射线并与内缩边距边界求交，返回夹紧后的边缘坐标及指路朝向弧度
            float minX = Math.Clamp(margin, 0, Math.Max(0, uiViewportWidth / 2));
            float maxX = Math.Max(minX, uiViewportWidth - minX);
            float minY = Math.Clamp(margin, 0, Math.Max(0, uiViewportHeight / 2));
            float maxY = Math.Max(minY, uiViewportHeight - minY);

            float cx = uiViewportWidth * 0.5f;
            float cy = uiViewportHeight * 0.5f;
            float dx = targetUiPos.X - cx;
            float dy = targetUiPos.Y - cy;

            if (MathF.Abs(dx) < 1e-4f && MathF.Abs(dy) < 1e-4f)
            {
                rotationRadians = 0f;
                return new Vector2(Math.Clamp(targetUiPos.X, minX, maxX), Math.Clamp(targetUiPos.Y, minY, maxY));
            }

            rotationRadians = MathF.Atan2(dy, dx);

            float tx = dx > 1e-5f
                ? (maxX - cx) / dx
                : (dx < -1e-5f ? (minX - cx) / dx : float.PositiveInfinity);
            float ty = dy > 1e-5f
                ? (maxY - cy) / dy
                : (dy < -1e-5f ? (minY - cy) / dy : float.PositiveInfinity);

            float t = MathF.Min(tx, ty);
            if (!float.IsFinite(t) || t <= 0f)
            {
                return new Vector2(Math.Clamp(targetUiPos.X, minX, maxX), Math.Clamp(targetUiPos.Y, minY, maxY));
            }

            float px = Math.Clamp(cx + t * dx, minX, maxX);
            float py = Math.Clamp(cy + t * dy, minY, maxY);
            return new Vector2(px, py);
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            // 与上文：由 SMAPI 在每帧 ApplyOverrides 与游戏读取输入前触发
            // 作用：处理雷达热键开关、Esc 关闭、顶部搜索栏点击以及输入期屏蔽游戏移动与快捷键
            if (!Context.IsWorldReady)
            {
                if (IsOpen)
                    Close();
                return;
            }

            RefinedStorageConfig config = _getConfig();

            if (!IsOpen)
            {
                if (config.ItemRadarKey != null &&
                    config.ItemRadarKey.JustPressed() &&
                    Context.IsPlayerFree &&
                    Game1.activeClickableMenu == null &&
                    !Game1.eventUp &&
                    Game1.farmEvent == null)
                {
                    _helper?.Input.SuppressActiveKeybinds(config.ItemRadarKey);
                    _helper?.Input.Suppress(e.Button);
                    Open();
                }

                return;
            }

            if (config.ItemRadarKey != null && config.ItemRadarKey.JustPressed())
            {
                _helper?.Input.SuppressActiveKeybinds(config.ItemRadarKey);
                _helper?.Input.Suppress(e.Button);
                Close();
                PlaySoundSafe("bigDeSelect");
                return;
            }

            if (e.Button == SButton.Escape)
            {
                _helper?.Input.Suppress(SButton.Escape);
                Close();
                PlaySoundSafe("bigDeSelect");
                return;
            }

            // 与上文：已确认雷达处于打开状态且非热键/Esc 退出指令
            // 作用：按 UI 缩放鼠标坐标响应关闭、清空、切换中英与候选词点击，并屏蔽聚焦期间的键盘游戏热键
            UpdateBarLayout();
            if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
            {
                int mouseX = Game1.getMouseX(ui_scale: true);
                int mouseY = Game1.getMouseY(ui_scale: true);

                if (e.Button == SButton.MouseLeft)
                {
                    if (_closeButtonBounds.Contains(mouseX, mouseY))
                    {
                        _helper?.Input.Suppress(e.Button);
                        Close();
                        PlaySoundSafe("bigDeSelect");
                        return;
                    }

                    if (_searchBox != null)
                    {
                        if (_searchBox.Selected && _searchBox.IsComposing && _searchBox.CandidateBarBounds.Contains(mouseX, mouseY))
                        {
                            _helper?.Input.Suppress(e.Button);
                            _searchBox.ReceiveLeftClick(mouseX, mouseY);
                            SyncSearchResults(force: false);
                            return;
                        }

                        if (_clearButtonBounds.Contains(mouseX, mouseY))
                        {
                            _helper?.Input.Suppress(e.Button);
                            _searchBox.Clear(keepFocus: true);
                            PlaySoundSafe("smallSelect");
                            SyncSearchResults(force: false);
                            return;
                        }

                        if (_searchBox.ReceiveLeftClick(mouseX, mouseY))
                        {
                            _helper?.Input.Suppress(e.Button);
                            SyncSearchResults(force: false);
                            return;
                        }
                    }

                    if (_barBounds.Contains(mouseX, mouseY))
                    {
                        _helper?.Input.Suppress(e.Button);
                        return;
                    }
                }
                else
                {
                    if (_searchBox != null && _searchBox.ReceiveRightClick(mouseX, mouseY))
                    {
                        _helper?.Input.Suppress(e.Button);
                        SyncSearchResults(force: false);
                        return;
                    }

                    if (_barBounds.Contains(mouseX, mouseY) || _closeButtonBounds.Contains(mouseX, mouseY))
                    {
                        _helper?.Input.Suppress(e.Button);
                        return;
                    }
                }

                return;
            }

            if (_searchBox != null && _searchBox.Selected && e.Button.TryGetKeyboard(out Keys kbKey))
            {
                if (!IsModifierOrBackspace(kbKey) && !IsCtrlOrAltDown())
                    _helper?.Input.Suppress(e.Button);
            }
        }

        private void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：在拼音候选浮窗内滚动滚轮时翻页候选词并拦截滚轮切换玩家快捷栏
            if (!IsOpen || _searchBox == null || !_searchBox.Selected || !_searchBox.IsComposing)
                return;

            int mouseX = Game1.getMouseX(ui_scale: true);
            int mouseY = Game1.getMouseY(ui_scale: true);
            if (_searchBox.ReceiveScrollWheel(e.Delta, mouseX, mouseY))
                _helper?.Input.SuppressScrollWheel();
        }

        private void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
        {
            // 与上文：在 SMAPI ApplyOverrides 抹除被抑制的游戏热键之前触发
            // 作用：校验雷达存活条件并将未受屏蔽的原键盘输入及时转发给 InGamePinyinBox 更新拼写与匹配结果
            if (!IsOpen)
                return;

            if (!Context.IsWorldReady || Game1.player?.currentLocation == null ||
                !Game1.displayHUD ||
                Game1.activeClickableMenu != null || Game1.eventUp || Game1.farmEvent != null)
            {
                Close();
                return;
            }

            UpdateBarLayout();
            if (_namesDirty)
            {
                RefreshLocationItemNames(Game1.player.currentLocation);
                _namesDirty = false;
            }

            _searchBox?.Update(Game1.currentGameTime, _locationItemNames);
            SyncSearchResults(force: false);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // 与上文：OnUpdateTicking 已处理本帧按键采集与查询变更
            // 作用：周期性或在箱子内容变更后刷新同场景匹配列表，保持雷达结果与多人实时改动同步
            if (!IsOpen)
                return;

            if (!Context.IsWorldReady || Game1.player?.currentLocation == null)
            {
                Close();
                return;
            }

            _refreshTickCounter++;
            if (_searchDirty || _refreshTickCounter >= RefreshIntervalTicks)
            {
                _refreshTickCounter = 0;
                _searchDirty = false;
                if (_namesDirty)
                {
                    RefreshLocationItemNames(Game1.player.currentLocation);
                    _namesDirty = false;
                }
                SyncSearchResults(force: true);
            }
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            // 与上文：雷达处于打开状态且已完成当前场景匹配列表计算
            // 作用：渲染地面金色高亮框、屏内 80x96 真实物品气泡、屏外边缘指路小箭头以及顶部极简拼音搜索栏
            if (!IsOpen || !Context.IsWorldReady || Game1.player?.currentLocation == null ||
                !Game1.displayHUD || Game1.activeClickableMenu != null || Game1.eventUp || Game1.farmEvent != null)
            {
                return;
            }

            UpdateBarLayout();
            double timeMs = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0.0;
            int uiWidth = Game1.uiViewport.Width;
            int uiHeight = Game1.uiViewport.Height;

            if (!string.IsNullOrWhiteSpace(_lastSearchQuery) && _matches.Count > 0)
            {
                float pulse = 0.55f + 0.35f * (float)Math.Sin(timeMs / 220.0);
                for (int i = 0; i < _matches.Count; i++)
                {
                    RadarChestMatch match = _matches[i];
                    Vector2 centerUi = WorldBubbleRenderer.GetChestCenterUiPos(match.Tile);
                    bool onScreen = WorldBubbleRenderer.IsUiPointInViewport(centerUi, uiWidth, uiHeight, 32f, 32f);

                    if (onScreen)
                    {
                        DrawTileGoldHighlight(e.SpriteBatch, match.Tile, pulse);

                        var entries = new List<BubbleItemEntry>(match.Matches.Count);
                        for (int m = 0; m < match.Matches.Count; m++)
                        {
                            RadarMatchItem itemMatch = match.Matches[m];
                            entries.Add(new BubbleItemEntry(itemMatch.Proto, itemMatch.TotalCount, string.Empty));
                        }

                        Vector2 anchorUi = WorldBubbleRenderer.GetChestBubbleAnchorUiPos(match.Tile);
                        WorldBubbleRenderer.DrawChestItemBubble(e.SpriteBatch, anchorUi, entries, timeMs);
                    }
                    else
                    {
                        Vector2 arrowPos = ComputeOffscreenArrowPosition(
                            centerUi,
                            uiWidth,
                            uiHeight,
                            OffscreenArrowMargin,
                            out float rotation);
                        WorldBubbleRenderer.DrawOffscreenArrow(e.SpriteBatch, arrowPos, rotation);
                    }
                }
            }

            // 与上文：世界地面高亮、气泡与屏外箭头已绘制在下层
            // 作用：绘制顶部极简木框、搜索框、清空叉号、关闭按钮与拼音候选浮窗及按钮悬停提示
            IClickableMenu.drawTextureBox(
                e.SpriteBatch,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                _barBounds.X,
                _barBounds.Y,
                _barBounds.Width,
                _barBounds.Height,
                Color.White,
                scale: 1f,
                drawShadow: true);

            if (_searchBox != null)
            {
                _searchBox.DrawBox(e.SpriteBatch);

                if (Game1.mouseCursors != null)
                {
                    bool hasText = !string.IsNullOrEmpty(_searchBox.Text) || _searchBox.IsComposing;
                    e.SpriteBatch.Draw(
                        Game1.mouseCursors,
                        new Vector2(_clearButtonBounds.X, _clearButtonBounds.Y),
                        new Rectangle(337, 494, 12, 12),
                        Color.White * (hasText ? 0.9f : 0.35f),
                        0f,
                        Vector2.Zero,
                        3f,
                        SpriteEffects.None,
                        0.93f);

                    e.SpriteBatch.Draw(
                        Game1.mouseCursors,
                        new Vector2(_closeButtonBounds.X, _closeButtonBounds.Y),
                        new Rectangle(337, 494, 12, 12),
                        Color.White,
                        0f,
                        Vector2.Zero,
                        3.5f,
                        SpriteEffects.None,
                        0.93f);
                }

                _searchBox.DrawCandidatePopup(e.SpriteBatch);
            }

            if (Game1.smallFont != null)
            {
                int mouseX = Game1.getMouseX(ui_scale: true);
                int mouseY = Game1.getMouseY(ui_scale: true);
                if (_closeButtonBounds.Contains(mouseX, mouseY))
                {
                    IClickableMenu.drawHoverText(e.SpriteBatch, "关闭", Game1.smallFont);
                }
                else if (_clearButtonBounds.Contains(mouseX, mouseY) &&
                         _searchBox != null &&
                         (!string.IsNullOrEmpty(_searchBox.Text) || _searchBox.IsComposing))
                {
                    IClickableMenu.drawHoverText(e.SpriteBatch, "清空", Game1.smallFont);
                }
                else if (_searchBox != null && _searchBox.ModeButtonBounds.Contains(mouseX, mouseY))
                {
                    IClickableMenu.drawHoverText(e.SpriteBatch, "Shift 切中/英", Game1.smallFont);
                }
            }
        }

        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：玩家跨场景传送时强制关闭雷达并无条件恢复系统输入法，防止新地图键盘卡死
            if (e.IsLocalPlayer && IsOpen)
                Close();
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：游戏打开或切换任意菜单时立即关闭雷达并恢复系统输入法
            if (IsOpen)
                Close();
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：加载存档时重置雷达状态并确保系统输入法处于恢复态
            Close();
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：返回标题画面时强制关闭雷达并恢复系统输入法
            Close();
        }

        private void OnChestInventoryChanged(object? sender, ChestInventoryChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：当前场景任意箱内物品改动时标记刷新雷达匹配与词库优先级
            if (IsOpen && ReferenceEquals(e.Location, Game1.player?.currentLocation))
            {
                _namesDirty = true;
                _searchDirty = true;
            }
        }

        private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：当前场景摆放或拆除箱子时标记刷新雷达匹配与词库优先级
            if (IsOpen && ReferenceEquals(e.Location, Game1.player?.currentLocation))
            {
                _namesDirty = true;
                _searchDirty = true;
            }
        }

        private void Open()
        {
            // 与上文：OnButtonPressed 确认环境自由并按下雷达热键后调用
            // 作用：预热词库、懒加载顶部拼音搜索框并聚焦抑制系统输入法，开启雷达会话
            PinyinLexicon.Instance.EnsureGameVocabularyLoaded();

            if (_searchBox == null)
            {
                Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
                _searchBox = new InGamePinyinBox(textBoxTexture, Game1.smallFont, Game1.textColor);
            }

            _searchBox.ChineseMode = true;

            RefreshLocationItemNames(Game1.player?.currentLocation);
            _namesDirty = false;
            _searchDirty = false;
            _refreshTickCounter = 0;

            UpdateBarLayout();
            _searchBox.Clear(keepFocus: true);
            _lastSearchQuery = string.Empty;
            _matches.Clear();
            IsOpen = true;
            PlaySoundSafe("bigSelect");
        }

        private void Close()
        {
            // 独立逻辑，无上文
            // 作用：关闭雷达、无条件失焦搜索框并调用 NativeImeSuppressor.Restore 确保键盘输入完全释放
            IsOpen = false;
            if (_searchBox != null)
                _searchBox.Clear(keepFocus: false);

            NativeImeSuppressor.Restore();
            _matches.Clear();
            _locationItemNames.Clear();
            _lastSearchQuery = string.Empty;
            _searchDirty = false;
            _namesDirty = true;
            _refreshTickCounter = 0;
        }

        private void UpdateBarLayout()
        {
            // 独立逻辑，无上文
            // 作用：依据当前 UI 视口宽度将极简搜索栏居中定位在屏幕顶部并排版清空与关闭按钮
            int uiWidth = Game1.game1 != null ? Game1.uiViewport.Width : 1280;
            int barWidth = Math.Min(520, Math.Max(360, uiWidth - 40));
            const int barHeight = 76;
            int barX = (uiWidth - barWidth) / 2;
            const int barY = 18;

            _barBounds = new Rectangle(barX, barY, barWidth, barHeight);

            int closeSize = 42;
            _closeButtonBounds = new Rectangle(barX + barWidth - 14 - closeSize, barY + (barHeight - closeSize) / 2, closeSize, closeSize);

            int clearSize = 36;
            int modeWidth = 44;
            int boxX = barX + 16;
            int boxHeight = 48;
            int boxY = barY + (barHeight - boxHeight) / 2;
            int boxWidth = Math.Max(140, _closeButtonBounds.X - 12 - clearSize - 6 - modeWidth - 6 - boxX);

            if (_searchBox != null)
            {
                _searchBox.X = boxX;
                _searchBox.Y = boxY;
                _searchBox.Width = boxWidth;
                _searchBox.Height = boxHeight;
                _clearButtonBounds = new Rectangle(_searchBox.ModeButtonBounds.Right + 6, barY + (barHeight - clearSize) / 2, clearSize, clearSize);
            }
            else
            {
                _clearButtonBounds = new Rectangle(boxX + boxWidth + 6 + modeWidth + 6, barY + (barHeight - clearSize) / 2, clearSize, clearSize);
            }
        }

        private void SyncSearchResults(bool force)
        {
            // 与上文：搜索框文本或场景箱内库存发生变化后调用
            // 作用：在非空查询变动或强制刷新时更新同场景雷达命中集合，空查询立即清空标记
            string query = _searchBox?.EffectiveSearchQuery ?? string.Empty;
            if (!force && string.Equals(query, _lastSearchQuery, StringComparison.Ordinal))
                return;

            _lastSearchQuery = query;
            _matches.Clear();
            if (string.IsNullOrWhiteSpace(query))
                return;

            List<RadarChestMatch> found = SearchLocationChests(Game1.player?.currentLocation, query);
            _matches.AddRange(found);
        }

        private void RefreshLocationItemNames(GameLocation? location)
        {
            // 独立逻辑，无上文
            // 作用：采集当前场景全部合法玩家箱内的物品显示名称集合以提升拼音候选词置顶精度
            _locationItemNames.Clear();
            if (location?.objects == null)
                return;

            foreach (KeyValuePair<Vector2, StardewValley.Object> pair in location.objects.Pairs)
            {
                if (pair.Value is not Chest chest || !ChestFilter.IsLinkablePlayerChest(chest) || chest.Items == null)
                    continue;

                int capacity = GetChestCapacitySafe(chest);
                int limit = Math.Min(chest.Items.Count, Math.Max(0, capacity));
                for (int i = 0; i < limit; i++)
                {
                    Item? item = chest.Items[i];
                    if (item == null || item.Stack <= 0)
                        continue;

                    string displayName = ReadTextSafe(() => item.DisplayName);
                    if (!string.IsNullOrWhiteSpace(displayName))
                        _locationItemNames.Add(displayName);
                }
            }
        }

        private static void DrawTileGoldHighlight(SpriteBatch b, Vector2 tile, float pulseAlpha)
        {
            // 独立逻辑，无上文
            // 作用：在 UI 坐标系下沿命中物理箱所占地砖外框绘制呼吸闪烁金色高亮边框
            if (Game1.staminaRect == null)
                return;

            Vector2 topLeft = WorldBubbleRenderer.WorldToUiPosition(new Vector2(tile.X * 64f, tile.Y * 64f));
            Vector2 bottomRight = WorldBubbleRenderer.WorldToUiPosition(new Vector2((tile.X + 1f) * 64f, (tile.Y + 1f) * 64f));

            int x = (int)Math.Round(topLeft.X);
            int y = (int)Math.Round(topLeft.Y);
            int width = Math.Max(8, (int)Math.Round(bottomRight.X - topLeft.X));
            int height = Math.Max(8, (int)Math.Round(bottomRight.Y - topLeft.Y));
            const int border = 3;

            Color fillColor = new Color(255, 215, 64) * (0.20f * pulseAlpha);
            Color borderColor = new Color(255, 220, 70) * pulseAlpha;

            b.Draw(Game1.staminaRect, new Rectangle(x, y, width, height), fillColor);
            b.Draw(Game1.staminaRect, new Rectangle(x, y, width, border), borderColor);
            b.Draw(Game1.staminaRect, new Rectangle(x, y + height - border, width, border), borderColor);
            b.Draw(Game1.staminaRect, new Rectangle(x, y, border, height), borderColor);
            b.Draw(Game1.staminaRect, new Rectangle(x + width - border, y, border, height), borderColor);
        }

        private static RadarMatchItem? FindMergeTarget(List<RadarMatchItem> chestMatches, Item item, string displayName)
        {
            // 独立逻辑，无上文
            // 作用：在同一物理箱内按原版堆叠兼容性（或不可堆叠物品同 ID 品质名）查找可合并数量的匹配项
            for (int i = 0; i < chestMatches.Count; i++)
            {
                RadarMatchItem existing = chestMatches[i];
                if (existing.Proto.canStackWith(item))
                    return existing;

                if (item.maximumStackSize() <= 1 &&
                    string.Equals(existing.Proto.QualifiedItemId, item.QualifiedItemId, StringComparison.Ordinal) &&
                    existing.Proto.Quality == item.Quality &&
                    string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
                {
                    return existing;
                }
            }

            return null;
        }

        private static bool MatchesAllTokens(Item item, string[] tokens)
        {
            // 与上文：SearchLocationChests 在遍历每格有效物品时调用
            // 作用：以空格分词 AND 逻辑匹配展示名、内部名、分类名及汉语全拼与首字母简拼
            string displayName = ReadTextSafe(() => item.DisplayName);
            string internalName = ReadTextSafe(() => item.Name);
            string categoryName = ReadTextSafe(() => item.getCategoryName());

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                bool matched = displayName.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || internalName.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || categoryName.Contains(token, StringComparison.OrdinalIgnoreCase);

                if (!matched && TryExtractAsciiPinyinToken(token, out string cleanToken))
                {
                    matched = PinyinLexicon.Instance.MatchesPinyinOrInitials(displayName, cleanToken)
                        || PinyinLexicon.Instance.MatchesPinyinOrInitials(categoryName, cleanToken);
                }

                if (!matched)
                    return false;
            }

            return true;
        }

        private static bool TryExtractAsciiPinyinToken(string token, out string cleanToken)
        {
            // 独立逻辑，无上文
            // 作用：剔除拼音分词撇号并校验是否纯 ASCII 字母以决定是否启用拼音简拼检索
            if (string.IsNullOrEmpty(token))
            {
                cleanToken = string.Empty;
                return false;
            }

            var sb = new StringBuilder(token.Length);
            for (int i = 0; i < token.Length; i++)
            {
                char ch = token[i];
                if (ch == '\'')
                    continue;

                if (ch >= 'a' && ch <= 'z')
                {
                    sb.Append(ch);
                }
                else if (ch >= 'A' && ch <= 'Z')
                {
                    sb.Append((char)(ch - 'A' + 'a'));
                }
                else
                {
                    cleanToken = string.Empty;
                    return false;
                }
            }

            cleanToken = sb.ToString();
            return cleanToken.Length > 0;
        }

        private static string ReadItemDisplayName(Item item)
        {
            // 独立逻辑，无上文
            // 作用：安全提取物品展示名，缺省时回退内部名或限定 ID
            string displayName = ReadTextSafe(() => item.DisplayName);
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            string internalName = ReadTextSafe(() => item.Name);
            if (!string.IsNullOrWhiteSpace(internalName))
                return internalName;

            return item.QualifiedItemId ?? string.Empty;
        }

        private static string ReadTextSafe(Func<string?> getter)
        {
            // 独立逻辑，无上文
            // 作用：在未初始化游戏本地化环境的无头测试下安全读取物品文本属性
            try
            {
                return getter() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int GetChestCapacitySafe(Chest chest)
        {
            // 独立逻辑，无上文
            // 作用：安全获取箱子实际格子容量并在无头测试中提供箱型默认兜底
            try
            {
                int capacity = chest.GetActualCapacity();
                if (capacity > 0)
                    return capacity;
            }
            catch
            {
            }

            return chest.SpecialChestType == Chest.SpecialChestTypes.BigChest ? 70 : 36;
        }

        private static bool IsModifierOrBackspace(Keys key)
        {
            // 独立逻辑，无上文
            // 作用：放行修饰键与退格键以免阻断单按 Shift 切中英、组合热键与 Back 长按连删
            return key == Keys.Back
                || key == Keys.LeftShift
                || key == Keys.RightShift
                || key == Keys.LeftControl
                || key == Keys.RightControl
                || key == Keys.LeftAlt
                || key == Keys.RightAlt
                || key == Keys.CapsLock;
        }

        private static bool IsCtrlOrAltDown()
        {
            // 独立逻辑，无上文
            // 作用：安全判断当前是否按住 Ctrl 或 Alt 修饰键以放行系统复制粘贴等组合指令
            try
            {
                if (Game1.game1 == null || Game1.input == null)
                    return false;

                KeyboardState kb = Game1.input.GetKeyboardState();
                return kb.IsKeyDown(Keys.LeftControl)
                    || kb.IsKeyDown(Keys.RightControl)
                    || kb.IsKeyDown(Keys.LeftAlt)
                    || kb.IsKeyDown(Keys.RightAlt);
            }
            catch
            {
                return false;
            }
        }

        private static void PlaySoundSafe(string cueName)
        {
            // 独立逻辑，无上文
            // 作用：在音频系统就绪时安全播放原版界面反馈音效
            try
            {
                if (Game1.soundBank != null)
                    Game1.playSound(cueName);
            }
            catch
            {
            }
        }
    }
}
