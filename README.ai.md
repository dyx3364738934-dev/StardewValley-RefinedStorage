# 简易存储 (RefinedStorage) - AI 维护工程契约

## 1. 身份与构建规格
- **Mod 名称 / ID / 版本**：`简易存储` / `Koko.RefinedStorage` / `2.4.1`（`EntryDll: RefinedStorage.dll`）
- **运行目标**：Stardew Valley 1.6+、SMAPI 4.0.0+、`.NET 6.0`（`net6.0`）
- **外部依赖特性**：`RefinedStorage.csproj` 显式引用 `SMAPI.Toolkit.CoreInterfaces.dll`（`Private=false`），用于跨程序集解析 `IManifest` 供 `IGenericModConfigMenuApi` 代理匹配；内嵌资源 `Assets\pinyin_chars.txt` 的逻辑名为 `RefinedStorage.Assets.pinyin_chars.txt`。

## 2. 轻量化与代码纪律铁律
- **配置上限（严格 6 项）**：`RefinedStorageConfig` 仅保留 `ExpandSingleChest`（`false`）、`StackToWholeRoom`（`true`）、`EnableChestHoverBubble`（`true`）、`EnableWorkbenchRangeBoost`（`true`）、`FavoriteKey`（`MouseMiddle`）、`ItemRadarKey`（`LeftControl + F`）。严禁新增开关、调试微调或热配置；手边范围固定为常量 `QuickStackService.DefaultNearbyStackRangeTiles = 5`。
- **零私有物品池**：所有物品直接保存在各原版 `Chest.Items` 内，兼容原版存档与随时卸载。
- **注释与编码规范**：全部源码与文档必须 UTF-8 无 BOM、0 emoji；所有新增或改动代码块首必须保留固定两行中文块首注释（`// 与上文：...` 或 `// 独立逻辑，无上文` + `// 作用：...`），块内不逐行注释。
- **日志克制**：启动时 `LogLevel.Info` 严格保留 `ModEntry.Entry` 中 1 行加载日志；Harmony 补丁挂载与词库规模使用 `LogLevel.Trace`。

## 3. 目录与模块职责
- `ModEntry.cs` / `HarmonyPatches.cs`：Mod 入口、事件装配、对外暴露 `IRefinedStorageApi.TriggerSort(Chest)`，以及 4 个 Harmony 补丁（`Chest.checkForAction`、`Chest.ShowMenu`、`CraftingPage.getContainerContents`、`Chest.draw`）。
- `Integration/`：`IGenericModConfigMenuApi.cs` 与 `GmcmRegistrar.cs`，软接入 GMCM 扁平注册 6 项（禁止 `AddParagraph` 与 `AddSectionTitle`）。
- `Storage/`：
  - `ChestFilter.cs`：`IsLinkablePlayerChest` 仅放行合法玩家普通箱与大箱（排除祝尼魔箱、迷你出货箱、冰箱、礼物盒、装填器/施肥器及全局库存箱）。
  - `ChestNetwork.cs`：8 方向 BFS 连通同场景合法玩家箱并按 `(Y, X)` 排序。
  - `LinkedChestGroup.cs`：混搭容量（36/70）统一槽位地址映射、`PreferredColumns`（12/14）及 `ComputeChangeStamp()` 零堆分配变更戳。
  - `StorageOps.cs`：`Deposit`、`FillOutStacks`、`Tidy`（最小差异写回与数量守恒 `RolledBack` 事务保护）。
  - `StorageOpQueue.cs`：按组规范键 `GetGroupKey`（`minY, minX`）串行排队执行跨箱整理与一键堆叠，支持联机客机转交房主。
- `QuickStack/`：`FavoriteLock.cs`（兼容键 `Koko.QuickStack/Favorite`）、`QuickStackService.cs`（两趟存入 + 亲和延展 + `SimulateFinalPlan` 只读推演）、`QuickStackController.cs`（HUD 左侧动态按钮、贝塞尔飞入动画与「存入 [纹理]x个数」视觉悬停盒）。
- `UI/`：`RefinedChestMenu.cs`（合并开箱菜单）、`WindowedSlotList.cs`（滚动与多词 AND / 拼音过滤）、`InGamePinyinBox.cs`（游戏内自建中文输入框）、`ChestHoverPreviewController.cs`（0ms 地上箱大网格悬停与纯纹理闪烁）、`WorldBubbleRenderer.cs`（`4.0f` / `80x96` 世界落点气泡）、`ItemRadarController.cs`（同场景找物雷达）。
- `Ime/` & `Assets/`：`NativeImeSuppressor.cs`（`GetActiveWindow` + `ImmAssociateContext` + `SDL_StopTextInput` 隔离系统 IME）、`PinyinEngine.cs`、`PinyinLexicon.cs`、`Assets\pinyin_chars.txt`。

## 4. 核心技术不变量
- **SDV 1.6 分类码**：工具分类为 `-99`（`StardewValley.Object.toolCategory`），近战武器分类为 `-98`（`StardewValley.Object.weaponCategory`）；`QuickStackService.ShouldExclude` 恒跳过工具、近战武器与收藏物品。
- **箱体纯纹理闪烁**：`HarmonyPatches.Postfix_ChestDraw` 挂载于 4 参 `Chest.draw(SpriteBatch, int, int, float)` 并注入 `___currentLidFrame`；仅在 `ChestHoverPreviewController.IsActivelyHoveredChest(__instance)` 为真时调用 `DrawHoveredChestTextureFlash`，利用 `BuildWhiteSilhouettePixels` 合并箱体与箱盖 Alpha 并集纯白掩膜及 `ResolveChestSpriteSourceIndices` 精确匹配原版与染色箱源矩形（`(BC)130`、`BigChest`、`232`、`BigStoneChest` 等）；地上悬停检测 `TryFindHoveredChest` 同时命中底座格与上方半格箱盖（`cursorWorldY >= belowTile.Y * 64f - 52f`）。
- **多人并发开箱**：`Prefix_ChestCheckForAction` 在他人持有锁时安全复刻开箱并跳过原版锁阻塞，`Postfix_ChestShowMenu` 在单箱早退前仅释放本机 `IsLockHeld()` 锁。
- **滚轮与旧 Mod 冲突免疫**：`RefinedChestMenu.receiveScrollWheelAction` 不调用 `base` 以免疫 `IClickableMenu` 滚轮劫持；`OnGameLaunched` 对共存 `Koko.LinkedChests` 执行 `UnpatchAll("Koko.LinkedChests")`，对共存 `Koko.QuickStack` 设置 `IsSuppressedByExternalMod = true`。

## 5. 构建、测试、部署与双仓发布约定
- **Release 编译**：`dotnet build "C:\Users\33647\Desktop\大宗\stardew-smapi\smapi开发\RefinedStorage\RefinedStorage.csproj" -c Release --no-incremental`（必须 0 警告 0 错误）。
- **自动化回归**：运行 `verify_rs` 确认 `PASS=105, FAIL=0`。
- **游戏目录部署**：仅向 `E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\RefinedStorage\` 部署运行时 5 项（`RefinedStorage.dll`、`RefinedStorage.pdb`、`RefinedStorage.deps.json`、`manifest.json`、`README.md`），不放置 `README.ai.md` 并完整保留玩家 `config.json`。
- **安装包与双仓**：发布 Zip 严格仅含上述 5 个正斜杠 `/` 条目（不含 `config.json` 与 `README.ai.md`）；独立仓 `smapi-repos\RefinedStorage`（`dyx3364738934-dev/StardewValley-RefinedStorage` `master`）仅同步源码与双文档，严禁提交 `bin/`、`obj/`、`releases/`、`*.dll`、`*.pdb`、`*.zip` 与 `config.json`；总仓 `smapi开发` 仅本地提交、禁止 push。
