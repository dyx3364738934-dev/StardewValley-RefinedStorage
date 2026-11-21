using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RefinedStorage.Storage;
using RefinedStorage.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace RefinedStorage.QuickStack
{
    public readonly struct HoverPreviewToken
    {
        public Item? Proto { get; }
        public string Text { get; }
        public Rectangle Bounds { get; }
        public int RowIndex { get; }
        public bool IsIcon => Proto != null;

        public HoverPreviewToken(Item? proto, string text, Rectangle bounds, int rowIndex)
        {
            // 独立逻辑，无上文
            // 作用：封装悬停视觉盒中单个文本段或物品真实纹理图标的内容、屏幕矩形与所属行号
            Proto = proto;
            Text = text ?? string.Empty;
            Bounds = bounds;
            RowIndex = rowIndex;
        }
    }

    public sealed class HoverPreviewBoxLayout
    {
        public Rectangle BoxBounds { get; }
        public IReadOnlyList<HoverPreviewToken> Tokens { get; }
        public int RowCount { get; }
        public int ShownItemCount { get; }
        public bool HasMoreEllipsis { get; }
        public bool FlippedBelow { get; }

        public HoverPreviewBoxLayout(
            Rectangle boxBounds,
            IReadOnlyList<HoverPreviewToken> tokens,
            int rowCount,
            int shownItemCount,
            bool hasMoreEllipsis,
            bool flippedBelow)
        {
            // 独立逻辑，无上文
            // 作用：保存 HUD 整理按钮视觉悬停盒的外框区域、全部绘制单元及换行翻转元数据
            BoxBounds = boxBounds;
            Tokens = tokens;
            RowCount = rowCount;
            ShownItemCount = shownItemCount;
            HasMoreEllipsis = hasMoreEllipsis;
            FlippedBelow = flippedBelow;
        }
    }

    public sealed class QuickStackController
    {
        private const double SingleSpriteDurationMs = 550.0;
        private const int ProbeIntervalTicks = 10;
        private const string ButtonHoverText = "一键堆叠";

        private sealed class FlyingSprite
        {
            public Item Proto { get; init; } = null!;
            public Vector2 StartPos { get; set; }
            public Vector2 CurrentPos { get; set; }
            public float Scale { get; set; } = 1f;
            public float Alpha { get; set; }
            public float Progress { get; set; }
            public bool HasLaunched { get; set; }
            public double StartTimeMs { get; init; }
            public GameLocation Location { get; init; } = null!;
            public double TotalDurationMs { get; init; }
        }

        private readonly struct RelativeTokenSpec
        {
            public Item? Proto { get; }
            public string Text { get; }
            public int X { get; }
            public int Width { get; }

            public RelativeTokenSpec(Item? proto, string text, int x, int width)
            {
                // 独立逻辑，无上文
                // 作用：记录悬停盒单行排版阶段的相对横坐标与绘制内容
                Proto = proto;
                Text = text;
                X = x;
                Width = width;
            }
        }

        private readonly IModHelper? _helper;
        private readonly Func<RefinedStorageConfig> _getConfig;
        private readonly List<FlyingSprite> _flyingSprites = new List<FlyingSprite>();
        private ClickableTextureComponent? _hudButton;
        private bool _hasQuickStackableItems;
        private bool _probeDirty = true;
        private int _probeTickCounter;
        private float _buttonBounceScale = 1f;
        private FinalStackPlan? _cachedHoverPlan;
        private bool _hoverPlanDirty = true;
        private GameLocation? _hoverPlanLocation;
        private Vector2 _hoverPlanPlayerPos;
        private string? _hoverPlanConfigFingerprint;

        public bool IsSuppressedByExternalMod { get; set; }
        public bool IsHoveringHudButton { get; private set; }
        public int ActiveFlyingCount => _flyingSprites.Count;
        public bool HasQuickStackableItems => _hasQuickStackableItems;
        public bool IsHudButtonVisible =>
            !IsSuppressedByExternalMod &&
            (_hasQuickStackableItems || _flyingSprites.Count > 0);

        public QuickStackController(IModHelper helper, Func<RefinedStorageConfig> getConfig)
            : this(getConfig)
        {
            // 独立逻辑，无上文
            // 作用：向 SMAPI 注册输入、帧更新、HUD/菜单渲染以及触发可堆叠探测脏标记的世界与背包事件
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.World.ObjectListChanged += OnObjectListChanged;
            helper.Events.World.ChestInventoryChanged += OnChestInventoryChanged;
            helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        public QuickStackController(Func<RefinedStorageConfig> getConfig)
        {
            // 独立逻辑，无上文
            // 作用：保存配置读取委托以支持运行时装配与无 SMAPI 事件总线的无头单元测试
            _getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
        }

        public static Rectangle ComputeHudButtonBounds(int uiViewportWidth, int toolbarY)
        {
            // 独立逻辑，无上文
            // 作用：计算位于原版物品栏左侧 16 像素处 64x64 整理按钮的 UI 坐标边界并随工具栏上下翻转联动
            return new Rectangle(uiViewportWidth / 2 - 480, toolbarY - 88, 64, 64);
        }

        public static HoverPreviewBoxLayout ComputeHoverPreviewBoxLayout(
            IReadOnlyList<QuickStackItemSummary>? summaries,
            Rectangle buttonBounds,
            int uiViewportWidth,
            int uiViewportHeight,
            Func<string, Vector2>? measureText = null)
        {
            // 独立逻辑，无上文
            // 作用：按“存入 [物品真实纹理]x个数 / ...”排版最多 8 种物品并自动换行（<=4 项且 <=可用行宽），定位并夹紧按钮上/下方 12px 悬停盒
            if (summaries == null || summaries.Count == 0)
            {
                return new HoverPreviewBoxLayout(Rectangle.Empty, Array.Empty<HoverPreviewToken>(), 0, 0, false, false);
            }

            var valid = new List<QuickStackItemSummary>(summaries.Count);
            for (int i = 0; i < summaries.Count; i++)
            {
                QuickStackItemSummary summary = summaries[i];
                if (summary?.Proto != null && summary.TotalAmount > 0)
                    valid.Add(summary);
            }

            if (valid.Count == 0)
            {
                return new HoverPreviewBoxLayout(Rectangle.Empty, Array.Empty<HoverPreviewToken>(), 0, 0, false, false);
            }

            const int maxShownItems = 8;
            const int maxItemsPerRow = 4;
            const int boxPadX = 18;
            const int boxPadY = 14;
            const int screenMargin = 8;
            const int gapFromButton = 12;
            const int iconSize = 42;
            const int rowGap = 6;
            const int prefixGap = 6;
            const int countGap = 2;
            const int slashGap = 6;

            int shownCount = Math.Min(maxShownItems, valid.Count);
            bool hasMore = valid.Count > maxShownItems;

            Func<string, Vector2> measure = measureText ?? MeasureSmallTextSafe;
            float lineHeight = Math.Max(24f, measure("存入x0123456789/").Y);
            int rowHeight = Math.Max(iconSize, (int)Math.Ceiling(lineHeight));

            int maxBoxWidth = Math.Max(120, Math.Min(700, uiViewportWidth - 32));
            int maxRowWidth = Math.Max(60, maxBoxWidth - boxPadX * 2);

            int prefixW = (int)Math.Ceiling(measure("存入").X);
            int slashW = (int)Math.Ceiling(measure("/").X);
            int ellipsisW = hasMore ? (int)Math.Ceiling(measure("/...").X) : 0;

            var rows = new List<List<RelativeTokenSpec>>();
            var currentRow = new List<RelativeTokenSpec>();
            int cursorX = 0;
            int itemsInRow = 0;

            currentRow.Add(new RelativeTokenSpec(null, "存入", cursorX, prefixW));
            cursorX += prefixW + prefixGap;

            for (int i = 0; i < shownCount; i++)
            {
                QuickStackItemSummary item = valid[i];
                string countText = $"x{item.TotalAmount}";
                int countW = (int)Math.Ceiling(measure(countText).X);
                int sepNeed = i > 0 ? slashW + slashGap * 2 : 0;
                int unitW = sepNeed + iconSize + countGap + countW;

                if (itemsInRow > 0 && (itemsInRow >= maxItemsPerRow || cursorX + unitW > maxRowWidth))
                {
                    rows.Add(currentRow);
                    currentRow = new List<RelativeTokenSpec>();
                    cursorX = 0;
                    itemsInRow = 0;
                }

                if (i > 0)
                {
                    if (itemsInRow > 0)
                        cursorX += slashGap;
                    currentRow.Add(new RelativeTokenSpec(null, "/", cursorX, slashW));
                    cursorX += slashW + slashGap;
                }

                currentRow.Add(new RelativeTokenSpec(item.Proto, string.Empty, cursorX, iconSize));
                cursorX += iconSize + countGap;
                currentRow.Add(new RelativeTokenSpec(null, countText, cursorX, countW));
                cursorX += countW;
                itemsInRow++;
            }

            if (hasMore)
            {
                if (itemsInRow > 0 && cursorX + slashGap + ellipsisW > maxRowWidth)
                {
                    rows.Add(currentRow);
                    currentRow = new List<RelativeTokenSpec>();
                    cursorX = 0;
                }
                else if (itemsInRow > 0)
                {
                    cursorX += slashGap;
                }

                currentRow.Add(new RelativeTokenSpec(null, "/...", cursorX, ellipsisW));
            }

            if (currentRow.Count > 0)
                rows.Add(currentRow);

            int maxObservedWidth = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                List<RelativeTokenSpec> row = rows[r];
                if (row.Count == 0)
                    continue;
                RelativeTokenSpec last = row[row.Count - 1];
                int rowEnd = last.X + last.Width;
                if (rowEnd > maxObservedWidth)
                    maxObservedWidth = rowEnd;
            }

            int boxWidth = Math.Max(80, maxObservedWidth + boxPadX * 2);
            int boxHeight = boxPadY * 2 + rows.Count * rowHeight + Math.Max(0, rows.Count - 1) * rowGap;

            int maxX = Math.Max(screenMargin, uiViewportWidth - boxWidth - screenMargin);
            int boxX = Math.Clamp(buttonBounds.Center.X - boxWidth / 2, screenMargin, maxX);

            int maxY = Math.Max(screenMargin, uiViewportHeight - boxHeight - screenMargin);
            int yAbove = buttonBounds.Y - boxHeight - gapFromButton;
            int boxY;
            bool flippedBelow;
            if (yAbove < screenMargin)
            {
                flippedBelow = true;
                int yBelow = buttonBounds.Bottom + gapFromButton;
                boxY = Math.Clamp(yBelow, screenMargin, maxY);
            }
            else
            {
                flippedBelow = false;
                boxY = Math.Clamp(yAbove, screenMargin, maxY);
            }

            Rectangle boxBounds = new Rectangle(boxX, boxY, boxWidth, boxHeight);
            var tokens = new List<HoverPreviewToken>();
            for (int r = 0; r < rows.Count; r++)
            {
                int rowY = boxY + boxPadY + r * (rowHeight + rowGap);
                List<RelativeTokenSpec> row = rows[r];
                for (int t = 0; t < row.Count; t++)
                {
                    RelativeTokenSpec spec = row[t];
                    Rectangle tokenBounds = new Rectangle(boxX + boxPadX + spec.X, rowY, spec.Width, rowHeight);
                    tokens.Add(new HoverPreviewToken(spec.Proto, spec.Text, tokenBounds, r));
                }
            }

            return new HoverPreviewBoxLayout(boxBounds, tokens, rows.Count, shownCount, hasMore, flippedBelow);
        }

        public void ScheduleFlyingAnimations(
            IReadOnlyList<QuickStackItemSummary>? summaries,
            GameLocation? location,
            double? baseTimeMs = null)
        {
            // 独立逻辑，无上文
            // 作用：按汇总物品种类依次生成错峰起飞、时长 550ms 的独立飞行精灵副本队列
            if (summaries == null || summaries.Count == 0 || location == null)
                return;

            var valid = new List<QuickStackItemSummary>(summaries.Count);
            for (int i = 0; i < summaries.Count; i++)
            {
                QuickStackItemSummary summary = summaries[i];
                if (summary?.Proto != null && summary.TotalAmount > 0)
                    valid.Add(summary);
            }

            if (valid.Count == 0)
                return;

            double baseTime = baseTimeMs ?? (Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0.0);
            int staggerMs = valid.Count <= 1 ? 0 : Math.Min(130, Math.Max(45, 1600 / valid.Count));

            for (int i = 0; i < valid.Count; i++)
            {
                QuickStackItemSummary summary = valid[i];
                Item spriteProto = summary.Proto.getOne();
                spriteProto.Stack = summary.TotalAmount;

                _flyingSprites.Add(new FlyingSprite
                {
                    Proto = spriteProto,
                    StartTimeMs = baseTime + (double)i * staggerMs,
                    Location = location,
                    TotalDurationMs = SingleSpriteDurationMs
                });
            }
        }

        public IReadOnlyList<double> GetFlyingStartTimesMs()
        {
            // 与上文：在 ScheduleFlyingAnimations 入队飞行精灵后供测试检查起飞时间序列
            // 作用：返回当前队列中全部飞行精灵的计划起飞毫秒时间戳只读副本
            var times = new double[_flyingSprites.Count];
            for (int i = 0; i < _flyingSprites.Count; i++)
                times[i] = _flyingSprites[i].StartTimeMs;
            return times;
        }

        private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：本地玩家背包内容变化时标记需要刷新可整理状态探测并失效悬停终态预览缓存
            if (e.IsLocalPlayer)
            {
                _probeDirty = true;
                InvalidateHoverPreviewCache();
            }
        }

        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：玩家跨场景传送时清空旧场景飞行精灵与终态预览缓存并标记重新探测新场景箱子
            if (e.IsLocalPlayer)
            {
                _flyingSprites.Clear();
                _probeDirty = true;
                IsHoveringHudButton = false;
                InvalidateHoverPreviewCache();
            }
        }

        private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：当前场景摆件增删（如放置或拆除箱子）时标记重新探测并失效悬停预览缓存
            if (ReferenceEquals(e.Location, Game1.currentLocation))
            {
                _probeDirty = true;
                InvalidateHoverPreviewCache();
            }
        }

        private void OnChestInventoryChanged(object? sender, ChestInventoryChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：当前场景箱内物品变化时标记重新探测并失效悬停预览缓存以实时响应联机队友存取
            if (ReferenceEquals(e.Location, Game1.currentLocation))
            {
                _probeDirty = true;
                InvalidateHoverPreviewCache();
            }
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：打开或关闭菜单后标记重新探测并失效悬停预览缓存，及时同步菜单中手动存取后的状态
            _probeDirty = true;
            IsHoveringHudButton = false;
            InvalidateHoverPreviewCache();
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            // 与上文：由 SMAPI 输入事件触发，先校验未被旧版外置 Mod 接管且世界已就绪
            // 作用：仅在自由移动且 HUD 可见时拦截左键点击整理按钮并执行堆叠，菜单中保留收藏锁定按键
            if (IsSuppressedByExternalMod || !Context.IsWorldReady)
                return;

            RefinedStorageConfig config = _getConfig();
            if (e.Button == SButton.MouseLeft &&
                IsHudButtonVisible &&
                Context.IsPlayerFree &&
                Game1.displayHUD &&
                Game1.activeClickableMenu == null &&
                !Game1.eventUp &&
                Game1.farmEvent == null)
            {
                Rectangle buttonBounds = GetCurrentHudButtonBounds(out _, out _);
                int mouseX = Game1.getMouseX(ui_scale: true);
                int mouseY = Game1.getMouseY(ui_scale: true);
                if (buttonBounds.Contains(mouseX, mouseY))
                {
                    _helper?.Input.Suppress(e.Button);
                    RunQuickStack(config);
                    _probeDirty = true;
                    return;
                }
            }

            if (e.Button == config.FavoriteKey &&
                FavoriteLock.TryToggleAtCursor(e.Cursor.ScreenPixels))
            {
                _probeDirty = true;
                InvalidateHoverPreviewCache();
            }
        }

        private void RunQuickStack(RefinedStorageConfig config)
        {
            // 与上文：OnButtonPressed 捕获到 HUD 整理按钮左键点击后调用
            // 作用：通过位置串行队列执行一键堆叠并立即失效预览与探测缓存，仅在真实转移时播音效、飞动画并显示分物品汇总提示
            Farmer? player = Game1.player;
            GameLocation? location = player?.currentLocation;
            if (player == null || location == null)
                return;

            InvalidateHoverPreviewCache();
            QuickStackResult result = StorageOpQueue.EnqueueOrExecuteQuickStack(player, location, config);
            InvalidateHoverPreviewCache();

            int totalMoved = result.StackedCount + result.DepositedCount;
            if (result.FoundAnyChest && totalMoved > 0)
            {
                PlaySoundSafe("openChest");

                if (result.ItemSummaries.Count > 0)
                    ScheduleFlyingAnimations(result.ItemSummaries, location);

                _hasQuickStackableItems = QuickStackService.HasQuickStackableItems(player, location, config);
                _probeDirty = false;

                string summaryText = QuickStackService.FormatSummaryNotification(result.ItemSummaries);
                if (!string.IsNullOrEmpty(summaryText))
                    ShowHudMessage(summaryText, 3500f);

                return;
            }

            _hasQuickStackableItems = false;
            _probeDirty = false;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // 与上文：游戏主循环每 tick 触发，负责节流探测可整理状态与推进飞行精灵和回弹缩放
            // 作用：在脏标记或每 10 tick 刷新按钮显隐基准，同步悬停互锁态并驱动飞行精灵沿贝塞尔弧线飞入按钮
            if (!Context.IsWorldReady || Game1.player?.currentLocation == null)
            {
                _hasQuickStackableItems = false;
                _flyingSprites.Clear();
                _probeDirty = true;
                _probeTickCounter = 0;
                _buttonBounceScale = 1f;
                IsHoveringHudButton = false;
                InvalidateHoverPreviewCache();
                return;
            }

            RefinedStorageConfig config = _getConfig();
            _probeTickCounter++;
            if (IsSuppressedByExternalMod)
            {
                _hasQuickStackableItems = false;
                _probeDirty = false;
                IsHoveringHudButton = false;
            }
            else if (_probeDirty || _probeTickCounter >= ProbeIntervalTicks)
            {
                _probeTickCounter = 0;
                _probeDirty = false;
                _hasQuickStackableItems = QuickStackService.HasQuickStackableItems(
                    Game1.player,
                    Game1.player.currentLocation,
                    config);
            }

            if (!IsHudButtonVisible || !Context.IsPlayerFree)
                IsHoveringHudButton = false;

            if (_buttonBounceScale > 1f)
                _buttonBounceScale = Math.Max(1f, _buttonBounceScale - 0.025f);

            if (_flyingSprites.Count == 0)
                return;

            double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0.0;
            Rectangle buttonBounds = GetCurrentHudButtonBounds(out _, out _);
            Vector2 targetCenter = new Vector2(buttonBounds.Center.X, buttonBounds.Center.Y);
            UpdateFlyingSprites(now, Game1.player, targetCenter);
        }

        private void UpdateFlyingSprites(double now, Farmer player, Vector2 targetCenter)
        {
            // 与上文：OnUpdateTicked 已锁定当前时间与目标整理按钮屏幕 UI 中心点
            // 作用：在起飞瞬间捕获玩家屏幕躯干坐标并播音效，随后按向上二阶贝塞尔弧线与末尾缩飞入按钮并触发落地回弹
            GameLocation currentLocation = player.currentLocation;
            bool playedLaunchSound = false;

            for (int i = _flyingSprites.Count - 1; i >= 0; i--)
            {
                FlyingSprite fi = _flyingSprites[i];
                if (!ReferenceEquals(fi.Location, currentLocation))
                {
                    _flyingSprites.RemoveAt(i);
                    continue;
                }

                double elapsed = now - fi.StartTimeMs;
                if (elapsed < 0)
                    continue;

                if (!fi.HasLaunched)
                {
                    fi.HasLaunched = true;
                    fi.StartPos = CapturePlayerTorsoUiPos(player);
                    fi.CurrentPos = fi.StartPos;
                    if (!playedLaunchSound)
                    {
                        playedLaunchSound = true;
                        PlaySoundSafe("shwip");
                    }
                }

                fi.Progress = (float)Math.Min(1.0, elapsed / fi.TotalDurationMs);
                float eased = EaseOutCubic(fi.Progress);

                float dist = Vector2.Distance(fi.StartPos, targetCenter);
                float arcLift = Math.Clamp(dist * 0.28f, 48f, 180f);
                float controlY = Math.Max(16f, Math.Min(fi.StartPos.Y, targetCenter.Y) - arcLift);
                Vector2 controlPos = new Vector2((fi.StartPos.X + targetCenter.X) * 0.5f, controlY);
                float inv = 1f - eased;
                fi.CurrentPos = (inv * inv) * fi.StartPos
                    + (2f * inv * eased) * controlPos
                    + (eased * eased) * targetCenter;

                if (fi.Progress < 0.12f)
                {
                    float t = fi.Progress / 0.12f;
                    fi.Alpha = t;
                    fi.Scale = MathHelper.Lerp(0.75f, 1.05f, t);
                }
                else if (fi.Progress < 0.65f)
                {
                    float t = (fi.Progress - 0.12f) / 0.53f;
                    fi.Alpha = 1f;
                    fi.Scale = MathHelper.Lerp(1.05f, 1f, t);
                }
                else
                {
                    float t = (fi.Progress - 0.65f) / 0.35f;
                    fi.Alpha = MathHelper.Lerp(1f, 0.85f, t);
                    fi.Scale = MathHelper.Lerp(1f, 0.42f, t);
                }

                if (fi.Progress >= 1f)
                {
                    _flyingSprites.RemoveAt(i);
                    _buttonBounceScale = 1.24f;
                    if (_flyingSprites.Count == 0)
                        _probeDirty = true;
                }
            }
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            // 与上文：SMAPI 在原版 HUD 绘制后触发本图层事件
            // 作用：在自由状态绘制整理按钮、飞行精灵，以及悬停时按缓存推演渲染 80x96 世界气泡与纯视觉悬停盒
            if (!Context.IsWorldReady || Game1.player == null || !Game1.displayHUD ||
                Game1.activeClickableMenu != null || Game1.eventUp || Game1.farmEvent != null)
            {
                IsHoveringHudButton = false;
                return;
            }

            if (!IsHudButtonVisible || Game1.mouseCursors == null)
            {
                IsHoveringHudButton = false;
                return;
            }

            Rectangle bounds = GetCurrentHudButtonBounds(out float transparency, out Rectangle toolbarBounds);
            if (transparency <= 0f)
            {
                IsHoveringHudButton = false;
                return;
            }

            _hudButton ??= new ClickableTextureComponent(
                "RefinedStorage.QuickStackHudButton",
                bounds,
                string.Empty,
                ButtonHoverText,
                Game1.mouseCursors,
                new Rectangle(103, 469, 16, 16),
                4f);
            _hudButton.bounds = bounds;

            int mouseX = Game1.getMouseX(ui_scale: true);
            int mouseY = Game1.getMouseY(ui_scale: true);
            _hudButton.tryHover(mouseX, mouseY, 0.25f);

            float savedScale = _hudButton.scale;
            _hudButton.scale = savedScale * _buttonBounceScale;
            _hudButton.draw(e.SpriteBatch, Color.White * transparency, 0.86f);
            _hudButton.scale = savedScale;

            GameLocation? currentLocation = Game1.player.currentLocation;
            for (int i = 0; i < _flyingSprites.Count; i++)
            {
                FlyingSprite fi = _flyingSprites[i];
                if (!fi.HasLaunched || !ReferenceEquals(fi.Location, currentLocation))
                    continue;

                Vector2 drawPos = fi.CurrentPos - new Vector2(32f, 32f);
                fi.Proto.drawInMenu(
                    e.SpriteBatch,
                    drawPos,
                    fi.Scale,
                    fi.Alpha * transparency,
                    0.9f,
                    StackDrawType.Draw,
                    Color.White * (fi.Alpha * transparency),
                    drawShadow: false);
            }

            bool hovering = Context.IsPlayerFree && bounds.Contains(mouseX, mouseY);
            IsHoveringHudButton = hovering;
            if (!hovering)
            {
                if (_cachedHoverPlan != null)
                    InvalidateHoverPreviewCache();
                return;
            }

            // 与上文：已确认玩家处于自由态且鼠标正悬停在整理按钮矩形内
            // 作用：按缓存读取终态落点计划，绘制屏内避让工具栏的 80x96 气泡并在按钮旁绘制视觉悬停盒，无计划时仅显示简短按钮名称
            RefinedStorageConfig config = _getConfig();
            bool drewVisualBox = false;
            if (currentLocation != null)
            {
                FinalStackPlan? plan = GetOrComputeHoverPlan(Game1.player, currentLocation, config);
                if (plan != null && plan.HasAnyTransfer && plan.ChestPlans.Count > 0)
                {
                    double timeMs = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0.0;
                    int uiWidth = Game1.uiViewport.Width;
                    int uiHeight = Game1.uiViewport.Height;

                    for (int i = 0; i < plan.ChestPlans.Count; i++)
                    {
                        ChestIncomingPlan chestPlan = plan.ChestPlans[i];
                        if (chestPlan?.Items == null || chestPlan.Items.Count == 0)
                            continue;

                        if (!WorldBubbleRenderer.IsChestVisibleInViewport(chestPlan.Tile, uiWidth, uiHeight, 24f, 24f))
                            continue;

                        Vector2 anchorUi = WorldBubbleRenderer.GetChestBubbleAnchorUiPos(chestPlan.Tile);
                        Rectangle bubbleBounds = WorldBubbleRenderer.ComputeBubbleBounds(anchorUi, timeMs);
                        if (bubbleBounds.Intersects(toolbarBounds) || bubbleBounds.Intersects(bounds))
                            continue;

                        var entries = new List<BubbleItemEntry>(chestPlan.Items.Count);
                        for (int j = 0; j < chestPlan.Items.Count; j++)
                        {
                            ChestIncomingItem incoming = chestPlan.Items[j];
                            if (incoming?.Proto != null && incoming.AddedAmount > 0)
                                entries.Add(new BubbleItemEntry(incoming.Proto, incoming.AddedAmount, "+"));
                        }

                        if (entries.Count > 0)
                            WorldBubbleRenderer.DrawChestItemBubble(e.SpriteBatch, anchorUi, entries, timeMs, transparency);
                    }

                    HoverPreviewBoxLayout boxLayout = ComputeHoverPreviewBoxLayout(plan.ItemSummaries, bounds, uiWidth, uiHeight);
                    if (boxLayout.Tokens.Count > 0)
                    {
                        DrawHoverPreviewBox(e.SpriteBatch, boxLayout, transparency);
                        drewVisualBox = true;
                    }
                }
            }

            if (!drewVisualBox && Game1.smallFont != null)
                IClickableMenu.drawHoverText(e.SpriteBatch, ButtonHoverText, Game1.smallFont);
        }

        private FinalStackPlan? GetOrComputeHoverPlan(Farmer player, GameLocation location, RefinedStorageConfig config)
        {
            // 与上文：OnRenderedHud 在确认鼠标悬停于整理按钮后调用
            // 作用：仅在脏标记失效、玩家位移或 StackToWholeRoom 配置变化时调用 SimulateFinalPlan 推演并缓存终态落点
            string fingerprint = config.StackToWholeRoom.ToString();

            bool moved = Vector2.DistanceSquared(_hoverPlanPlayerPos, player.Position) > 4f;
            if (_hoverPlanDirty ||
                _cachedHoverPlan == null ||
                !ReferenceEquals(_hoverPlanLocation, location) ||
                moved ||
                !string.Equals(_hoverPlanConfigFingerprint, fingerprint, StringComparison.Ordinal))
            {
                _cachedHoverPlan = QuickStackService.SimulateFinalPlan(player, location, config);
                _hoverPlanLocation = location;
                _hoverPlanPlayerPos = player.Position;
                _hoverPlanConfigFingerprint = fingerprint;
                _hoverPlanDirty = false;
            }

            return _cachedHoverPlan;
        }

        private void InvalidateHoverPreviewCache()
        {
            // 独立逻辑，无上文
            // 作用：清零并失效一键堆叠终态气泡预览缓存
            _hoverPlanDirty = true;
            _cachedHoverPlan = null;
            _hoverPlanLocation = null;
            _hoverPlanConfigFingerprint = null;
        }

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：在未受外置 Mod 接管时，于当前打开菜单的玩家背包槽位绘制收藏星标
            if (IsSuppressedByExternalMod || !Context.IsWorldReady)
                return;

            FavoriteLock.DrawFavoriteStars(e.SpriteBatch);
        }

        private static void DrawHoverPreviewBox(SpriteBatch b, HoverPreviewBoxLayout layout, float alpha)
        {
            // 与上文：OnRenderedHud 已计算完成 HoverPreviewBoxLayout 且确认存在至少一项可转移物品
            // 作用：以原版木框绘制“存入 [物品真实纹理]x个数 / ...”视觉悬停盒
            if (b == null || layout == null || layout.Tokens.Count == 0 || layout.BoxBounds.IsEmpty)
                return;

            Rectangle box = layout.BoxBounds;
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                box.X,
                box.Y,
                box.Width,
                box.Height,
                Color.White * alpha,
                scale: 1f,
                drawShadow: true);

            for (int i = 0; i < layout.Tokens.Count; i++)
            {
                HoverPreviewToken token = layout.Tokens[i];
                if (token.IsIcon && token.Proto != null)
                {
                    Vector2 center = new Vector2(
                        token.Bounds.X + token.Bounds.Width * 0.5f,
                        token.Bounds.Y + token.Bounds.Height * 0.5f);
                    Vector2 drawPos = center - new Vector2(32f, 32f);
                    token.Proto.drawInMenu(
                        b,
                        drawPos,
                        0.64f,
                        alpha,
                        0.92f,
                        StackDrawType.HideButShowQuality,
                        Color.White * alpha,
                        drawShadow: false);
                }
                else if (!string.IsNullOrEmpty(token.Text) && Game1.smallFont != null)
                {
                    Vector2 textSize = MeasureSmallTextSafe(token.Text);
                    float textY = token.Bounds.Y + (token.Bounds.Height - textSize.Y) * 0.5f + 1f;
                    Utility.drawTextWithShadow(
                        b,
                        token.Text,
                        Game1.smallFont,
                        new Vector2(token.Bounds.X, textY),
                        Game1.textColor * alpha,
                        scale: 1f,
                        layerDepth: 0.92f);
                }
            }
        }

        private static Vector2 MeasureSmallTextSafe(string text)
        {
            // 独立逻辑，无上文
            // 作用：优先使用 Game1.smallFont 测量文本尺寸，无头环境字体未就绪时按中英文字符宽估算
            if (string.IsNullOrEmpty(text))
                return Vector2.Zero;

            try
            {
                if (Game1.smallFont != null)
                    return Game1.smallFont.MeasureString(text);
            }
            catch
            {
            }

            float width = 0f;
            for (int i = 0; i < text.Length; i++)
                width += text[i] > 127 ? 24f : 13f;
            return new Vector2(width, 28f);
        }

        private static Rectangle GetCurrentHudButtonBounds(out float transparency, out Rectangle toolbarBounds)
        {
            // 独立逻辑，无上文
            // 作用：读取实时 Toolbar 纵坐标、透明度与外框矩形，并返回整理按钮的当前 UI 矩形区域
            int uiWidth = Game1.uiViewport.Width;
            if (Game1.onScreenMenus != null)
            {
                for (int i = 0; i < Game1.onScreenMenus.Count; i++)
                {
                    if (Game1.onScreenMenus[i] is Toolbar toolbar && toolbar.yPositionOnScreen > 0)
                    {
                        transparency = toolbar.transparency;
                        toolbarBounds = ComputeToolbarVisualBounds(toolbar, uiWidth, toolbar.yPositionOnScreen);
                        return ComputeHudButtonBounds(uiWidth, toolbar.yPositionOnScreen);
                    }
                }
            }

            int safeMarginY = 0;
            try
            {
                safeMarginY = Utility.makeSafeMarginY(8);
            }
            catch
            {
            }

            transparency = 1f;
            int fallbackToolbarY = Game1.uiViewport.Height + 8 - safeMarginY;
            toolbarBounds = new Rectangle(uiWidth / 2 - 416, fallbackToolbarY - 104, 832, 108);
            return ComputeHudButtonBounds(uiWidth, fallbackToolbarY);
        }

        private static Rectangle ComputeToolbarVisualBounds(Toolbar toolbar, int uiWidth, int toolbarY)
        {
            // 独立逻辑，无上文
            // 作用：根据原版 Toolbar 按钮槽位合集计算工具栏木框占用的屏幕避让矩形
            if (toolbar.buttons != null && toolbar.buttons.Count > 0)
            {
                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int maxR = int.MinValue;
                int maxB = int.MinValue;
                for (int i = 0; i < toolbar.buttons.Count; i++)
                {
                    ClickableComponent? btn = toolbar.buttons[i];
                    if (btn == null)
                        continue;
                    if (btn.bounds.X < minX) minX = btn.bounds.X;
                    if (btn.bounds.Y < minY) minY = btn.bounds.Y;
                    if (btn.bounds.Right > maxR) maxR = btn.bounds.Right;
                    if (btn.bounds.Bottom > maxB) maxB = btn.bounds.Bottom;
                }

                if (minX < maxR && minY < maxB)
                    return new Rectangle(minX - 24, minY - 24, (maxR - minX) + 48, (maxB - minY) + 48);
            }

            return new Rectangle(uiWidth / 2 - 416, toolbarY - 104, 832, 108);
        }

        private static Vector2 CapturePlayerTorsoUiPos(Farmer player)
        {
            // 独立逻辑，无上文
            // 作用：将玩家实时世界躯干位置映射为与 RenderedHud 一致的 UI 缩放屏幕坐标
            Point standing = player.StandingPixel;
            Vector2 localTorso = Game1.GlobalToLocal(Game1.viewport, new Vector2(standing.X, standing.Y - 32f));
            return Game1.options != null ? Utility.ModifyCoordinatesForUIScale(localTorso) : localTorso;
        }

        private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

        private static void PlaySoundSafe(string cueName)
        {
            // 独立逻辑，无上文
            // 作用：在无头或音效上下文未就绪时安全屏蔽播放异常
            try
            {
                Game1.playSound(cueName);
            }
            catch
            {
            }
        }

        private static void ShowHudMessage(string message, float timeLeft)
        {
            // 独立逻辑，无上文
            // 作用：以无图标样式与指定停留毫秒数安全弹出游戏左下角 HUD 提示消息
            try
            {
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type)
                {
                    noIcon = true,
                    timeLeft = timeLeft
                });
            }
            catch
            {
            }
        }
    }
}
