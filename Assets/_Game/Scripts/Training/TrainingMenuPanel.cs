using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// TrainingMenuPanel — 训练菜单（Esc 开关）
// 视觉/交互沿用暂停菜单：垂直按钮列 + 黄色选中高亮；行文字显示当前开关状态
//   0 继续（关闭菜单）
//   1 CPU（木桩）   开/关
//   2 无敌          开/关
//   3 慢动作        开/关
//   4 判定框        开/关
//   5 帧数据面板    开/关
//   6 HUD           开/关
//   7 重置位置
//   8 退出训练
// 导航：↑↓ / W S / 手柄左摇杆·十字键 移动
//   J / 小键盘1 / 回车 / 空格 / 手柄A 确认
//   K / 小键盘2 / 手柄B 关闭（Esc 由 TrainingModeController 统一 toggle）
// ============================================================
namespace SuperSmashLike.Training
{
    public class TrainingMenuPanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Refs")]
        public TrainingModeController controller;
        public TrainingBoxOverlay boxOverlay;
        public TrainingFrameDataPanel framePanel;

        [Header("标签页（横向）")]
        public GameObject trainPage;      // 训练页（现有 9 行）
        public GameObject mainPage;       // 主菜单页
        public Button[] tabButtons;       // 顶部标签按钮
        public TMP_Text tabTitle;         // 当前页名
        public Button[] mainButtons;      // 主菜单页按钮（继续 / 退出训练）

        [Header("Rows（按顺序）0继续 1CPU 2无敌 3慢动作 4判定框 5帧数据 6HUD 7重置 8退出")]
        public Button[] rowButtons = new Button[9];
        public TextMeshProUGUI[] rowLabels = new TextMeshProUGUI[9];

        [Header("Nav")]
        [Tooltip("选中高亮色（黄色，与暂停菜单一致）")]
        public Color selectionColor = new Color(1f, 0.85f, 0.2f, 1f);
        public float navRepeatDelay = 0.18f;

        #endregion

        #region 2. 运行时状态

        private int _index;
        private float _nextNav;
        private int _tab;
        private TMP_Text[] _rowStatus;   // 每行右侧的"开/关"状态字
        private SuperSmashLike.UI.MoveListUI _moveList;

        private Button[] ActiveButtons => _tab == 0 ? rowButtons : mainButtons;

        #endregion

        #region 3. 生命周期

        private void OnEnable()
        {
            _index = 0;
            // 引用兜底（重排布局后组件引用可能丢失 → 开关点了没反应）
            if (controller == null) controller = Object.FindObjectOfType<TrainingModeController>(true);
            if (boxOverlay == null) boxOverlay = Object.FindObjectOfType<TrainingBoxOverlay>(true);
            if (framePanel == null) framePanel = Object.FindObjectOfType<TrainingFrameDataPanel>(true);

            OrderMainButtons();   // 主菜单按钮按视觉位置(上→下)排序，保证循环顺序与所见一致

            ApplyRowColors();
            ApplyRowClicks();
            EnsureRowStatus();
            Refresh();
            SelectTab(0);
        }

        // 【做什么】给每个开关行右侧创建一个"开/关"状态字（运行时创建，不改场景布局）
        private void EnsureRowStatus()
        {
            if (rowButtons == null) return;
            if (_rowStatus != null && _rowStatus.Length == rowButtons.Length) return;

            _rowStatus = new TMP_Text[rowButtons.Length];
            for (int i = 0; i < rowButtons.Length; i++)
            {
                var b = rowButtons[i];
                if (b == null) continue;

                var go = new GameObject("RowStatus", typeof(RectTransform));
                go.transform.SetParent(b.transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);   // 行右侧
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-24f, 0f);
                rt.sizeDelta = new Vector2(90f, 40f);

                var t = go.AddComponent<TextMeshProUGUI>();
                var label = b.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) t.font = label.font;
                t.fontSize = 28;
                t.alignment = TextAlignmentOptions.Right;
                t.color = Color.black;
                t.raycastTarget = false;
                _rowStatus[i] = t;
            }
        }

        // 【做什么】切换标签页（横向：训练 / 主菜单）
        public void SelectTab(int t)
        {
            _tab = (t == 1) ? 1 : 0;
            if (trainPage != null) trainPage.SetActive(_tab == 0);
            if (mainPage != null) mainPage.SetActive(_tab == 1);
            if (tabTitle != null) tabTitle.text = _tab == 0 ? "训练" : "主菜单";

            // 标签按钮：字体黑色 + 选中用"底色高亮"（不再把字变黄）
            if (tabButtons != null)
                for (int i = 0; i < tabButtons.Length; i++)
                {
                    if (tabButtons[i] == null) continue;
                    var tcb = tabButtons[i].colors;
                    tcb.normalColor = (i == _tab) ? selectionColor : Color.white;
                    tcb.selectedColor = selectionColor;
                    tcb.highlightedColor = selectionColor;
                    tabButtons[i].colors = tcb;

                    var t2 = tabButtons[i].GetComponentInChildren<TMP_Text>(true);
                    if (t2 != null) t2.color = Color.black;
                }

            _index = 0;
            if (ActiveButtons != null && ActiveButtons.Length > 0) SelectRow(0);
        }

        // 【做什么】主菜单按钮按 RectTransform 的 y 从大到小排序（上→下），
        //   使方向键循环顺序与界面所见一致（不受重排布局影响）
        private void OrderMainButtons()
        {
            if (mainButtons == null || mainButtons.Length < 2) return;
            System.Array.Sort(mainButtons, (a, b) =>
            {
                if (a == null || b == null) return 0;
                var ra = a.GetComponent<RectTransform>();
                var rb = b.GetComponent<RectTransform>();
                float ya = ra != null ? ra.anchoredPosition.y : 0f;
                float yb = rb != null ? rb.anchoredPosition.y : 0f;
                return yb.CompareTo(ya);
            });
        }

        // 【做什么】主菜单动作：按"按钮上显示的文字"分发（与玩家所见一致，不受物体名/顺序影响）
        private void ActivateMain(int i)
        {
            var act = mainButtons;
            if (act == null || i < 0 || i >= act.Length || act[i] == null) return;

            var t = act[i].GetComponentInChildren<TMP_Text>(true);
            string label = t != null ? t.text : "";

            if (label.Contains("出招表")) ShowMoveList();
            else if (label.Contains("重新开始")) RestartGame();
            else if (label.Contains("标题") || label.Contains("退出")) { if (controller != null) controller.ExitTraining(); }
            else { if (controller != null) controller.SetMenu(false); }   // 继续
        }

        private void RestartGame()
        {
            if (controller != null) controller.ExitTraining();
            // 重新开始 → 走"训练流程"（选人→选图→训练），而不是回普通流程
            if (GameManager.Instance != null) GameManager.Instance.RequestTraining();
        }

        private void ShowMoveList()
        {
            // 【注意】出招表面板平时是关闭(未激活)的，必须 includeInactive=true 才找得到
            var ml = Object.FindObjectOfType<SuperSmashLike.UI.MoveListUI>(true);
            if (ml == null) return;

            ml.transform.SetAsLastSibling();   // 置顶：避免被全屏的训练菜单挡住
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            var fd = gs != null ? gs.GetSelection(0) : null;
            ml.ShowFor(fd, 0, true);
        }

        // 【做什么】给每行按钮绑定 onClick（鼠标点击也能用，不只是键盘索引）
        private void ApplyRowClicks()
        {
            if (rowButtons != null)
                for (int i = 0; i < rowButtons.Length; i++)
                {
                    var b = rowButtons[i]; if (b == null) continue;
                    int idx = i;
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() => Activate(idx));
                }
            if (tabButtons != null)
                for (int i = 0; i < tabButtons.Length; i++)
                {
                    var b = tabButtons[i]; if (b == null) continue;
                    int idx = i;
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() => SelectTab(idx));
                }
            // 【注意】不再动 mainButtons 的 onClick —— 留给在 Inspector 手动连线（更可靠）
        }

        // ===== 供 Inspector 里给主菜单按钮连线（public）=====
        public void MenuContinue() { if (controller != null) controller.SetMenu(false); }
        public void MenuRestart() => RestartGame();
        public void MenuShowMoveList() => ShowMoveList();
        public void MenuExitTraining() { if (controller != null) controller.ExitTraining(); }

        private void Update()
        {
            // 出招表打开时：键盘交给出招表处理，菜单不抢键（避免 K 关错对象）
            if (_moveList == null) _moveList = Object.FindObjectOfType<SuperSmashLike.UI.MoveListUI>(true);
            if (_moveList != null && _moveList.gameObject.activeSelf) return;

            var gp = Gamepad.current;

            // ---- 切换标签页（Q/E 或 手柄 LB/RB）----
            int tabDir = 0;
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Tab)) tabDir = -1;
            else if (Input.GetKeyDown(KeyCode.E)) tabDir = 1;
            if (tabDir == 0 && gp != null && Time.unscaledTime >= _nextNav)
            {
                if (gp.leftShoulder.wasPressedThisFrame) { tabDir = -1; _nextNav = Time.unscaledTime + navRepeatDelay; }
                else if (gp.rightShoulder.wasPressedThisFrame) { tabDir = 1; _nextNav = Time.unscaledTime + navRepeatDelay; }
            }
            if (tabDir != 0 && tabButtons != null && tabButtons.Length > 0)
            {
                SelectTab((_tab + tabDir + tabButtons.Length) % tabButtons.Length);
                return;
            }

            // ---- 关闭（Esc 由控制器 toggle，这里只管 K/小键盘2/手柄B）----
            if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Keypad2)
                || (gp != null && gp.buttonEast.wasPressedThisFrame))
            {
                if (controller != null) controller.SetMenu(false);
                return;
            }

            // ---- 确认 / 切换 ----
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Keypad1)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)
                || (gp != null && gp.buttonSouth.wasPressedThisFrame))
            {
                if (_tab == 0) Activate(_index); else ActivateMain(_index);
                return;
            }

            // ---- 上下移动 ----
            int dir = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = 1;

            if (dir == 0 && gp != null && Time.unscaledTime >= _nextNav)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;
                if (nav.y > 0.5f) dir = -1;
                else if (nav.y < -0.5f) dir = 1;
            }

            var act = ActiveButtons;
            if (dir != 0 && act != null && act.Length > 0)
            {
                _index = (_index + dir + act.Length) % act.Length;
                SelectRow(_index);
                _nextNav = Time.unscaledTime + navRepeatDelay;
            }
        }

        #endregion

        #region 4. 行操作

        private void Activate(int i)
        {
            switch (i)
            {
                case 0: if (controller != null) controller.SetMenu(false); break;
                case 1: if (controller != null) controller.SetCpuEnabled(!controller.CpuEnabled); break;
                case 2: if (controller != null) controller.Invincible = !controller.Invincible; break;
                case 3: if (controller != null) controller.SetSlowMo(!controller.SlowMoActive); break;
                case 4: if (boxOverlay != null) boxOverlay.showBoxes = !boxOverlay.showBoxes; break;
                case 5: ToggleFrameData(); break;
                case 6: if (controller != null) controller.SetHudVisible(!controller.HudVisible); break;
                case 7: if (controller != null) controller.ResetBothPublic(); break;
                case 8: if (controller != null) controller.ExitTraining(); break;
            }
            Refresh();
        }

        // 【做什么】切换"帧数据"：同时控制 帧数据面板 + 帧数表（统一开关）
        private void ToggleFrameData()
        {
            bool now = !FrameDataVisible();
            if (controller != null) controller.FrameDataOn = now;
            var go = FrameDataTarget();
            if (go != null) go.SetActive(now);
        }

        private bool FrameDataVisible()
        {
            if (controller != null) return controller.FrameDataOn;
            var go = FrameDataTarget();
            return go == null || go.activeSelf;
        }

        private GameObject FrameDataTarget()
        {
            if (framePanel == null) return null;
            return framePanel.frameText != null ? framePanel.frameText.gameObject : framePanel.gameObject;
        }

        // 【做什么】按当前各模块真实状态刷新每行文字
        public void Refresh()
        {
            if (rowLabels == null) return;
            SetLabel(0, "继续");
            SetLabel(1, $"CPU（木桩）      {(controller != null && !controller.CpuEnabled ? "开" : "关")}");
            SetLabel(2, $"无敌             {(controller != null && controller.Invincible ? "开" : "关")}");
            SetLabel(3, $"慢动作           {(controller != null && controller.SlowMoActive ? "开" : "关")}");
            SetLabel(4, $"判定框           {(boxOverlay != null && boxOverlay.showBoxes ? "开" : "关")}");
            SetLabel(5, $"帧数据面板       {(FrameDataVisible() ? "开" : "关")}");
            SetLabel(6, $"HUD              {(controller != null && controller.HudVisible ? "开" : "关")}");
            SetLabel(7, "重置位置");
            SetLabel(8, "退出训练");

            // 开关行：开=按钮底色高亮（绿），关=白
            SetRowState(1, controller != null && !controller.CpuEnabled);   // 木桩开 = CPU 冻结
            SetRowState(2, controller != null && controller.Invincible);
            SetRowState(3, controller != null && controller.SlowMoActive);
            SetRowState(4, boxOverlay != null && boxOverlay.showBoxes);
            SetRowState(5, FrameDataVisible());
            SetRowState(6, controller != null && controller.HudVisible);
        }

        // 【做什么】按开关状态给按钮上底色高亮（开=绿 / 关=白），并在右侧写"开/关"
        // 【注意】"开"高亮与"选中"高亮叠加：选中时用两者混合色
        private void SetRowState(int i, bool on)
        {
            if (rowButtons == null || i >= rowButtons.Length || rowButtons[i] == null) return;
            var b = rowButtons[i];

            var onColor = new Color(0.45f, 0.9f, 0.45f, 1f);
            var cb = b.colors;
            cb.normalColor = on ? onColor : Color.white;
            cb.selectedColor = on ? Color.Lerp(onColor, selectionColor, 0.5f) : selectionColor;
            cb.highlightedColor = cb.selectedColor;
            b.colors = cb;

            if (_rowStatus != null && i < _rowStatus.Length && _rowStatus[i] != null)
                _rowStatus[i].text = on ? "开" : "关";
        }

        private void SetLabel(int i, string s)
        {
            if (rowLabels.Length > i && rowLabels[i] != null) rowLabels[i].text = s;
        }

        // 【做什么】给所有按钮（训练行 / 主菜单行 / 标签）设黄色选中高亮 + 关闭自动导航
        private void ApplyRowColors()
        {
            StyleButtons(rowButtons);
            StyleButtons(mainButtons);
            StyleButtons(tabButtons);
        }

        private void StyleButtons(Button[] arr)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                var b = arr[i];
                if (b == null) continue;
                var nav = b.navigation;
                nav.mode = Navigation.Mode.None;
                b.navigation = nav;

                var cb = b.colors;
                cb.normalColor = Color.white;
                cb.selectedColor = selectionColor;
                cb.highlightedColor = selectionColor;
                cb.pressedColor = selectionColor * 0.8f;
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0.05f;
                b.colors = cb;
            }
        }

        private void SelectRow(int i)
        {
            var act = ActiveButtons;
            if (act == null || act.Length == 0) return;
            if (i < 0 || i >= act.Length) return;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(act[i].gameObject);
        }

        #endregion
    }
}
