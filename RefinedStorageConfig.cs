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
    }
}
