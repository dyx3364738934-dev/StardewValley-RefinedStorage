using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using RefinedStorage.Storage;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
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

    public sealed class ChestIncomingItem
    {
        public Item Proto { get; }
        public string DisplayName { get; }
        public int AddedAmount { get; set; }

        public ChestIncomingItem(Item proto, string displayName, int addedAmount)
        {
            // 独立逻辑，无上文
            // 作用：保存终态推演中单个物理箱子内预计新增入箱的物品原型、显示名称与新增数量
            Proto = proto;
            DisplayName = displayName;
            AddedAmount = addedAmount;
        }
    }

    public sealed class ChestIncomingPlan
    {
        public Chest Chest { get; }
        public Vector2 Tile { get; }
        public List<ChestIncomingItem> Items { get; } = new List<ChestIncomingItem>();

        public ChestIncomingPlan(Chest chest, Vector2 tile)
        {
            // 独立逻辑，无上文
            // 作用：保存终态推演中预计收到新增物品的单只物理箱实例、格子坐标与物品明细列表
            Chest = chest;
            Tile = tile;
        }
    }

    public sealed class FinalStackPlan
    {
        public bool HasAnyTransfer { get; set; }
        public int TotalMovedCount { get; set; }
        public List<QuickStackItemSummary> ItemSummaries { get; set; } = new List<QuickStackItemSummary>();
        public List<ChestIncomingPlan> ChestPlans { get; set; } = new List<ChestIncomingPlan>();
    }

    public static class QuickStackService
    {
        public const int DefaultNearbyStackRangeTiles = 5;

        public static bool ShouldExclude(Item? item, RefinedStorageConfig? config)
        {
            // 独立逻辑，无上文
            // 作用：固定按 SDV 1.6 分类码（-99 为工具，-98 为近战武器）与收藏锁判定物品是否应跳过堆叠
            if (item == null)
                return true;

            if (item.Category == -99 || item.Category == -98)
                return true;

            if (FavoriteLock.IsFavorite(item))
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

                if (backpackItem != null && backpackItem.Stack > 0)
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
            // 作用：对每个受影响箱组去重执行一次跨箱整理并汇总返回结果
            if ((stackedCount + depositedCount) > 0)
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

        public static FinalStackPlan SimulateFinalPlan(Farmer? player, GameLocation? location, RefinedStorageConfig? config)
        {
            // 独立逻辑，无上文
            // 作用：在完全隔离的内存副本沙箱中推演 Pass 1、Pass 2 与自动排序落点，精确归因终态物理箱收货计划且零修改真实库存
            var plan = new FinalStackPlan();
            if (player?.Items == null || location?.objects == null || config == null)
                return plan;

            List<(Chest Chest, Vector2 Tile)> eligibleChests = CollectEligibleChests(player, location, config);
            if (eligibleChests.Count == 0)
                return plan;

            Dictionary<Chest, LinkedChestGroup> chestToGroup = BuildChestToGroupMap(eligibleChests, location);
            var stackMeta = new Dictionary<Item, SandboxStackMeta>(ReferenceEqualityComparer.Instance);
            var sandboxByChest = new Dictionary<Chest, SandboxChest>(ReferenceEqualityComparer.Instance);

            for (int c = 0; c < eligibleChests.Count; c++)
            {
                var (chest, tile) = eligibleChests[c];
                int capacity = Math.Max(0, chest.GetActualCapacity());
                var sbChest = new SandboxChest(chest, tile, capacity);
                Inventory? realItems = chest.Items;
                if (realItems != null && capacity > 0)
                {
                    int limit = Math.Min(realItems.Count, capacity);
                    for (int s = 0; s < limit; s++)
                    {
                        Item? existing = realItems[s];
                        if (existing == null || existing.Stack <= 0)
                            continue;

                        Item clone = CloneSandboxItem(existing);
                        sbChest.Slots[s] = clone;
                        stackMeta[clone] = new SandboxStackMeta(clone.Stack, 0);
                    }
                }
                sandboxByChest[chest] = sbChest;
            }

            // 与上文：各合格目标箱已有库存已完成只读克隆入沙箱
            // 作用：克隆背包非排除物品并执行 Pass 1 合并与 Pass 2 联排补空，统计总搬运量、摘要与受影响箱组
            var virtualBackpack = new Item?[player.Items.Count];
            for (int i = 0; i < player.Items.Count; i++)
            {
                Item? realItem = player.Items[i];
                if (realItem == null || realItem.Stack <= 0 || ShouldExclude(realItem, config))
                    continue;
                virtualBackpack[i] = CloneSandboxItem(realItem);
            }

            var affectedGroups = new HashSet<LinkedChestGroup>(ReferenceEqualityComparer.Instance);
            var affinityCache = new Dictionary<LinkedChestGroup, bool>(ReferenceEqualityComparer.Instance);
            var summaryByName = new Dictionary<string, QuickStackItemSummary>(StringComparer.Ordinal);
            int totalMoved = 0;

            for (int i = 0; i < virtualBackpack.Length; i++)
            {
                Item? backpackItem = virtualBackpack[i];
                if (backpackItem == null || backpackItem.Stack <= 0)
                    continue;

                Item proto = backpackItem.getOne();

                for (int c = 0; c < eligibleChests.Count; c++)
                {
                    SandboxChest sbChest = sandboxByChest[eligibleChests[c].Chest];
                    for (int s = 0; s < sbChest.Capacity; s++)
                    {
                        Item? existing = sbChest.Slots[s];
                        if (existing == null || existing.Stack <= 0)
                            continue;

                        if (!IsSameKindAndQuality(existing, backpackItem) || existing.getRemainingStackSpace() <= 0)
                            continue;

                        int beforeStack = backpackItem.Stack;
                        int remain = existing.addToStack(backpackItem);
                        int used = beforeStack - remain;
                        if (used <= 0)
                            continue;

                        totalMoved += used;
                        AccumulateSummary(plan.ItemSummaries, summaryByName, proto, used);
                        stackMeta[existing].AddedFromBackpack += used;
                        if (chestToGroup.TryGetValue(sbChest.RealChest, out LinkedChestGroup? grp))
                            affectedGroups.Add(grp);

                        backpackItem = backpackItem.ConsumeStack(used);
                        if (backpackItem == null || backpackItem.Stack <= 0)
                        {
                            virtualBackpack[i] = null;
                            backpackItem = null;
                            break;
                        }

                        virtualBackpack[i] = backpackItem;
                    }

                    if (backpackItem == null || backpackItem.Stack <= 0)
                        break;
                }

                if (backpackItem != null && backpackItem.Stack > 0)
                {
                    affinityCache.Clear();
                    for (int c = 0; c < eligibleChests.Count && backpackItem != null && backpackItem.Stack > 0; c++)
                    {
                        SandboxChest sbChest = sandboxByChest[eligibleChests[c].Chest];
                        if (!chestToGroup.TryGetValue(sbChest.RealChest, out LinkedChestGroup? group))
                            continue;

                        if (!affinityCache.TryGetValue(group, out bool hasAffinity))
                        {
                            hasAffinity = SandboxGroupContainsMatch(group, backpackItem, sandboxByChest);
                            affinityCache[group] = hasAffinity;
                        }

                        if (!hasAffinity || sbChest.Capacity == 0)
                            continue;

                        for (int s = 0; s < sbChest.Capacity && backpackItem != null && backpackItem.Stack > 0; s++)
                        {
                            Item? existing = sbChest.Slots[s];
                            if (existing != null && existing.Stack > 0)
                                continue;

                            int maxStack = backpackItem.maximumStackSize();
                            if (maxStack <= 0)
                                maxStack = 1;

                            int put = Math.Min(backpackItem.Stack, maxStack);
                            if (put <= 0)
                                break;

                            Item placed = backpackItem.getOne();
                            placed.Stack = put;
                            sbChest.Slots[s] = placed;
                            stackMeta[placed] = new SandboxStackMeta(0, put);

                            totalMoved += put;
                            AccumulateSummary(plan.ItemSummaries, summaryByName, proto, put);
                            affectedGroups.Add(group);

                            backpackItem = backpackItem.ConsumeStack(put);
                            if (backpackItem == null || backpackItem.Stack <= 0)
                            {
                                virtualBackpack[i] = null;
                                backpackItem = null;
                                break;
                            }

                            virtualBackpack[i] = backpackItem;
                        }
                    }
                }
            }

            if (totalMoved <= 0)
                return plan;

            // 与上文：沙箱已完成 Pass 1 与 Pass 2 物品搬运且 totalMoved > 0
            // 作用：对每个受影响箱组模拟自动整理并按组内终态槽位分布精确归因新增数量，汇总出各物理箱收货计划
            var chestAddedBySlotItem = new Dictionary<Chest, Dictionary<Item, int>>(ReferenceEqualityComparer.Instance);

            foreach (LinkedChestGroup group in affectedGroups)
                SimulateGroupSortAndAttribute(group, runVirtualSort: true, sandboxByChest, stackMeta, chestAddedBySlotItem);

            for (int c = 0; c < eligibleChests.Count; c++)
            {
                SandboxChest sbChest = sandboxByChest[eligibleChests[c].Chest];
                if (!chestAddedBySlotItem.TryGetValue(sbChest.RealChest, out Dictionary<Item, int>? itemAddMap) || itemAddMap.Count == 0)
                    continue;

                var chestPlan = new ChestIncomingPlan(sbChest.RealChest, sbChest.Tile);
                for (int s = 0; s < sbChest.Capacity; s++)
                {
                    Item? slotItem = sbChest.Slots[s];
                    if (slotItem == null || slotItem.Stack <= 0)
                        continue;

                    if (!itemAddMap.TryGetValue(slotItem, out int added) || added <= 0)
                        continue;

                    AddChestIncomingItem(chestPlan.Items, slotItem, added);
                }

                if (chestPlan.Items.Count > 0)
                    plan.ChestPlans.Add(chestPlan);
            }

            plan.TotalMovedCount = totalMoved;
            plan.HasAnyTransfer = totalMoved > 0 && plan.ChestPlans.Count > 0;
            return plan;
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

            if (candidateItems.Count == 0)
                return false;

            // 与上文：未命中 Pass 1 未满堆且待排查列表非空
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

            string displayName = GetItemDisplayName(proto);

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
            // 作用：筛选符合白名单与手边/全屋距离范围的箱子，并按距玩家像素距离升序稳定排列
            var eligible = new List<(Chest Chest, Vector2 Tile, float DistSq)>();
            Vector2 playerPos = player.Position;
            float maxDist = DefaultNearbyStackRangeTiles * 64f;
            float maxDistSq = maxDist * maxDist;

            foreach (KeyValuePair<Vector2, StardewValley.Object> pair in location.objects.Pairs)
            {
                if (pair.Value is not Chest chest || !ChestFilter.IsLinkablePlayerChest(chest))
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

        private sealed class SandboxChest
        {
            public Chest RealChest { get; }
            public Vector2 Tile { get; }
            public int Capacity { get; }
            public Item?[] Slots { get; }

            public SandboxChest(Chest realChest, Vector2 tile, int capacity)
            {
                // 独立逻辑，无上文
                // 作用：保存终态沙箱推演中单个物理箱子的副本槽位数组与真实容量边界
                RealChest = realChest;
                Tile = tile;
                Capacity = capacity;
                Slots = new Item?[capacity];
            }
        }

        private sealed class SandboxStackMeta
        {
            public int OrigChestAmount { get; }
            public int AddedFromBackpack { get; set; }
            public int PreTidyStack { get; set; }
            public int PreTidyAdded { get; set; }

            public SandboxStackMeta(int origChestAmount, int addedFromBackpack)
            {
                // 独立逻辑，无上文
                // 作用：追踪沙箱内单堆物品在箱内原有数量与本次背包转入增量，用于排序合并后的精确归因
                OrigChestAmount = origChestAmount;
                AddedFromBackpack = addedFromBackpack;
            }
        }

        private sealed class KindDeltaBucket
        {
            public Item Proto { get; }
            public int TotalGroupDelta { get; set; }

            public KindDeltaBucket(Item proto, int totalGroupDelta)
            {
                // 独立逻辑，无上文
                // 作用：记录箱组内同款同品质可堆叠物品在整理前的新增转入总量
                Proto = proto;
                TotalGroupDelta = totalGroupDelta;
            }
        }

        private static Item CloneSandboxItem(Item source)
        {
            // 独立逻辑，无上文
            // 作用：创建保留原物品类型、品质与风味数据的独立副本并恢复堆叠数量，确保推演零修改真实物品
            Item clone = source.getOne();
            clone.Stack = source.Stack;
            return clone;
        }

        private static string GetItemDisplayName(Item item)
        {
            // 独立逻辑，无上文
            // 作用：提取物品本地化显示名称，缺失时依次回退至内部名称与 QualifiedItemId
            return string.IsNullOrWhiteSpace(item.DisplayName)
                ? (item.Name ?? item.QualifiedItemId ?? string.Empty)
                : item.DisplayName;
        }

        private static bool SandboxGroupContainsMatch(
            LinkedChestGroup group,
            Item target,
            Dictionary<Chest, SandboxChest> sandboxByChest)
        {
            // 与上文：在沙箱 Pass 2 判定箱组是否具备同款亲和资格时调用
            // 作用：仅读取组内各箱当前沙箱副本槽位，任一成员箱含同款同品质可堆叠物品即返回 true
            IReadOnlyList<(Chest Chest, Vector2 Tile)> members = group.Members;
            for (int m = 0; m < members.Count; m++)
            {
                if (!sandboxByChest.TryGetValue(members[m].Chest, out SandboxChest? sbChest))
                    continue;

                for (int s = 0; s < sbChest.Capacity; s++)
                {
                    Item? item = sbChest.Slots[s];
                    if (item == null || item.Stack <= 0)
                        continue;

                    if (IsSameKindAndQuality(item, target))
                        return true;
                }
            }

            return false;
        }

        private static void SimulateGroupSortAndAttribute(
            LinkedChestGroup group,
            bool runVirtualSort,
            Dictionary<Chest, SandboxChest> sandboxByChest,
            Dictionary<Item, SandboxStackMeta> stackMeta,
            Dictionary<Chest, Dictionary<Item, int>> chestAddedBySlotItem)
        {
            // 与上文：SimulateFinalPlan 对每个发生物品转入的 LinkedChestGroup 分别结算终态落点
            // 作用：在组内沙箱副本上模拟原版合并排序与按容回填，并通过多阶段归因把组内增量精确分配至终态物理箱槽位
            var workList = new List<Item>();
            var buckets = new List<KindDeltaBucket>();
            long sumBefore = 0;
            IReadOnlyList<(Chest Chest, Vector2 Tile)> members = group.Members;

            for (int m = 0; m < members.Count; m++)
            {
                if (!sandboxByChest.TryGetValue(members[m].Chest, out SandboxChest? sbChest))
                    continue;

                for (int s = 0; s < sbChest.Capacity; s++)
                {
                    Item? item = sbChest.Slots[s];
                    if (item == null || item.Stack <= 0)
                        continue;

                    sumBefore += item.Stack;
                    workList.Add(item);

                    SandboxStackMeta meta = stackMeta[item];
                    meta.PreTidyStack = item.Stack;
                    meta.PreTidyAdded = meta.AddedFromBackpack;

                    KindDeltaBucket? matchedBucket = null;
                    for (int b = 0; b < buckets.Count; b++)
                    {
                        if (ReferenceEquals(buckets[b].Proto, item) || IsSameKindAndQuality(buckets[b].Proto, item))
                        {
                            matchedBucket = buckets[b];
                            break;
                        }
                    }

                    if (matchedBucket == null)
                    {
                        matchedBucket = new KindDeltaBucket(item, meta.PreTidyAdded);
                        buckets.Add(matchedBucket);
                    }
                    else
                    {
                        matchedBucket.TotalGroupDelta += meta.PreTidyAdded;
                    }
                }
            }

            if (runVirtualSort && workList.Count > 0)
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

                if (sumAfter == sumBefore && sorted.Count <= group.TotalCapacity)
                {
                    for (int m = 0; m < members.Count; m++)
                    {
                        if (sandboxByChest.TryGetValue(members[m].Chest, out SandboxChest? sbChest))
                            Array.Clear(sbChest.Slots, 0, sbChest.Slots.Length);
                    }

                    for (int g = 0; g < sorted.Count; g++)
                    {
                        SlotAddress addr = group.GetAddress(g);
                        if (addr.ChestIndex < 0 || addr.ChestIndex >= members.Count)
                            continue;

                        if (sandboxByChest.TryGetValue(members[addr.ChestIndex].Chest, out SandboxChest? targetChest) &&
                            addr.LocalSlot >= 0 &&
                            addr.LocalSlot < targetChest.Capacity)
                        {
                            targetChest.Slots[addr.LocalSlot] = sorted[g];
                        }
                    }
                }
            }

            var finalEntries = new List<(SandboxChest Chest, Item Item, SandboxStackMeta Meta)>();
            for (int m = 0; m < members.Count; m++)
            {
                if (!sandboxByChest.TryGetValue(members[m].Chest, out SandboxChest? sbChest))
                    continue;

                for (int s = 0; s < sbChest.Capacity; s++)
                {
                    Item? item = sbChest.Slots[s];
                    if (item != null && item.Stack > 0 && stackMeta.TryGetValue(item, out SandboxStackMeta? meta))
                        finalEntries.Add((sbChest, item, meta));
                }
            }

            for (int b = 0; b < buckets.Count; b++)
            {
                KindDeltaBucket bucket = buckets[b];
                if (bucket.TotalGroupDelta <= 0)
                    continue;

                var surviving = new List<(SandboxChest Chest, Item Item, SandboxStackMeta Meta)>();
                for (int f = 0; f < finalEntries.Count; f++)
                {
                    Item item = finalEntries[f].Item;
                    if (ReferenceEquals(bucket.Proto, item) || IsSameKindAndQuality(bucket.Proto, item))
                        surviving.Add(finalEntries[f]);
                }

                if (surviving.Count == 0)
                    continue;

                int[] assigned = new int[surviving.Count];
                int rem = bucket.TotalGroupDelta;

                for (int j = 0; j < surviving.Count && rem > 0; j++)
                {
                    int preAdded = surviving[j].Meta.PreTidyAdded;
                    if (preAdded <= 0)
                        continue;

                    int capRoom = surviving[j].Item.Stack - assigned[j];
                    int take = Math.Min(rem, Math.Min(preAdded, capRoom));
                    if (take > 0)
                    {
                        assigned[j] += take;
                        rem -= take;
                    }
                }

                for (int j = 0; j < surviving.Count && rem > 0; j++)
                {
                    int growth = surviving[j].Item.Stack - surviving[j].Meta.PreTidyStack;
                    if (growth <= 0)
                        continue;

                    int capRoom = surviving[j].Item.Stack - assigned[j];
                    int take = Math.Min(rem, Math.Min(growth, capRoom));
                    if (take > 0)
                    {
                        assigned[j] += take;
                        rem -= take;
                    }
                }

                for (int j = 0; j < surviving.Count && rem > 0; j++)
                {
                    int aboveOrig = surviving[j].Item.Stack - Math.Max(assigned[j], surviving[j].Meta.OrigChestAmount);
                    if (aboveOrig <= 0)
                        continue;

                    int take = Math.Min(rem, aboveOrig);
                    if (take > 0)
                    {
                        assigned[j] += take;
                        rem -= take;
                    }
                }

                for (int j = 0; j < surviving.Count && rem > 0; j++)
                {
                    int room = surviving[j].Item.Stack - assigned[j];
                    if (room <= 0)
                        continue;

                    int take = Math.Min(rem, room);
                    assigned[j] += take;
                    rem -= take;
                }

                for (int j = 0; j < surviving.Count; j++)
                {
                    if (assigned[j] <= 0)
                        continue;

                    Chest realChest = surviving[j].Chest.RealChest;
                    if (!chestAddedBySlotItem.TryGetValue(realChest, out Dictionary<Item, int>? map))
                    {
                        map = new Dictionary<Item, int>(ReferenceEqualityComparer.Instance);
                        chestAddedBySlotItem[realChest] = map;
                    }
                    map[surviving[j].Item] = assigned[j];
                }
            }
        }

        private static void AddChestIncomingItem(List<ChestIncomingItem> items, Item slotItem, int added)
        {
            // 与上文：在最终物理箱槽位中发现 assigned > 0 的物品增量
            // 作用：将同只箱子内跨多格的同款同品质新增物品合并为单一收货明细条目并保留原型
            for (int i = 0; i < items.Count; i++)
            {
                if (IsSameKindAndQuality(items[i].Proto, slotItem))
                {
                    items[i].AddedAmount += added;
                    return;
                }
            }

            Item proto = slotItem.getOne();
            items.Add(new ChestIncomingItem(proto, GetItemDisplayName(proto), added));
        }
    }
}
