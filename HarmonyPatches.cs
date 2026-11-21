using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using RefinedStorage.Storage;
using RefinedStorage.UI;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Network;
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
            // 作用：缓存工作台私有字段引用并应用开箱展开、多人并发开箱拦截、工作台全场景与箱体纹理闪烁补丁
            _standaloneMenuField = AccessTools.Field(typeof(CraftingPage), "_standaloneMenu");
            if (_standaloneMenuField == null)
            {
                ModEntry.Instance.Monitor.Log(
                    "[Harmony] 未发现 CraftingPage._standaloneMenu 字段，工作台全场景补丁将被跳过。",
                    LogLevel.Warn);
            }

            ApplyChestShowMenuPatch(harmony);
            ApplyChestCheckForActionPatch(harmony);
            ApplyWorkbenchPatch(harmony);
            ApplyChestDrawPatch(harmony);
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
                ModEntry.Instance.Monitor.Log("[Harmony] 已应用补丁：Chest.ShowMenu()", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] Chest.ShowMenu() 补丁失败：{ex}", LogLevel.Error);
            }
        }

        private static void ApplyChestCheckForActionPatch(Harmony harmony)
        {
            // 与上文：由 Apply 调度执行，负责补充多人并发开箱交互入口
            // 作用：对 Chest.checkForAction(Farmer, bool) 挂载 Prefix，允许在他人持锁期间绕过互斥阻塞并发开箱
            try
            {
                MethodInfo? checkForAction = AccessTools.Method(
                    typeof(Chest),
                    nameof(Chest.checkForAction),
                    new[] { typeof(Farmer), typeof(bool) });
                if (checkForAction == null)
                {
                    ModEntry.Instance.Monitor.Log("[Harmony] 未找到 Chest.checkForAction(Farmer, bool)，多人并发开箱拦截不可用。", LogLevel.Warn);
                    return;
                }

                harmony.Patch(
                    checkForAction,
                    prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Prefix_ChestCheckForAction)));
                ModEntry.Instance.Monitor.Log("[Harmony] 已应用补丁：Chest.checkForAction(Farmer, bool)", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] Chest.checkForAction(Farmer, bool) 补丁失败：{ex}", LogLevel.Error);
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
                ModEntry.Instance.Monitor.Log("[Harmony] 已应用补丁：CraftingPage.getContainerContents()", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] CraftingPage.getContainerContents() 补丁失败：{ex}", LogLevel.Error);
            }
        }

        private static void ApplyChestDrawPatch(Harmony harmony)
        {
            // 与上文：由 Apply 调度执行，在工作台补丁之后挂载世界箱体绘制后置钩子
            // 作用：对 Chest.draw(SpriteBatch, int, int, float) 挂载 Postfix，为悬停箱叠加同位纯纹理轮廓闪烁
            try
            {
                MethodInfo? chestDraw = AccessTools.Method(
                    typeof(Chest),
                    nameof(Chest.draw),
                    new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) });
                if (chestDraw == null)
                {
                    ModEntry.Instance.Monitor.Log("[Harmony] 未找到 Chest.draw(SpriteBatch, int, int, float)，箱体纹理闪烁不可用。", LogLevel.Warn);
                    return;
                }

                harmony.Patch(
                    chestDraw,
                    postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Postfix_ChestDraw)));
                ModEntry.Instance.Monitor.Log("[Harmony] 已应用补丁：Chest.draw(SpriteBatch, int, int, float)", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"[Harmony] Chest.draw(SpriteBatch, int, int, float) 补丁失败：{ex}", LogLevel.Error);
            }
        }

        private static bool Prefix_ChestCheckForAction(Chest __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
        {
            // 与上文：由 Chest.checkForAction(Farmer, bool) 前置拦截，在原版请求互斥锁前生效
            // 作用：按原版守卫核验玩家与白名单箱子，若正被他人持锁则直接安全触发开箱并跳过原版锁阻塞
            if (justCheckingForActivity || !Context.IsWorldReady)
                return true;

            if (__instance == null || who == null || !ReferenceEquals(who, Game1.player))
                return true;

            if (__instance.playerChest?.Value != true || __instance.giftbox?.Value == true || !ChestFilter.IsLinkablePlayerChest(__instance))
                return true;

            NetMutex? mutex = __instance.GetMutex();
            if (mutex == null || !mutex.IsLocked() || mutex.IsLockHeld())
                return true;

            if (!Game1.didPlayerJustRightClick(ignoreNonMouseHeldInput: true))
                return true;

            // 与上文：已确认满足原版右键开箱前提且目标箱互斥锁正被其他玩家持有
            // 作用：复刻原版锁成功路径安全播放开箱音效、停止角色移动并打开菜单（由 Postfix 接管），屏蔽原版互斥等待
            try
            {
                Game1.playSound(__instance.fridge?.Value == true ? "doorCreak" : "openChest");
            }
            catch
            {
            }

            try
            {
                Game1.player.Halt();
            }
            catch
            {
            }

            try
            {
                __instance.ShowMenu();
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[Harmony] 并发开箱打开菜单失败：{ex}", LogLevel.Error);
                return true;
            }

            __result = true;
            return false;
        }

        private static void Postfix_ChestShowMenu(Chest __instance)
        {
            // 与上文：Chest.ShowMenu() 刚创建原版 ItemGrabMenu 并设为当前活跃菜单
            // 作用：释放自身持锁后按单箱配置判断早退，否则替换为保留原版状态与高亮委托的 RefinedChestMenu
            RefinedStorageConfig config = ModEntry.Instance?.Config ?? new RefinedStorageConfig();
            if (_isReplacingMenu || !Context.IsWorldReady)
                return;

            if (!ChestFilter.IsLinkablePlayerChest(__instance))
                return;

            if (Game1.activeClickableMenu is not ItemGrabMenu vanillaMenu)
                return;

            NetMutex? mutex = __instance.GetMutex();
            if (mutex != null && mutex.IsLockHeld())
                mutex.ReleaseLock();

            if (vanillaMenu is RefinedChestMenu)
                return;

            GameLocation? location = __instance.Location ?? Game1.player?.currentLocation;
            if (location == null)
                return;

            try
            {
                _isReplacingMenu = true;

                List<(Chest Chest, Microsoft.Xna.Framework.Vector2 Tile)> members = ChestNetwork.FindLinkedChests(__instance, location);
                if (members.Count <= 1 && !config.ExpandSingleChest)
                    return;

                var group = new LinkedChestGroup(__instance, location, members);
                var expandedMenu = new RefinedChestMenu(group, config);

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
                ModEntry.Instance?.Monitor.Log($"[Harmony] 展开联排箱子菜单失败：{ex}", LogLevel.Error);
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
            if (ModEntry.Instance?.Config?.EnableWorkbenchRangeBoost != true || !Context.IsWorldReady)
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

        private static void Postfix_ChestDraw(
            Chest __instance,
            SpriteBatch spriteBatch,
            int x,
            int y,
            float alpha,
            int ___currentLidFrame)
        {
            // 与上文：原版 Chest.draw(SpriteBatch, int, int, float) 刚完成箱体与箱盖世界绘制
            // 作用：仅对当前活跃悬停箱在同一世界坐标与深度层叠加纯纹理闪烁，非悬停箱零开销快退
            if (!ChestHoverPreviewController.IsActivelyHoveredChest(__instance) || spriteBatch == null)
                return;

            try
            {
                ChestHoverPreviewController.DrawHoveredChestTextureFlash(spriteBatch, __instance, x, y, alpha, ___currentLidFrame);
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[Harmony] 绘制悬停箱纹理闪烁失败：{ex}", LogLevel.Debug);
            }
        }
    }
}
