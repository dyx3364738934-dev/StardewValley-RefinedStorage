using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RefinedStorage.Ime;
using RefinedStorage.QuickStack;
using RefinedStorage.Storage;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace RefinedStorage.UI
{
    public sealed class RefinedChestMenu : ItemGrabMenu
    {
        private LinkedChestGroup _group;
        private readonly RefinedStorageConfig _config;
        private WindowedSlotList _window;
        private int _columns;
        private int _visibleRows;
        private long _lastGroupStamp;
        private readonly HashSet<string> _chestItemNames = new HashSet<string>(StringComparer.Ordinal);

        private InGamePinyinBox? _searchBox;
        private ClickableTextureComponent? _clearSearchButton;
        private string _lastSearchText = string.Empty;

        private ClickableTextureComponent? _upArrow;
        private ClickableTextureComponent? _downArrow;
        private ClickableTextureComponent? _scrollBar;
        private Rectangle _scrollBarRunner;
        private bool _scrolling;

        private int _upperBoxDrawX;
        private int _upperBoxDrawY;
        private int _upperBoxDrawWidth;
        private int _upperBoxDrawHeight;
        private Rectangle _upperBoxVisibleBounds;
        private Rectangle _lowerBoxVisibleBounds;

        public LinkedChestGroup Group => _group;

        public RefinedChestMenu(LinkedChestGroup group, RefinedStorageConfig config)
            : base(
                inventory: new List<Item>(),
                reverseGrab: false,
                showReceivingMenu: true,
                highlightFunction: InventoryMenu.highlightAllItems,
                behaviorOnItemSelectFunction: null,
                message: null,
                behaviorOnItemGrab: null,
                snapToBottom: false,
                canBeExitedWithKey: true,
                playRightClickSound: true,
                allowRightClick: true,
                showOrganizeButton: true,
                source: source_chest,
                sourceItem: group?.AnchorChest,
                whichSpecialButton: -1,
                context: group?.AnchorChest,
                heldItemExitBehavior: ItemExitBehavior.ReturnToPlayer,
                allowExitWithHeldItem: false)
        {
            // 独立逻辑，无上文
            // 作用：初始化联排箱组窗口视图、存取回调、搜索栏、滚动条、自适应双栏布局与初始变更戳记
            _group = group ?? throw new ArgumentNullException(nameof(group));
            _config = config ?? new RefinedStorageConfig();
            _columns = _group.PreferredColumns;
            _visibleRows = ComputeVisibleRows(_group.TotalCapacity, _columns);

            _window = new WindowedSlotList(_group, _columns, _visibleRows);
            behaviorFunction = OnDepositFromInventory;
            behaviorOnItemGrab = OnGrabFromChest;

            RefreshChestItemNames();
            InitializeSearchControls();
            InitializeScrollBarControls();
            ApplyLayout();
            _lastGroupStamp = _group.ComputeChangeStamp();
        }

        public override void RepositionSideButtons()
        {
            // 与上文：基类构造函数或布局刷新请求重新排版右侧按钮
            // 作用：重置包括垃圾桶与确定按钮在内的右侧基准横坐标，并在存在滚动条时统一右移 56 像素避让
            base.RepositionSideButtons();
            if (ItemsToGrabMenu == null)
            {
                return;
            }

            int baseX = ItemsToGrabMenu.xPositionOnScreen + ItemsToGrabMenu.width + IClickableMenu.borderWidth * 2;
            if (trashCan != null)
            {
                trashCan.bounds.X = baseX;
            }
            if (okButton != null)
            {
                okButton.bounds.X = baseX;
            }

            if (_window != null && _window.MaxScrollRow > 0)
            {
                const int scrollShift = 56;
                if (organizeButton != null)
                {
                    organizeButton.bounds.X += scrollShift;
                }
                if (fillStacksButton != null)
                {
                    fillStacksButton.bounds.X += scrollShift;
                }
                if (colorPickerToggleButton != null)
                {
                    colorPickerToggleButton.bounds.X += scrollShift;
                }
                if (specialButton != null)
                {
                    specialButton.bounds.X += scrollShift;
                }
                if (junimoNoteIcon != null)
                {
                    junimoNoteIcon.bounds.X += scrollShift;
                }
                if (trashCan != null)
                {
                    trashCan.bounds.X += scrollShift;
                }
                if (okButton != null)
                {
                    okButton.bounds.X += scrollShift;
                }
            }
        }

        public override bool isWithinBounds(int x, int y)
        {
            // 独立逻辑，无上文
            // 作用：将上下两个木框与展开的颜色选择器纳入点击有效区域，防止在扩大后的上框边缘误触发丢弃手中物品
            if (_upperBoxVisibleBounds.Contains(x, y) || _lowerBoxVisibleBounds.Contains(x, y))
            {
                return true;
            }

            if (chestColorPicker != null && chestColorPicker.visible && chestColorPicker.isWithinBounds(x, y))
            {
                return true;
            }

            return false;
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            // 与上文：游戏视口尺寸发生变化
            // 作用：按新视口尺寸重新居中整个双栏菜单、搜索框、滚动条与右侧按钮
            ApplyLayout();
        }

        public override void receiveKeyPress(Keys key)
        {
            // 独立逻辑，无上文
            // 作用：搜索框聚焦或同帧消费 Esc 时拦截全部菜单快捷键，按键采集交由 InGamePinyinBox.Update 原子处理
            if (_searchBox != null && (_searchBox.Selected || _searchBox.ConsumedEscapeThisTick))
            {
                return;
            }

            base.receiveKeyPress(key);
        }

        public override void receiveScrollWheelAction(int direction)
        {
            // 与上文：在菜单活跃期间收到鼠标滚轮事件
            // 作用：优先将候选浮窗内滚轮交给输入法翻页，否则直接滚动箱组投影窗口（不调用空基类以隔离 IClickableMenu 滚轮前缀劫持）
            if (_config.EnableSearchBar && _searchBox != null &&
                _searchBox.ReceiveScrollWheel(direction, Game1.getMouseX(), Game1.getMouseY()))
            {
                return;
            }

            if (_window.MaxScrollRow <= 0 || _window.TotalRows <= _window.VisibleRows)
            {
                return;
            }

            int previousRow = _window.ScrollRow;
            if (direction > 0)
            {
                _window.ScrollRow--;
            }
            else if (direction < 0)
            {
                _window.ScrollRow++;
            }

            if (_window.ScrollRow != previousRow)
            {
                UpdateScrollBarPosition();
                Game1.playSound("shiny4");
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            // 与上文：玩家在箱子界面执行鼠标左键点击
            // 作用：依次处理搜索栏焦点与清空、滚动条操作、侧边功能按钮、尾行虚位拦截及真实物品存取
            if (HandleSearchLeftClick(x, y))
            {
                return;
            }

            if (HandleScrollBarLeftClick(x, y))
            {
                return;
            }

            if (HandleChromeButtonsLeftClick(x, y))
            {
                return;
            }

            int visibleSlot = ItemsToGrabMenu.getInventoryPositionOfClick(x, y);
            if (visibleSlot >= 0 && !_window.TryMapVisibleSlot(visibleSlot, out _))
            {
                return;
            }

            base.heldItem = base.inventory.leftClick(x, y, base.heldItem, !destroyItemOnClick);
            if (base.heldItem == null && showReceivingMenu)
            {
                base.heldItem = ItemsToGrabMenu.leftClick(x, y, base.heldItem, playSound: false);
                if (base.heldItem != null)
                {
                    behaviorOnItemGrab?.Invoke(base.heldItem, Game1.player);
                    HandlePickedUpSpecialOrReturnToPlayer(x, y);
                }
            }
            else if ((reverseGrab || behaviorFunction != null) && isWithinBounds(x, y) && base.heldItem != null)
            {
                behaviorFunction?.Invoke(base.heldItem, Game1.player);
                if (destroyItemOnClick)
                {
                    base.heldItem = null;
                    return;
                }
            }
            else if (base.heldItem != null && !isWithinBounds(x, y) && base.heldItem.canBeTrashed())
            {
                DropHeldItem();
            }
        }

        public override void receiveRightClick(int x, int y, bool playSound = true)
        {
            // 与上文：玩家在箱子界面执行鼠标右键点击
            // 作用：优先将候选栏、模式按钮与框内右键清空交由输入法处理，外部点击失焦后按原版规则存取
            if (_config.EnableSearchBar && _searchBox != null)
            {
                bool handled = _searchBox.ReceiveRightClick(x, y);
                if (handled)
                {
                    SyncSearchProjectionIfChanged();
                    return;
                }
            }

            if (!allowRightClick)
            {
                receiveRightClickOnlyToolAttachments(x, y);
                return;
            }

            int visibleSlot = ItemsToGrabMenu.getInventoryPositionOfClick(x, y);
            if (visibleSlot >= 0 && !_window.TryMapVisibleSlot(visibleSlot, out _))
            {
                return;
            }

            base.heldItem = base.inventory.rightClick(x, y, base.heldItem, playSound && playRightClickSound);
            if (base.heldItem == null && showReceivingMenu)
            {
                base.heldItem = ItemsToGrabMenu.rightClick(x, y, base.heldItem, playSound: false);
                if (base.heldItem != null)
                {
                    behaviorOnItemGrab?.Invoke(base.heldItem, Game1.player);
                    HandlePickedUpSpecialOrReturnToPlayer(x, y);
                }
            }
            else if ((reverseGrab || behaviorFunction != null) && isWithinBounds(x, y) && base.heldItem != null)
            {
                behaviorFunction?.Invoke(base.heldItem, Game1.player);
                if (destroyItemOnClick)
                {
                    base.heldItem = null;
                }
            }
        }

        public override void leftClickHeld(int x, int y)
        {
            // 与上文：receiveLeftClick 已在滑块上启动 _scrolling 拖拽态
            // 作用：随指针纵坐标平滑更新滚动行号与滑块位置
            base.leftClickHeld(x, y);
            if (!_scrolling || _window.MaxScrollRow <= 0 || _scrollBar == null)
            {
                return;
            }

            int travel = Math.Max(1, _scrollBarRunner.Height - _scrollBar.bounds.Height);
            float ratio = Math.Clamp((float)(y - _scrollBarRunner.Y - _scrollBar.bounds.Height / 2f) / travel, 0f, 1f);
            int targetRow = (int)Math.Round(ratio * _window.MaxScrollRow);

            if (targetRow != _window.ScrollRow)
            {
                _window.ScrollRow = targetRow;
                UpdateScrollBarPosition();
                Game1.playSound("shiny4");
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            // 与上文：左键松开结束拖拽
            // 作用：复位滚动条拖拽标志
            base.releaseLeftClick(x, y);
            _scrolling = false;
        }

        public override void performHoverAction(int x, int y)
        {
            // 与上文：鼠标在菜单内移动悬停
            // 作用：候选浮窗悬停时清空物品提示防穿透，同时处理模式按钮、清空叉号与滚动条悬停提示
            base.performHoverAction(x, y);
            chestColorPicker?.performHoverAction(x, y);

            if (_window.MaxScrollRow > 0)
            {
                _upArrow?.tryHover(x, y);
                _downArrow?.tryHover(x, y);
                _scrollBar?.tryHover(x, y);
            }

            if (_config.EnableSearchBar && _searchBox != null && _searchBox.Selected && _searchBox.IsComposing &&
                _searchBox.CandidateBarBounds.Contains(x, y))
            {
                hoveredItem = null;
                hoverText = string.Empty;
                return;
            }

            if (_config.EnableSearchBar && _searchBox != null && _searchBox.ModeButtonBounds.Contains(x, y) && hoveredItem == null)
            {
                hoverText = _searchBox.BuiltInImeEnabled ? "点击或按 Shift 切换中/英输入" : "自建输入法已禁用 (仅英文)";
            }

            if (_config.EnableSearchBar && _clearSearchButton != null)
            {
                _clearSearchButton.tryHover(x, y, 0.25f);
                if (_clearSearchButton.containsPoint(x, y) &&
                    (!string.IsNullOrEmpty(_searchBox?.Text) || _searchBox?.IsComposing == true) &&
                    hoveredItem == null)
                {
                    hoverText = "清空搜索";
                }
            }
        }

        public override void update(GameTime time)
        {
            // 与上文：游戏每帧更新菜单状态
            // 作用：校验箱组并在仅邻居被拆时安全重连不踢人、每帧零分配检测队友库存改动实时刷新，并驱动输入法采集
            base.update(time);
            if (Game1.activeClickableMenu != this)
            {
                return;
            }

            if (!_group.ValidateStillPresent())
            {
                if (!TryRebuildGroupOnAnchorPreserved())
                {
                    emergencyShutDown();
                    Game1.exitActiveMenu();
                    return;
                }
            }

            long currentStamp = _group.ComputeChangeStamp();
            if (currentStamp != _lastGroupStamp)
            {
                _lastGroupStamp = currentStamp;
                RefreshChestItemNames();
                RefreshProjection();
            }

            if (_searchBox != null)
            {
                _searchBox.Update(time, _chestItemNames);
                SyncSearchProjectionIfChanged();
            }
        }

        public override void draw(SpriteBatch b)
        {
            // 独立逻辑，无上文
            // 作用：按背景、下半背包、上半木框、搜索框、物品网格、虚位暗格、侧边按钮、滚动条、飞行动画、候选浮窗与悬浮提示鼠标指针的图层顺序绘制
            if (drawBG && !Game1.options.showClearBackgrounds)
            {
                b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.5f);
            }

            base.draw(b, drawUpperPortion: false, drawDescriptionArea: false);

            if (showReceivingMenu)
            {
                DrawLeftBadgesAndUpperBox(b);
                DrawHeaderBar(b);
                ItemsToGrabMenu.draw(b);
                DrawUnmappedSlotMasks(b);
            }

            DrawSideChromeAndScrollBar(b);

            poof?.draw(b, localPosition: true);
            foreach (TransferredItemSprite transferredItemSprite in _transferredItemSprites)
            {
                transferredItemSprite.Draw(b);
            }

            if (_config.EnableSearchBar)
            {
                _searchBox?.DrawCandidatePopup(b);
            }

            DrawTooltipsAndCursor(b);
        }

        protected override void cleanupBeforeExit()
        {
            // 独立逻辑，无上文
            // 作用：退出菜单前解除键盘订阅，再交由基类安全返还光标上的残留物品
            ReleaseSearchFocus();
            base.cleanupBeforeExit();
        }

        public override void emergencyShutDown()
        {
            // 独立逻辑，无上文
            // 作用：异常或强制关闭菜单时解除键盘订阅，防止游戏键盘输入被卡死
            ReleaseSearchFocus();
            base.emergencyShutDown();
        }

        private void InitializeSearchControls()
        {
            // 与上文：在构造阶段确认 _config 配置后调用
            // 作用：预热游戏拼音词库，加载原版文本框贴图并创建游戏内拼音输入框与清空按钮
            if (!_config.EnableSearchBar)
            {
                return;
            }

            PinyinLexicon.Instance.EnsureGameVocabularyLoaded();
            Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
            bool enableIme = _config.EnableBuiltInPinyinIme;
            _searchBox = new InGamePinyinBox(textBoxTexture, Game1.smallFont, Game1.textColor)
            {
                BuiltInImeEnabled = enableIme,
                ChineseMode = enableIme
            };

            _clearSearchButton = new ClickableTextureComponent(
                new Rectangle(0, 0, 36, 36),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                3f);
        }

        private void InitializeScrollBarControls()
        {
            // 与上文：在构造阶段创建好窗口视图后调用
            // 作用：创建原版 ShopMenu 风格滚动条的上下箭头与滑块组件
            _upArrow = new ClickableTextureComponent(
                new Rectangle(0, 0, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 459, 11, 12),
                4f);

            _downArrow = new ClickableTextureComponent(
                new Rectangle(0, 0, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 472, 11, 12),
                4f);

            _scrollBar = new ClickableTextureComponent(
                new Rectangle(0, 0, 24, 40),
                Game1.mouseCursors,
                new Rectangle(435, 463, 6, 10),
                4f);
        }

        private void ApplyLayout()
        {
            // 与上文：构造完成全部子控件或视口尺寸改变
            // 作用：统一计算菜单居中坐标、上下木框边界、颜色选择器、搜索栏、滚动条与游戏手柄邻接关系
            int viewportWidth = Game1.uiViewport.Width;
            int viewportHeight = Game1.uiViewport.Height;

            int gridWidth = _columns * 64;
            int gridHeight = 64 * _visibleRows + 16;
            int headerOffset = _config.EnableSearchBar ? 86 : 70;
            const int upperBottomPad = 32;
            const int lowerMenuHeight = 672;
            const int sectionGap = 12;

            int upperVisibleHeight = headerOffset + gridHeight + upperBottomPad;
            int lowerVisibleHeight = lowerMenuHeight - (IClickableMenu.borderWidth + IClickableMenu.spaceToClearTopBorder + 192 + 64);
            int totalCombinedHeight = upperVisibleHeight + sectionGap + lowerVisibleHeight;

            width = gridWidth + IClickableMenu.borderWidth * 2 + IClickableMenu.spaceToClearSideBorder * 2;
            height = lowerMenuHeight;

            int maxLeft = Math.Max(0, viewportWidth - width - 120);
            xPositionOnScreen = Math.Max(0, Math.Min((viewportWidth - width) / 2, maxLeft));

            int maxTop = Math.Max(8, viewportHeight - totalCombinedHeight - 8);
            int desiredTop = Math.Max(76, (viewportHeight - totalCombinedHeight) / 2);
            int upperVisibleTop = Math.Min(desiredTop, maxTop);

            int gridX = xPositionOnScreen + 36;
            int gridY = upperVisibleTop + headerOffset;
            int upperVisibleBottom = gridY + gridHeight + upperBottomPad;

            _upperBoxDrawX = gridX - IClickableMenu.borderWidth - IClickableMenu.spaceToClearSideBorder;
            _upperBoxDrawY = upperVisibleTop - 64;
            _upperBoxDrawWidth = gridWidth + IClickableMenu.borderWidth * 2 + IClickableMenu.spaceToClearSideBorder * 2;
            _upperBoxDrawHeight = upperVisibleBottom - _upperBoxDrawY;
            _upperBoxVisibleBounds = new Rectangle(_upperBoxDrawX, upperVisibleTop, _upperBoxDrawWidth, upperVisibleHeight);

            int lowerVisibleTop = upperVisibleBottom + sectionGap;
            yPositionOnScreen = lowerVisibleTop - 264;
            _lowerBoxVisibleBounds = new Rectangle(
                xPositionOnScreen - IClickableMenu.borderWidth / 2,
                lowerVisibleTop,
                width,
                lowerVisibleHeight);

            int playerInventoryX = xPositionOnScreen + 36 + (gridWidth - 768) / 2;
            int playerInventoryY = yPositionOnScreen + 312;
            base.inventory.SetPosition(playerInventoryX, playerInventoryY);

            if (dropItemInvisibleButton != null)
            {
                dropItemInvisibleButton.bounds.X = xPositionOnScreen - IClickableMenu.borderWidth - IClickableMenu.spaceToClearSideBorder - 128;
                dropItemInvisibleButton.bounds.Y = playerInventoryY - 12;
            }
            if (trashCan != null)
            {
                trashCan.bounds.Y = yPositionOnScreen + height - 192 - 32 - IClickableMenu.borderWidth - 104;
            }
            if (okButton != null)
            {
                okButton.bounds.Y = yPositionOnScreen + height - 192 - IClickableMenu.borderWidth;
            }

            InventoryMenu.highlightThisItem? chestHighlight = ItemsToGrabMenu?.highlightMethod ?? InventoryMenu.highlightAllItems;
            ItemsToGrabMenu = new InventoryMenu(
                gridX,
                gridY,
                playerInventory: false,
                _window,
                chestHighlight,
                _window.VisibleCapacity,
                _window.VisibleRows);
            ItemsToGrabMenu.populateClickableComponentList();
            for (int i = 0; i < ItemsToGrabMenu.inventory.Count; i++)
            {
                ClickableComponent slot = ItemsToGrabMenu.inventory[i];
                if (slot != null)
                {
                    slot.myID += 53910;
                    slot.upNeighborID += 53910;
                    slot.rightNeighborID += 53910;
                    slot.downNeighborID = -7777;
                    slot.leftNeighborID += 53910;
                    slot.fullyImmutable = true;
                }
            }

            if (_searchBox != null)
            {
                _searchBox.X = gridX;
                _searchBox.Y = gridY - 56;
                _searchBox.Width = Math.Min(260, Math.Max(180, gridWidth - 360));
                if (_clearSearchButton != null)
                {
                    int clearY = _searchBox.Y + Math.Max(0, (_searchBox.Height - 36) / 2);
                    _clearSearchButton.bounds = new Rectangle(_searchBox.ModeButtonBounds.Right + 6, clearY, 36, 36);
                }
            }

            if (chestColorPicker != null)
            {
                chestColorPicker.xPositionOnScreen = xPositionOnScreen;
                chestColorPicker.yPositionOnScreen = upperVisibleTop - 72;
                if (discreteColorPickerCC != null)
                {
                    for (int j = 0; j < discreteColorPickerCC.Count; j++)
                    {
                        discreteColorPickerCC[j].bounds = new Rectangle(
                            chestColorPicker.xPositionOnScreen + IClickableMenu.borderWidth / 2 + j * 9 * 4,
                            chestColorPicker.yPositionOnScreen + IClickableMenu.borderWidth / 2,
                            36,
                            28);
                    }
                }
            }

            int scrollX = _upperBoxDrawX + _upperBoxDrawWidth + 16;
            int scrollTopY = upperVisibleTop + 16;
            int scrollBottomY = upperVisibleBottom - 16 - 48;
            if (_upArrow != null)
            {
                _upArrow.bounds = new Rectangle(scrollX, scrollTopY, 44, 48);
            }
            if (_downArrow != null)
            {
                _downArrow.bounds = new Rectangle(scrollX, scrollBottomY, 44, 48);
            }
            int runnerTop = scrollTopY + 48 + 4;
            int runnerHeight = Math.Max(40, scrollBottomY - runnerTop - 4);
            _scrollBarRunner = new Rectangle(scrollX + 10, runnerTop, 24, runnerHeight);
            if (_scrollBar != null)
            {
                _scrollBar.bounds = new Rectangle(_scrollBarRunner.X, _scrollBarRunner.Y, 24, 40);
            }
            UpdateScrollBarPosition();

            RepositionSideButtons();
            BindSideNeighbors();
            populateClickableComponentList();
            SetupBorderNeighbors();
            if (Game1.options.SnappyMenus)
            {
                snapToDefaultClickableComponent();
            }
        }

        private void BindSideNeighbors()
        {
            // 与上文：ItemsToGrabMenu 与右侧按钮均已完成坐标排版
            // 作用：建立箱子网格最右列、玩家背包最右列与右侧操作按钮的手柄导航关联
            if (organizeButton != null)
            {
                foreach (ClickableComponent slot in ItemsToGrabMenu.GetBorder(InventoryMenu.BorderSide.Right))
                {
                    slot.rightNeighborID = organizeButton.myID;
                }
            }

            if (trashCan != null)
            {
                trashCan.leftNeighborID = 11;
            }
            if (okButton != null)
            {
                okButton.leftNeighborID = 11;
            }
            if (base.inventory.inventory.Count >= 12 && base.inventory.inventory[11] != null)
            {
                base.inventory.inventory[11].rightNeighborID = 5948;
            }

            ClickableComponent? rightBorderSlot = null;
            foreach (ClickableComponent slot in ItemsToGrabMenu.GetBorder(InventoryMenu.BorderSide.Right))
            {
                rightBorderSlot = slot;
                break;
            }

            if (rightBorderSlot != null)
            {
                if (organizeButton != null)
                {
                    organizeButton.leftNeighborID = rightBorderSlot.myID;
                }
                if (specialButton != null)
                {
                    specialButton.leftNeighborID = rightBorderSlot.myID;
                }
                if (fillStacksButton != null)
                {
                    fillStacksButton.leftNeighborID = rightBorderSlot.myID;
                }
                if (junimoNoteIcon != null)
                {
                    junimoNoteIcon.leftNeighborID = rightBorderSlot.myID;
                }
            }
        }

        private void OnDepositFromInventory(Item item, Farmer who)
        {
            // 独立逻辑，无上文
            // 作用：将物品存入联排箱组并清理或返还余量到玩家背包与光标，同步刷新箱内优先词与投影
            if (item == null)
            {
                return;
            }

            if (item.Stack == 0)
            {
                item.Stack = 1;
            }

            Item? leftover = StorageOps.Deposit(item, _group, out _);
            if (leftover == null)
            {
                who.removeItemFromInventory(item);
                base.heldItem = null;
            }
            else
            {
                leftover = who.addItemToInventory(leftover);
                base.heldItem = leftover;
            }

            RefreshChestItemNames();
            if (_window.IsFilterActive)
            {
                RefreshProjection();
            }
            _lastGroupStamp = _group.ComputeChangeStamp();
        }

        private void OnGrabFromChest(Item item, Farmer who)
        {
            // 独立逻辑，无上文
            // 作用：从箱组取物后刷新箱内优先词、搜索过滤投影与变更戳记，绝不重建菜单
            RefreshChestItemNames();
            if (_window.IsFilterActive)
            {
                RefreshProjection();
            }
            _lastGroupStamp = _group.ComputeChangeStamp();
        }

        private bool HandleSearchLeftClick(int x, int y)
        {
            // 与上文：receiveLeftClick 入口优先判定搜索栏交互
            // 作用：优先拦截候选浮窗与清空叉号点击，再转发输入框与切换按钮并及时同步过滤投影
            if (!_config.EnableSearchBar || _searchBox == null)
            {
                return false;
            }

            if (_searchBox.Selected && _searchBox.IsComposing && _searchBox.CandidateBarBounds.Contains(x, y))
            {
                _searchBox.ReceiveLeftClick(x, y);
                SyncSearchProjectionIfChanged();
                return true;
            }

            if (_clearSearchButton != null && _clearSearchButton.containsPoint(x, y))
            {
                ClearSearchQuery(keepFocus: false);
                return true;
            }

            bool handled = _searchBox.ReceiveLeftClick(x, y);
            if (handled)
            {
                SyncSearchProjectionIfChanged();
            }
            return handled;
        }

        private bool HandleScrollBarLeftClick(int x, int y)
        {
            // 与上文：在搜索栏之后判定滚动条区域点击
            // 作用：处理上下箭头单行滚动、滑块拖拽启动与滑道点击翻页
            if (_window.MaxScrollRow <= 0)
            {
                return false;
            }

            if (_upArrow != null && _upArrow.containsPoint(x, y))
            {
                if (_window.ScrollRow > 0)
                {
                    _window.ScrollRow--;
                    UpdateScrollBarPosition();
                    Game1.playSound("shwip");
                }
                return true;
            }

            if (_downArrow != null && _downArrow.containsPoint(x, y))
            {
                if (_window.ScrollRow < _window.MaxScrollRow)
                {
                    _window.ScrollRow++;
                    UpdateScrollBarPosition();
                    Game1.playSound("shwip");
                }
                return true;
            }

            if (_scrollBar != null && _scrollBar.containsPoint(x, y))
            {
                _scrolling = true;
                return true;
            }

            if (_scrollBarRunner.Contains(x, y) && _scrollBar != null)
            {
                if (y < _scrollBar.bounds.Y)
                {
                    _window.ScrollRow -= _window.VisibleRows;
                }
                else
                {
                    _window.ScrollRow += _window.VisibleRows;
                }
                UpdateScrollBarPosition();
                Game1.playSound("shwip");
                return true;
            }

            return false;
        }

        private bool HandleChromeButtonsLeftClick(int x, int y)
        {
            // 与上文：在滚动条之后判定颜色选择器与右侧功能按钮点击
            // 作用：拦截并接管整理、一键堆叠、调色板、祝尼魔便签、垃圾桶与确定按钮，杜绝基类重建菜单
            if (chestColorPicker != null && chestColorPicker.visible && chestColorPicker.isWithinBounds(x, y))
            {
                chestColorPicker.receiveLeftClick(x, y);
                if (sourceItem is Chest chest)
                {
                    chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(chestColorPicker.colorSelection);
                }
                return true;
            }

            if (colorPickerToggleButton != null && colorPickerToggleButton.containsPoint(x, y))
            {
                Game1.player.showChestColorPicker = !Game1.player.showChestColorPicker;
                if (chestColorPicker != null)
                {
                    chestColorPicker.visible = Game1.player.showChestColorPicker;
                }
                try
                {
                    Game1.playSound("drumkit6");
                }
                catch
                {
                }
                SetupBorderNeighbors();
                return true;
            }

            if (organizeButton != null && organizeButton.containsPoint(x, y))
            {
                HandleOrganizeButton();
                return true;
            }

            if (fillStacksButton != null && fillStacksButton.containsPoint(x, y))
            {
                HandleFillStacksButton();
                return true;
            }

            if (junimoNoteIcon != null && junimoNoteIcon.containsPoint(x, y))
            {
                if (readyToClose())
                {
                    ReleaseSearchFocus();
                    Game1.activeClickableMenu = new JunimoNoteMenu(fromGameMenu: true)
                    {
                        menuToReturnTo = this
                    };
                }
                return true;
            }

            if (okButton != null && okButton.containsPoint(x, y) && readyToClose())
            {
                exitThisMenu();
                Event? currentEvent = Game1.currentLocation?.currentEvent;
                if (currentEvent != null && currentEvent.CurrentCommand > 0)
                {
                    currentEvent.CurrentCommand++;
                }
                Game1.playSound("bigDeSelect");
                return true;
            }

            if (trashCan != null && trashCan.containsPoint(x, y) && base.heldItem != null && base.heldItem.canBeTrashed())
            {
                Utility.trashItem(base.heldItem);
                base.heldItem = null;
                return true;
            }

            return false;
        }

        private void HandleOrganizeButton()
        {
            // 与上文：玩家点击了右侧整理按钮
            // 作用：执行事务化跨箱整理并原地刷新优先词、投影与变更戳记，按配置弹出汇总提示
            OpResult result = StorageOps.Tidy(_group, out int stackCount, out _);
            RefreshChestItemNames();
            RefreshProjection();
            _lastGroupStamp = _group.ComputeChangeStamp();
            Game1.playSound("Ship");

            if (!_config.ShowNotification)
            {
                return;
            }

            if (result == OpResult.Ok)
            {
                Game1.addHUDMessage(new HUDMessage($"整理了 {_group.ChestCount} 个链接箱子，共 {stackCount} 组物品", HUDMessage.newQuest_type)
                {
                    noIcon = true,
                    timeLeft = 2000f
                });
            }
            else if (result == OpResult.RolledBack)
            {
                Game1.addHUDMessage(new HUDMessage("跨箱整理校验未通过，已安全回滚", HUDMessage.error_type)
                {
                    noIcon = true,
                    timeLeft = 2500f
                });
            }
        }

        private void HandleFillStacksButton()
        {
            // 与上文：玩家点击了右侧快速堆叠按钮
            // 作用：调用全组同款亲和快速堆叠（保护收藏锁定物品），原地刷新投影并触发箱内抖动与飞行精灵动画
            var touchedGlobalSlots = new List<int>();
            var transferredFromSlots = new List<(Item Proto, int PlayerSlot)>();
            StorageOps.FillOutStacks(
                base.inventory.actualInventory,
                _group,
                touchedGlobalSlots,
                transferredFromSlots,
                item => _config.EnableFavoriteLock && FavoriteLock.IsFavorite(item));

            RefreshChestItemNames();
            RefreshProjection();
            _lastGroupStamp = _group.ComputeChangeStamp();

            if (touchedGlobalSlots.Count > 0)
            {
                var touchedSet = new HashSet<int>(touchedGlobalSlots);
                for (int visIdx = 0; visIdx < _window.VisibleCapacity; visIdx++)
                {
                    if (_window.TryMapVisibleSlot(visIdx, out int globalSlot) && touchedSet.Contains(globalSlot))
                    {
                        ItemsToGrabMenu.ShakeItem(visIdx);
                    }
                }
            }

            foreach ((Item proto, int playerSlot) in transferredFromSlots)
            {
                if (proto != null && playerSlot >= 0 && playerSlot < base.inventory.inventory.Count)
                {
                    ClickableComponent slotComponent = base.inventory.inventory[playerSlot];
                    _transferredItemSprites.Add(new TransferredItemSprite(proto.getOne(), slotComponent.bounds.X, slotComponent.bounds.Y));
                }
            }

            Game1.playSound("Ship");
        }

        private void HandlePickedUpSpecialOrReturnToPlayer(int x, int y)
        {
            // 与上文：从箱子网格取出物品至光标且已触发 OnGrabFromChest
            // 作用：复刻原版特殊物品（矮人语指南、失落之书、星之果实、配方）即时消耗并尝试自动收进玩家背包
            string? qualifiedId = base.heldItem?.QualifiedItemId;
            if (qualifiedId == "(O)326")
            {
                base.heldItem = null;
                Game1.player.canUnderstandDwarves = true;
                poof = new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 320, 64, 64), 50f, 8, 0, new Vector2(x - x % 64 + 16, y - y % 64 + 16), flicker: false, flipped: false);
                Game1.playSound("fireball");
                return;
            }

            if (qualifiedId == "(O)102")
            {
                base.heldItem = null;
                Game1.player.foundArtifact("102", 1);
                poof = new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 320, 64, 64), 50f, 8, 0, new Vector2(x - x % 64 + 16, y - y % 64 + 16), flicker: false, flipped: false);
                Game1.playSound("fireball");
                return;
            }

            if (base.heldItem is StardewValley.Object obj && obj.QualifiedItemId == "(O)434")
            {
                base.heldItem = null;
                exitThisMenu(playSound: false);
                Game1.player.eatObject(obj, overrideFullness: true);
                return;
            }

            if (base.heldItem != null && base.heldItem.IsRecipe)
            {
                base.heldItem.LearnRecipe();
                poof = new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 320, 64, 64), 50f, 8, 0, new Vector2(x - x % 64 + 16, y - y % 64 + 16), flicker: false, flipped: false);
                Game1.playSound("newRecipe");
                base.heldItem = null;
                return;
            }

            if (Game1.player.addItemToInventoryBool(base.heldItem))
            {
                base.heldItem = null;
                Game1.playSound("coin");
            }
        }

        private void ClearSearchQuery(bool keepFocus)
        {
            // 与上文：清空按钮左键点击时调用
            // 作用：清空搜索文本与拼写缓冲、重置滚动行号并立即重算投影与侧边按钮避让
            if (_searchBox == null)
            {
                return;
            }

            _searchBox.Clear(keepFocus);
            _lastSearchText = string.Empty;
            _window.ScrollRow = 0;
            RefreshProjection();
            Game1.playSound("smallSelect");
        }

        private void SyncSearchProjectionIfChanged()
        {
            // 与上文：在每帧 update 或鼠标交互改变输入框状态后调用
            // 作用：对比有效搜索查询串并在发生变动时归零滚动行、重算过滤投影
            if (_searchBox == null)
            {
                return;
            }

            string currentQuery = _searchBox.EffectiveSearchQuery;
            if (!string.Equals(currentQuery, _lastSearchText, StringComparison.Ordinal))
            {
                _lastSearchText = currentQuery;
                _window.ScrollRow = 0;
                RefreshProjection();
            }
        }

        private void RefreshChestItemNames()
        {
            // 与上文：菜单开启或箱内物品因存取整理堆叠发生变化时调用
            // 作用：采集联排箱组现存物品展示名称集合，用于拼音候选词箱内优先置顶
            _chestItemNames.Clear();
            int totalCapacity = _group.TotalCapacity;
            for (int i = 0; i < totalCapacity; i++)
            {
                Item? item = _group.GetSlot(i);
                if (item == null || item.Stack <= 0)
                {
                    continue;
                }

                try
                {
                    string displayName = item.DisplayName;
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        _chestItemNames.Add(displayName);
                    }
                }
                catch
                {
                }
            }
        }

        private void RefreshProjection()
        {
            // 与上文：搜索词、存物、取物、整理或堆叠导致真实库存或筛选条件变化
            // 作用：以 EffectiveSearchQuery 重算 WindowedSlotList 投影，同步更新滚动条滑块与侧边按钮偏移
            _window.RebuildProjection(_searchBox?.EffectiveSearchQuery);
            RepositionSideButtons();
            UpdateScrollBarPosition();
        }

        private void UpdateScrollBarPosition()
        {
            // 与上文：依赖 _window.ScrollRow、_window.MaxScrollRow 与已计算的 _scrollBarRunner
            // 作用：按当前滚动行占比定位滚动条滑块纵坐标
            if (_scrollBar == null)
            {
                return;
            }

            if (_window.MaxScrollRow <= 0)
            {
                _scrollBar.bounds.Y = _scrollBarRunner.Y;
                return;
            }

            int travel = Math.Max(0, _scrollBarRunner.Height - _scrollBar.bounds.Height);
            float ratio = (float)_window.ScrollRow / _window.MaxScrollRow;
            _scrollBar.bounds.Y = _scrollBarRunner.Y + (int)Math.Round(ratio * travel);
        }

        private static int ComputeVisibleRows(int totalCapacity, int columns)
        {
            // 独立逻辑，无上文
            // 作用：根据当前视口高度与箱组总容量计算自适应可见行数
            int viewportHeight = Game1.uiViewport.Height;
            int maxFitRows = viewportHeight >= 900 ? 5 : (viewportHeight >= 760 ? 4 : 3);
            int neededRows = Math.Max(3, (totalCapacity + columns - 1) / columns);
            return Math.Min(maxFitRows, neededRows);
        }

        private bool TryRebuildGroupOnAnchorPreserved()
        {
            // 与上文：update 检测到既有成员箱列表不再完整有效时调用
            // 作用：若锚点箱仍在原坐标且符合白名单，则重扫连通网络原地重建箱组与窗口布局，避免因邻居被拆误踢当前玩家
            GameLocation? location = _group.Location;
            Chest anchor = _group.AnchorChest;
            if (location?.objects == null || anchor == null || !ChestFilter.IsLinkablePlayerChest(anchor))
            {
                return false;
            }

            if (!location.objects.TryGetValue(_group.AnchorTile, out StardewValley.Object? anchorObj) ||
                !ReferenceEquals(anchorObj, anchor))
            {
                return false;
            }

            List<(Chest Chest, Vector2 Tile)> members = ChestNetwork.FindLinkedChests(anchor, location);
            if (members.Count == 0)
            {
                return false;
            }

            int previousScrollRow = _window.ScrollRow;
            _group = new LinkedChestGroup(anchor, location, members);
            _columns = _group.PreferredColumns;
            _visibleRows = ComputeVisibleRows(_group.TotalCapacity, _columns);
            _window = new WindowedSlotList(_group, _columns, _visibleRows);

            RefreshChestItemNames();
            _window.ScrollRow = previousScrollRow;
            _window.RebuildProjection(_searchBox?.EffectiveSearchQuery);
            ApplyLayout();
            _lastGroupStamp = _group.ComputeChangeStamp();
            return true;
        }

        private void ReleaseSearchFocus()
        {
            // 独立逻辑，无上文
            // 作用：安全清除搜索框聚焦状态与键盘订阅，并保险调用 NativeImeSuppressor.Restore
            if (_searchBox != null)
            {
                _searchBox.Selected = false;
            }

            NativeImeSuppressor.Restore();
        }

        private void DrawLeftBadgesAndUpperBox(SpriteBatch b)
        {
            // 与上文：draw 已完成背景遮罩与下半玩家背包绘制
            // 作用：绘制下半背包左侧背包徽标、上半联排木框以及木框左侧箱子徽标
            b.Draw(Game1.mouseCursors, new Vector2(xPositionOnScreen - 64, yPositionOnScreen + height / 2 + 64 + 16), new Rectangle(16, 368, 12, 16), Color.White, 4.712389f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            b.Draw(Game1.mouseCursors, new Vector2(xPositionOnScreen - 64, yPositionOnScreen + height / 2 + 64 - 16), new Rectangle(21, 368, 11, 16), Color.White, 4.712389f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            b.Draw(Game1.mouseCursors, new Vector2(xPositionOnScreen - 40, yPositionOnScreen + height / 2 + 64 - 44), new Rectangle(4, 372, 8, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);

            Game1.drawDialogueBox(_upperBoxDrawX, _upperBoxDrawY, _upperBoxDrawWidth, _upperBoxDrawHeight, speaker: false, drawOnlyBox: true);

            int chestBadgeY = ItemsToGrabMenu.yPositionOnScreen + 20;
            b.Draw(Game1.mouseCursors, new Vector2(ItemsToGrabMenu.xPositionOnScreen - 100, chestBadgeY + 60), new Rectangle(16, 368, 12, 16), Color.White, 4.712389f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            b.Draw(Game1.mouseCursors, new Vector2(ItemsToGrabMenu.xPositionOnScreen - 100, chestBadgeY + 28), new Rectangle(21, 368, 11, 16), Color.White, 4.712389f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            b.Draw(Game1.mouseCursors, new Vector2(ItemsToGrabMenu.xPositionOnScreen - 80, chestBadgeY), new Rectangle(127, 412, 10, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
        }

        private void DrawHeaderBar(SpriteBatch b)
        {
            // 与上文：上半木框已绘制完毕，紧贴顶部留白绘制搜索与状态栏
            // 作用：绘制木框内顶部自建拼音搜索框、清空叉号按钮与箱组容量状态文本，彻底规避顶部颜色选择器遮挡
            if (_config.EnableSearchBar && _searchBox != null)
            {
                _searchBox.DrawBox(b);

                if (_clearSearchButton != null)
                {
                    bool hasContent = !string.IsNullOrEmpty(_searchBox.Text) || _searchBox.IsComposing;
                    float alpha = hasContent ? 1f : 0.35f;
                    _clearSearchButton.draw(b, Color.White * alpha, 0.89f);
                }
            }

            int occupied = _group.CountOccupiedSlots();
            string statusText = _window.IsFilterActive
                ? $"{_group.ChestCount} 箱 · {occupied}/{_group.TotalCapacity} (匹配 {_window.TotalProjectedSlots})"
                : $"{_group.ChestCount} 箱 · {occupied}/{_group.TotalCapacity}";

            Vector2 textSize = Game1.smallFont.MeasureString(statusText);
            float statusX = ItemsToGrabMenu.xPositionOnScreen + ItemsToGrabMenu.width - textSize.X;
            float statusY = _searchBox != null
                ? _searchBox.Y + (_searchBox.Height - textSize.Y) / 2f
                : ItemsToGrabMenu.yPositionOnScreen - textSize.Y - 12f;

            Utility.drawTextWithShadow(b, statusText, Game1.smallFont, new Vector2(statusX, statusY), Game1.textColor);
        }

        private void DrawUnmappedSlotMasks(SpriteBatch b)
        {
            // 与上文：ItemsToGrabMenu 已完成标准格子背景与真实物品绘制
            // 作用：为尾行未映射虚位叠加原版 tile 57 暗格遮罩，直观区分不可交互空位
            Rectangle maskSource = Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 57);
            for (int visIdx = 0; visIdx < _window.VisibleCapacity && visIdx < ItemsToGrabMenu.inventory.Count; visIdx++)
            {
                if (_window.TryMapVisibleSlot(visIdx, out _))
                {
                    continue;
                }

                Rectangle bounds = ItemsToGrabMenu.inventory[visIdx].bounds;
                b.Draw(Game1.menuTexture, new Vector2(bounds.X, bounds.Y), maskSource, Color.White * 0.5f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.5f);
            }
        }

        private void DrawSideChromeAndScrollBar(SpriteBatch b)
        {
            // 与上文：物品网格与虚位暗格遮罩已绘制完成
            // 作用：绘制调色板按钮、颜色选择器、整理与堆叠按钮、祝尼魔图标以及原版贴图滚动条
            if (colorPickerToggleButton != null)
            {
                colorPickerToggleButton.draw(b);
            }
            else
            {
                specialButton?.draw(b);
            }

            chestColorPicker?.draw(b);
            organizeButton?.draw(b);
            fillStacksButton?.draw(b);
            junimoNoteIcon?.draw(b);

            if (_window.MaxScrollRow > 0)
            {
                _upArrow?.draw(b);
                _downArrow?.draw(b);
                IClickableMenu.drawTextureBox(
                    b,
                    Game1.mouseCursors,
                    new Rectangle(403, 383, 6, 6),
                    _scrollBarRunner.X,
                    _scrollBarRunner.Y,
                    _scrollBarRunner.Width,
                    _scrollBarRunner.Height,
                    Color.White,
                    4f);
                _scrollBar?.draw(b);
            }
        }

        private void DrawTooltipsAndCursor(SpriteBatch b)
        {
            // 与上文：全部菜单组件、粒子与飞行物品均已绘制在底层
            // 作用：最后绘制文字提示、物品详细 Tooltip、光标持有物品及鼠标指针，防止被控件遮挡
            if (!string.IsNullOrEmpty(hoverText) && (hoveredItem == null || ItemsToGrabMenu == null))
            {
                if (hoverAmount > 0)
                {
                    IClickableMenu.drawToolTip(b, hoverText, "", null, heldItem: true, -1, 0, null, -1, null, hoverAmount);
                }
                else
                {
                    IClickableMenu.drawHoverText(b, hoverText, Game1.smallFont);
                }
            }

            if (hoveredItem != null)
            {
                IClickableMenu.drawToolTip(b, hoveredItem.getDescription(), hoveredItem.DisplayName, hoveredItem, base.heldItem != null);
            }

            base.heldItem?.drawInMenu(b, new Vector2(Game1.getOldMouseX() + 8, Game1.getOldMouseY() + 8), 1f);
            Game1.mouseCursorTransparency = 1f;
            drawMouse(b);
        }
    }
}
