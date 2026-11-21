using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RefinedStorage.Storage;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Objects;

namespace RefinedStorage.UI
{
    public sealed class ChestHoverPreviewController
    {
        public const int CellSize = 60;
        public const int MinCellSize = 40;
        public const int PanelPadding = 20;
        public const int EmptyBubbleWidth = 128;
        public const int EmptyBubbleHeight = 72;
        private const int ScreenMargin = 8;
        private const int VerticalGapAboveChest = 18;
        private const int VerticalOffsetBelowChest = 140;
        private const int MaxSilhouetteCacheEntries = 64;

        private static readonly Dictionary<(Texture2D Source, Rectangle BodyRect, Rectangle LidRect), Texture2D> _silhouetteCache =
            new Dictionary<(Texture2D Source, Rectangle BodyRect, Rectangle LidRect), Texture2D>();
        private static Chest? _activeHoveredChest;

        private readonly IModHelper? _helper;
        private readonly Func<RefinedStorageConfig> _getConfig;
        private readonly Func<bool>? _isSuppressedByQuickStackHover;
        private readonly Func<bool>? _isSuppressedByRadar;

        private Chest? _hoveredChest;
        private Vector2 _hoveredTile;
        private bool _isBubbleVisible;

        public ChestHoverPreviewController()
            : this(() => new RefinedStorageConfig())
        {
            // 独立逻辑，无上文
            // 作用：提供无参构造以便无头单元测试直接实例化控制器
        }

        public ChestHoverPreviewController(Func<RefinedStorageConfig> getConfig)
        {
            // 独立逻辑，无上文
            // 作用：保存配置读取委托以支持无 SMAPI 事件总线的无头测试装配
            _getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
        }

        public ChestHoverPreviewController(
            IModHelper helper,
            Func<RefinedStorageConfig> getConfig,
            Func<bool>? isSuppressedByQuickStackHover = null,
            Func<bool>? isSuppressedByRadar = null)
            : this(getConfig)
        {
            // 与上文：先通过基础构造保存配置委托，再接入 SMAPI 生命周期与抑制回调
            // 作用：订阅帧更新、HUD 气泡渲染、跨场景传送、菜单变更与退回标题事件以驱动 0ms 地上箱悬停与纹理缓存清理
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));
            _isSuppressedByQuickStackHover = isSuppressedByQuickStackHover;
            _isSuppressedByRadar = isSuppressedByRadar;

            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        public static bool TryFindHoveredChest(
            GameLocation? location,
            Vector2 cursorTile,
            float? cursorWorldY,
            out Chest? chest,
            out Vector2 chestTile)
        {
            // 独立逻辑，无上文
            // 作用：依次检测指针当前格与下方邻格箱体上半截盖子区域，命中合法玩家箱时返回真实箱体与物理格坐标
            chest = null;
            chestTile = Vector2.Zero;
            if (location?.objects == null)
                return false;

            if (location.objects.TryGetValue(cursorTile, out StardewValley.Object? obj) &&
                obj is Chest directChest &&
                ChestFilter.IsLinkablePlayerChest(directChest))
            {
                chest = directChest;
                chestTile = cursorTile;
                return true;
            }

            Vector2 belowTile = new Vector2(cursorTile.X, cursorTile.Y + 1f);
            if (location.objects.TryGetValue(belowTile, out StardewValley.Object? belowObj) &&
                belowObj is Chest belowChest &&
                ChestFilter.IsLinkablePlayerChest(belowChest) &&
                (!cursorWorldY.HasValue || cursorWorldY.Value >= belowTile.Y * 64f - 52f))
            {
                chest = belowChest;
                chestTile = belowTile;
                return true;
            }

            return false;
        }

        public static bool IsActivelyHoveredChest(Chest? chest)
        {
            // 独立逻辑，无上文
            // 作用：供 Chest.draw 后置补丁以引用相等快路径判定目标箱是否为当前正悬停高亮的箱体
            return chest != null && ReferenceEquals(_activeHoveredChest, chest);
        }

        public static float ComputeChestTextureFlashAlpha(double timeMs)
        {
            // 独立逻辑，无上文
            // 作用：按正弦呼吸周期计算箱体纯纹理闪烁叠层的透明度（范围 0.10f 至 0.55f）
            return 0.10f + 0.45f * (0.5f + 0.5f * (float)Math.Sin(timeMs / 130.0));
        }

        public static Color[] BuildWhiteSilhouettePixels(Color[] bodyPixels, Color[]? lidPixels)
        {
            // 独立逻辑，无上文
            // 作用：对箱体与箱盖像素阵列逐点取最大透明度并集，生成背景全零、实体纯白的预乘轮廓掩膜像素
            if (bodyPixels == null || bodyPixels.Length == 0)
                return Array.Empty<Color>();

            Color[] result = new Color[bodyPixels.Length];
            for (int i = 0; i < bodyPixels.Length; i++)
            {
                byte a = (lidPixels != null && i < lidPixels.Length)
                    ? Math.Max(bodyPixels[i].A, lidPixels[i].A)
                    : bodyPixels[i].A;
                result[i] = new Color(a, a, a, a);
            }

            return result;
        }

        public static void ResolveChestSpriteSourceIndices(
            string? qualifiedItemId,
            int parentSheetIndex,
            bool isColored,
            int lidFrame,
            out int? bodyIndex,
            out int lidIndex)
        {
            // 独立逻辑，无上文
            // 作用：复刻原版 Chest.draw 的染色分支索引映射，解析箱体主体与当前盖子帧在图集中的图块编号
            if (!isColored ||
                (qualifiedItemId != "(BC)130" &&
                 qualifiedItemId != "(BC)232" &&
                 qualifiedItemId != "(BC)BigChest" &&
                 qualifiedItemId != "(BC)BigStoneChest"))
            {
                bodyIndex = null;
                lidIndex = lidFrame;
                return;
            }

            switch (qualifiedItemId)
            {
                case "(BC)130":
                    bodyIndex = 168;
                    lidIndex = lidFrame + 38;
                    break;
                case "(BC)BigChest":
                    bodyIndex = 312;
                    lidIndex = lidFrame + 8;
                    break;
                default:
                    bodyIndex = parentSheetIndex;
                    lidIndex = lidFrame;
                    break;
            }
        }

        public static void ClearSilhouetteCache()
        {
            // 独立逻辑，无上文
            // 作用：逐一释放已缓存的箱体纯白轮廓掩膜纹理并清空缓存字典
            foreach (Texture2D tex in _silhouetteCache.Values)
            {
                if (tex != null && !tex.IsDisposed)
                    tex.Dispose();
            }

            _silhouetteCache.Clear();
        }

        public static void DrawHoveredChestTextureFlash(
            SpriteBatch spriteBatch,
            Chest chest,
            int x,
            int y,
            float alpha,
            int currentLidFrame)
        {
            // 与上文：在 HarmonyPatches.Postfix_ChestDraw 命中当前激活悬停箱后触发
            // 作用：复刻原版 Chest.draw 的坐标与深度排序，并在原箱上方叠加呼吸暖白纯纹理轮廓闪烁
            if (spriteBatch == null || chest == null || Game1.viewport.Width <= 0 || Game1.viewport.Height <= 0)
                return;

            float num = x;
            float num2 = y;
            if (chest.localKickStartTile.HasValue)
            {
                num = Utility.Lerp(chest.localKickStartTile.Value.X, num, chest.kickProgress);
                num2 = Utility.Lerp(chest.localKickStartTile.Value.Y, num2, chest.kickProgress);
            }

            float num3 = Math.Max(0f, ((num2 + 1f) * 64f - 24f) / 10000f) + num * 1E-05f;
            if (chest.localKickStartTile.HasValue)
                num2 -= (float)Math.Sin((double)chest.kickProgress * Math.PI) * 0.5f;

            int lidFrame = currentLidFrame > 0 ? currentLidFrame : (chest.startingLidFrame?.Value ?? 0);
            bool isColored = chest.playerChoiceColor != null && !chest.playerChoiceColor.Value.Equals(Color.Black);

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(chest.QualifiedItemId);
            Texture2D? sourceTexture = data?.GetTexture();
            if (data == null || sourceTexture == null || sourceTexture.IsDisposed)
                return;

            ResolveChestSpriteSourceIndices(
                chest.QualifiedItemId,
                chest.ParentSheetIndex,
                isColored,
                lidFrame,
                out int? bodyIndex,
                out int lidIndex);

            Rectangle bodyRect = bodyIndex.HasValue ? data.GetSourceRect(0, bodyIndex.Value) : data.GetSourceRect();
            Rectangle lidRect = data.GetSourceRect(0, lidIndex);
            Texture2D? silhouette = GetOrCreateSilhouetteTexture(sourceTexture, bodyRect, lidRect);
            if (silhouette == null || silhouette.IsDisposed)
                return;

            double timeMs = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0.0;
            float flashAlpha = ComputeChestTextureFlashAlpha(timeMs);
            Color drawColor = new Color(255, 250, 220) * (flashAlpha * Math.Clamp(alpha, 0f, 1f));
            Vector2 localPos = Game1.GlobalToLocal(Game1.viewport, new Vector2(num * 64f, (num2 - 1f) * 64f));

            spriteBatch.Draw(
                silhouette,
                localPos,
                null,
                drawColor,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                num3 + 3E-05f);
        }

        private static Texture2D? GetOrCreateSilhouetteTexture(Texture2D sourceTexture, Rectangle bodyRect, Rectangle lidRect)
        {
            // 与上文：调用方已根据箱子当前外观与盖子帧解析出主体与箱盖源矩形
            // 作用：按源纹理与双源矩形键缓存并提取合并后的预乘纯白轮廓掩膜，超限或纹理失效时先清旧缓存
            if (sourceTexture == null || sourceTexture.IsDisposed)
            {
                if (_silhouetteCache.Count > 0)
                    ClearSilhouetteCache();
                return null;
            }

            if (bodyRect.Width <= 0 || bodyRect.Height <= 0)
                return null;

            Rectangle textureBounds = new Rectangle(0, 0, sourceTexture.Width, sourceTexture.Height);
            if (!textureBounds.Contains(bodyRect))
                return null;

            GraphicsDevice? graphicsDevice = sourceTexture.GraphicsDevice ?? Game1.graphics?.GraphicsDevice;
            if (graphicsDevice == null || graphicsDevice.IsDisposed)
                return null;

            var key = (sourceTexture, bodyRect, lidRect);
            if (_silhouetteCache.TryGetValue(key, out Texture2D? cached))
            {
                if (cached != null && !cached.IsDisposed)
                    return cached;

                ClearSilhouetteCache();
            }

            bool shouldClearCache = _silhouetteCache.Count >= MaxSilhouetteCacheEntries;
            if (!shouldClearCache)
            {
                foreach (var pair in _silhouetteCache)
                {
                    if (pair.Key.Source.IsDisposed || pair.Value == null || pair.Value.IsDisposed)
                    {
                        shouldClearCache = true;
                        break;
                    }
                }
            }

            if (shouldClearCache)
                ClearSilhouetteCache();

            int pixelCount = bodyRect.Width * bodyRect.Height;
            Color[] bodyPixels = new Color[pixelCount];
            sourceTexture.GetData(0, bodyRect, bodyPixels, 0, pixelCount);

            Color[]? lidPixels = null;
            if (lidRect != bodyRect &&
                lidRect.Width == bodyRect.Width &&
                lidRect.Height == bodyRect.Height &&
                textureBounds.Contains(lidRect))
            {
                lidPixels = new Color[pixelCount];
                sourceTexture.GetData(0, lidRect, lidPixels, 0, pixelCount);
            }

            Color[] maskPixels = BuildWhiteSilhouettePixels(bodyPixels, lidPixels);
            Texture2D silhouette = new Texture2D(graphicsDevice, bodyRect.Width, bodyRect.Height);
            silhouette.SetData(maskPixels);
            _silhouetteCache[key] = silhouette;
            return silhouette;
        }

        public static List<Item> CollectChestPreviewItems(Chest? chest)
        {
            // 独立逻辑，无上文
            // 作用：只读收集物理箱实际容量范围内的全部非空有效物品，过滤幽灵条目并保持箱内槽序
            var result = new List<Item>();
            Inventory? items = chest?.Items;
            if (chest == null || items == null)
                return result;

            int capacity = GetChestCapacitySafe(chest);
            int limit = Math.Min(items.Count, Math.Max(0, capacity));
            for (int i = 0; i < limit; i++)
            {
                Item? item = items[i];
                if (item != null && item.Stack > 0)
                    result.Add(item);
            }

            return result;
        }

        public static Rectangle ComputeBubblePanelBounds(
            Vector2 chestTopUiPos,
            int itemCount,
            bool isBigChest,
            int uiViewportWidth,
            int uiViewportHeight,
            out int columns,
            out int rows,
            out bool flippedBelow)
        {
            // 与上文：兼容既有 8 参数调用签名并转发至支持视口自适应格距的计算入口
            // 作用：计算无顶部标题栏的纯净箱内容网格气泡尺寸与定位矩形
            return ComputeBubblePanelBounds(
                chestTopUiPos,
                itemCount,
                isBigChest,
                uiViewportWidth,
                uiViewportHeight,
                out columns,
                out rows,
                out flippedBelow,
                out _);
        }

        public static Rectangle ComputeBubblePanelBounds(
            Vector2 chestTopUiPos,
            int itemCount,
            bool isBigChest,
            int uiViewportWidth,
            int uiViewportHeight,
            out int columns,
            out int rows,
            out bool flippedBelow,
            out int effectiveCellSize)
        {
            // 独立逻辑，无上文
            // 作用：按视口空间自适应缩放格距（60px 基准，溢出时等比回落至不低于 40px）并计算无标题网格气泡边界
            int panelWidth;
            int panelHeight;

            if (itemCount <= 0)
            {
                columns = 0;
                rows = 0;
                effectiveCellSize = CellSize;
                panelWidth = EmptyBubbleWidth;
                panelHeight = EmptyBubbleHeight;
            }
            else
            {
                int maxCols = isBigChest ? 10 : 9;
                columns = Math.Min(maxCols, Math.Max(1, itemCount));
                rows = (itemCount + columns - 1) / columns;

                int maxPanelWidth = Math.Max(1, uiViewportWidth - ScreenMargin * 2);
                int maxPanelHeight = Math.Max(1, uiViewportHeight - ScreenMargin * 2);
                int fullWidth = PanelPadding * 2 + columns * CellSize;
                int fullHeight = PanelPadding * 2 + rows * CellSize;

                if (fullWidth <= maxPanelWidth && fullHeight <= maxPanelHeight)
                {
                    effectiveCellSize = CellSize;
                }
                else
                {
                    float scaleW = (float)Math.Max(0, maxPanelWidth - PanelPadding * 2) / Math.Max(1, columns * CellSize);
                    float scaleH = (float)Math.Max(0, maxPanelHeight - PanelPadding * 2) / Math.Max(1, rows * CellSize);
                    float ratio = Math.Min(1f, Math.Min(scaleW, scaleH));
                    effectiveCellSize = Math.Clamp((int)Math.Floor(CellSize * ratio), MinCellSize, CellSize);
                }

                panelWidth = PanelPadding * 2 + columns * effectiveCellSize;
                panelHeight = PanelPadding * 2 + rows * effectiveCellSize;
            }

            int maxX = Math.Max(ScreenMargin, uiViewportWidth - panelWidth - ScreenMargin);
            int x = Math.Clamp((int)Math.Round(chestTopUiPos.X - panelWidth * 0.5f), ScreenMargin, maxX);

            int maxY = Math.Max(ScreenMargin, uiViewportHeight - panelHeight - ScreenMargin);
            int yAbove = (int)Math.Round(chestTopUiPos.Y) - panelHeight - VerticalGapAboveChest;
            int y;
            if (yAbove < ScreenMargin)
            {
                flippedBelow = true;
                int yBelow = (int)Math.Round(chestTopUiPos.Y) + VerticalOffsetBelowChest;
                y = Math.Clamp(yBelow, ScreenMargin, maxY);
            }
            else
            {
                flippedBelow = false;
                y = Math.Clamp(yAbove, ScreenMargin, maxY);
            }

            return new Rectangle(x, y, panelWidth, panelHeight);
        }

        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：本地玩家跨场景传送时立即清空悬停箱引用与气泡可见态
            if (e.IsLocalPlayer)
                ResetHoverState();
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：打开或切换游戏菜单时立即隐藏地上箱悬停气泡与闪动状态
            ResetHoverState();
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            // 独立逻辑，无上文
            // 作用：退回游戏标题画面时清空当前悬停箱状态并释放全部轮廓掩膜纹理缓存
            ResetHoverState();
            ClearSilhouetteCache();
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // 与上文：受配置开关、HUD 自由状态及雷达/一键堆叠悬停抑制回调约束
            // 作用：0ms 瞬时探测指针命中（含箱体上半截盖子）的合法玩家箱并立即点亮气泡与静态悬停追踪，移开即收起
            if (!Context.IsWorldReady || Game1.player?.currentLocation == null)
            {
                ResetHoverState();
                return;
            }

            RefinedStorageConfig config = _getConfig();
            if (!CanEvaluateHover(config) || IsCursorOverToolbarSlot())
            {
                ResetHoverState();
                return;
            }

            float? cursorWorldY = null;
            if (_helper?.Input != null)
            {
                try
                {
                    cursorWorldY = _helper.Input.GetCursorPosition().AbsolutePixels.Y;
                }
                catch
                {
                }
            }

            if (!TryFindHoveredChest(Game1.player.currentLocation, Game1.currentCursorTile, cursorWorldY, out Chest? targetChest, out Vector2 chestTile) ||
                targetChest == null)
            {
                ResetHoverState();
                return;
            }

            _hoveredChest = targetChest;
            _hoveredTile = chestTile;
            _isBubbleVisible = true;
            _activeHoveredChest = targetChest;
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            // 与上文：在 UpdateTicked 置位 _isBubbleVisible 并保持自由交互状态后渲染
            // 作用：使用原版木框绘制无顶部标题栏的大尺寸箱内容网格缩略图与堆叠数量
            if (!_isBubbleVisible || _hoveredChest == null || !Context.IsWorldReady || Game1.player?.currentLocation == null)
                return;

            RefinedStorageConfig config = _getConfig();
            if (!CanEvaluateHover(config))
            {
                ResetHoverState();
                return;
            }

            GameLocation location = Game1.player.currentLocation;
            if (location.objects == null ||
                !location.objects.TryGetValue(_hoveredTile, out StardewValley.Object? obj) ||
                !ReferenceEquals(obj, _hoveredChest) ||
                !ChestFilter.IsLinkablePlayerChest(_hoveredChest))
            {
                ResetHoverState();
                return;
            }

            List<Item> items = CollectChestPreviewItems(_hoveredChest);
            int capacity = GetChestCapacitySafe(_hoveredChest);
            bool isBigChest = _hoveredChest.SpecialChestType == Chest.SpecialChestTypes.BigChest || capacity >= 70;
            Vector2 chestTopUiPos = WorldBubbleRenderer.GetChestBubbleAnchorUiPos(_hoveredTile);

            Rectangle panelBounds = ComputeBubblePanelBounds(
                chestTopUiPos,
                items.Count,
                isBigChest,
                Game1.uiViewport.Width,
                Game1.uiViewport.Height,
                out int columns,
                out _,
                out _,
                out int effectiveCellSize);

            IClickableMenu.drawTextureBox(
                e.SpriteBatch,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                panelBounds.X,
                panelBounds.Y,
                panelBounds.Width,
                panelBounds.Height,
                Color.White,
                scale: 1f,
                drawShadow: true);

            if (items.Count == 0)
            {
                if (Game1.smallFont != null)
                {
                    const string emptyText = "空";
                    Vector2 textSize = Game1.smallFont.MeasureString(emptyText);
                    Vector2 textPos = new Vector2(
                        panelBounds.X + (panelBounds.Width - textSize.X) * 0.5f,
                        panelBounds.Y + (panelBounds.Height - textSize.Y) * 0.5f + 2f);
                    Utility.drawTextWithShadow(e.SpriteBatch, emptyText, Game1.smallFont, textPos, Game1.textColor * 0.78f);
                }

                return;
            }

            // 与上文：木框背景已绘制且存在非空物品列表
            // 作用：按自适应格距逐格渲染底纹、等比放大物品纹理与右下角堆叠数量
            float cellScaleRatio = (float)effectiveCellSize / CellSize;
            float itemScale = 0.88f * cellScaleRatio;
            float baseDigitScale = 3.0f * cellScaleRatio;

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                int col = i % columns;
                int row = i / columns;
                int cellX = panelBounds.X + PanelPadding + col * effectiveCellSize;
                int cellY = panelBounds.Y + PanelPadding + row * effectiveCellSize;

                if (Game1.staminaRect != null)
                {
                    e.SpriteBatch.Draw(
                        Game1.staminaRect,
                        new Rectangle(cellX + 2, cellY + 2, effectiveCellSize - 4, effectiveCellSize - 4),
                        new Color(180, 140, 95) * 0.22f);
                }

                Vector2 slotCenter = new Vector2(cellX + effectiveCellSize * 0.5f, cellY + effectiveCellSize * 0.5f);
                Vector2 drawPos = slotCenter - new Vector2(32f, 32f);
                item.drawInMenu(
                    e.SpriteBatch,
                    drawPos,
                    itemScale,
                    1f,
                    0.89f,
                    StackDrawType.HideButShowQuality,
                    Color.White,
                    drawShadow: false);

                if (item.Stack > 1)
                {
                    float digitScale = baseDigitScale;
                    int digitsWidth = Utility.getWidthOfTinyDigitString(item.Stack, digitScale);
                    if (digitsWidth > effectiveCellSize - 4)
                    {
                        digitScale *= (effectiveCellSize - 4f) / Math.Max(1, digitsWidth);
                        digitsWidth = Utility.getWidthOfTinyDigitString(item.Stack, digitScale);
                    }

                    Vector2 digitPos = new Vector2(
                        cellX + effectiveCellSize - 2f - digitsWidth,
                        cellY + effectiveCellSize - 6f * digitScale - 1f);
                    Utility.drawTinyDigits(item.Stack, e.SpriteBatch, digitPos, digitScale, 0.92f, Color.White);
                }
            }
        }

        private bool CanEvaluateHover(RefinedStorageConfig config)
        {
            // 独立逻辑，无上文
            // 作用：集中判断配置启用态、自由活动状态以及雷达/整理悬停互锁抑制状态
            if (!config.EnableChestHoverBubble || !Context.IsPlayerFree || !Game1.displayHUD ||
                Game1.activeClickableMenu != null || Game1.eventUp || Game1.farmEvent != null)
            {
                return false;
            }

            if (_isSuppressedByQuickStackHover?.Invoke() == true)
                return false;

            if (_isSuppressedByRadar?.Invoke() == true)
                return false;

            return true;
        }

        private static bool IsCursorOverToolbarSlot()
        {
            // 独立逻辑，无上文
            // 作用：防止鼠标悬停底部原版工具栏格子时误触发下方地砖箱子的悬停预览
            if (Game1.onScreenMenus == null)
                return false;

            int mouseX = Game1.getMouseX(ui_scale: true);
            int mouseY = Game1.getMouseY(ui_scale: true);
            for (int i = 0; i < Game1.onScreenMenus.Count; i++)
            {
                if (Game1.onScreenMenus[i] is Toolbar toolbar && toolbar.buttons != null)
                {
                    for (int b = 0; b < toolbar.buttons.Count; b++)
                    {
                        ClickableComponent? slot = toolbar.buttons[b];
                        if (slot != null && slot.containsPoint(mouseX, mouseY))
                            return true;
                    }
                }
            }

            return false;
        }

        private static int GetChestCapacitySafe(Chest chest)
        {
            // 独立逻辑，无上文
            // 作用：安全获取箱子实际格子容量并在无头异常场景按箱型回退标准容量
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

        private void ResetHoverState()
        {
            // 独立逻辑，无上文
            // 作用：立即重置当前悬停箱引用、静态活跃箱追踪、物理坐标与气泡可见标志
            _hoveredChest = null;
            _hoveredTile = Vector2.Zero;
            _isBubbleVisible = false;
            _activeHoveredChest = null;
        }
    }
}
