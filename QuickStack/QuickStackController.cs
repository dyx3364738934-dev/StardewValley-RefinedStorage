using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace RefinedStorage.QuickStack
{
    public sealed class QuickStackController
    {
        private const double SingleSpriteDurationMs = 550.0;
        private const int ProbeIntervalTicks = 10;
        private const string ButtonHoverText = "一键整理堆叠到箱子";

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

        private readonly IModHelper? _helper;
        private readonly Func<RefinedStorageConfig> _getConfig;
        private readonly List<FlyingSprite> _flyingSprites = new List<FlyingSprite>();
        private ClickableTextureComponent? _hudButton;
        private bool _hasQuickStackableItems;
        private bool _probeDirty = true;
        private int _probeTickCounter;
        private float _buttonBounceScale = 1f;

        public bool IsSuppressedByExternalMod { get; set; }
        public int ActiveFlyingCount => _flyingSprites.Count;
        public bool HasQuickStackableItems => _hasQuickStackableItems;
        public bool IsHudButtonVisible =>
            !IsSuppressedByExternalMod &&
            _getConfig().EnableQuickStack &&
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
            // 作用：本地玩家背包内容变化时标记需要刷新可整理状态探测
            if (e.IsLocalPlayer)
                _probeDirty = true;
        }

        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：玩家跨场景传送时清空旧场景飞行精灵并标记重新探测新场景箱子
            if (e.IsLocalPlayer)
            {
                _flyingSprites.Clear();
                _probeDirty = true;
            }
        }

        private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：当前场景摆件增删（如放置或拆除箱子）时标记重新探测
            if (ReferenceEquals(e.Location, Game1.currentLocation))
                _probeDirty = true;
        }

        private void OnChestInventoryChanged(object? sender, ChestInventoryChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：当前场景箱内物品变化时标记重新探测以实时响应联机队友存取
            if (ReferenceEquals(e.Location, Game1.currentLocation))
                _probeDirty = true;
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：打开或关闭菜单后标记重新探测，及时同步菜单中手动存取后的按钮显隐
            _probeDirty = true;
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
                Rectangle buttonBounds = GetCurrentHudButtonBounds(out _);
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

            if (config.EnableFavoriteLock && e.Button == config.FavoriteKey)
                FavoriteLock.TryToggleAtCursor(e.Cursor.ScreenPixels);
        }

        private void RunQuickStack(RefinedStorageConfig config)
        {
            // 与上文：OnButtonPressed 捕获到 HUD 整理按钮左键点击后调用
            // 作用：执行一键堆叠并刷新探测缓存，按结果播放开箱音效、调度依次飞入按钮动画及分物品汇总提示
            Farmer? player = Game1.player;
            GameLocation? location = player?.currentLocation;
            if (player == null || location == null)
                return;

            QuickStackResult result = QuickStackService.Execute(player, location, config);
            if (!result.FoundAnyChest)
            {
                _hasQuickStackableItems = false;
                _probeDirty = false;
                if (config.ShowNotification)
                    ShowHudMessage("附近没有箱子", 2000f);
                return;
            }

            int totalMoved = result.StackedCount + result.DepositedCount;
            if (totalMoved > 0)
            {
                PlaySoundSafe("openChest");

                if (config.EnableQuickStackAnimation && result.ItemSummaries.Count > 0)
                    ScheduleFlyingAnimations(result.ItemSummaries, location);

                _hasQuickStackableItems = QuickStackService.HasQuickStackableItems(player, location, config);
                _probeDirty = false;

                if (config.ShowNotification)
                {
                    string summaryText = QuickStackService.FormatSummaryNotification(result.ItemSummaries);
                    if (!string.IsNullOrEmpty(summaryText))
                        ShowHudMessage(summaryText, 3500f);
                }

                return;
            }

            _hasQuickStackableItems = false;
            _probeDirty = false;
            if (config.ShowNotification)
                ShowHudMessage("没有可堆叠的物品", 2000f);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // 与上文：游戏主循环每 tick 触发，负责节流探测可整理状态与推进飞行精灵和回弹缩放
            // 作用：在脏标记或每 10 tick 刷新按钮显隐基准，并驱动已调度的飞行精灵沿贝塞尔弧线飞向按钮中心
            if (!Context.IsWorldReady || Game1.player?.currentLocation == null)
            {
                _hasQuickStackableItems = false;
                _flyingSprites.Clear();
                _probeDirty = true;
                _probeTickCounter = 0;
                _buttonBounceScale = 1f;
                return;
            }

            RefinedStorageConfig config = _getConfig();
            _probeTickCounter++;
            if (IsSuppressedByExternalMod || !config.EnableQuickStack)
            {
                _hasQuickStackableItems = false;
                _probeDirty = false;
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

            if (_buttonBounceScale > 1f)
                _buttonBounceScale = Math.Max(1f, _buttonBounceScale - 0.025f);

            if (_flyingSprites.Count == 0)
                return;

            double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0.0;
            Rectangle buttonBounds = GetCurrentHudButtonBounds(out _);
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
            // 作用：在非过场与无菜单状态下绘制物品栏左侧整理按钮、已起飞物品精灵与悬停提示文本
            if (!Context.IsWorldReady || Game1.player == null || !Game1.displayHUD ||
                Game1.activeClickableMenu != null || Game1.eventUp || Game1.farmEvent != null)
            {
                return;
            }

            if (!IsHudButtonVisible || Game1.mouseCursors == null)
                return;

            Rectangle bounds = GetCurrentHudButtonBounds(out float transparency);
            if (transparency <= 0f)
                return;

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

            if (bounds.Contains(mouseX, mouseY) && Game1.smallFont != null)
                IClickableMenu.drawHoverText(e.SpriteBatch, ButtonHoverText, Game1.smallFont);
        }

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：在未受外置 Mod 接管且启用收藏锁定时，于当前打开菜单的玩家背包槽位绘制收藏星标
            if (IsSuppressedByExternalMod || !Context.IsWorldReady)
                return;

            if (_getConfig().EnableFavoriteLock)
                FavoriteLock.DrawFavoriteStars(e.SpriteBatch);
        }

        private static Rectangle GetCurrentHudButtonBounds(out float transparency)
        {
            // 独立逻辑，无上文
            // 作用：读取实时 Toolbar 纵坐标与透明度并返回整理按钮的当前 UI 矩形区域
            if (Game1.onScreenMenus != null)
            {
                for (int i = 0; i < Game1.onScreenMenus.Count; i++)
                {
                    if (Game1.onScreenMenus[i] is Toolbar toolbar && toolbar.yPositionOnScreen > 0)
                    {
                        transparency = toolbar.transparency;
                        return ComputeHudButtonBounds(Game1.uiViewport.Width, toolbar.yPositionOnScreen);
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
            return ComputeHudButtonBounds(Game1.uiViewport.Width, fallbackToolbarY);
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
