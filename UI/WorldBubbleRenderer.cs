using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace RefinedStorage.UI
{
    public readonly struct BubbleItemEntry
    {
        public Item Proto { get; }
        public int Count { get; }
        public string Prefix { get; }

        public BubbleItemEntry(Item proto, int count, string prefix = "")
        {
            // 独立逻辑，无上文
            // 作用：封装世界气泡单件展示物品的原型、数量与前缀文本
            Proto = proto;
            Count = count;
            Prefix = prefix ?? string.Empty;
        }
    }

    public static class WorldBubbleRenderer
    {
        public const float BubbleScale = 4.0f;
        public const float BubbleWidth = 20f * BubbleScale;
        public const float BubbleHeight = 24f * BubbleScale;

        private static readonly Rectangle BubbleSourceRect = new Rectangle(141, 465, 20, 24);
        private static readonly Rectangle RightArrowSourceRect = new Rectangle(365, 495, 12, 11);

        public static Vector2 WorldToUiPosition(Vector2 worldPixels)
        {
            // 独立逻辑，无上文
            // 作用：将游戏世界像素坐标转换为与 RenderedHud 一致的 UI 缩放屏幕坐标
            Vector2 local = Game1.GlobalToLocal(Game1.viewport, worldPixels);
            return Game1.options != null ? Utility.ModifyCoordinatesForUIScale(local) : local;
        }

        public static Vector2 GetChestBubbleAnchorUiPos(Vector2 tile)
        {
            // 与上文：依赖 WorldToUiPosition 完成世界到 UI 坐标映射
            // 作用：计算物理箱正上方顶边中心锚点的 UI 坐标供窄气泡居中对齐
            return WorldToUiPosition(new Vector2(tile.X * 64f + 32f, tile.Y * 64f - 68f));
        }

        public static Vector2 GetChestCenterUiPos(Vector2 tile)
        {
            // 与上文：依赖 WorldToUiPosition 完成世界到 UI 坐标映射
            // 作用：计算物理箱格中心点的 UI 坐标供视口内判定与屏外指路箭头瞄准
            return WorldToUiPosition(new Vector2(tile.X * 64f + 32f, tile.Y * 64f + 32f));
        }

        public static Rectangle ComputeBubbleBounds(Vector2 anchorUiPos, double timeMs)
        {
            // 独立逻辑，无上文
            // 作用：结合温和呼吸浮沉偏移计算 80x96 原版气泡在 UI 坐标下的外接矩形
            float bobOffset = 4f * (float)Math.Sin(timeMs / 250.0);
            int x = (int)Math.Round(anchorUiPos.X - BubbleWidth * 0.5f);
            int y = (int)Math.Round(anchorUiPos.Y - BubbleHeight + bobOffset);
            return new Rectangle(x, y, (int)Math.Ceiling(BubbleWidth), (int)Math.Ceiling(BubbleHeight));
        }

        public static bool IsUiPointInViewport(
            Vector2 uiPos,
            int uiViewportWidth,
            int uiViewportHeight,
            float marginX = 24f,
            float marginY = 24f)
        {
            // 独立逻辑，无上文
            // 作用：判定给定 UI 坐标点是否处于当前视口及边缘容差范围之内
            return uiPos.X >= -marginX
                && uiPos.X <= uiViewportWidth + marginX
                && uiPos.Y >= -marginY
                && uiPos.Y <= uiViewportHeight + marginY;
        }

        public static bool IsChestVisibleInViewport(
            Vector2 tile,
            int uiViewportWidth,
            int uiViewportHeight,
            float marginX = 24f,
            float marginY = 24f)
        {
            // 与上文：综合箱体中心点与视口内判定辅助函数
            // 作用：判断指定格位物理箱是否落入当前玩家屏幕可视范围内
            Vector2 centerUiPos = GetChestCenterUiPos(tile);
            return IsUiPointInViewport(centerUiPos, uiViewportWidth, uiViewportHeight, marginX, marginY);
        }

        public static void DrawChestItemBubble(
            SpriteBatch b,
            Vector2 anchorUiPos,
            IReadOnlyList<BubbleItemEntry>? items,
            double timeMs,
            float alpha = 1f)
        {
            // 独立逻辑，无上文
            // 作用：绘制 80x96 原版气泡、按 0.9s 轮播真实物品纹理与品质色、右下角描边数量及多物品右上角徽标
            if (b == null || items == null || items.Count == 0 || Game1.mouseCursors == null)
                return;

            float safeAlpha = Math.Clamp(alpha, 0f, 1f);
            if (safeAlpha <= 0f)
                return;

            float bobOffset = 4f * (float)Math.Sin(timeMs / 250.0);
            Vector2 bubbleTopLeft = new Vector2(
                anchorUiPos.X - BubbleWidth * 0.5f,
                anchorUiPos.Y - BubbleHeight + bobOffset);

            b.Draw(
                Game1.mouseCursors,
                bubbleTopLeft,
                BubbleSourceRect,
                Color.White * safeAlpha,
                0f,
                Vector2.Zero,
                BubbleScale,
                SpriteEffects.None,
                0.88f);

            int activeIndex = items.Count <= 1 ? 0 : ((int)(Math.Max(0.0, timeMs) / 900.0)) % items.Count;
            BubbleItemEntry active = items[activeIndex];
            if (active.Proto != null)
            {
                Vector2 itemCenter = new Vector2(bubbleTopLeft.X + BubbleWidth * 0.5f, bubbleTopLeft.Y + 37f);
                Vector2 itemDrawPos = itemCenter - new Vector2(32f, 32f);
                active.Proto.drawInMenu(
                    b,
                    itemDrawPos,
                    0.78f,
                    safeAlpha,
                    0.89f,
                    StackDrawType.HideButShowQuality,
                    Color.White * safeAlpha,
                    drawShadow: false);
            }

            SpriteFont? font = Game1.tinyFont ?? Game1.smallFont;
            if (font == null)
                return;

            // 与上文：气泡底框与轮播物品本体已绘制完成
            // 作用：在气泡右下角绘制带黑边的放大数量文本，并在多物品轮播时于右上角绘制总种数徽标
            if (active.Count > 0 || !string.IsNullOrEmpty(active.Prefix))
            {
                string countText = string.Concat(active.Prefix, active.Count.ToString());
                Vector2 baseSize = MeasureStringSafe(font, countText);
                float textScale = Math.Min(1.15f, 22f / Math.Max(1f, baseSize.Y));
                if (baseSize.X * textScale > 68f)
                    textScale = 68f / Math.Max(1f, baseSize.X);

                Vector2 countPos = new Vector2(
                    bubbleTopLeft.X + BubbleWidth - 6f - baseSize.X * textScale,
                    bubbleTopLeft.Y + 72f - baseSize.Y * textScale);
                Color countColor = string.Equals(active.Prefix, "+", StringComparison.Ordinal)
                    ? new Color(210, 255, 195)
                    : Color.White;
                DrawOutlinedText(b, font, countText, countPos, countColor, textScale, safeAlpha, 0.90f);
            }

            if (items.Count >= 2)
            {
                string badgeText = $"x{items.Count}";
                Vector2 badgeBaseSize = MeasureStringSafe(font, badgeText);
                float badgeScale = Math.Min(0.95f, 18f / Math.Max(1f, badgeBaseSize.Y));
                if (badgeBaseSize.X * badgeScale > 38f)
                    badgeScale = 38f / Math.Max(1f, badgeBaseSize.X);

                Vector2 badgePos = new Vector2(
                    bubbleTopLeft.X + BubbleWidth - 6f - badgeBaseSize.X * badgeScale,
                    bubbleTopLeft.Y + 6f);
                DrawOutlinedText(b, font, badgeText, badgePos, new Color(255, 232, 130), badgeScale, safeAlpha, 0.91f);
            }
        }

        public static void DrawOffscreenArrow(
            SpriteBatch b,
            Vector2 edgeUiPos,
            float rotationRadians,
            Color? tint = null,
            float scale = 3f,
            float alpha = 1f)
        {
            // 独立逻辑，无上文
            // 作用：在视口边缘按指定朝向角度绘制原版指路小箭头及其暗色阴影
            if (b == null || Game1.mouseCursors == null)
                return;

            float safeAlpha = Math.Clamp(alpha, 0f, 1f);
            if (safeAlpha <= 0f)
                return;

            Vector2 origin = new Vector2(6f, 5.5f);
            Color arrowColor = (tint ?? new Color(255, 220, 95)) * safeAlpha;

            b.Draw(
                Game1.mouseCursors,
                edgeUiPos + new Vector2(2f, 2f),
                RightArrowSourceRect,
                Color.Black * (0.55f * safeAlpha),
                rotationRadians,
                origin,
                scale,
                SpriteEffects.None,
                0.90f);

            b.Draw(
                Game1.mouseCursors,
                edgeUiPos,
                RightArrowSourceRect,
                arrowColor,
                rotationRadians,
                origin,
                scale,
                SpriteEffects.None,
                0.91f);
        }

        private static void DrawOutlinedText(
            SpriteBatch b,
            SpriteFont font,
            string text,
            Vector2 position,
            Color color,
            float scale,
            float alpha,
            float layerDepth)
        {
            // 独立逻辑，无上文
            // 作用：以四邻域黑色描边绘制高对比度小号数值或角标文本
            Color outlineColor = Color.Black * (0.9f * alpha);
            b.DrawString(font, text, position + new Vector2(-1f, 0f), outlineColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth - 0.002f);
            b.DrawString(font, text, position + new Vector2(1f, 0f), outlineColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth - 0.002f);
            b.DrawString(font, text, position + new Vector2(0f, -1f), outlineColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth - 0.002f);
            b.DrawString(font, text, position + new Vector2(0f, 1f), outlineColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth - 0.002f);
            b.DrawString(font, text, position, color * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth);
        }

        private static Vector2 MeasureStringSafe(SpriteFont font, string text)
        {
            // 独立逻辑，无上文
            // 作用：安全测量字体尺寸并在缺字异常时返回保守估算宽高
            try
            {
                return font.MeasureString(text);
            }
            catch
            {
                return new Vector2(text.Length * 10f, 16f);
            }
        }
    }
}
