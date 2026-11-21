using HarmonyLib;
using RefinedStorage.Ime;
using RefinedStorage.QuickStack;
using RefinedStorage.Storage;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;

namespace RefinedStorage
{
    public interface IRefinedStorageApi
    {
        void TriggerSort(Chest chest);
    }

    public sealed class RefinedStorageApiImpl : IRefinedStorageApi
    {
        public void TriggerSort(Chest chest)
        {
            // 独立逻辑，无上文
            // 作用：校验联排整理开关与场景后，触发指定箱子所属联排网络跨箱整理
            if (chest == null || !ModEntry.Instance.Config.EnableLinkedSort)
                return;

            GameLocation? location = chest.Location ?? Game1.player?.currentLocation;
            if (location == null)
                return;

            StorageOps.TidyChests(location, chest, out _, out _);
        }
    }

    public sealed class ModEntry : Mod
    {
        internal static ModEntry Instance { get; private set; } = null!;
        internal RefinedStorageConfig Config { get; private set; } = null!;
        private Harmony? _harmony;
        private QuickStackController? _quickStackController;

        public override void Entry(IModHelper helper)
        {
            // 独立逻辑，无上文
            // 作用：保存 Mod 单例与用户配置，装配一键堆叠控制器、注册启动与存档词库事件并挂载 Harmony 补丁
            Instance = this;
            Config = helper.ReadConfig<RefinedStorageConfig>();

            _quickStackController = new QuickStackController(helper, () => Config);
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;

            _harmony = new Harmony("Koko.RefinedStorage");
            HarmonyPatches.Apply(_harmony);

            string features = "邻居箱子自动连通";
            if (Config.EnableExpandedStorage)
                features += " + 开箱展开合并存储";
            if (Config.AllowConcurrentOpen)
                features += " + 多人并发开箱";
            if (Config.EnableWorkbenchRangeBoost)
                features += " + 工作台全场景覆盖";
            if (Config.EnableBuiltInPinyinIme)
                features += " + 游戏内自建拼音输入法";
            if (Config.EnableQuickStack || Config.EnableFavoriteLock)
                features += " + 物品栏一键堆叠与收藏锁定";
            Monitor.Log($"精致存储（RefinedStorage v2.3.0）已加载：{features}。", LogLevel.Info);
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            // 与上文：Entry 已完成事件与 Harmony 初始化，此时所有其他 Mod 已加载进注册表
            // 作用：检测旧版滚动箱子与外置一键堆叠，卸载冲突滚轮补丁并抑制内置热键绘制以防止双倍触发
            if (Helper.ModRegistry.IsLoaded("Koko.LinkedChests"))
            {
                Monitor.Log(
                    "检测到旧版滚动箱子（Koko.LinkedChests）仍在加载，已卸载其 Harmony 补丁以防滚轮切箱冲突；建议从 Mods 目录移除 LinkedChests。",
                    LogLevel.Warn);
                _harmony?.UnpatchAll("Koko.LinkedChests");
            }

            if (Helper.ModRegistry.IsLoaded("Koko.QuickStack"))
            {
                Monitor.Log(
                    "检测到独立版一键堆叠（Koko.QuickStack）仍在加载，已让渡内置一键堆叠按钮与收藏星标绘制以防双倍触发；建议从 Mods 目录移除 QuickStack。",
                    LogLevel.Warn);
                if (_quickStackController != null)
                    _quickStackController.IsSuppressedByExternalMod = true;
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            // 与上文：Entry 已完成基础配置与补丁注册，此时存档与游戏物品注册表已完全就绪
            // 作用：预热游戏全物品中文名称至拼音词库并记录单字与词条规模
            PinyinLexicon.Instance.EnsureGameVocabularyLoaded();
            Monitor.Log(
                $"拼音词库已就绪：收录单字 {PinyinLexicon.Instance.CharacterCount} 个，词条 {PinyinLexicon.Instance.PhraseCount} 条。",
                LogLevel.Trace);
        }

        public override object? GetApi()
        {
            return new RefinedStorageApiImpl();
        }
    }
}
