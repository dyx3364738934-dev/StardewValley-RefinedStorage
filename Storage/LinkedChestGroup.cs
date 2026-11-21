using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Objects;

namespace RefinedStorage.Storage
{
    public readonly struct SlotAddress
    {
        public int ChestIndex { get; }
        public int LocalSlot { get; }

        public SlotAddress(int chestIndex, int localSlot)
        {
            ChestIndex = chestIndex;
            LocalSlot = localSlot;
        }
    }

    public sealed class LinkedChestGroup
    {
        private readonly (Chest Chest, Vector2 Tile)[] _members;
        private readonly int[] _capacities;
        private readonly int[] _offsets;

        public Chest AnchorChest { get; }
        public GameLocation Location { get; }
        public IReadOnlyList<(Chest Chest, Vector2 Tile)> Members { get; }
        public int ChestCount => Members.Count;
        public int TotalCapacity { get; }
        public bool HasBigChest { get; }
        public int PreferredColumns => HasBigChest ? 14 : 12;

        public LinkedChestGroup(Chest anchorChest, GameLocation location, IReadOnlyList<(Chest Chest, Vector2 Tile)> members)
        {
            // 独立逻辑，无上文
            // 作用：保存锚点与场景引用并拷贝成员箱列表，预计算全局槽位偏移、总容量与大箱子标记
            AnchorChest = anchorChest ?? throw new ArgumentNullException(nameof(anchorChest));
            Location = location;

            int count = members?.Count ?? 0;
            var copied = new List<(Chest Chest, Vector2 Tile)>(count);
            if (members != null)
            {
                for (int i = 0; i < count; i++)
                {
                    var entry = members[i];
                    if (entry.Chest != null)
                        copied.Add(entry);
                }
            }

            _members = copied.ToArray();
            Members = Array.AsReadOnly(_members);
            _capacities = new int[_members.Length];
            _offsets = new int[_members.Length];

            int offset = 0;
            bool hasBig = false;
            for (int i = 0; i < _members.Length; i++)
            {
                Chest chest = _members[i].Chest;
                int cap = Math.Max(0, chest.GetActualCapacity());
                _capacities[i] = cap;
                _offsets[i] = offset;
                offset += cap;

                if (chest.SpecialChestType == Chest.SpecialChestTypes.BigChest || cap >= 70)
                    hasBig = true;
            }

            TotalCapacity = offset;
            HasBigChest = hasBig;
        }

        public SlotAddress GetAddress(int globalSlot)
        {
            // 与上文：依赖构造阶段预计算好的 _offsets 与 _capacities 数组
            // 作用：把全局连续槽位下标映射为成员箱序号与箱内槽位，越界返回 (-1, -1)
            if (globalSlot < 0 || globalSlot >= TotalCapacity || _offsets.Length == 0)
                return new SlotAddress(-1, -1);

            int lo = 0;
            int hi = _offsets.Length - 1;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo + 1) >> 1);
                if (_offsets[mid] <= globalSlot)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            while (lo < _capacities.Length && globalSlot >= _offsets[lo] + _capacities[lo])
                lo++;

            if (lo >= _capacities.Length)
                return new SlotAddress(-1, -1);

            return new SlotAddress(lo, globalSlot - _offsets[lo]);
        }

        public Item? GetSlot(int globalSlot)
        {
            // 与上文：依赖 GetAddress 解析目标箱子与箱内槽位
            // 作用：从真实 Chest.Items 读取物品，未扩展到该下标或越界时返回 null
            SlotAddress addr = GetAddress(globalSlot);
            if (addr.ChestIndex < 0)
                return null;

            Inventory? items = _members[addr.ChestIndex].Chest.Items;
            if (items == null || addr.LocalSlot >= items.Count)
                return null;

            return items[addr.LocalSlot];
        }

        public void SetSlot(int globalSlot, Item? item)
        {
            // 与上文：依赖 GetAddress 定位目标成员箱与该箱子的容量上限
            // 作用：安全写入真实 Chest.Items[localSlot]，拒绝 Stack <= 0 幽灵物并在写入非空时按需补 null 扩容
            SlotAddress addr = GetAddress(globalSlot);
            if (addr.ChestIndex < 0)
                return;

            Inventory? items = _members[addr.ChestIndex].Chest.Items;
            int capacity = _capacities[addr.ChestIndex];
            if (items == null || addr.LocalSlot < 0 || addr.LocalSlot >= capacity)
                return;

            if (item != null && item.Stack <= 0)
                item = null;

            if (item == null)
            {
                if (addr.LocalSlot < items.Count)
                    items[addr.LocalSlot] = null!;
                return;
            }

            while (items.Count <= addr.LocalSlot)
                items.Add(null!);

            if (!ReferenceEquals(items[addr.LocalSlot], item))
            {
                if (item.NetFields != null)
                    item.onDetachedFromParent();
                items[addr.LocalSlot] = item;
            }
        }

        public int CountOccupiedSlots()
        {
            // 与上文：遍历构造时锁定的成员箱及其实际容量上限
            // 作用：统计整组容器内有效非空槽位数量（排除 Stack <= 0 的幽灵条目）
            int occupied = 0;
            for (int c = 0; c < _members.Length; c++)
            {
                Inventory? items = _members[c].Chest.Items;
                if (items == null)
                    continue;

                int limit = Math.Min(items.Count, _capacities[c]);
                for (int s = 0; s < limit; s++)
                {
                    Item? item = items[s];
                    if (item != null && item.Stack > 0)
                        occupied++;
                }
            }

            return occupied;
        }

        public bool ValidateStillPresent()
        {
            // 独立逻辑，无上文
            // 作用：校验所属场景是否仍完整保有锚点箱与所有成员箱且均合法未锁
            if (Location?.objects == null || AnchorChest == null || _members.Length == 0)
                return false;

            if (!ChestFilter.IsLinkablePlayerChest(AnchorChest))
                return false;

            bool anchorPresent = false;
            for (int i = 0; i < _members.Length; i++)
            {
                var (chest, tile) = _members[i];
                if (!Location.objects.TryGetValue(tile, out StardewValley.Object? obj) || !ReferenceEquals(obj, chest))
                    return false;

                if (!ChestFilter.IsLinkablePlayerChest(chest) || ChestFilter.IsLockedByOther(chest, AnchorChest))
                    return false;

                if (ReferenceEquals(chest, AnchorChest))
                    anchorPresent = true;
            }

            return anchorPresent;
        }

        public void NormalizeChestLists()
        {
            // 与上文：基于已绑定的成员箱列表直接维护其底层 Inventory
            // 作用：清除 Stack <= 0 幽灵条目并仅修剪尾部多余 null，保留中间空槽布局而不坍缩
            for (int c = 0; c < _members.Length; c++)
            {
                Inventory? items = _members[c].Chest.Items;
                if (items == null)
                    continue;

                for (int s = 0; s < items.Count; s++)
                {
                    Item? item = items[s];
                    if (item != null && item.Stack <= 0)
                        items[s] = null!;
                }

                while (items.Count > 0 && items[items.Count - 1] == null)
                    items.RemoveAt(items.Count - 1);
            }
        }
    }
}
