using System;
using GenericModConfigMenu;
using StardewModdingAPI;

namespace RefinedStorage.Integration
{
    public static class GmcmRegistrar
    {
        private const string GmcmUniqueId = "spacechase0.GenericModConfigMenu";

        public static void Register(
            IModHelper helper,
            IManifest manifest,
            Func<RefinedStorageConfig> getConfig,
            Action resetConfig,
            Action saveConfig)
        {
            // 独立逻辑，无上文
            // 作用：在游戏启动完成阶段软加载 GMCM 接口并扁平注册 6 项配置，未安装 GMCM 时静默跳过
            if (helper == null || manifest == null || getConfig == null || resetConfig == null || saveConfig == null)
                return;

            IGenericModConfigMenuApi? api = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GmcmUniqueId);
            if (api == null)
                return;

            api.Register(
                mod: manifest,
                reset: resetConfig,
                save: saveConfig);

            api.AddBoolOption(
                mod: manifest,
                getValue: () => getConfig().ExpandSingleChest,
                setValue: value => getConfig().ExpandSingleChest = value,
                name: () => "单箱也用大窗口",
                tooltip: null,
                fieldId: nameof(RefinedStorageConfig.ExpandSingleChest));

            api.AddBoolOption(
                mod: manifest,
                getValue: () => getConfig().StackToWholeRoom,
                setValue: value => getConfig().StackToWholeRoom = value,
                name: () => "全屋一键堆叠",
                tooltip: () => "关掉仅堆叠5格内箱",
                fieldId: nameof(RefinedStorageConfig.StackToWholeRoom));

            api.AddBoolOption(
                mod: manifest,
                getValue: () => getConfig().EnableChestHoverBubble,
                setValue: value => getConfig().EnableChestHoverBubble = value,
                name: () => "箱子悬停气泡",
                tooltip: null,
                fieldId: nameof(RefinedStorageConfig.EnableChestHoverBubble));

            api.AddBoolOption(
                mod: manifest,
                getValue: () => getConfig().EnableWorkbenchRangeBoost,
                setValue: value => getConfig().EnableWorkbenchRangeBoost = value,
                name: () => "工作台全场景取料",
                tooltip: null,
                fieldId: nameof(RefinedStorageConfig.EnableWorkbenchRangeBoost));

            api.AddKeybind(
                mod: manifest,
                getValue: () => getConfig().FavoriteKey,
                setValue: value => getConfig().FavoriteKey = value,
                name: () => "收藏锁定按键",
                tooltip: () => "悬停按此键锁定防吸走",
                fieldId: nameof(RefinedStorageConfig.FavoriteKey));

            api.AddKeybindList(
                mod: manifest,
                getValue: () => getConfig().ItemRadarKey,
                setValue: value => getConfig().ItemRadarKey = value,
                name: () => "找物雷达热键",
                tooltip: null,
                fieldId: nameof(RefinedStorageConfig.ItemRadarKey));
        }
    }
}
