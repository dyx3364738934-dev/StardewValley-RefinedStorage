using StardewModdingAPI;

namespace RefinedStorage
{
    public sealed class RefinedStorageConfig
    {
        public bool EnableExpandedStorage { get; set; } = true;
        public bool EnableSearchBar { get; set; } = true;
        public bool EnableLinkedSort { get; set; } = true;
        public bool EnableWorkbenchRangeBoost { get; set; } = true;
        public bool ShowNotification { get; set; } = true;
        public bool ExpandSingleChest { get; set; } = false;
        public bool EnableBuiltInPinyinIme { get; set; } = true;
        public bool AllowConcurrentOpen { get; set; } = true;

        public bool EnableQuickStack { get; set; } = true;
        public int QuickStackRange { get; set; } = 5;
        public bool StackToWholeRoom { get; set; } = true;
        public bool ExcludeTools { get; set; } = true;
        public bool ExcludeWeapons { get; set; } = true;
        public bool EnableQuickStackAnimation { get; set; } = true;
        public bool AutoSortAfterQuickStack { get; set; } = true;
        public bool DepositToEmptySlots { get; set; } = true;
        public bool EnableFavoriteLock { get; set; } = true;
        public SButton FavoriteKey { get; set; } = SButton.MouseMiddle;
    }
}
