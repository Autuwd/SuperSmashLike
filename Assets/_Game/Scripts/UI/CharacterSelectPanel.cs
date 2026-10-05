using SuperSmashLike.Core;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
            HandlePlayerInput(0); // P1 WASD + J/K
            HandlePlayerInput(1); // P2 ↑←↓→ + J/K
            HandleGamepadInput(); // Joystick1(P3)/Joystick2(P4) + Submit/Cancel

            UpdateStartButton();

            // F1/Help：显示当前高亮角色的出招表（选人界面可用）
            if (Input.GetKeyDown(KeyCode.F1))
            {
                int pidToShow = -1;
                for (int p = 0; p < 4; p++)
                {
                    if (_active[p] && !_confirmed[p])
                    {
                        pidToShow = p;
                        break;
                    }
                }
                if (pidToShow < 0)
                {
                    for (int p = 0; p < 4; p++)
                    {
                        if (_active[p])
                        {
                            pidToShow = p;
                            break;
                        }
                    }
                }

                if (pidToShow >= 0 && _fighterList.Count > 0 && moveListUI != null)
                {
                    int hIdx = _highlightIndex[pidToShow] % _fighterList.Count;
                    moveListUI.ShowFor(_fighterList[hIdx], pidToShow);
                }
            }
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
        }

        #endregion

        #region Input

        // P1 WASD+J/K，P2 ↑←↓→+J/K
        private void HandlePlayerInput(int pid)
        {
            if (pid < 0 || pid > 1) return;
            if (!_active[pid] || _confirmed[pid]) return;

            int count = _cards.Count;
            if (count == 0) return;

            bool moved = false;

            if (pid == 0)
            {
                // P1 WASD
                if (Input.GetKeyDown(KeyCode.A))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] - 1 + count) % count;
                    moved = true;
                }
                else if (Input.GetKeyDown(KeyCode.D))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] + 1) % count;
                    moved = true;
                }
                else if (Input.GetKeyDown(KeyCode.W))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] - gridCols + count) % count;
                    moved = true;
                }
                else if (Input.GetKeyDown(KeyCode.S))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] + gridCols) % count;
                    moved = true;
                }

                if (Input.GetKeyDown(KeyCode.J))
                {
                    ConfirmPlayer(pid);
                }
                else if (Input.GetKeyDown(KeyCode.K))
                {
                    CancelPlayer(pid);
                }
            }
            else if (pid == 1)
            {
                // P2 方向键
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] - 1 + count) % count;
                    moved = true;
                }
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] + 1) % count;
                    moved = true;
                }
                else if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] - gridCols + count) % count;
                    moved = true;
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    _highlightIndex[pid] = (_highlightIndex[pid] + gridCols) % count;
                    moved = true;
                }

                if (Input.GetKeyDown(KeyCode.J))
                {
                    ConfirmPlayer(pid);
                }
                else if (Input.GetKeyDown(KeyCode.K))
                {
                    CancelPlayer(pid);
                }
            }

            if (moved)
            {
                RefreshAllVisuals();
                UpdatePlayerPreviews();
            }
        }

        // 手柄输入：Joystick1 → P3（A=Button0 确认，B=Button1 取消），Joystick2 → P4
        private void HandleGamepadInput()
        {
            // Joystick 1（P3）
            if (Input.GetKeyDown(KeyCode.Joystick1Button0))
            {
                TryGamepadConfirm(2);
            }
            if (Input.GetKeyDown(KeyCode.Joystick1Button1))
            {
                TryGamepadCancel(2);
            }

            // Joystick 2（P4）
            if (Input.GetKeyDown(KeyCode.Joystick2Button0))
            {
                TryGamepadConfirm(3);
            }
            if (Input.GetKeyDown(KeyCode.Joystick2Button1))
            {
                TryGamepadCancel(3);
            }

            // Submit（优先：未确认活跃 → 确认；全部确认 → 开始比赛）
            if (Input.GetButtonDown("Submit"))
            {
                for (int p = 0; p < 4; p++)
                {
                    if (_active[p] && !_confirmed[p])
                    {
                        ConfirmPlayer(p);
                        return;
                    }
                }

                if (AllConfirmed())
                {
                    StartMatch();
                    return;
                }
            }

            // Cancel（优先取消最近确认的玩家：P4→P3→P2→P1）
            if (Input.GetButtonDown("Cancel"))
            {
                for (int p = 3; p >= 0; p--)
                {
                    if (_active[p] && _confirmed[p])
                    {
                        CancelPlayer(p);
                        return;
                    }
                }
            }
        }

        private void TryGamepadConfirm(int preferredPid)
        {
            if (preferredPid >= 0 && preferredPid < 4 && !_active[preferredPid])
                _active[preferredPid] = true;

            for (int p = 0; p < 4; p++)
            {
                if (_active[p] && !_confirmed[p])
                {
                    ConfirmPlayer(p);
                    return;
                }
            }

            if (AllConfirmed())
            {
                StartMatch();
            }
        }

        private void TryGamepadCancel(int preferredPid)
        {
            for (int p = 3; p >= 0; p--)
            {
                if (_active[p] && _confirmed[p])
                {
                    CancelPlayer(p);
                    return;
                }
            }
        }

        #endregion

        #region Selection

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

                // 先检查是否有玩家已确认该卡牌
                bool isConfirmedByAny = false;
                int confirmedByPid = -1;
                for (int p = 0; p < 4; p++)
                {
                    if (_active[p] && _confirmed[p])
                    {
                        int cIdx = _highlightIndex[p] % _cards.Count;
                        if (cIdx == i)
                        {
                            isConfirmedByAny = true;
                            confirmedByPid = p;
                            break;
                        }
                    }
                }

                if (isConfirmedByAny)
                {
                    cv.SetConfirmed(confirmedByPid, true);
                    continue;
                }

                // 未确认：优先显示第一个未确认活跃玩家的高亮（P1 → P2 → P3 → P4）
                int highlightedByPid = -1;
                for (int p = 0; p < 4; p++)
                {
                    if (_active[p] && !_confirmed[p])
                    {
                        int hIdx = _highlightIndex[p] % _cards.Count;
                        if (hIdx == i)
                        {
                            highlightedByPid = p;
                            break; // P1 优先
                        }
                    }
                }

                if (highlightedByPid >= 0)
                    cv.SetHighlighted(highlightedByPid);
                else
                    cv.ClearState();
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
                float speed = 0f;
                // 尝试读取常见字段（按项目现有字段名适配）
                // moveSpeed 如果 FighterData 没有，可忽略
                System.Reflection.FieldInfo ms = fd.GetType().GetField("moveSpeed");
                if (ms != null) speed = (float)ms.GetValue(fd);

                featureText.text = $"特色：{weight} | 跳跃 {jump:F1} | 移速 {speed:F1}";
            }
        }

        private void UpdateStartButton()
        {
            if (startButton == null) return;

            bool allOk = AllConfirmed();
            startButton.gameObject.SetActive(allOk);
            startButton.interactable = allOk;
        }

        private void StartMatch()
        {
            if (!AllConfirmed()) return;
            if (GameManager.Instance == null) return;
            GameManager.Instance.StartMatch();
        }

        #endregion
    }
}
