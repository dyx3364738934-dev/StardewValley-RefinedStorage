using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.Xna.Framework;
using RefinedStorage.QuickStack;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;

namespace RefinedStorage.Storage
{
    public static class StorageOpQueue
    {
        public const string TidyRequestMessageType = "RefinedStorage.TidyGroupRequest";
        private const string ModUniqueId = "Koko.RefinedStorage";

        private sealed class TidyGroupMessage
        {
            public string LocationName { get; set; } = string.Empty;
            public int TileX { get; set; }
            public int TileY { get; set; }
        }

        private sealed class QueuedTask
        {
            public Action Execute { get; init; } = null!;
            public bool Completed { get; set; }
            public Exception? Error { get; set; }
        }

        private sealed class GroupQueueState
        {
            public object Sync { get; } = new object();
            public Queue<QueuedTask> Queue { get; } = new Queue<QueuedTask>();
            public bool IsRunning { get; set; }
            public int OwnerThreadId { get; set; }
            public int ActiveUsers { get; set; }
        }

        private static readonly object QueuesLock = new object();
        private static readonly Dictionary<string, GroupQueueState> Queues = new Dictionary<string, GroupQueueState>(StringComparer.Ordinal);
        private static IModHelper? _helper;
        private static int _pendingCount;

        public static int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));

        public static void Initialize(IModHelper helper, Func<RefinedStorageConfig> getConfig)
        {
            // 独立逻辑，无上文
            // 作用：保存 SMAPI 辅助接口并校验配置委托，订阅联机 Mod 消息以串行处理客机发来的整理请求
            if (helper == null)
                throw new ArgumentNullException(nameof(helper));
            if (getConfig == null)
                throw new ArgumentNullException(nameof(getConfig));

            if (_helper != null)
            {
                try
                {
                    _helper.Events.Multiplayer.ModMessageReceived -= OnModMessageReceived;
                }
                catch
                {
                }
            }

            _helper = helper;
            helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        public static string GetGroupKey(GameLocation? location, IReadOnlyList<(Chest Chest, Vector2 Tile)> members)
        {
            // 独立逻辑，无上文
            // 作用：基于场景名与整组最小 (Y, X) 坐标计算确定性规范键，确保点击同组任意箱子命中同一串行队列
            string locName = GetLocationKey(location);
            if (members == null || members.Count == 0)
                return $"{locName}|empty";

            Vector2 minTile = members[0].Tile;
            for (int i = 1; i < members.Count; i++)
            {
                Vector2 tile = members[i].Tile;
                int cmpY = tile.Y.CompareTo(minTile.Y);
                if (cmpY < 0 || (cmpY == 0 && tile.X.CompareTo(minTile.X) < 0))
                    minTile = tile;
            }

            int minY = (int)MathF.Round(minTile.Y);
            int minX = (int)MathF.Round(minTile.X);
            return string.Create(CultureInfo.InvariantCulture, $"{locName}|{minY},{minX}");
        }

        public static OpResult EnqueueOrExecuteTidy(
            GameLocation location,
            Chest anchorChest,
            out int chestCount,
            out int stackCount,
            out bool deferredToHost)
        {
            // 独立逻辑，无上文
            // 作用：计算锚点连通组与规范队列键，在客机且房主安装同版 Mod 时转发房主串行整理，否则在本地按组键 FIFO 串行执行
            chestCount = 0;
            stackCount = 0;
            deferredToHost = false;

            if (location == null || anchorChest == null || !ChestFilter.IsLinkablePlayerChest(anchorChest))
                return OpResult.NothingThere;

            List<(Chest Chest, Vector2 Tile)> members = ChestNetwork.FindLinkedChests(anchorChest, location);
            if (members.Count == 0)
                return OpResult.NothingThere;

            Vector2 anchorTile = anchorChest.TileLocation;
            for (int i = 0; i < members.Count; i++)
            {
                if (ReferenceEquals(members[i].Chest, anchorChest))
                {
                    anchorTile = members[i].Tile;
                    break;
                }
            }

            string groupKey = GetGroupKey(location, members);
            if (Context.IsWorldReady &&
                !Context.IsMainPlayer &&
                HasRemoteHostMod(out IModHelper? helper))
            {
                helper!.Multiplayer.SendMessage(
                    new TidyGroupMessage
                    {
                        LocationName = GetLocationKey(location),
                        TileX = (int)MathF.Round(anchorTile.X),
                        TileY = (int)MathF.Round(anchorTile.Y)
                    },
                    TidyRequestMessageType,
                    modIDs: new[] { ModUniqueId },
                    playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID });

                chestCount = members.Count;
                stackCount = CountOccupiedStacks(members);
                deferredToHost = true;
                return OpResult.Ok;
            }

            OpResult result = OpResult.NothingThere;
            int localChestCount = 0;
            int localStackCount = 0;

            RunSerialized("tidy|" + groupKey, () =>
            {
                result = StorageOps.TidyChests(location, anchorChest, out localChestCount, out localStackCount);
            });

            chestCount = localChestCount;
            stackCount = localStackCount;
            return result;
        }

        public static QuickStackResult EnqueueOrExecuteQuickStack(
            Farmer player,
            GameLocation location,
            RefinedStorageConfig config)
        {
            // 独立逻辑，无上文
            // 作用：按场景规范键串行化执行一键堆叠，防止同帧并发触发或重入踩踏背包与箱子状态
            if (player == null || location == null || config == null)
                return new QuickStackResult();

            string locKey = "quickstack|" + GetLocationKey(location);
            QuickStackResult result = new QuickStackResult();
            RunSerialized(locKey, () =>
            {
                result = QuickStackService.Execute(player, location, config);
            });

            return result;
        }

        public static void ResetForTests()
        {
            // 独立逻辑，无上文
            // 作用：清空全部串行队列状态并重置挂起计数及事件订阅，供无头单元测试隔离清理
            if (_helper != null)
            {
                try
                {
                    _helper.Events.Multiplayer.ModMessageReceived -= OnModMessageReceived;
                }
                catch
                {
                }
            }

            _helper = null;

            List<GroupQueueState> snapshot;
            lock (QueuesLock)
            {
                snapshot = new List<GroupQueueState>(Queues.Values);
                Queues.Clear();
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                GroupQueueState state = snapshot[i];
                lock (state.Sync)
                {
                    while (state.Queue.Count > 0)
                    {
                        QueuedTask task = state.Queue.Dequeue();
                        task.Completed = true;
                    }
                    state.IsRunning = false;
                    state.OwnerThreadId = 0;
                    Monitor.PulseAll(state.Sync);
                }
            }

            Volatile.Write(ref _pendingCount, 0);
        }

        private static void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            // 与上文：由 Initialize 订阅 SMAPI 联机消息总线并在房主主线程触发
            // 作用：校验来自客机的整理分组请求合法性并定位场景锚点箱，压入对应规范键串行队列执行跨箱整理
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            if (!string.Equals(e.FromModID, ModUniqueId, StringComparison.Ordinal) ||
                !string.Equals(e.Type, TidyRequestMessageType, StringComparison.Ordinal))
            {
                return;
            }

            TidyGroupMessage? msg;
            try
            {
                msg = e.ReadAs<TidyGroupMessage>();
            }
            catch
            {
                return;
            }

            if (msg == null || string.IsNullOrWhiteSpace(msg.LocationName))
                return;

            GameLocation? location = FindLocationByName(msg.LocationName);
            if (location?.objects == null)
                return;

            Vector2 tile = new Vector2(msg.TileX, msg.TileY);
            if (!location.objects.TryGetValue(tile, out StardewValley.Object? obj) ||
                obj is not Chest chest ||
                !ChestFilter.IsLinkablePlayerChest(chest))
            {
                return;
            }

            List<(Chest Chest, Vector2 Tile)> members = ChestNetwork.FindLinkedChests(chest, location);
            if (members.Count == 0)
                return;

            string groupKey = GetGroupKey(location, members);
            RunSerialized("tidy|" + groupKey, () =>
            {
                StorageOps.TidyChests(location, chest, out _, out _);
            });
        }

        private static void RunSerialized(string queueKey, Action action)
        {
            // 与上文：EnqueueOrExecuteTidy、EnqueueOrExecuteQuickStack 与房主消息处理统一委托本调度器
            // 作用：按规范键维护独立 FIFO 队列，支持同线程重入直通、多线程并发顺序出队并在空闲时自动回收队列槽位
            GroupQueueState state;
            lock (QueuesLock)
            {
                if (!Queues.TryGetValue(queueKey, out state!))
                {
                    state = new GroupQueueState();
                    Queues[queueKey] = state;
                }
                state.ActiveUsers++;
            }

            try
            {
                bool reentrant = false;
                var task = new QueuedTask { Execute = action };
                bool isRunner = false;

                lock (state.Sync)
                {
                    if (state.IsRunning && state.OwnerThreadId == Environment.CurrentManagedThreadId)
                    {
                        reentrant = true;
                    }
                    else
                    {
                        state.Queue.Enqueue(task);
                        Interlocked.Increment(ref _pendingCount);
                        if (!state.IsRunning)
                        {
                            state.IsRunning = true;
                            state.OwnerThreadId = Environment.CurrentManagedThreadId;
                            isRunner = true;
                        }
                        else
                        {
                            while (!task.Completed)
                                Monitor.Wait(state.Sync);
                        }
                    }
                }

                if (reentrant)
                {
                    Interlocked.Increment(ref _pendingCount);
                    try
                    {
                        action();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pendingCount);
                    }
                    return;
                }

                if (isRunner)
                {
                    try
                    {
                        while (true)
                        {
                            QueuedTask? next;
                            lock (state.Sync)
                            {
                                if (state.Queue.Count == 0)
                                {
                                    state.IsRunning = false;
                                    state.OwnerThreadId = 0;
                                    break;
                                }

                                next = state.Queue.Dequeue();
                            }

                            try
                            {
                                next.Execute();
                            }
                            catch (Exception ex)
                            {
                                next.Error = ex;
                            }
                            finally
                            {
                                Interlocked.Decrement(ref _pendingCount);
                                lock (state.Sync)
                                {
                                    next.Completed = true;
                                    Monitor.PulseAll(state.Sync);
                                }
                            }
                        }
                    }
                    finally
                    {
                        lock (state.Sync)
                        {
                            if (state.IsRunning && state.OwnerThreadId == Environment.CurrentManagedThreadId)
                            {
                                state.IsRunning = false;
                                state.OwnerThreadId = 0;
                                Monitor.PulseAll(state.Sync);
                            }
                        }
                    }
                }

                if (task.Error != null)
                    ExceptionDispatchInfo.Capture(task.Error).Throw();
            }
            finally
            {
                lock (QueuesLock)
                {
                    state.ActiveUsers--;
                    if (state.ActiveUsers <= 0 && !state.IsRunning && state.Queue.Count == 0)
                    {
                        if (Queues.TryGetValue(queueKey, out GroupQueueState? existing) && ReferenceEquals(existing, state))
                            Queues.Remove(queueKey);
                    }
                }
            }
        }

        private static bool HasRemoteHostMod(out IModHelper? helper)
        {
            // 与上文：EnqueueOrExecuteTidy 在客机侧判断是否可以将整理请求安全委托给房主
            // 作用：核查本地注册表与房主端 Mod 列表，确认房主已安装不低于 2.4.0 版本的 RefinedStorage
            helper = _helper ?? ModEntry.Instance?.Helper;
            if (helper?.Multiplayer == null || helper.ModRegistry == null || Game1.MasterPlayer == null)
                return false;

            if (helper.ModRegistry.Get(ModUniqueId) == null)
                return false;

            long hostId = Game1.MasterPlayer.UniqueMultiplayerID;
            IMultiplayerPeer? hostPeer = helper.Multiplayer.GetConnectedPlayer(hostId);
            if (hostPeer == null)
            {
                foreach (IMultiplayerPeer peer in helper.Multiplayer.GetConnectedPlayers())
                {
                    if (peer.IsHost)
                    {
                        hostPeer = peer;
                        break;
                    }
                }
            }

            if (hostPeer == null || !hostPeer.HasSmapi)
                return false;

            IMultiplayerPeerMod? hostMod = hostPeer.GetMod(ModUniqueId);
            if (hostMod == null)
                return false;

            object? versionObj = hostMod.GetType().GetProperty("Version")?.GetValue(hostMod);
            string verText = (versionObj?.ToString() ?? string.Empty).Split('-')[0];
            if (Version.TryParse(verText, out Version? parsed))
                return parsed >= new Version(2, 4, 0);

            return true;
        }

        private static GameLocation? FindLocationByName(string locationName)
        {
            // 与上文：OnModMessageReceived 从客机消息解析出目标场景唯一名称
            // 作用：优先通过原版名称索引查找场景，未命中时遍历农舍建筑等动态室内场景兜底匹配
            GameLocation? location = Game1.getLocationFromName(locationName);
            if (location != null)
                return location;

            GameLocation? matched = null;
            Utility.ForEachLocation(loc =>
            {
                if (string.Equals(loc.NameOrUniqueName, locationName, StringComparison.Ordinal))
                {
                    matched = loc;
                    return false;
                }
                return true;
            });

            return matched;
        }

        private static string GetLocationKey(GameLocation? location)
        {
            // 独立逻辑，无上文
            // 作用：统一提取场景稳定键，兼容室内建筑唯一名与无头测试空命名场景
            if (location == null)
                return "__local__";

            if (!string.IsNullOrEmpty(location.NameOrUniqueName))
                return location.NameOrUniqueName;

            if (!string.IsNullOrEmpty(location.Name))
                return location.Name;

            return "__local__";
        }

        private static int CountOccupiedStacks(IReadOnlyList<(Chest Chest, Vector2 Tile)> members)
        {
            // 与上文：客机将整理转发给房主执行后需要填充返回体上的堆叠估算数
            // 作用：只读统计连通箱组内有效非空物品格数量，绝不改动真实箱子库存
            int count = 0;
            for (int m = 0; m < members.Count; m++)
            {
                Chest chest = members[m].Chest;
                var items = chest.Items;
                if (items == null)
                    continue;

                int limit = Math.Min(items.Count, Math.Max(0, chest.GetActualCapacity()));
                for (int s = 0; s < limit; s++)
                {
                    Item? item = items[s];
                    if (item != null && item.Stack > 0)
                        count++;
                }
            }

            return count;
        }
    }
}
