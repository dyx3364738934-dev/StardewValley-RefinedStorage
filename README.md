# RefinedStorage（精致存储）

Stardew Valley 1.6+ SMAPI Mod。将 8 方向相邻的玩家箱子自动连通为一个可滚动、可搜索、可直接存取整理的统一存储视图，同时保留每个箱子的真实槽位与容量上限。

## 核心特性

- **开箱自动展开联排网络**：打开任意相连箱子即展示全组物品，12 列（纯小箱）或 14 列（含大箱子）自适应布局。
- **零私有物品池**：物品始终直接存放在各箱子的真实 `Items` 列表内，兼容原版存档与卸载安全。
- **严格容器白名单**：仅连通玩家普通箱子（36 格）与大箱子（70 格），自动排除祝尼魔箱、迷你出货箱、冰箱、礼物盒、自动装填器、施肥器、全局库存箱与非玩家箱；开启 `AllowConcurrentOpen`（默认）时支持多人同开不受他人锁箱排斥，关闭时自动排除他人正在锁定的箱子。
- **多人联机并发开箱与实时同步（v2.1.1）**：默认允许多个玩家同时打开同一个箱子或同一组联排箱子（互不踢出、不独占互斥锁），每帧零分配增量戳记自动同步队友存取与整理改动；邻居箱被队友拆除时原地安全重连，不会把当前玩家踢出界面。
- **物品栏动态一键堆叠与收藏锁定（v2.3.0）**：完整合并并升级原 `QuickStack` 功能——取消 `Tab` 热键占用，恢复星露谷原版 `Tab` 切换工具栏行为；改为在背包与附近箱子存在可堆叠或可亲和存入物品时，于物品栏左侧动态显示箱子整理 HUD 按钮（随工具栏顶/底翻转与透明度实时联动）。左键点击按钮即可按距离合并进同款未满堆并按联排组亲和补入空位，自动去重整理联排箱组；各种物品依次从玩家身上沿着弧线飞入该 HUD 按钮并触发按钮回弹，同时在左下角显示分物品汇总提示（如 `整理石头x100，木头x999`）。在背包或箱子界面中键点击物品即可切换紫色收藏星标（兼容旧键 `Koko.QuickStack/Favorite`），防止一键堆叠或箱内快速堆叠误吸走常用物品。
- **三层滚轮与共存冲突防护（v2.2.0+）**：全面替代旧版 `Koko.LinkedChests` 与 `Koko.QuickStack`；移除菜单空基类滚轮转发杜绝滚轮切箱劫持，并在启动阶段自动卸载共存 `Koko.LinkedChests` 补丁、安全让渡共存 `Koko.QuickStack` 功能防止双倍触发。
- **原版风格交互**：保留原版木框纹理、调色板、右侧快速堆叠与整理按钮、鼠标滚轮翻页、原版游标滚动条以及多关键词 AND 搜索过滤。
- **游戏内自建中文拼音输入法与简拼直搜（v2.1.0）**：内置 8,105 《通用规范汉字表》字库与游戏全物品词库，聚焦搜索栏时自动屏蔽系统输入法防遮挡与双倍录入；支持原版羊皮纸风候选浮窗、箱内已有物品置顶以及全拼/音节前缀/首字母简拼实时筛物。
- **工作台全场景安全覆盖**：独立工作台自动读取当前场景内所有合法玩家箱子，同时屏蔽非普通玩家箱以防止材料异常消耗。
- **QuickStack 联动**：暴露 `IRefinedStorageApi.TriggerSort(Chest)`，与 QuickStack 一键堆叠无缝协作。

## 搜索与自建拼音输入法操作说明

- **切换中/英模式**：点击搜索框右侧 `[中]`/`[英]` 按钮，或单独轻按一次 `Shift`（左/右 `Shift` 松开且未夹带其他按键）。
- **输入与选词（`[中]` 模式）**：
  - `A-Z`：输入拼音字母（支持 `v`/`u` 等价输入 `ü`，如 `lv`/`lu` -> 绿、`nv`/`nu` -> 女），箱内现有物品对应的词条优先排在 `1.` 号位。
  - `Space`：选当前页第 1 个候选词上屏。
  - `1-5`（主键盘或小键盘）：选当前页第 1-5 个候选词上屏；未拼写时直接输入数字 `1-5`。
  - `-` / `,` / `PageUp` / `Up`（或候选窗 `<` 按钮/滚轮上滑）：候选词上一页。
  - `=` / `.` / `PageDown` / `Down`（或候选窗 `>` 按钮/滚轮下滑）：候选词下一页。
  - 鼠标左键点击候选词：直接选词上屏。
  - `Backspace`：删除一个拼音字母或已上屏字符（长按自动连删）。
  - `Enter`：拼写中时将当前拼音字母直接落入搜索框；非拼写状态则退出搜索框聚焦。
  - `Esc`：拼写中时仅清空当前拼音缓冲（保持聚焦）；非拼写状态则退出搜索框聚焦。
- **拼音与简拼实时直搜**：无论处于 `[中]` 拼写状态还是 `[英]` 英文状态，均可直接按全拼（如 `mutou` -> 木头）、音节前缀（如 `fangf` -> 防风草）或首字母简拼（如 `ym` -> 硬木、`wcsp` -> 五彩碎片、`lbs` -> 绿宝石）实时筛选箱内物品，并支持空格分隔多关键词 AND 过滤。

## 配置项 (`config.json`)

| 配置项 | 说明 | 默认值 |
| --- | --- | --- |
| `EnableExpandedStorage` | 开箱自动展开联排箱子合并存储界面 | `true` |
| `EnableSearchBar` | 在合并存储窗口顶部显示搜索过滤栏 | `true` |
| `EnableLinkedSort` | 启用跨箱联排整理功能与对外 API | `true` |
| `EnableWorkbenchRangeBoost` | 工作台读取同场景全部合法玩家箱子 | `true` |
| `ShowNotification` | 整理操作完成后显示 HUD 分物品汇总提示 | `true` |
| `ExpandSingleChest` | 单个未连排箱子是否也启用精致存储界面 | `false` |
| `EnableBuiltInPinyinIme` | 启用游戏内自建原版风拼音输入法并隔离系统 IME | `true` |
| `AllowConcurrentOpen` | 允许联机多人并发打开同一箱子或联排箱组并实时同步变更 | `true` |
| `EnableQuickStack` | 启用物品栏左侧动态一键堆叠按钮与联排亲和存入 | `true` |
| `QuickStackRange` | 关闭全房堆叠时的搜索半径（格数） | `5` |
| `StackToWholeRoom` | 一键堆叠是否作用于当前场景全部合法玩家箱 | `true` |
| `ExcludeTools` | 一键堆叠自动跳过工具（`Category == -99`） | `true` |
| `ExcludeWeapons` | 一键堆叠自动跳过近战武器（`Category == -98`） | `true` |
| `EnableQuickStackAnimation` | 一键堆叠完成后播放物品依次从玩家飞入整理按钮动画 | `true` |
| `AutoSortAfterQuickStack` | 一键堆叠完成后对受影响箱组自动执行一次跨箱整理 | `true` |
| `DepositToEmptySlots` | 同款堆叠满后自动向同箱组空位继续存入同类物品 | `true` |
| `EnableFavoriteLock` | 启用物品收藏锁定星标并跳过一键堆叠与箱内快速堆叠 | `true` |
| `FavoriteKey` | 在背包或箱子界面切换物品收藏锁定的快捷键 | `MouseMiddle` |

## 依赖与兼容

- Stardew Valley 1.6+
- SMAPI 4.0.0+
- 已完整替代旧版 `Koko.LinkedChests`（滚动箱子）与 `Koko.QuickStack`（一键堆叠），请勿在 `Mods` 目录重复安装旧版；如意外双加载，启动时将自动卸载 `Koko.LinkedChests` 冲突补丁并对 `Koko.QuickStack` 让渡功能与星标绘制
- 对外暴露 `IRefinedStorageApi.TriggerSort(Chest)`（UniqueID `Koko.RefinedStorage`）

## 源码与发布

- GitHub 仓库：https://github.com/dyx3364738934-dev/StardewValley-RefinedStorage
- 联动 Mod（Quick Stack）：https://github.com/dyx3364738934-dev/StardewValley-QuickStack
