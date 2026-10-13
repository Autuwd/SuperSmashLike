using SuperSmashLike.Core;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SuperSmashLike.UI
{
    public class CharacterSelectPanel : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField] private Transform rosterRoot;
        [SerializeField] private GameObject cardPrefab;
        [SerializeField] private Button startButton;
        [SerializeField] private int gridCols = 3;

        [Header("Player Previews")]
        [SerializeField] private GameObject previewP1;
        [SerializeField] private GameObject previewP2;
        [SerializeField] private GameObject previewP3;
        [SerializeField] private GameObject previewP4;

        [SerializeField] private TextMeshProUGUI nameP1;
        [SerializeField] private TextMeshProUGUI nameP2;
        [SerializeField] private TextMeshProUGUI nameP3;
        [SerializeField] private TextMeshProUGUI nameP4;

        [SerializeField] private TextMeshProUGUI statusP1;
        [SerializeField] private TextMeshProUGUI statusP2;
        [SerializeField] private TextMeshProUGUI statusP3;
        [SerializeField] private TextMeshProUGUI statusP4;

        [SerializeField] private TextMeshProUGUI featureP1;
        [SerializeField] private TextMeshProUGUI featureP2;
        [SerializeField] private TextMeshProUGUI featureP3;
        [SerializeField] private TextMeshProUGUI featureP4;

        [Header("Move List")]
        [SerializeField] private MoveListUI moveListUI;

        private readonly int[] _highlightIndex = new int[4] { 0, 0, 0, 0 };
        private readonly bool[] _confirmed = new bool[4] { false, false, false, false };
        private readonly bool[] _active = new bool[4] { false, false, false, false };

        private readonly List<GameObject> _cards = new List<GameObject>();
        private readonly List<FighterData> _fighterList = new List<FighterData>();

        private float _gpNextMoveTime;
        private const float GPRepeatDelay = 0.18f;

        private int _moveListOwnerPid = -1; // 当前打开出招表的玩家，-1=未打开（用于再按一次关闭）

        private Graphic _startBlinkGraphic;   // "开始游戏"按钮闪烁提示用的图形（懒加载）

        [Header("Start Button Blink")]
        [Tooltip("全部确认后'开始游戏'按钮的闪烁颜色（与菜单高亮同款黄色）")]
        [SerializeField] private Color startBlinkColor = new Color(1f, 0.85f, 0.2f, 1f);

        #region Lifecycle

        private void OnEnable()
        {
            BuildRoster();
            ResetAllPlayers();
            RefreshAllVisuals();
            UpdatePlayerPreviews();
            UpdateStartButton();

            if (startButton != null)
                startButton.onClick.AddListener(StartMatch);
        }

        private void OnDisable()
        {
            if (startButton != null)
                startButton.onClick.RemoveListener(StartMatch);

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i])
                    Destroy(_cards[i]);
            }
            _cards.Clear();
            _fighterList.Clear();
        }

        private void Update()
        {
            // 按键与战斗键一一对应（见 Controls.inputactions）
            HandlePlayerInput(0);  // P1: WASD 移动 + J=攻击(确认) K=必杀(取消) L=防御(随机) Space=跳跃(出招表)
            HandlePlayerInput(1);  // P2: ↑←↓→ 移动 + 小键盘1=攻击(确认) 2=必杀(取消) 3=防御(随机) 0=跳跃(出招表)
            HandleGamepadInput();  // 手柄 = P2: 摇杆/十字键 + A=攻击(确认) X=必杀(出招表) LT=防御(随机) B=取消(返回标题)

            UpdateStartButton();
        }

        #endregion

        #region Roster

        private void BuildRoster()
        {
            if (GameManager.Instance == null || GameManager.Instance.gameSettings == null)
                return;

            GameSettings g = GameManager.Instance.gameSettings;
            var fighters = g.GetSelectableFighters();

            _fighterList.Clear();
            if (fighters != null)
            {
                foreach (var f in fighters)
                    _fighterList.Add(f);
            }

            // 清理旧卡牌
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i]) Destroy(_cards[i]);
            }
            _cards.Clear();

            for (int i = 0; i < _fighterList.Count; i++)
            {
                FighterData fd = _fighterList[i];
                GameObject card = Instantiate(cardPrefab, rosterRoot);
                _cards.Add(card);

                int idx = i;
                CardView view = card.GetComponent<CardView>();
                if (view != null)
                {
                    view.SetData(fd, () =>
                    {
                        // 点击卡牌：优先分配给第一个未确认的活跃玩家
                        for (int p = 0; p < 4; p++)
                        {
                            if (_active[p] && !_confirmed[p])
                            {
                                _highlightIndex[p] = idx;
                                RefreshAllVisuals();
                                UpdatePlayerPreviews();
                                return;
                            }
                        }
                        // 如果所有活跃玩家都已确认，不处理点击
                    });
                }
            }
        }

        private void ResetAllPlayers()
        {
            for (int i = 0; i < 4; i++)
            {
                _highlightIndex[i] = 0;
                _confirmed[i] = false;
                _active[i] = false;
            }

            // 默认激活 P1 + P2
            _active[0] = true;
            _active[1] = true;
            _active[2] = false;
            _active[3] = false;

            if (startButton != null)
                startButton.gameObject.SetActive(false);

            _moveListOwnerPid = -1;
        }

        #endregion

        #region Input

        // P1: WASD + J/K/L/Space(攻击/必杀/防御/跳跃)   P2: ↑←↓→ + 小键盘1/2/3/0
        private void HandlePlayerInput(int pid)
        {
            if (pid < 0 || pid > 1) return;
            if (!_active[pid]) return;

            KeyCode up, down, left, right, confirm, cancel, reroll, moves;
            if (pid == 0)
            {
                // P1：与战斗键对应 —— J=攻击 K=必杀 L=防御 Space=跳跃
                up = KeyCode.W;      down = KeyCode.S;
                left = KeyCode.A;    right = KeyCode.D;
                confirm = KeyCode.J; cancel = KeyCode.K;
                // 允许 1/2 也可操作
                reroll = KeyCode.L;  moves = KeyCode.Space;
            }
            else
            {
                // P2：与战斗键对应 —— 小键盘1=攻击 2=必杀 3=防御 0=跳跃
                up = KeyCode.UpArrow;    down = KeyCode.DownArrow;
                left = KeyCode.LeftArrow; right = KeyCode.RightArrow;
                confirm = KeyCode.Keypad1; cancel = KeyCode.Keypad2;
                reroll = KeyCode.Keypad3;  moves = KeyCode.Keypad0;
            }

            // 出招表打开时：方向键归出招表切换 Tab（交由 MoveListUI 处理），
            // 本面板不再移动卡牌；取消键(K/小键盘2)=返回，关闭出招表。
            if (IsMoveListOpen)
            {
                bool kbCancel = Input.GetKeyDown(cancel);
                if (pid == 1) kbCancel |= Input.GetKeyDown(KeyCode.Keypad2);
                if (kbCancel || (Input.GetKeyDown(moves) && _moveListOwnerPid == pid))
                    CloseMoveList();
                return;
            }

            // 已确认：允许取消 / 出招表 / 全部确认时用确认键开始
            if (_confirmed[pid])
            {
                bool kbCancel2 = Input.GetKeyDown(cancel);
                bool kbConfirm2 = Input.GetKeyDown(confirm);
                // 小键盘只对 P2 生效
                if (pid == 1)
                {
                    kbCancel2 |= Input.GetKeyDown(KeyCode.Keypad2);
                    kbConfirm2 |= Input.GetKeyDown(KeyCode.Keypad1);
                }
                if (kbCancel2)
                    CancelPlayer(pid);
                else if (Input.GetKeyDown(moves))
                    ShowMoveListFor(pid);
                else if (kbConfirm2 && AllConfirmed())
                    StartMatch();
                return;
            }

            int count = _cards.Count;
            if (count == 0) return;

            bool moved = false;
            if (Input.GetKeyDown(left))
            {
                _highlightIndex[pid] = (_highlightIndex[pid] - 1 + count) % count;
                moved = true;
            }
            else if (Input.GetKeyDown(right))
            {
                _highlightIndex[pid] = (_highlightIndex[pid] + 1) % count;
                moved = true;
            }
            else if (Input.GetKeyDown(up))
            {
                _highlightIndex[pid] = (_highlightIndex[pid] - gridCols + count) % count;
                moved = true;
            }
            else if (Input.GetKeyDown(down))
            {
                _highlightIndex[pid] = (_highlightIndex[pid] + gridCols) % count;
                moved = true;
            }

                bool kbConfirmUnconfirmed = Input.GetKeyDown(confirm);
                if (pid == 1) kbConfirmUnconfirmed |= Input.GetKeyDown(KeyCode.Keypad1);
                if (kbConfirmUnconfirmed)
                    ConfirmPlayer(pid);
            else if (Input.GetKeyDown(reroll))
                RandomizeFor(pid);
            else if (Input.GetKeyDown(moves))
                ShowMoveListFor(pid);
            else if (Input.GetKeyDown(cancel) || (pid == 1 && Input.GetKeyDown(KeyCode.Keypad2)))
                BackToTitle();   // 未确认时按取消返回标题界面

            if (moved)
            {
                RefreshAllVisuals();
                UpdatePlayerPreviews();
            }
        }

        private void HandleGamepadInput()
        {
            var gp = Gamepad.current;
            if (gp == null) return;
            const int pid = 1;                       // 手柄 = P2
            if (!_active[pid]) return;

            // 出招表打开时：方向键归出招表切换 Tab；B=返回，关闭出招表。
            if (IsMoveListOpen)
            {
                if (gp.buttonEast.wasPressedThisFrame) CloseMoveList();
                return;
            }

            // 已确认：B/Y=取消(先解除确认) / X=出招表 / 全部确认时 A=确定(开始)
            if (_confirmed[pid])
            {
                if (gp.buttonNorth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame)
                    CancelPlayer(pid);
                else if (gp.buttonWest.wasPressedThisFrame)
                    ShowMoveListFor(pid);
                else if (gp.buttonSouth.wasPressedThisFrame && AllConfirmed())
                    StartMatch();
                return;
            }

            int count = _cards.Count;
            if (count == 0) return;

            // 方向：摇杆优先，其次十字键；边沿触发 + 重复延迟（防一帧跳多格）
            Vector2 nav = gp.leftStick.ReadValue();
            Vector2 dpad = gp.dpad.ReadValue();
            if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;

            if (Time.unscaledTime >= _gpNextMoveTime)
            {
                bool moved = false;
                if      (nav.x >  0.5f) { _highlightIndex[pid] = (_highlightIndex[pid] + 1) % count; moved = true; }
                else if (nav.x < -0.5f) { _highlightIndex[pid] = (_highlightIndex[pid] - 1 + count) % count; moved = true; }
                else if (nav.y >  0.5f) { _highlightIndex[pid] = (_highlightIndex[pid] - gridCols + count) % count; moved = true; }
                else if (nav.y < -0.5f) { _highlightIndex[pid] = (_highlightIndex[pid] + gridCols) % count; moved = true; }

                if (moved) { RefreshAllVisuals(); UpdatePlayerPreviews(); _gpNextMoveTime = Time.unscaledTime + GPRepeatDelay; }
            }

            // 与战斗键对应：A=攻击(确认) / LT=防御(随机) / X=必杀(出招表) / B=取消(返回标题)
            if (gp.buttonSouth.wasPressedThisFrame)        ConfirmPlayer(pid);   // A 确认
            else if (gp.leftTrigger.wasPressedThisFrame)   RandomizeFor(pid);    // LT 随机
            else if (gp.buttonWest.wasPressedThisFrame)    ShowMoveListFor(pid); // X 出招表
            else if (gp.buttonEast.wasPressedThisFrame)    BackToTitle();        // B 返回标题
        }

        #endregion

        #region Selection

        // 随机指定玩家的高亮（不改变其他玩家）
        private void RandomizeFor(int pid)
        {
            if (pid < 0 || pid > 3) return;
            if (!_active[pid] || _confirmed[pid]) return;
            if (_cards.Count == 0) return;

            _highlightIndex[pid] = Random.Range(0, _cards.Count);
            RefreshAllVisuals();
            UpdatePlayerPreviews();
        }

        // 显示当前玩家的高亮角色出招表；同一玩家再按一次对应键 → 关闭（toggle）
        private void ShowMoveListFor(int pid)
        {
            if (pid < 0 || pid > 3) return;
            if (!_active[pid]) return;
            if (_fighterList.Count == 0 || moveListUI == null) return;

            // 再按一次对应按键即可关闭
            if (_moveListOwnerPid == pid && moveListUI.gameObject.activeSelf)
            {
                CloseMoveList();
                return;
            }

            int idx = _highlightIndex[pid] % _fighterList.Count;
            moveListUI.ShowFor(_fighterList[idx], pid);
            _moveListOwnerPid = pid;
        }

        // 出招表是否处于打开状态
        private bool IsMoveListOpen => moveListUI != null && moveListUI.gameObject.activeSelf;

        // 关闭出招表（返回键 / 再按一次出招表键 / 手柄 B）
        private void CloseMoveList()
        {
            if (moveListUI != null) moveListUI.Close();
            _moveListOwnerPid = -1;
        }

        private void ConfirmPlayer(int pid)
        {
            if (pid < 0 || pid > 3) return;
            if (!_active[pid]) _active[pid] = true;
            if (_confirmed[pid]) return;

            int count = _fighterList.Count;
            if (count == 0) return;
            int idx = _highlightIndex[pid] % count;

            if (GameManager.Instance != null && GameManager.Instance.gameSettings != null)
            {
                GameSettings s = GameManager.Instance.gameSettings;
                s.SetSelection(pid, _fighterList[idx]);
            }

            _confirmed[pid] = true;
            RefreshAllVisuals();
            UpdatePlayerPreviews();
            UpdateStartButton();
        }

        private void CancelPlayer(int pid)
        {
            if (pid < 0 || pid > 3) return;
            if (!_confirmed[pid]) return;

            _confirmed[pid] = false;

            // 取消确认后允许重新选择该玩家
            RefreshAllVisuals();
            UpdatePlayerPreviews();
            UpdateStartButton();
        }

        // 取消键：未确认 → 返回标题；已确认 → 取消当前玩家确认（不返回标题）
        private void BackToTitle()
        {
            // 如果当前有玩家处于已确认状态 → 优先取消确认，而不是直接返回标题（符合直觉）
            for (int p = 0; p < 4; p++)
            {
                if (_active[p] && _confirmed[p])
                {
                    CancelPlayer(p);
                    return;
                }
            }

            CloseMoveList();
            if (GameManager.Instance != null)
                GameManager.Instance.SwitchState(GameState.Title);
        }

        private bool AllConfirmed()
        {
            bool hasActive = false;
            for (int i = 0; i < 4; i++)
            {
                if (_active[i])
                {
                    hasActive = true;
                    if (!_confirmed[i]) return false;
                }
            }
            return hasActive;
        }

        #endregion

        #region UI

        private void RefreshAllVisuals()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var cv = _cards[i].GetComponent<CardView>();
                if (cv == null) continue;

                // 收集标记该卡牌的所有玩家（位掩码）→ 支持多人同选时叠加色
                int highlightMask = 0;   // 未确认的高亮
                int confirmMask = 0;     // 已确认
                for (int p = 0; p < 4; p++)
                {
                    if (!_active[p]) continue;
                    if (_highlightIndex[p] % _cards.Count != i) continue;

                    if (_confirmed[p]) confirmMask |= (1 << p);
                    else               highlightMask |= (1 << p);
                }

                cv.SetState(highlightMask, confirmMask);
            }
        }

        private void UpdatePlayerPreviews()
        {
            SetPreview(previewP1, nameP1, statusP1, featureP1, 0);
            SetPreview(previewP2, nameP2, statusP2, featureP2, 1);
            SetPreview(previewP3, nameP3, statusP3, featureP3, 2);
            SetPreview(previewP4, nameP4, statusP4, featureP4, 3);
        }

        private void SetPreview(GameObject panel, TMP_Text nameText, TMP_Text statusText, TMP_Text featureText, int pid)
        {
            if (panel == null) return;

            if (!_active[pid])
            {
                panel.SetActive(false);
                return;
            }

            panel.SetActive(true);

            int count = _fighterList.Count;
            if (count == 0) return;
            int idx = _highlightIndex[pid] % count;
            FighterData fd = _fighterList[idx];

            if (nameText != null)
                nameText.text = fd.fighterName;

            if (statusText != null)
            {
                if (_confirmed[pid])
                    statusText.text = "<color=#00FF66>【已确认】</color>";
                else
                    statusText.text = "<color=#FFCC00>【未确认】</color>";
            }

            if (featureText != null && fd != null)
            {
                // 填充体重/跳跃/移速/特性（字段不存在时用安全回退）
                string weight = fd.weightClass.ToString();
                float jump = fd.jumpForce;
                float speed = fd.airSpeed;
                featureText.text = $"特色：{weight} | 跳跃 {jump:F1} | 移速 {speed:F1}";
            }
        }

        private void UpdateStartButton()
        {
            if (startButton == null) return;

            bool allOk = AllConfirmed();
            startButton.gameObject.SetActive(allOk);
            startButton.interactable = allOk;

            // 全部确认后 → "开始游戏"按钮用菜单同款黄色呼吸闪烁，提醒玩家按键开赛
            if (allOk)
            {
                if (_startBlinkGraphic == null)
                {
                    // 关掉 ColorTint，避免 Selectable 的状态色与闪烁色互相打架
                    startButton.transition = Selectable.Transition.None;
                    _startBlinkGraphic = startButton.targetGraphic != null
                        ? startButton.targetGraphic
                        : startButton.GetComponent<Graphic>();
                }
                if (_startBlinkGraphic != null)
                {
                    // 深黄 ↔ 亮黄循环（用非缩放时间，不受 timeScale 影响）
                    float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                    Color c = Color.Lerp(startBlinkColor * 0.4f, startBlinkColor, t);
                    c.a = 1f;
                    _startBlinkGraphic.color = c;
                }
            }
            else if (_startBlinkGraphic != null)
            {
                _startBlinkGraphic.color = Color.white;
            }
        }

        private void StartMatch()
        {
            if (!AllConfirmed()) return;
            if (GameManager.Instance == null) return;
            // M4-D2：全部角色确认后先进入「地图选择」，不再直接开赛
            GameManager.Instance.SwitchState(GameState.StageSelect);
        }

        #endregion
    }
}
