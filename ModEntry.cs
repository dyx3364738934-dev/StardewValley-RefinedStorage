using HarmonyLib;
using RefinedStorage.Ime;
using RefinedStorage.Integration;
using RefinedStorage.QuickStack;
using RefinedStorage.Storage;
using RefinedStorage.UI;
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
            // 作用：校验目标箱子与场景后，经由串行组队列触发指定箱子所属联排网络跨箱整理
            if (chest == null)
                return;

            GameLocation? location = chest.Location ?? Game1.player?.currentLocation;
            if (location == null)
                return;

            StorageOpQueue.EnqueueOrExecuteTidy(location, chest, out _, out _, out _);
        }
    }

    public sealed class ModEntry : Mod
    {
        internal static ModEntry Instance { get; private set; } = null!;
        internal RefinedStorageConfig Config { get; private set; } = null!;
        private Harmony? _harmony;
        private QuickStackController? _quickStackController;
        private ItemRadarController? _itemRadarController;
        private ChestHoverPreviewController? _chestHoverPreviewController;

        public override void Entry(IModHelper helper)
        {
            // 独立逻辑，无上文
            // 作用：保存 Mod 单例与用户配置，初始化串行队列、一键堆叠、找物雷达与地上箱悬停预览控制器并挂载 Harmony 补丁
            Instance = this;
            Config = helper.ReadConfig<RefinedStorageConfig>();

            StorageOpQueue.Initialize(helper, () => Config);
            _quickStackController = new QuickStackController(helper, () => Config);
            _itemRadarController = new ItemRadarController(helper, () => Config);
            _chestHoverPreviewController = new ChestHoverPreviewController(
                helper,
                () => Config,
                () => _quickStackController?.IsHoveringHudButton ?? false,
                () => _itemRadarController?.IsOpen ?? false);

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;

            _harmony = new Harmony("Koko.RefinedStorage");
            HarmonyPatches.Apply(_harmony);

            Monitor.Log($"已加载（v{ModManifest.Version}）", LogLevel.Info);
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            // 与上文：Entry 已完成事件与 Harmony 初始化，此时所有其他 Mod 已加载进注册表
            // 作用：软注册 GMCM 配置菜单并检测旧版滚动箱子与外置一键堆叠，卸载冲突补丁且抑制重复绘制
            GmcmRegistrar.Register(
                Helper,
                ModManifest,
                () => Config,
                () => Config = new RefinedStorageConfig(),
                () => Helper.WriteConfig(Config));

            if (Helper.ModRegistry.IsLoaded("Koko.LinkedChests"))
            {
                Monitor.Log("检测到冲突 Mod Koko.LinkedChests，已自动接管，建议移除。", LogLevel.Warn);
                _harmony?.UnpatchAll("Koko.LinkedChests");
            }

            if (Helper.ModRegistry.IsLoaded("Koko.QuickStack"))
            {
                Monitor.Log("检测到冲突 Mod Koko.QuickStack，已自动接管，建议移除。", LogLevel.Warn);
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
