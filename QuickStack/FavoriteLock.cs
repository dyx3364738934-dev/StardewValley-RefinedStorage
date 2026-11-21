using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace RefinedStorage.QuickStack
{
    public static class FavoriteLock
    {
        public const string ModDataKey = "Koko.QuickStack/Favorite";
        private static readonly Rectangle StarSourceRect = new Rectangle(338, 400, 8, 8);

        public static bool IsFavorite(Item? item)
        {
            // 独立逻辑，无上文
            // 作用：兼容旧版 QuickStack 键名检查物品是否处于收藏锁定状态
            return item != null && item.modData != null && item.modData.ContainsKey(ModDataKey);
        }

        public static bool ToggleFavorite(Item? item)
        {
            // 与上文：依赖 IsFavorite 相同的 ModDataKey 存储约定
            // 作用：切换物品收藏锁定标记并返回切换后的锁定状态
            if (item == null || item.modData == null)
                return false;

            if (item.modData.ContainsKey(ModDataKey))
            {
                item.modData.Remove(ModDataKey);
                return false;
            }

            item.modData[ModDataKey] = "true";
            return true;
        }

        public static InventoryMenu? GetPlayerInventoryMenu()
        {
            // 独立逻辑，无上文
            // 作用：从当前活跃界面提取玩家背包网格，兼容独立背包页、含 RefinedChestMenu 在内的箱子菜单与 GameMenu 背包标签页
            IClickableMenu? menu = Game1.activeClickableMenu;
            if (menu is InventoryPage invPage)
                return invPage.inventory;

            if (menu is ItemGrabMenu grabMenu)
                return grabMenu.inventory;

            if (menu is GameMenu gameMenu &&
                gameMenu.currentTab >= 0 &&
                gameMenu.currentTab < gameMenu.pages.Count &&
                gameMenu.pages[gameMenu.currentTab] is InventoryPage tabInv)
            {
                return tabInv.inventory;
            }

            return null;
        }

        public static bool TryToggleAtCursor(Vector2 screenPixels)
        {
            // 与上文：通过 GetPlayerInventoryMenu 获取当前界面的玩家背包网格
            // 作用：定位鼠标悬停格位并切换对应物品收藏状态，按结果安全播放提示音效
            InventoryMenu? inv = GetPlayerInventoryMenu();
            if (inv == null || inv.inventory == null || inv.actualInventory == null)
                return false;

            int count = inv.inventory.Count < inv.actualInventory.Count ? inv.inventory.Count : inv.actualInventory.Count;
            for (int i = 0; i < count; i++)
            {
                ClickableComponent? slot = inv.inventory[i];
                if (slot == null || !slot.containsPoint((int)screenPixels.X, (int)screenPixels.Y))
                    continue;

                Item? item = inv.actualInventory[i];
                if (item == null)
                    return false;

                bool favorited = ToggleFavorite(item);
                try
                {
                    Game1.playSound(favorited ? "coin" : "cancel");
                }
                catch
                {
                }
                return true;
            }

            return false;
        }

        public static void DrawFavoriteStars(SpriteBatch b)
        {
            // 与上文：在菜单绘制完成后叠加渲染玩家背包内的收藏锁定指示图标
            // 作用：遍历当前界面玩家背包槽位，在已收藏物品左上角绘制紫色星标
            if (b == null)
                return;

            InventoryMenu? inv = GetPlayerInventoryMenu();
            if (inv == null || inv.inventory == null || inv.actualInventory == null)
                return;

            int count = inv.inventory.Count < inv.actualInventory.Count ? inv.inventory.Count : inv.actualInventory.Count;
            for (int i = 0; i < count; i++)
            {
                Item? item = inv.actualInventory[i];
                if (item == null || !IsFavorite(item))
                    continue;

                ClickableComponent? slot = inv.inventory[i];
                if (slot == null)
                    continue;

                b.Draw(
                    Game1.mouseCursors,
                    new Vector2(slot.bounds.X + 2, slot.bounds.Y + 2),
                    StarSourceRect,
                    Color.MediumPurple,
                    0f,
                    Vector2.Zero,
                    2f,
                    SpriteEffects.None,
                    1f);
            }
        }
    }
}
