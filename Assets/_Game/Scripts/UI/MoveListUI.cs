using SuperSmashLike.Core;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SuperSmashLike.UI
{
    public class MoveListUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI playerText;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject itemPrefab;
        [SerializeField] private GameObject emptyHint;
        [SerializeField] private ScrollRect scrollRect;

        [Header("Tabs")]
        [SerializeField] private Button tabJab;
        [SerializeField] private Button tabTilt;
        [SerializeField] private Button tabSmash;
        [SerializeField] private Button tabAerial;
        [SerializeField] private Button tabSpecial;
        [SerializeField] private Button tabGrab;

        [Header("Tab Navigation")]
        [Tooltip("方向键切换 Tab 的手柄重复延迟（秒）")]
        [SerializeField] private float tabRepeatDelay = 0.18f;
        [Tooltip("当前选中 Tab 的颜色")]
        [SerializeField] private Color activeTabColor = new Color(1f, 0.85f, 0.2f, 1f);
        [Tooltip("未选中 Tab 的颜色")]
        [SerializeField] private Color inactiveTabColor = Color.white;

        [Header("Player Tabs (仅战斗暂停的出招表)")]
        [Tooltip("玩家切换 Tab 的容器（子物体在运行时按已参战玩家生成）")]
        [SerializeField] private RectTransform playerTabBar;
        [Tooltip("Q/E 按键提示的文字颜色")]
        [SerializeField] private Color playerHintColor = Color.white;
        [Tooltip("每个玩家 Tab 按钮的尺寸")]
        [SerializeField] private Vector2 playerTabSize = new Vector2(110f, 60f);
        [Tooltip("两侧按键提示的尺寸")]
        [SerializeField] private Vector2 playerHintSize = new Vector2(56f, 60f);
        [Tooltip("键盘：左 / 右 切换玩家的提示文字")]
        [SerializeField] private string hintKeyPrev = "Q";
        [SerializeField] private string hintKeyNext = "E";
        [Tooltip("手柄：左 / 右 肩键切换玩家的提示文字")]
        [SerializeField] private string hintPadPrev = "LB";
        [SerializeField] private string hintPadNext = "RB";

        // Tab 的排列顺序（方向键左右/上下循环的单位）
        private static readonly MoveCategory[] TabOrder =
        {
            MoveCategory.Jab, MoveCategory.Tilt, MoveCategory.Smash,
            MoveCategory.Aerial, MoveCategory.Special, MoveCategory.Grab
        };

        private List<FighterData.MoveListEntry> _allEntries;
        private MoveCategory _currentTab;
        private float _nextTabTime;

        private int _currentPlayerId = -1;   // 当前展示的玩家（-1=未指定）
        private bool _allowPlayerSwitch;      // 是否允许在出招表内切换玩家（战斗暂停=允许）

        // 玩家 Tab 栏的运行时内容（按钮 = 已参战玩家；两端 = Q/E 提示）
        private readonly List<Button> _playerTabs = new List<Button>();
        private readonly List<int> _playerTabIds = new List<int>();
        private bool _playerBarDirty = true;   // true = 下次显示时重建玩家 Tab

        private TextMeshProUGUI _hintPrev;     // 左侧切换提示（键盘 Q / 手柄 LB）
        private TextMeshProUGUI _hintNext;     // 右侧切换提示（键盘 E / 手柄 RB）
        private bool _useGamepadHints;         // 最近一次使用的设备是手柄 → 显示肩键提示

        private void Start()
        {
            if (tabJab) tabJab.onClick.AddListener(() => SwitchTab(MoveCategory.Jab));
            if (tabTilt) tabTilt.onClick.AddListener(() => SwitchTab(MoveCategory.Tilt));
            if (tabSmash) tabSmash.onClick.AddListener(() => SwitchTab(MoveCategory.Smash));
            if (tabAerial) tabAerial.onClick.AddListener(() => SwitchTab(MoveCategory.Aerial));
            if (tabSpecial) tabSpecial.onClick.AddListener(() => SwitchTab(MoveCategory.Special));
            if (tabGrab) tabGrab.onClick.AddListener(() => SwitchTab(MoveCategory.Grab));
            SwitchTab(MoveCategory.Jab);
        }

        // 方向键/摇杆切换 Tab —— 出招表在"战斗暂停"和"角色选择"两处共用（同一个共享组件），
        // 打开时由本组件接管方向键；角色选择界面里 P2 的方向键选人由 CharacterSelectPanel
        // 在出招表打开时主动让出（见其 HandlePlayerInput/HandleGamepadInput）。
        private void Update()
        {
            if (!gameObject.activeInHierarchy) return;

            // 出招表在"战斗暂停"和"角色选择"两处共用（同一个共享组件）：
            // 只要它打开，方向键就归它切换 Tab；唯独"战斗中(未暂停)"不接管。
            if (GameManager.Instance != null &&
                GameManager.Instance.CurrentGameState == GameState.Battle)
                return;

            var gp = Gamepad.current;

            // 根据"最近使用的设备"切换玩家切换按键的提示文案（Q/E ↔ LB/RB）
            UpdateHintDevice(gp);

            // 切换玩家（仅战斗暂停的出招表允许）：键盘 Q/E、手柄 LB/RB
            if (_allowPlayerSwitch)
            {
                bool prevP = Input.GetKeyDown(KeyCode.Q) || (gp != null && gp.leftShoulder.wasPressedThisFrame);
                bool nextP = Input.GetKeyDown(KeyCode.E) || (gp != null && gp.rightShoulder.wasPressedThisFrame);
                if (prevP || nextP) SwitchPlayer(prevP ? -1 : 1);
            }

            int dir = 0;

            // 键盘：上下左右（含 WASD）任一键都可切换；左/上=上一个，右/下=下一个
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) ||
                Input.GetKeyDown(KeyCode.UpArrow)   || Input.GetKeyDown(KeyCode.W))
                dir = -1;
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ||
                     Input.GetKeyDown(KeyCode.DownArrow)  || Input.GetKeyDown(KeyCode.S))
                dir = 1;

            // 手柄：摇杆优先，其次十字键；边沿 + 重复延迟（防一帧跳多格）
            if (dir == 0 && gp != null && Time.unscaledTime >= _nextTabTime)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;

                if      (nav.x >  0.5f || nav.y < -0.5f) dir = 1;
                else if (nav.x < -0.5f || nav.y >  0.5f) dir = -1;
            }

            if (dir != 0)
            {
                CycleTab(dir);
                _nextTabTime = Time.unscaledTime + tabRepeatDelay;
            }
        }

        public void Show(FighterData data)
        {
            gameObject.SetActive(true);
            _allowPlayerSwitch = false;
            Populate(data, -1);
        }

        public void ShowFor(FighterData fd)
        {
            gameObject.SetActive(true);
            _allowPlayerSwitch = false;
            Populate(fd, -1);
        }

        public void ShowFor(FighterData fd, int playerId)
        {
            gameObject.SetActive(true);
            _allowPlayerSwitch = false;
            Populate(fd, playerId);
        }

        // 战斗暂停用：允许在出招表内用 Q/E（手柄 LB/RB）切换查看 P1/P2 的出招表
        public void ShowFor(FighterData fd, int playerId, bool allowPlayerSwitch)
        {
            gameObject.SetActive(true);
            _allowPlayerSwitch = allowPlayerSwitch;
            _playerBarDirty = true;     // 每次打开重建，以反映当前参战玩家
            Populate(fd, playerId);
        }

        public void ShowCurrent()
        {
            if (GameManager.Instance == null) return;
            var gs = GameManager.Instance.gameSettings;
            if (gs == null) return;

            FighterData fd = gs.GetSelection(0);
            if (fd == null && GameManager.Instance.ActivePlayers.Count > 0)
                fd = GameManager.Instance.ActivePlayers[0].fighterData;

            Show(fd);
        }

        public void Populate(FighterData fd)
        {
            Populate(fd, -1);
        }

        public void Populate(FighterData fd, int playerId)
        {
            _currentPlayerId = playerId;
            UpdatePlayerBar();

            if (fd == null)
            {
                if (titleText) titleText.text = "";
                if (playerText) playerText.text = "";
                ShowEmpty();
                return;
            }

            if (titleText) titleText.text = fd.fighterName;
            if (playerText != null)
            {
                // 只显示 P1 / P2 这类简洁标识（"玩家 1（P1）"太冗余）
                if (playerId >= 0 && playerId <= 3)
                    playerText.text = "P" + (playerId + 1);
                else
                    playerText.text = "";
            }

            _allEntries = fd.GetMoveEntries();
            if (_allEntries == null || _allEntries.Count == 0)
            {
                ShowEmpty();
                return;
            }
            SwitchTab(_currentTab);
        }

        public void SwitchTab(MoveCategory cat)
        {
            _currentTab = cat;
            ClearContent();

            if (_allEntries == null)
            {
                ApplyTabTint(cat);
                return;
            }

            foreach (var e in _allEntries)
            {
                if (e.category != cat) continue;
                if (itemPrefab == null || contentRoot == null) continue;

                var go = Instantiate(itemPrefab, contentRoot);
                var item = go.GetComponent<MoveListItem>();
                if (item != null) item.Setup(e);
            }

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;

            ApplyTabTint(cat);
        }

        public void Close() => gameObject.SetActive(false);

        // 方向键循环切换：dir = -1 上一个 / +1 下一个
        private void CycleTab(int dir)
        {
            int i = System.Array.IndexOf(TabOrder, _currentTab);
            if (i < 0) i = 0;
            i = (i + dir + TabOrder.Length) % TabOrder.Length;
            SwitchTab(TabOrder[i]);
        }

        // 在出招表内切换玩家：dir = -1 上一个 / +1 下一个（只在已分配角色的玩家间循环）
        private void SwitchPlayer(int dir)
        {
            if (GameManager.Instance == null || GameManager.Instance.gameSettings == null) return;
            var gs = GameManager.Instance.gameSettings;

            var players = new List<int>();
            for (int p = 0; p < 4; p++)
            {
                if (gs.GetSelection(p) != null) players.Add(p);
            }
            if (players.Count == 0) return;

            int cur = players.IndexOf(_currentPlayerId);
            int next = cur < 0 ? 0 : (cur + dir + players.Count) % players.Count;
            int pid = players[next];

            Populate(gs.GetSelection(pid), pid);
        }

        // ===== 玩家 Tab 栏（战斗暂停时显示：Q [P1][P2] E） =====

        // 显示/隐藏玩家 Tab 栏；需要时重建（玩家集合变化）
        private void UpdatePlayerBar()
        {
            if (playerTabBar == null) return;

            if (!_allowPlayerSwitch)
            {
                playerTabBar.gameObject.SetActive(false);
                if (playerText != null) playerText.gameObject.SetActive(true);   // 非战斗：用文本标签
                return;
            }

            playerTabBar.gameObject.SetActive(true);
            // 有玩家 Tab 栏时，右上角的 "P1/P2" 文本就冗余了 → 隐藏
            if (playerText != null) playerText.gameObject.SetActive(false);

            if (_playerBarDirty)
            {
                BuildPlayerTabs();
                _playerBarDirty = false;
            }
            else
            {
                ApplyPlayerTabTint();
            }
        }

        // 运行时生成：左提示 Q + 各参战玩家的 Tab 按钮 + 右提示 E
        // 视觉上直接复用分类 Tab 按钮（Tab_Jab）作为模板，保证风格一致。
        private void BuildPlayerTabs()
        {
            if (playerTabBar == null || tabJab == null) return;

            // 清空旧内容（Destroy 为延迟销毁，列表先清干净即可）
            for (int i = playerTabBar.childCount - 1; i >= 0; i--)
                Destroy(playerTabBar.GetChild(i).gameObject);
            _playerTabs.Clear();
            _playerTabIds.Clear();
            _hintPrev = null;
            _hintNext = null;

            var labelTpl = tabJab.GetComponentInChildren<TextMeshProUGUI>(true);
            if (labelTpl == null) return;

            _hintPrev = CreateHint(labelTpl);

            if (GameManager.Instance != null && GameManager.Instance.gameSettings != null)
            {
                var gs = GameManager.Instance.gameSettings;
                for (int p = 0; p < 4; p++)
                {
                    if (gs.GetSelection(p) == null) continue;   // 只显示已参战的玩家

                    var go = Instantiate(tabJab.gameObject, playerTabBar);
                    go.name = "PlayerTab_P" + (p + 1);

                    var rt = go.GetComponent<RectTransform>();
                    if (rt != null) SetCenteredSize(rt, playerTabSize);

                    var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (label != null) label.text = "P" + (p + 1);

                    var btn = go.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();     // 去掉克隆自模板的点击行为
                        int captured = p;
                        btn.onClick.AddListener(() => SelectPlayer(captured));

                        // 关掉 UGUI 自动导航，避免 EventSystem 自动选中造成"第二个高亮"
                        var nav = btn.navigation;
                        nav.mode = Navigation.Mode.None;
                        btn.navigation = nav;
                    }

                    _playerTabs.Add(btn);
                    _playerTabIds.Add(p);
                }
            }

            _hintNext = CreateHint(labelTpl);
            RefreshHintTexts();
            ApplyPlayerTabTint();
        }

        // 复制一个 Tab 文本作为按键提示（文案由 RefreshHintTexts 按设备填充）
        private TextMeshProUGUI CreateHint(TextMeshProUGUI labelTpl)
        {
            var go = Instantiate(labelTpl.gameObject, playerTabBar);
            go.name = "PlayerHint";

            var rt = go.GetComponent<RectTransform>();
            if (rt != null) SetCenteredSize(rt, playerHintSize);

            var t = go.GetComponent<TextMeshProUGUI>();
            if (t != null) t.color = playerHintColor;
            return t;
        }

        // 按"最近使用的设备"刷新两侧提示文案：键盘 Q/E ↔ 手柄 LB/RB
        private void RefreshHintTexts()
        {
            if (_hintPrev != null) _hintPrev.text = _useGamepadHints ? hintPadPrev : hintKeyPrev;
            if (_hintNext != null) _hintNext.text = _useGamepadHints ? hintPadNext : hintKeyNext;
        }

        // 检测最近一次输入来自键盘还是手柄，变化时切换提示文案
        private void UpdateHintDevice(Gamepad gp)
        {
            bool gamepadActive = gp != null && (
                gp.leftShoulder.wasPressedThisFrame  || gp.rightShoulder.wasPressedThisFrame ||
                gp.buttonSouth.wasPressedThisFrame   || gp.buttonEast.wasPressedThisFrame   ||
                gp.buttonWest.wasPressedThisFrame    || gp.buttonNorth.wasPressedThisFrame  ||
                gp.startButton.wasPressedThisFrame   || gp.selectButton.wasPressedThisFrame ||
                gp.leftStick.ReadValue().sqrMagnitude > 0.25f ||
                gp.dpad.ReadValue().sqrMagnitude > 0.25f);

            if (gamepadActive == _useGamepadHints) return;   // 设备没变，不刷新

            if (gamepadActive) { _useGamepadHints = true;  RefreshHintTexts(); }
            else if (Input.anyKeyDown) { _useGamepadHints = false; RefreshHintTexts(); }
        }

        // 让子物体以中心锚点固定尺寸，交由 LayoutGroup 排版
        private static void SetCenteredSize(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        // 点击某个玩家 Tab → 切到该玩家的出招表
        private void SelectPlayer(int playerId)
        {
            if (GameManager.Instance == null || GameManager.Instance.gameSettings == null) return;
            var fd = GameManager.Instance.gameSettings.GetSelection(playerId);
            if (fd == null) return;
            Populate(fd, playerId);
        }

        // 高亮当前玩家 Tab（与分类 Tab 用同一套颜色）
        private void ApplyPlayerTabTint()
        {
            for (int i = 0; i < _playerTabs.Count; i++)
            {
                var btn = _playerTabs[i];
                if (btn == null) continue;

                bool on = _playerTabIds[i] == _currentPlayerId;
                var cb = btn.colors;
                cb.normalColor      = on ? activeTabColor : inactiveTabColor;
                cb.selectedColor    = on ? activeTabColor : inactiveTabColor;
                cb.highlightedColor = on ? activeTabColor : inactiveTabColor;
                btn.colors = cb;
            }
        }

        private Button GetTabButton(MoveCategory c)
        {
            switch (c)
            {
                case MoveCategory.Jab:     return tabJab;
                case MoveCategory.Tilt:    return tabTilt;
                case MoveCategory.Smash:   return tabSmash;
                case MoveCategory.Aerial:  return tabAerial;
                case MoveCategory.Special: return tabSpecial;
                case MoveCategory.Grab:    return tabGrab;
            }
            return null;
        }

        // 高亮当前 Tab：改 ColorTint 的 normal/selected 色（不依赖 EventSystem 选中态）
        private void ApplyTabTint(MoveCategory active)
        {
            foreach (var cat in TabOrder)
            {
                var btn = GetTabButton(cat);
                if (btn == null) continue;

                bool on = (cat == active);
                var cb = btn.colors;
                cb.normalColor = on ? activeTabColor : inactiveTabColor;
                cb.selectedColor = on ? activeTabColor : inactiveTabColor;
                btn.colors = cb;
            }
        }

        private void ClearContent()
        {
            if (contentRoot == null) return;
            foreach (Transform child in contentRoot)
                Destroy(child.gameObject);
            if (emptyHint) emptyHint.SetActive(false);
        }

        private void ShowEmpty()
        {
            ClearContent();
            if (emptyHint) emptyHint.SetActive(true);
        }
    }
}
