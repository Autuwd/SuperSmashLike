using System.Collections.Generic;
using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// MainMenuPanel — 主页（竖直按钮列表）
// 主页只放几个"进入模式"的按钮；确认进入对应模块子页；子页内取消返回主页。
// 导航：↑↓ / W S / 十字键·摇杆 = 移动
//       J / 小键盘1 / 回车 / 空格 / 手柄A = 确认（进子页 / 触发）
//       K / 小键盘2 / 手柄B         = 取消（返回主页）
//       子页内 ←→ / A D             = 调整数值/开关
// 注意：Q/E 切换与底部按键提示属于"菜单"，不在主页显示。
// ============================================================
namespace SuperSmashLike.UI
{
    public class MainMenuPanel : MonoBehaviour
    {
        [Header("主页按钮（竖直，按顺序）")]
        public Button[] rootButtons = new Button[7];

        [Header("各按钮对应的模块子页")]
        public GameObject[] pages = new GameObject[7];

        [Header("开始游戏页")]
        public TMP_Text playerCountLabel;
        public TMP_Text aiCountLabel;
        [Range(1, 4)] public int playerCount = 1;
        [Range(0, 3)] public int aiCount = 1;

        [Header("设备选择（每个玩家一行，超出玩家数的行自动隐藏）")]
        public GameObject[] deviceRowObjs = new GameObject[4];
        public TMP_Text[] devLabels = new TMP_Text[4];
        [Range(0, 2)] public int[] devChoice = { 0, 2, 0, 2 };   // 0=键盘P1区 1=键盘P2区 2=手柄

        [Header("样式")]
        public Color selectionColor = new Color(1f, 0.85f, 0.2f, 1f);

        private int _rootIndex;
        private int _page = -1;
        private readonly List<Selectable> _rows = new();
        private int _row;
        private float _nextNav;
        private float _nextAdj;   // 手柄水平调整冷却时间戳（问题③）

        private void OnEnable()
        {
            WireButtons();
            StyleRootButtons();
            ShowRoot();
            RefreshCounts();
        }

        private void Update()
        {
            var gp = Gamepad.current;

            // 取消：子页 → 返回主页
            if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Keypad2) || Input.GetKeyDown(KeyCode.Escape)
                || (gp != null && gp.buttonEast.wasPressedThisFrame))
            {
                if (_page >= 0) ShowRoot();
                return;
            }

            // 确认
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Keypad1)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)
                || (gp != null && gp.buttonSouth.wasPressedThisFrame))
            {
                if (_page < 0) OpenPage(_rootIndex);
                else ConfirmRow();
                return;
            }

            // 上下移动
            int dir = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = 1;
            if (dir == 0 && gp != null && Time.unscaledTime >= _nextNav)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;
                if (nav.y > 0.5f) { dir = -1; _nextNav = Time.unscaledTime + 0.18f; }
                else if (nav.y < -0.5f) { dir = 1; _nextNav = Time.unscaledTime + 0.18f; }
            }
            if (dir != 0)
            {
                if (_page < 0 && rootButtons != null && rootButtons.Length > 0)
                {
                    _rootIndex = (_rootIndex + dir + rootButtons.Length) % rootButtons.Length;
                    HighlightRoot(_rootIndex);
                }
                else if (_rows.Count > 0)
                {
                    _row = (_row + dir + _rows.Count) % _rows.Count;
                    SelectRow(_row);
                }
            }

            // 子页内：左右调整
            if (_page >= 0)
            {
                int adj = 0;
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) adj = -1;
                else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) adj = 1;

                // 手柄水平调整（问题③修复：此前只支持键盘左右 → 摇杆/十字键水平无反应）
                if (adj == 0 && gp != null && Time.unscaledTime >= _nextAdj)
                {
                    Vector2 nav = gp.leftStick.ReadValue();
                    Vector2 dpad = gp.dpad.ReadValue();
                    if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;
                    if (nav.x > 0.5f) { adj = 1; _nextAdj = Time.unscaledTime + 0.18f; }
                    else if (nav.x < -0.5f) { adj = -1; _nextAdj = Time.unscaledTime + 0.18f; }
                }

                if (adj != 0) AdjustCurrent(adj);
            }
        }

        #region 主页 / 子页

        private void ShowRoot()
        {
            _page = -1;
            if (pages != null)
                for (int i = 0; i < pages.Length; i++)
                    if (pages[i] != null) pages[i].SetActive(false);

            if (rootButtons != null && rootButtons.Length > 0)
            {
                _rootIndex = Mathf.Clamp(_rootIndex, 0, rootButtons.Length - 1);
                HighlightRoot(_rootIndex);
            }
        }

        private void OpenPage(int i)
        {
            if (pages == null || i < 0 || i >= pages.Length || pages[i] == null) return;
            _page = i;
            for (int k = 0; k < pages.Length; k++)
                if (pages[k] != null) pages[k].SetActive(k == i);

            BuildRows();
            if (_rows.Count > 0) SelectRow(_row = 0);
        }

        private void BuildRows()
        {
            _rows.Clear();
            if (pages == null || _page < 0 || pages[_page] == null) return;
            foreach (var s in pages[_page].GetComponentsInChildren<Selectable>(true))
            {
                if (s == null || !s.gameObject.activeInHierarchy) continue;
                _rows.Add(s);
            }
        }

        // 【做什么】人数变化后重建行列表并保持当前选中（问题③修复）
        // 【为什么】设备行随 playerCount 显隐，但 _rows 此前仅在 OpenPage 时构建一次：
        //   上页后再调人数 → 新出现的设备行不在 _rows 里 → 方向键永远选不中。
        private void RebuildRowsKeepSelection()
        {
            if (_page < 0) return;
            string keepName = (_rows.Count > 0 && _row >= 0 && _row < _rows.Count)
                ? _rows[_row].gameObject.name
                : null;
            BuildRows();
            if (_rows.Count == 0) { _row = 0; return; }
            int idx = string.IsNullOrEmpty(keepName) ? -1 : _rows.FindIndex(s => s.gameObject.name == keepName);
            _row = idx >= 0 ? idx : Mathf.Clamp(_row, 0, _rows.Count - 1);
            SelectRow(_row);
        }

        #endregion

        #region 高亮

        private void StyleRootButtons()
        {
            if (rootButtons == null) return;
            for (int i = 0; i < rootButtons.Length; i++)
            {
                var b = rootButtons[i];
                if (b == null) continue;
                var nav = b.navigation;
                nav.mode = Navigation.Mode.None;
                b.navigation = nav;
                b.transition = Selectable.Transition.ColorTint;
                var cb = b.colors;
                cb.normalColor = Color.white;
                cb.selectedColor = selectionColor;   // 高亮=按钮底色
                cb.highlightedColor = selectionColor;
                cb.pressedColor = selectionColor * 0.8f;
                b.colors = cb;
            }
        }

        private void HighlightRoot(int i)
        {
            if (rootButtons == null || i < 0 || i >= rootButtons.Length) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(rootButtons[i].gameObject);
            else if (rootButtons[i] != null) rootButtons[i].Select();
        }

        private void SelectRow(int i)
        {
            if (_rows.Count == 0 || i < 0 || i >= _rows.Count) return;
            var s = _rows[i];
            var cb = s.colors;
            cb.normalColor = Color.white;
            cb.selectedColor = selectionColor;
            cb.highlightedColor = selectionColor;
            s.colors = cb;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(s.gameObject);
            else s.Select();
        }

        #endregion

        #region 子页动作

        private void ConfirmRow()
        {
            if (_rows.Count == 0) return;
            var s = _rows[_row];
            var tg = s.GetComponent<Toggle>();
            if (tg != null) { tg.isOn = !tg.isOn; return; }

            string nm = s.gameObject.name;
            int dp = DevIndexFromName(nm);
            if (dp >= 0) { CycleDevice(dp, 1); return; }

            var btn = s.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
        }

        private void AdjustCurrent(int dir)
        {
            if (_rows.Count == 0) return;
            var s = _rows[_row];
            var tg = s.GetComponent<Toggle>();
            if (tg != null) { tg.isOn = dir > 0; return; }

            string nm = s.gameObject.name;
            int dp = DevIndexFromName(nm);
            if (dp >= 0) CycleDevice(dp, dir);
            else if (nm.Contains("Player")) ChangePlayer(dir);
            else if (nm.Contains("Ai")) ChangeAi(dir);
        }

        private void WireButtons()
        {
            // 主页按钮 → 打开对应模块子页（鼠标点击也能用）
            if (rootButtons != null)
            {
                for (int i = 0; i < rootButtons.Length; i++)
                {
                    if (rootButtons[i] == null) continue;
                    int idx = i;
                    rootButtons[i].onClick.RemoveAllListeners();
                    rootButtons[i].onClick.AddListener(() => OpenPage(idx));
                }
            }

            Wire("PlayerInc", () => ChangePlayer(+1));
            Wire("PlayerDec", () => ChangePlayer(-1));
            Wire("AiInc", () => ChangeAi(+1));
            Wire("AiDec", () => ChangeAi(-1));
            Wire("StartGameBtn", StartGame);
            Wire("EnterTrainingBtn", StartTraining);

            // 设备行按钮（问题③修复：此前未接 onClick → 鼠标点击 DevP 行无任何反应）
            Wire("DevP1", () => CycleDevice(0, +1));
            Wire("DevP2", () => CycleDevice(1, +1));
            Wire("DevP3", () => CycleDevice(2, +1));
            Wire("DevP4", () => CycleDevice(3, +1));
        }

        private void Wire(string btnName, UnityEngine.Events.UnityAction action)
        {
            foreach (var b in GetComponentsInChildren<Button>(true))
            {
                if (b != null && b.name == btnName)
                {
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(action);
                }
            }
        }

        // 【问题③修复】人数变化后同步重建子页行列表（设备行显隐随之变化）
        // 【问题②约定】参与者总数（人类+AI）≤ 4：调人类超限时自动降 AI；AI 上限 = 4 - 人类
        public void ChangePlayer(int d)
        {
            playerCount = Mathf.Clamp(playerCount + d, 1, 4);
            aiCount = Mathf.Min(aiCount, 4 - playerCount);
            RefreshCounts();
            RebuildRowsKeepSelection();
        }

        public void ChangeAi(int d)
        {
            aiCount = Mathf.Clamp(aiCount + d, 0, 4 - playerCount);
            RefreshCounts();
        }

        private static string DeviceName(int d) => d == 2 ? "手柄" : (d == 1 ? "键盘P2区" : "键盘P1区");

        private void RefreshCounts()
        {
            if (playerCountLabel != null) playerCountLabel.text = playerCount.ToString();
            if (aiCountLabel != null) aiCountLabel.text = aiCount.ToString();

            // 每个玩家一行；单人（1）→ 全隐（默认键鼠+手柄皆可）
            for (int i = 0; i < deviceRowObjs.Length; i++)
            {
                if (deviceRowObjs[i] != null)
                    deviceRowObjs[i].SetActive(playerCount > 1 && i < playerCount);
                if (devLabels != null && i < devLabels.Length && devLabels[i] != null)
                    devLabels[i].text = DeviceName(devChoice[i]);
            }
        }

        private static int DevIndexFromName(string n)
        {
            int i = n.IndexOf("DevP");
            if (i < 0) return -1;
            int d = i + 4;
            if (d < n.Length && n[d] >= '1' && n[d] <= '4') return n[d] - '1';
            return -1;
        }

        private void CycleDevice(int player, int dir)
        {
            if (player < 0 || player >= devChoice.Length) return;
            devChoice[player] = (devChoice[player] + dir + 3) % 3;
            RefreshCounts();
        }

        public void StartGame()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.playerCount = playerCount;
                gm.aiCount = aiCount;
                // 单人：P1 默认"键鼠 + 手柄都可"（掩码在 InputManager.Start 里按 playerCount<=1 处理）
                if (playerCount <= 1)
                {
                    gm.playerDevice[0] = 0;
                    gm.playerDevice[1] = 2;
                }
                else
                {
                    for (int i = 0; i < gm.playerDevice.Length && i < devChoice.Length; i++)
                        gm.playerDevice[i] = devChoice[i];
                }
            }
            gm?.CancelTrainingRequest();
            gm?.SwitchState(GameState.CharacterSelect);
        }

        public void StartTraining()
        {
            GameManager.Instance?.RequestTraining();
        }

        #endregion
    }
}
