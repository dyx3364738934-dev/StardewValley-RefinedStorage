using HarmonyLib;
using RefinedStorage.Ime;
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

        public override void Entry(IModHelper helper)
        {
            // 独立逻辑，无上文
            // 作用：保存 Mod 单例与用户配置，注册存档加载词库预热事件并挂载开箱展开与工作台 Harmony 补丁
            Instance = this;
            Config = helper.ReadConfig<RefinedStorageConfig>();

            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;

            _harmony = new Harmony("Koko.RefinedStorage");
            HarmonyPatches.Apply(_harmony);

            string features = "邻居箱子自动连通";
            if (Config.EnableExpandedStorage)
                features += " + 开箱展开合并存储";
            if (Config.EnableWorkbenchRangeBoost)
                features += " + 工作台全场景覆盖";
            if (Config.EnableBuiltInPinyinIme)
                features += " + 游戏内自建拼音输入法";
            Monitor.Log($"精致存储（RefinedStorage v2.1.0）已加载：{features}。", LogLevel.Info);
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
