# 精致存储（RefinedStorage）

相邻箱子合并存储，支持找物与一键堆叠。

## 功能
- 相邻玩家箱子（8 方向）开箱自动合并显示，支持跨箱整理与中文拼音搜索。
- 鼠标移到地上箱子立即弹出内容预览，悬停时箱体闪烁。
- 物品栏左侧按需显示一键堆叠按钮，悬停可预览即将存入的物品。
- 工作台可读取同场景玩家箱子内的材料制作。

## 操作按键
- `MouseMiddle`（默认中键）：悬停物品切换收藏锁定，防一键堆叠吸走。
- `LeftControl + F`（默认）：开关同场景找物雷达；输入时 `Shift` 切中/英，`Esc` 关闭。

## 配置项（`config.json` / GMCM）
| 配置项 | 说明 | 默认值 |
| --- | --- | --- |
| `ExpandSingleChest` | 单箱也用大窗口 | `false` |
| `StackToWholeRoom` | 全屋一键堆叠（关掉仅堆叠5格内箱） | `true` |
| `EnableChestHoverBubble` | 箱子悬停气泡 | `true` |
| `EnableWorkbenchRangeBoost` | 工作台全场景取料 | `true` |
| `FavoriteKey` | 收藏锁定按键 | `MouseMiddle` |
| `ItemRadarKey` | 找物雷达热键 | `LeftControl + F` |

## 运行要求
- Stardew Valley 1.6+、SMAPI 4.0.0+；可选安装 Generic Mod Config Menu。
- 请勿同时安装旧版 `LinkedChests` 与 `QuickStack`。