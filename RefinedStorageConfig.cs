using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace RefinedStorage
{
    public sealed class RefinedStorageConfig
    {
        public bool ExpandSingleChest { get; set; } = false;
        public bool StackToWholeRoom { get; set; } = true;
        public bool EnableChestHoverBubble { get; set; } = true;
        public bool EnableWorkbenchRangeBoost { get; set; } = true;
        public SButton FavoriteKey { get; set; } = SButton.MouseMiddle;
        public KeybindList ItemRadarKey { get; set; } = KeybindList.Parse("LeftControl + F");
    }
}
