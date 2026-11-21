using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using RefinedStorage.Storage;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Network;
using StardewValley.Objects;

namespace RefinedStorage.QuickStack
{
    public sealed class StackTransferRecord
    {
        public Item Proto { get; }
        public int Amount { get; }
        public Vector2 ChestTile { get; }
        public GameLocation Location { get; }

        public StackTransferRecord(Item proto, int amount, Vector2 chestTile, GameLocation location)
        {
            // 独立逻辑，无上文
            // 作用：保存单次堆叠或存入空位转移的物品原型副本、数量及目标箱坐标与场景
            Proto = proto;
            Amount = amount;
            ChestTile = chestTile;
            Location = location;
        }
    }

    public sealed class QuickStackItemSummary
    {
        public Item Proto { get; }
        public string DisplayName { get; }
        public int TotalAmount { get; set; }

        public QuickStackItemSummary(Item proto, string displayName, int totalAmount)
        {
            // 独立逻辑，无上文
            // 作用：保存单种物品在一次一键堆叠中的汇总数量、显示名称与用于飞入动画的物品原型
            Proto = proto;
            DisplayName = displayName;
            TotalAmount = totalAmount;
        }
    }

    public sealed class QuickStackResult
    {
        public bool FoundAnyChest { get; set; }
        public int StackedCount { get; set; }
        public int DepositedCount { get; set; }
        public int DistinctSlotCount { get; set; }
        public int AffectedGroupCount { get; set; }
        public List<StackTransferRecord> Transfers { get; set; } = new List<StackTransferRecord>();
        public List<QuickStackItemSummary> ItemSummaries { get; set; } = new List<QuickStackItemSummary>();
    }

    public static class QuickStackService
    {
        public static bool ShouldExclude(Item? item, RefinedStorageConfig config)
        {
            // 独立逻辑，无上文
            // 作用：修正 SDV 1.6 分类码（-99 为工具，-98 为近战武器）并结合收藏锁判定物品是否应跳过堆叠
            if (item == null)
                return true;

            if (config == null)
                return false;

            if (config.ExcludeTools && item.Category == -99)
                return true;

            if (config.ExcludeWeapons && item.Category == -98)
                return true;

            if (config.EnableFavoriteLock && FavoriteLock.IsFavorite(item))
                return true;

            return false;
        }

        public static QuickStackResult Execute(Farmer? player, GameLocation? location, RefinedStorageConfig? config)
        {
            // 独立逻辑，无上文
            // 作用：收集合格玩家箱并按距离与连通组划分，执行 Pass 1 合并与 Pass 2 联排亲和补空存入及去重整理
            var result = new QuickStackResult();
            if (player?.Items == null || location?.objects == null || config == null)
                return result;

            List<(Chest Chest, Vector2 Tile)> eligibleChests = CollectEligibleChests(player, location, config);
            if (eligibleChests.Count == 0)
                return result;

            result.FoundAnyChest = true;

            // 与上文：已按距玩家由近及远收集合格目标箱列表
            // 作用：调用 BuildChestToGroupMap 将合格箱划分进互不相交的 LinkedChestGroup 供亲和与去重整理使用
            Dictionary<Chest, LinkedChestGroup> chestToGroup = BuildChestToGroupMap(eligibleChests, location);

            // 与上文：所有合格目标箱及其所属 LinkedChestGroup 映射已构建完成
            // 作用：按背包槽序执行 Pass 1 合并未满堆与 Pass 2 同组亲和存空位，聚合分物品汇总并统计受影响箱组
            var affectedGroups = new HashSet<LinkedChestGroup>(ReferenceEqualityComparer.Instance);
            var affinityCache = new Dictionary<LinkedChestGroup, bool>(ReferenceEqualityComparer.Instance);
            var summaryByName = new Dictionary<string, QuickStackItemSummary>(StringComparer.Ordinal);
            int stackedCount = 0;
            int depositedCount = 0;
            int distinctSlotCount = 0;

            for (int i = 0; i < player.Items.Count; i++)
            {
                Item? backpackItem = player.Items[i];
                if (backpackItem == null || backpackItem.Stack <= 0 || ShouldExclude(backpackItem, config))
                    continue;

                Item proto = backpackItem.getOne();
                bool movedAnyFromSlot = false;

                for (int c = 0; c < eligibleChests.Count; c++)
                {
                    var (chest, tile) = eligibleChests[c];
                    Inventory? items = chest.Items;
                    if (items == null)
                        continue;

                    int limit = Math.Min(items.Count, Math.Max(0, chest.GetActualCapacity()));
                    for (int s = 0; s < limit; s++)
                    {
                        Item? existing = items[s];
                        if (existing == null || existing.Stack <= 0)
                            continue;

                        if (!IsSameKindAndQuality(existing, backpackItem) || existing.getRemainingStackSpace() <= 0)
                            continue;

                        int beforeStack = backpackItem.Stack;
                        int remain = existing.addToStack(backpackItem);
                        int used = beforeStack - remain;
                        if (used <= 0)
                            continue;

                        stackedCount += used;
                        movedAnyFromSlot = true;
                        result.Transfers.Add(new StackTransferRecord(proto, used, tile, location));
                        AccumulateSummary(result.ItemSummaries, summaryByName, proto, used);
                        if (chestToGroup.TryGetValue(chest, out LinkedChestGroup? grp))
                            affectedGroups.Add(grp);

                        backpackItem = backpackItem.ConsumeStack(used);
                        if (backpackItem == null || backpackItem.Stack <= 0)
                        {
                            player.Items[i] = null!;
                            backpackItem = null;
                            break;
                        }

                        if (!ReferenceEquals(player.Items[i], backpackItem))
                            player.Items[i] = backpackItem;
                    }

                    if (backpackItem == null || backpackItem.Stack <= 0)
                        break;
                }

                if (config.DepositToEmptySlots && backpackItem != null && backpackItem.Stack > 0)
                {
                    affinityCache.Clear();
                    for (int c = 0; c < eligibleChests.Count && backpackItem != null && backpackItem.Stack > 0; c++)
                    {
                        var (chest, tile) = eligibleChests[c];
                        if (!chestToGroup.TryGetValue(chest, out LinkedChestGroup? group))
                            continue;

                        if (!affinityCache.TryGetValue(group, out bool hasAffinity))
                        {
                            hasAffinity = GroupContainsMatch(group, backpackItem);
                            affinityCache[group] = hasAffinity;
                        }

                        if (!hasAffinity)
                            continue;

                        int capacity = Math.Max(0, chest.GetActualCapacity());
                        Inventory? items = chest.Items;
                        if (items == null || capacity == 0)
                            continue;

                        for (int s = 0; s < capacity && backpackItem != null && backpackItem.Stack > 0; s++)
                        {
                            Item? existing = s < items.Count ? items[s] : null;
                            if (existing != null && existing.Stack > 0)
                                continue;

                            int maxStack = backpackItem.maximumStackSize();
                            if (maxStack <= 0)
                                maxStack = 1;

                            int put = Math.Min(backpackItem.Stack, maxStack);
                            if (put <= 0)
                                break;

                            while (items.Count <= s)
                                items.Add(null!);

                            Item placed = backpackItem.getOne();
                            placed.Stack = put;
                            if (placed.NetFields != null)
                                placed.onDetachedFromParent();
                            items[s] = placed;

                            depositedCount += put;
                            movedAnyFromSlot = true;
                            result.Transfers.Add(new StackTransferRecord(proto, put, tile, location));
                            AccumulateSummary(result.ItemSummaries, summaryByName, proto, put);
                            affectedGroups.Add(group);

                            backpackItem = backpackItem.ConsumeStack(put);
                            if (backpackItem == null || backpackItem.Stack <= 0)
                            {
                                player.Items[i] = null!;
                                backpackItem = null;
                                break;
                            }

                            if (!ReferenceEquals(player.Items[i], backpackItem))
                                player.Items[i] = backpackItem;
                        }
                    }
                }

                if (movedAnyFromSlot)
                    distinctSlotCount++;
            }

            // 与上文：背包全部槽位已完成向箱组的堆叠与存入迁移
            // 作用：按开关对每个受影响箱组去重执行一次跨箱整理并汇总返回结果
            if ((stackedCount + depositedCount) > 0 && config.EnableLinkedSort && config.AutoSortAfterQuickStack)
            {
                foreach (LinkedChestGroup group in affectedGroups)
                    StorageOps.Tidy(group, out _, out _);
            }

            result.StackedCount = stackedCount;
            result.DepositedCount = depositedCount;
            result.DistinctSlotCount = distinctSlotCount;
            result.AffectedGroupCount = affectedGroups.Count;
            return result;
        }

        public static bool HasQuickStackableItems(Farmer? player, GameLocation? location, RefinedStorageConfig? config)
        {
            // 独立逻辑，无上文
            // 作用：纯只读探测当前背包与周边合格箱之间是否存在可合并或可亲和补空存入的物品，绝不修改任何库存
            if (player?.Items == null || location?.objects == null || config == null)
                return false;

            List<(Chest Chest, Vector2 Tile)> eligibleChests = CollectEligibleChests(player, location, config);
            if (eligibleChests.Count == 0)
                return false;

            var candidateItems = new List<Item>();
            for (int i = 0; i < player.Items.Count; i++)
            {
                Item? backpackItem = player.Items[i];
                if (backpackItem == null || backpackItem.Stack <= 0 || ShouldExclude(backpackItem, config))
                    continue;

                candidateItems.Add(backpackItem);
                for (int c = 0; c < eligibleChests.Count; c++)
                {
                    Chest chest = eligibleChests[c].Chest;
                    Inventory? items = chest.Items;
                    if (items == null)
                        continue;

                    int limit = Math.Min(items.Count, Math.Max(0, chest.GetActualCapacity()));
                    for (int s = 0; s < limit; s++)
                    {
                        Item? existing = items[s];
                        if (existing == null || existing.Stack <= 0)
                            continue;

                        if (IsSameKindAndQuality(existing, backpackItem) && existing.getRemainingStackSpace() > 0)
                            return true;
                    }
                }
            }

            if (!config.DepositToEmptySlots || candidateItems.Count == 0)
                return false;

            // 与上文：未命中 Pass 1 未满堆且配置允许向同组空位存入
            // 作用：复用 BuildChestToGroupMap 划分联排箱组，筛选存在空槽位的箱组检查是否具备同款亲和
            Dictionary<Chest, LinkedChestGroup> chestToGroup = BuildChestToGroupMap(eligibleChests, location);
            var groupsWithEmpty = new List<LinkedChestGroup>();
            var seenGroups = new HashSet<LinkedChestGroup>(ReferenceEqualityComparer.Instance);
            foreach (LinkedChestGroup group in chestToGroup.Values)
            {
                if (seenGroups.Add(group) && GroupHasEmptySlot(group))
                    groupsWithEmpty.Add(group);
            }

            if (groupsWithEmpty.Count == 0)
                return false;

            for (int i = 0; i < candidateItems.Count; i++)
            {
                Item candidate = candidateItems[i];
                for (int g = 0; g < groupsWithEmpty.Count; g++)
                {
                    if (GroupContainsMatch(groupsWithEmpty[g], candidate))
                        return true;
                }
            }

            return false;
        }

        public static string FormatSummaryNotification(IReadOnlyList<QuickStackItemSummary>? summaries, int maxShownItems = 6)
        {
            // 独立逻辑，无上文
            // 作用：将分物品汇总列表格式化为整理提示文本，超出上限种数时自动截断并追加总种类数
            if (summaries == null || summaries.Count == 0)
                return string.Empty;

            if (maxShownItems <= 0)
                return $"整理共{summaries.Count}种";

            int shownCount = Math.Min(summaries.Count, maxShownItems);
            var parts = new string[shownCount];
            for (int i = 0; i < shownCount; i++)
            {
                QuickStackItemSummary summary = summaries[i];
                parts[i] = $"{summary.DisplayName}x{summary.TotalAmount}";
            }

            string text = "整理" + string.Join("，", parts);
            if (summaries.Count > maxShownItems)
                text += $"等共{summaries.Count}种";

            return text;
        }

        private static Dictionary<Chest, LinkedChestGroup> BuildChestToGroupMap(
            List<(Chest Chest, Vector2 Tile)> eligibleChests,
            GameLocation location)
        {
            // 与上文：调用方已按距离筛选并排序合格目标箱列表
            // 作用：结合 ChestNetwork 将合格箱划分进互不相交的 LinkedChestGroup 供 Execute 与只读探测共用
            var eligibleSet = new HashSet<Chest>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < eligibleChests.Count; i++)
                eligibleSet.Add(eligibleChests[i].Chest);

            var chestToGroup = new Dictionary<Chest, LinkedChestGroup>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < eligibleChests.Count; i++)
            {
                var (anchor, anchorTile) = eligibleChests[i];
                if (chestToGroup.ContainsKey(anchor))
                    continue;

                List<(Chest Chest, Vector2 Tile)> linked = ChestNetwork.FindLinkedChests(anchor, location);
                var groupMembers = new List<(Chest Chest, Vector2 Tile)>(linked.Count);
                for (int m = 0; m < linked.Count; m++)
                {
                    var member = linked[m];
                    if (eligibleSet.Contains(member.Chest) && !chestToGroup.ContainsKey(member.Chest))
                        groupMembers.Add(member);
                }

                if (groupMembers.Count == 0)
                    groupMembers.Add((anchor, anchorTile));

                var group = new LinkedChestGroup(anchor, location, groupMembers);
                for (int m = 0; m < groupMembers.Count; m++)
                    chestToGroup[groupMembers[m].Chest] = group;
            }

            return chestToGroup;
        }

        private static void AccumulateSummary(
            List<QuickStackItemSummary> summaries,
            Dictionary<string, QuickStackItemSummary> summaryByName,
            Item proto,
            int amount)
        {
            // 与上文：Pass 1 或 Pass 2 成功发生物品转移后调用
            // 作用：按首次出现顺序及物品显示名称累加转移总数，合并多格或跨趟同款记录
            if (proto == null || amount <= 0)
                return;

            string displayName = string.IsNullOrWhiteSpace(proto.DisplayName)
                ? (proto.Name ?? proto.QualifiedItemId ?? string.Empty)
                : proto.DisplayName;

            if (summaryByName.TryGetValue(displayName, out QuickStackItemSummary? existing))
            {
                existing.TotalAmount += amount;
                return;
            }

            var created = new QuickStackItemSummary(proto, displayName, amount);
            summaryByName[displayName] = created;
            summaries.Add(created);
        }

        private static bool GroupHasEmptySlot(LinkedChestGroup group)
        {
            // 与上文：HasQuickStackableItems 检查 Pass 2 同组亲和前置条件时调用
            // 作用：在组内所有成员箱的实际容量范围内只读查找是否存在任意空槽位或幽灵槽位
            IReadOnlyList<(Chest Chest, Vector2 Tile)> members = group.Members;
            for (int m = 0; m < members.Count; m++)
            {
                Chest chest = members[m].Chest;
                int capacity = Math.Max(0, chest.GetActualCapacity());
                Inventory? items = chest.Items;
                if (items == null || capacity == 0)
                    continue;

                for (int s = 0; s < capacity; s++)
                {
                    Item? existing = s < items.Count ? items[s] : null;
                    if (existing == null || existing.Stack <= 0)
                        return true;
                }
            }

            return false;
        }

        private static List<(Chest Chest, Vector2 Tile)> CollectEligibleChests(Farmer player, GameLocation location, RefinedStorageConfig config)
        {
            // 与上文：Execute 入口已确认玩家、场景与配置均非空
            // 作用：筛选符合白名单、并发锁策略与距离范围的箱子，并按距玩家像素距离升序稳定排列
            var eligible = new List<(Chest Chest, Vector2 Tile, float DistSq)>();
            Vector2 playerPos = player.Position;
            float maxDist = Math.Max(0, config.QuickStackRange) * 64f;
            float maxDistSq = maxDist * maxDist;
            bool excludeLocked = !config.AllowConcurrentOpen;

            foreach (KeyValuePair<Vector2, StardewValley.Object> pair in location.objects.Pairs)
            {
                if (pair.Value is not Chest chest || !ChestFilter.IsLinkablePlayerChest(chest))
                    continue;

                if (excludeLocked && IsLockedByAnotherPlayer(chest))
                    continue;

                Vector2 chestPos = pair.Key * 64f;
                float dx = playerPos.X - chestPos.X;
                float dy = playerPos.Y - chestPos.Y;
                float distSq = dx * dx + dy * dy;

                if (!config.StackToWholeRoom && distSq > maxDistSq)
                    continue;

                eligible.Add((chest, pair.Key, distSq));
            }

            eligible.Sort((a, b) =>
            {
                int cmpDist = a.DistSq.CompareTo(b.DistSq);
                if (cmpDist != 0)
                    return cmpDist;

                int cmpY = a.Tile.Y.CompareTo(b.Tile.Y);
                return cmpY != 0 ? cmpY : a.Tile.X.CompareTo(b.Tile.X);
            });

            var result = new List<(Chest Chest, Vector2 Tile)>(eligible.Count);
            for (int i = 0; i < eligible.Count; i++)
                result.Add((eligible[i].Chest, eligible[i].Tile));

            return result;
        }

        private static bool IsLockedByAnotherPlayer(Chest chest)
        {
            // 独立逻辑，无上文
            // 作用：空值安全地判定目标箱子互斥锁是否正被其他玩家持有
            try
            {
                NetMutex? mutex = chest.GetMutex();
                return mutex != null && mutex.IsLocked() && !mutex.IsLockHeld();
            }
            catch
            {
                return false;
            }
        }

        private static bool GroupContainsMatch(LinkedChestGroup group, Item target)
        {
            // 与上文：在 Pass 2 判定箱组是否具备同款亲和资格时调用
            // 作用：遍历整个连通箱组全部槽位，任一成员箱含同款同品质可堆叠物品即返回 true
            int totalCapacity = group.TotalCapacity;
            for (int g = 0; g < totalCapacity; g++)
            {
                Item? item = group.GetSlot(g);
                if (item == null || item.Stack <= 0)
                    continue;

                if (IsSameKindAndQuality(item, target))
                    return true;
            }

            return false;
        }

        private static bool IsSameKindAndQuality(Item existing, Item target)
        {
            // 独立逻辑，无上文
            // 作用：按 QualifiedItemId、Object 品质与原版 canStackWith 规则严格判定两物品是否可归为同款
            if (existing.QualifiedItemId != target.QualifiedItemId)
                return false;

            if (existing is StardewValley.Object objA && target is StardewValley.Object objB && objA.Quality != objB.Quality)
                return false;

            return existing.canStackWith(target);
        }
    }
}
