using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RefinedStorage.Storage;
using RefinedStorage.UI;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Objects;

namespace RefinedStorage
{
    internal static class HarmonyPatches
    {
        private static FieldInfo? _standaloneMenuField;
        private static bool _isReplacingMenu;

        public static void Apply(Harmony harmony)
        {
            // 独立逻辑，无上文
            // 作用：缓存工作台私有字段引用并应用开箱展开与工作台全场景补丁
            _standaloneMenuField = AccessTools.Field(typeof(CraftingPage), "_standaloneMenu");
            if (_standaloneMenuField == null)
            {
                ModEntry.Instance.Monitor.Log(
                    "[Harmony] 未发现 CraftingPage._standaloneMenu 字段，工作台全场景补丁将被跳过。",
                    LogLevel.Warn);
            }

            ApplyChestShowMenuPatch(harmony);
            ApplyWorkbenchPatch(harmony);
        }

        private static void ApplyChestShowMenuPatch(Harmony harmony)
        {
            // 与上文：由 Apply 调度执行，负责拦截原版箱子打开入口
            // 作用：对 Chest.ShowMenu() 挂载 Postfix，在条件满足时无缝替换为合并视图
            try
            {
                MethodInfo? showMenu = AccessTools.Method(typeof(Chest), nameof(Chest.ShowMenu), Type.EmptyTypes);
                if (showMenu == null)
                {
                    ModEntry.Instance.Monitor.Log("[Harmony] 未找到 Chest.ShowMenu()，开箱展开功能不可用。", LogLevel.Warn);
                    return;
                }

                harmony.Patch(
                    showMenu,
                    postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Postfix_ChestShowMenu)));
                ModEntry.Instance.Monitor.Log("[Harmony] 已应用补丁：Chest.ShowMenu()", LogLevel.Info);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] Chest.ShowMenu() 补丁失败：{ex}", LogLevel.Error);
            }
        }

        private static void ApplyWorkbenchPatch(Harmony harmony)
        {
            // 与上文：由 Apply 调度执行，已在 Apply 阶段缓存 _standaloneMenu 字段
            // 作用：对 CraftingPage.getContainerContents() 挂载 Postfix 以扩大合法箱子扫描范围
            try
            {
                MethodInfo? method = AccessTools.Method(typeof(CraftingPage), "getContainerContents", Type.EmptyTypes);
                if (method == null)
                {
                    ModEntry.Instance.Monitor.Log("[Harmony] 未找到 CraftingPage.getContainerContents()，工作台覆盖不可用。", LogLevel.Warn);
                    return;
                }

                harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Postfix_GetContainerContents)));
                ModEntry.Instance.Monitor.Log("[Harmony] 已应用补丁：CraftingPage.getContainerContents()", LogLevel.Info);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] CraftingPage.getContainerContents() 补丁失败：{ex}", LogLevel.Error);
            }
        }

        private static void Postfix_ChestShowMenu(Chest __instance)
        {
            // 与上文：Chest.ShowMenu() 刚创建原版 ItemGrabMenu 并设为当前活跃菜单
            // 作用：校验白名单与连通箱数，按配置替换为保留原版状态与高亮委托的 RefinedChestMenu
            if (_isReplacingMenu || !ModEntry.Instance.Config.EnableExpandedStorage || !Context.IsWorldReady)
                return;

            if (!ChestFilter.IsLinkablePlayerChest(__instance))
                return;

            if (Game1.activeClickableMenu is not ItemGrabMenu vanillaMenu || vanillaMenu is RefinedChestMenu)
                return;

            GameLocation? location = __instance.Location ?? Game1.player?.currentLocation;
            if (location == null)
                return;

            try
            {
                _isReplacingMenu = true;

                List<(Chest Chest, Microsoft.Xna.Framework.Vector2 Tile)> members = ChestNetwork.FindLinkedChests(__instance, location);
                if (members.Count <= 1 && !ModEntry.Instance.Config.ExpandSingleChest)
                    return;

                var group = new LinkedChestGroup(__instance, location, members);
                var expandedMenu = new RefinedChestMenu(group, ModEntry.Instance.Config);

                expandedMenu.inventory.moveItemSound = vanillaMenu.inventory.moveItemSound;
                if (vanillaMenu.inventory.highlightMethod != null)
                    expandedMenu.inventory.highlightMethod = vanillaMenu.inventory.highlightMethod;
                if (vanillaMenu.ItemsToGrabMenu?.highlightMethod != null && expandedMenu.ItemsToGrabMenu != null)
                    expandedMenu.ItemsToGrabMenu.highlightMethod = vanillaMenu.ItemsToGrabMenu.highlightMethod;
                if (vanillaMenu.exitFunction != null)
                    expandedMenu.exitFunction = vanillaMenu.exitFunction;

                Item? carriedItem = vanillaMenu.heldItem;
                vanillaMenu.heldItem = null;
                if (carriedItem != null)
                    expandedMenu.heldItem = carriedItem;

                Game1.activeClickableMenu = expandedMenu;
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] 展开联排箱子菜单失败：{ex}", LogLevel.Error);
            }
            finally
            {
                _isReplacingMenu = false;
            }
        }

        private static void Postfix_GetContainerContents(CraftingPage __instance, ref IList<Item>? __result)
        {
            // 与上文：原版 CraftingPage.getContainerContents() 已汇总邻格工作台容器内容
            // 作用：仅在独立非烹饪工作台下注入同场景合法玩家箱，避免误吞祝尼魔箱或冰箱物品
            if (!ModEntry.Instance.Config.EnableWorkbenchRangeBoost || !Context.IsWorldReady)
                return;

            if (_standaloneMenuField == null || _standaloneMenuField.GetValue(__instance) is not true || __instance.cooking)
                return;

            GameLocation? location = Game1.player?.currentLocation;
            if (location?.objects == null)
                return;

            try
            {
                __instance._materialContainers ??= new List<IInventory>();
                var existingContainers = new HashSet<object>(ReferenceEqualityComparer.Instance);
                for (int i = 0; i < __instance._materialContainers.Count; i++)
                    existingContainers.Add(__instance._materialContainers[i]);

                __result ??= new List<Item>();
                var seenItems = new HashSet<Item>(__result, ReferenceEqualityComparer.Instance);

                foreach (var pair in location.objects.Pairs)
                {
                    if (pair.Value is not Chest chest || !ChestFilter.IsLinkablePlayerChest(chest))
                        continue;

                    if (existingContainers.Add(chest.Items))
                        __instance._materialContainers.Add(chest.Items);

                    for (int i = 0; i < chest.Items.Count; i++)
                    {
                        if (chest.Items[i] is Item item && item.Stack > 0 && seenItems.Add(item))
                            __result.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] 工作台注入玩家箱失败：{ex}", LogLevel.Debug);
            }
        }
    }
}
