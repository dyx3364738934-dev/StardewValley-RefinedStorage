using StardewValley.Network;
using StardewValley.Objects;

namespace RefinedStorage.Storage
{
    public static class ChestFilter
    {
        public static bool IsLinkablePlayerChest(Chest? chest)
        {
            // 独立逻辑，无上文
            // 作用：严格校验玩家普通箱与大箱白名单，排除祝尼魔箱、迷你出货箱、冰箱、礼物盒与全局箱等特殊容器
            if (chest == null)
                return false;

            if (chest.playerChest?.Value != true || chest.fridge?.Value == true || chest.giftbox?.Value == true)
                return false;

            if (chest.globalInventoryId?.Value != null)
                return false;

            if (chest.specialChestType == null)
                return false;

            return chest.SpecialChestType == Chest.SpecialChestTypes.None
                || chest.SpecialChestType == Chest.SpecialChestTypes.BigChest;
        }

        public static bool IsLockedByOther(Chest chest, Chest anchorChest)
        {
            // 与上文：在连通发现与状态校验阶段配合白名单过滤容器
            // 作用：跳过锚点箱自身后，空值安全地检测目标箱 NetMutex 是否正被其他玩家持有锁定
            if (chest == null || ReferenceEquals(chest, anchorChest))
                return false;

            try
            {
                NetMutex? mutex = chest.GetMutex();
                return mutex != null && mutex.IsLocked() && !mutex.IsLockHeld();
            }
            catch
            {
                return false;
            }
        }
    }
}
