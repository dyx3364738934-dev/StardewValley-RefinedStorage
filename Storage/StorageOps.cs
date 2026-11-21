using System;
using System.Collections.Generic;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace RefinedStorage.Storage
{
    public enum OpResult
    {
        Ok,
        NothingThere,
        NoRoom,
        RolledBack
    }

    public static class StorageOps
    {
        public static Item? Deposit(Item? source, LinkedChestGroup group, out int movedCount)
        {
            // 独立逻辑，无上文
            // 作用：初始化迁入计数并校验来源物品与目标箱组有效性，无效时原样返回避免丢物
            movedCount = 0;
            if (source == null || source.Stack <= 0 || group == null || group.TotalCapacity <= 0)
                return source;

            // 与上文：来源物品有效且目标箱组容量大于零
            // 作用：第 1 趟按箱序遍历已有未满堆叠并合并数量，耗尽时提前返回 null
            int totalCapacity = group.TotalCapacity;
            for (int i = 0; i < totalCapacity; i++)
            {
                Item? existing = group.GetSlot(i);
                if (existing == null || existing.Stack <= 0)
                    continue;

                if (existing.getRemainingStackSpace() <= 0 || !existing.canStackWith(source))
                    continue;

                int beforeStack = source.Stack;
                int remain = existing.addToStack(source);
                int used = beforeStack - remain;
                if (used <= 0)
                    continue;

                movedCount += used;
                source = source.ConsumeStack(used);
                if (source == null || source.Stack <= 0)
                    return null;
            }

            // 与上文：第 1 趟未能完全合并，手中仍有余量
            // 作用：第 2 趟占领组内首个空槽并脱离原父节点，全部写入返回 null，无空位则原样返回余量
            for (int i = 0; i < totalCapacity; i++)
            {
                Item? existing = group.GetSlot(i);
                if (existing != null && existing.Stack > 0)
                    continue;

                int placed = source.Stack;
                if (source.NetFields != null)
                    source.onDetachedFromParent();
                group.SetSlot(i, source);
                movedCount += placed;
                return null;
            }

            return source;
        }

        public static int FillOutStacks(
            IList<Item?> playerItems,
            LinkedChestGroup group,
            List<int>? touchedGlobalSlots = null,
            List<(Item Proto, int PlayerSlot)>? transferredFromSlots = null,
            Func<Item, bool>? shouldExclude = null)
        {
            // 独立逻辑，无上文
            // 作用：构建整组箱内物品的 QualifiedItemId 索引，用于后续全组同款亲和判定
            if (playerItems == null || group == null || group.TotalCapacity <= 0)
                return 0;

            int totalCapacity = group.TotalCapacity;
            var affinityById = new Dictionary<string, List<Item>>();
            for (int g = 0; g < totalCapacity; g++)
            {
                Item? chestItem = group.GetSlot(g);
                if (chestItem == null || chestItem.Stack <= 0)
                    continue;

                string qid = chestItem.QualifiedItemId;
                if (!affinityById.TryGetValue(qid, out List<Item>? list))
                {
                    list = new List<Item>();
                    affinityById[qid] = list;
                }
                list.Add(chestItem);
            }

            if (affinityById.Count == 0)
                return 0;

            // 与上文：已建立箱组同款索引并确认组内非空
            // 作用：遍历玩家背包（跳过排除委托命中项），对组内已有的同款先并入未满堆、有余量再占首个空槽
            int totalMoved = 0;
            HashSet<int>? seenTouched = touchedGlobalSlots != null ? new HashSet<int>() : null;

            for (int i = 0; i < playerItems.Count; i++)
            {
                Item? current = playerItems[i];
                if (current == null || current.Stack <= 0)
                    continue;

                if (shouldExclude != null && shouldExclude(current))
                    continue;

                if (!affinityById.TryGetValue(current.QualifiedItemId, out List<Item>? candidates))
                    continue;

                bool hasAffinity = false;
                for (int c = 0; c < candidates.Count; c++)
                {
                    if (candidates[c].canStackWith(current))
                    {
                        hasAffinity = true;
                        break;
                    }
                }

                if (!hasAffinity)
                    continue;

                Item origProto = current.getOne();
                bool movedAny = false;
                int firstEmptySlot = -1;

                for (int g = 0; g < totalCapacity; g++)
                {
                    Item? chestItem = group.GetSlot(g);
                    if (chestItem == null || chestItem.Stack <= 0)
                    {
                        if (firstEmptySlot == -1)
                            firstEmptySlot = g;
                        continue;
                    }

                    if (chestItem.getRemainingStackSpace() <= 0 || !chestItem.canStackWith(current))
                        continue;

                    int beforeStack = current.Stack;
                    int remain = chestItem.addToStack(current);
                    int used = beforeStack - remain;
                    if (used <= 0)
                        continue;

                    movedAny = true;
                    totalMoved += used;
                    if (seenTouched != null && seenTouched.Add(g))
                        touchedGlobalSlots!.Add(g);

                    current = current.ConsumeStack(used);
                    if (current == null || current.Stack <= 0)
                    {
                        playerItems[i] = null;
                        current = null;
                        break;
                    }
                }

                if (current != null && current.Stack > 0 && firstEmptySlot >= 0)
                {
                    int movedNow = current.Stack;
                    if (current.NetFields != null)
                        current.onDetachedFromParent();
                    group.SetSlot(firstEmptySlot, current);
                    playerItems[i] = null;
                    totalMoved += movedNow;
                    movedAny = true;
                    if (seenTouched != null && seenTouched.Add(firstEmptySlot))
                        touchedGlobalSlots!.Add(firstEmptySlot);
                }

                if (movedAny)
                    transferredFromSlots?.Add((origProto, i));
            }

            return totalMoved;
        }

        public static OpResult Tidy(LinkedChestGroup group, out int stackCount, out int rewrittenSlots)
        {
            // 独立逻辑，无上文
            // 作用：对整组全槽位拍下 (Item, Stack) 快照并统计总数量，全空且无幽灵槽时直接返回 NothingThere
            stackCount = 0;
            rewrittenSlots = 0;
            if (group == null || group.TotalCapacity <= 0)
                return OpResult.NothingThere;

            int cap = group.TotalCapacity;
            var snapshot = new (Item? Item, int Stack)[cap];
            var workList = new List<Item>();
            long sumBefore = 0;
            bool hasGhost = false;

            for (int i = 0; i < cap; i++)
            {
                Item? item = group.GetSlot(i);
                if (item == null)
                {
                    snapshot[i] = (null, 0);
                    continue;
                }

                int stack = item.Stack;
                snapshot[i] = (item, stack);
                if (stack <= 0)
                {
                    hasGhost = true;
                    continue;
                }

                sumBefore += stack;
                workList.Add(item);
            }

            if (workList.Count == 0 && !hasGhost)
                return OpResult.NothingThere;

            // 与上文：事前快照与初始总数守恒基准已锁定
            // 作用：调用原版 organizeItemsInList 合并排序，校验数量与容量守恒后按箱序落盘并二次核对，失败立即全量回滚
            try
            {
                ItemGrabMenu.organizeItemsInList(workList);

                var sorted = new List<Item>(workList.Count);
                long sumAfter = 0;
                for (int i = 0; i < workList.Count; i++)
                {
                    Item? item = workList[i];
                    if (item != null && item.Stack > 0)
                    {
                        sorted.Add(item);
                        sumAfter += item.Stack;
                    }
                }

                if (sumAfter != sumBefore || sorted.Count > cap)
                {
                    Rollback(group, snapshot);
                    stackCount = 0;
                    rewrittenSlots = 0;
                    return OpResult.RolledBack;
                }

                int changed = 0;
                for (int i = 0; i < cap; i++)
                {
                    Item? target = i < sorted.Count ? sorted[i] : null;
                    var (priorItem, priorStack) = snapshot[i];
                    if (target == null && priorItem == null)
                        continue;
                    if (target != null && ReferenceEquals(target, priorItem) && target.Stack == priorStack)
                        continue;
                    changed++;
                }

                if (changed > 0)
                {
                    for (int i = 0; i < cap; i++)
                        group.SetSlot(i, null);

                    for (int i = 0; i < sorted.Count; i++)
                        group.SetSlot(i, sorted[i]);
                }

                group.NormalizeChestLists();

                long sumVerify = 0;
                int countVerify = 0;
                for (int i = 0; i < cap; i++)
                {
                    Item? current = group.GetSlot(i);
                    if (current != null && current.Stack > 0)
                    {
                        sumVerify += current.Stack;
                        countVerify++;
                    }
                }

                if (sumVerify != sumBefore || countVerify != sorted.Count)
                {
                    Rollback(group, snapshot);
                    stackCount = 0;
                    rewrittenSlots = 0;
                    return OpResult.RolledBack;
                }

                stackCount = sorted.Count;
                rewrittenSlots = changed;
                return OpResult.Ok;
            }
            catch
            {
                Rollback(group, snapshot);
                stackCount = 0;
                rewrittenSlots = 0;
                return OpResult.RolledBack;
            }
        }

        public static OpResult TidyChests(GameLocation location, Chest anchorChest, out int chestCount, out int stackCount)
        {
            // 独立逻辑，无上文
            // 作用：发现连通白名单箱组、构建 LinkedChestGroup 并执行事务化跨箱整理
            chestCount = 0;
            stackCount = 0;
            if (!ChestFilter.IsLinkablePlayerChest(anchorChest))
                return OpResult.NothingThere;

            List<(Chest Chest, Microsoft.Xna.Framework.Vector2 Tile)> members = ChestNetwork.FindLinkedChests(anchorChest, location);
            chestCount = members.Count;
            if (chestCount == 0)
                return OpResult.NothingThere;

            var group = new LinkedChestGroup(anchorChest, location, members);
            return Tidy(group, out stackCount, out _);
        }

        private static void Rollback(LinkedChestGroup group, (Item? Item, int Stack)[] snapshot)
        {
            // 与上文：Tidy 在排序校验或落盘后核对阶段检测到异常或守恒偏差
            // 作用：先清空全部槽位释放网同步父节点，再按事前快照恢复物品堆叠与槽位并吞掉二次异常以确保返回 RolledBack
            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                    group.SetSlot(i, null);

                for (int i = 0; i < snapshot.Length; i++)
                {
                    var (item, stack) = snapshot[i];
                    if (item == null || stack <= 0)
                        continue;

                    if (item.Stack != stack)
                        item.Stack = stack;
                    group.SetSlot(i, item);
                }

                group.NormalizeChestLists();
            }
            catch
            {
            }
        }
    }
}
