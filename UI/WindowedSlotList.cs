using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using RefinedStorage.Ime;
using RefinedStorage.Storage;
using StardewValley;

namespace RefinedStorage.UI
{
    public sealed class WindowedSlotList : IList<Item?>
    {
        private readonly LinkedChestGroup _group;
        private readonly List<int> _projectedGlobalSlots = new List<int>();
        private int _scrollRow;

        public int Columns { get; }
        public int VisibleRows { get; }
        public int VisibleCapacity => Columns * VisibleRows;
        public int TotalProjectedSlots => _projectedGlobalSlots.Count;
        public int TotalRows => TotalProjectedSlots <= 0 ? 0 : (TotalProjectedSlots + Columns - 1) / Columns;
        public int MaxScrollRow => Math.Max(0, TotalRows - VisibleRows);
        public bool IsFilterActive { get; private set; }

        public int ScrollRow
        {
            get => _scrollRow;
            set => _scrollRow = Math.Clamp(value, 0, MaxScrollRow);
        }

        public WindowedSlotList(LinkedChestGroup group, int columns, int visibleRows)
        {
            // 独立逻辑，无上文
            // 作用：校验箱组与网格尺寸参数并构建初始全量槽位投影
            _group = group ?? throw new ArgumentNullException(nameof(group));
            Columns = Math.Max(1, columns);
            VisibleRows = Math.Max(1, visibleRows);
            RebuildProjection(null);
        }

        public void RebuildProjection(string? searchQuery)
        {
            // 与上文：依赖构造阶段绑定的 _group 与网格列数
            // 作用：按搜索词空白与否切换全量或空格分词 AND 匹配投影，并自动钳制滚动行范围
            _projectedGlobalSlots.Clear();
            int totalCapacity = _group.TotalCapacity;

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                IsFilterActive = false;
                for (int i = 0; i < totalCapacity; i++)
                {
                    _projectedGlobalSlots.Add(i);
                }
            }
            else
            {
                IsFilterActive = true;
                string[] tokens = searchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < totalCapacity; i++)
                {
                    Item? item = _group.GetSlot(i);
                    if (item == null || item.Stack <= 0)
                    {
                        continue;
                    }

                    if (MatchesAllTokens(item, tokens))
                    {
                        _projectedGlobalSlots.Add(i);
                    }
                }
            }

            ScrollRow = _scrollRow;
        }

        public bool TryMapVisibleSlot(int visibleIndex, out int globalSlot)
        {
            // 与上文：基于 RebuildProjection 生成的有序投影列表与当前 ScrollRow
            // 作用：将可见网格索引映射到真实全局槽位，尾行未占满的虚位返回 false
            if (visibleIndex < 0 || visibleIndex >= VisibleCapacity)
            {
                globalSlot = -1;
                return false;
            }

            int projectedIndex = ScrollRow * Columns + visibleIndex;
            if (projectedIndex < 0 || projectedIndex >= _projectedGlobalSlots.Count)
            {
                globalSlot = -1;
                return false;
            }

            globalSlot = _projectedGlobalSlots[projectedIndex];
            return true;
        }

        public int Count => VisibleCapacity;

        public bool IsReadOnly => false;

        public Item? this[int index]
        {
            get => TryMapVisibleSlot(index, out int globalSlot) ? _group.GetSlot(globalSlot) : null;
            set
            {
                // 与上文：依赖 TryMapVisibleSlot 过滤尾行虚位写入
                // 作用：将可见槽位写入直通到底层真实箱组，虚位赋值保持安全无操作
                if (TryMapVisibleSlot(index, out int globalSlot))
                {
                    _group.SetSlot(globalSlot, value);
                }
            }
        }

        public int IndexOf(Item? item)
        {
            // 与上文：遍历当前滚动窗口内已映射的真实槽位
            // 作用：为 InventoryMenu.ShakeItem 等原版调用查找物品在当前窗口中的可见下标
            for (int i = 0; i < VisibleCapacity; i++)
            {
                if (!TryMapVisibleSlot(i, out int globalSlot))
                {
                    continue;
                }

                Item? slotItem = _group.GetSlot(globalSlot);
                if (item == null)
                {
                    if (slotItem == null)
                    {
                        return i;
                    }
                }
                else if (ReferenceEquals(slotItem, item) || EqualityComparer<Item?>.Default.Equals(slotItem, item))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool Contains(Item? item) => IndexOf(item) >= 0;

        public void CopyTo(Item?[] array, int arrayIndex)
        {
            // 独立逻辑，无上文
            // 作用：按可见窗口顺序复制当前投影页中的物品引用到目标数组
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (arrayIndex < 0 || arrayIndex > array.Length || array.Length - arrayIndex < VisibleCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }

            for (int i = 0; i < VisibleCapacity; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        public IEnumerator<Item?> GetEnumerator()
        {
            // 独立逻辑，无上文
            // 作用：按可见窗口索引顺序枚举当前可见页物品
            for (int i = 0; i < VisibleCapacity; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(Item? item) => throw new NotSupportedException("WindowedSlotList 为固定窗口视图，不支持 Add。");

        public void Clear() => throw new NotSupportedException("WindowedSlotList 为固定窗口视图，不支持 Clear。");

        public void Insert(int index, Item? item) => throw new NotSupportedException("WindowedSlotList 为固定窗口视图，不支持 Insert。");

        public bool Remove(Item? item) => throw new NotSupportedException("WindowedSlotList 为固定窗口视图，不支持 Remove。");

        public void RemoveAt(int index) => throw new NotSupportedException("WindowedSlotList 为固定窗口视图，不支持 RemoveAt。");

        private static bool MatchesAllTokens(Item item, string[] tokens)
        {
            // 与上文：在 RebuildProjection 过滤分支中被逐物品调用
            // 作用：忽略大小写检查所有分词是否直接命中名称/分类，未直接命中时对纯字母词启用拼音与简拼匹配
            string displayName = ReadTextSafe(() => item.DisplayName);
            string internalName = ReadTextSafe(() => item.Name);
            string categoryName = ReadTextSafe(() => item.getCategoryName());

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                bool matched = displayName.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || internalName.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || categoryName.Contains(token, StringComparison.OrdinalIgnoreCase);

                if (!matched && TryExtractAsciiPinyinToken(token, out string cleanToken))
                {
                    matched = PinyinLexicon.Instance.MatchesPinyinOrInitials(displayName, cleanToken)
                        || PinyinLexicon.Instance.MatchesPinyinOrInitials(categoryName, cleanToken);
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryExtractAsciiPinyinToken(string token, out string cleanToken)
        {
            // 与上文：MatchesAllTokens 在字面文本未命中后调用
            // 作用：剥离拼音分词撇号并校验是否全部由 ASCII 字母组成，输出纯小写查询串
            if (string.IsNullOrEmpty(token))
            {
                cleanToken = string.Empty;
                return false;
            }

            var sb = new StringBuilder(token.Length);
            for (int i = 0; i < token.Length; i++)
            {
                char ch = token[i];
                if (ch == '\'')
                {
                    continue;
                }

                if (ch >= 'a' && ch <= 'z')
                {
                    sb.Append(ch);
                }
                else if (ch >= 'A' && ch <= 'Z')
                {
                    sb.Append((char)(ch - 'A' + 'a'));
                }
                else
                {
                    cleanToken = string.Empty;
                    return false;
                }
            }

            cleanToken = sb.ToString();
            return cleanToken.Length > 0;
        }

        private static string ReadTextSafe(Func<string?> getter)
        {
            // 独立逻辑，无上文
            // 作用：安全读取物品文本属性，防止未初始化语言环境时抛出空引用异常
            try
            {
                return getter() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
