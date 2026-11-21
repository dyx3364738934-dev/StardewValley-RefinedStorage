using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace RefinedStorage.Storage
{
    public static class ChestNetwork
    {
        public static readonly Vector2[] AdjacentOffsets =
        {
            new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1),
            new Vector2(-1,  0),                      new Vector2(1,  0),
            new Vector2(-1,  1), new Vector2(0,  1), new Vector2(1,  1)
        };

        public static List<(Chest Chest, Vector2 Tile)> FindLinkedChests(Chest anchorChest, GameLocation location)
        {
            // 独立逻辑，无上文
            // 作用：校验锚点箱白名单并在缺少场景对象字典时回退返回合法单箱或空列表
            if (!ChestFilter.IsLinkablePlayerChest(anchorChest))
                return new List<(Chest Chest, Vector2 Tile)>();

            if (location?.objects == null)
                return new List<(Chest Chest, Vector2 Tile)> { (anchorChest, anchorChest.TileLocation) };

            // 与上文：已确认锚点箱合法且场景对象字典可用
            // 作用：收集场景内全部白名单箱的坐标映射并定位锚点箱所在格
            var tileToChest = new Dictionary<Vector2, Chest>();
            Vector2? startTile = null;
            foreach (KeyValuePair<Vector2, StardewValley.Object> pair in location.objects.Pairs)
            {
                if (pair.Value is not Chest chest || !ChestFilter.IsLinkablePlayerChest(chest))
                    continue;

                tileToChest[pair.Key] = chest;
                if (ReferenceEquals(chest, anchorChest))
                    startTile = pair.Key;
            }

            if (!startTile.HasValue)
                return new List<(Chest Chest, Vector2 Tile)> { (anchorChest, anchorChest.TileLocation) };

            // 与上文：已获取锚点坐标与全场景可连通箱映射表
            // 作用：执行 8 方向 BFS 查找整组连通箱并按 Y 升序、X 升序排序返回
            var result = new List<(Chest Chest, Vector2 Tile)>();
            var visited = new HashSet<Vector2> { startTile.Value };
            var seenChests = new HashSet<Chest>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<Vector2>();
            queue.Enqueue(startTile.Value);

            while (queue.Count > 0)
            {
                Vector2 tile = queue.Dequeue();
                if (!tileToChest.TryGetValue(tile, out Chest? chest))
                    continue;

                if (seenChests.Add(chest))
                    result.Add((chest, tile));

                for (int i = 0; i < AdjacentOffsets.Length; i++)
                {
                    Vector2 neighbor = tile + AdjacentOffsets[i];
                    if (tileToChest.ContainsKey(neighbor) && visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            result.Sort((a, b) =>
            {
                int cmpY = a.Tile.Y.CompareTo(b.Tile.Y);
                return cmpY != 0 ? cmpY : a.Tile.X.CompareTo(b.Tile.X);
            });

            return result;
        }
    }
}
