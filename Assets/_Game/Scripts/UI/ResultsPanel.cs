using System.Collections.Generic;
using TMPro;
using SuperSmashLike.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// ResultsPanel — 结算面板
// 职责：
//   1. 展示胜者（由 UIFlowController 转发 MatchManager.OnGameOver 的结果）
//   2. 提供"返回标题"与"重赛"按钮
//   3. **键盘/手柄导航**：方向键(或 WASD/摇杆/十字键)选按钮，J/Space/Enter/手柄A 确认
// 架构位置：UI 层
// 依赖：GameManager（切状态）、EventSystem（选中态）
// ============================================================
namespace SuperSmashLike.UI
{
    public class ResultsPanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Results UI")]
        [SerializeField] private TextMeshProUGUI resultTitle;   // "P1 获胜！" / "平局"
        [SerializeField] private TextMeshProUGUI statsText;     // 统计数据占位（尚未填充）

        [Header("Navigation")]
        [Tooltip("留空则自动收集本面板下所有可交互 Button")]
        [SerializeField] private Button[] navButtons;

        #endregion

        #region 2. 运行时状态

        private int _lastWinnerId = -1;   // -1 = 平局/无胜者
        private readonly List<Button> _buttons = new List<Button>();
        private int _index;
        private float _gpNextMoveTime;
        private const float GpRepeat = 0.18f;

        #endregion

        #region 3. Unity 生命周期

        private void OnEnable()
        {
            if (resultTitle != null)
                resultTitle.text = _lastWinnerId >= 0 ? $"P{_lastWinnerId + 1} 获胜！" : "平局";

            CollectButtons();
            _index = 0;
            Highlight();
        }

        private void Update()
        {
            // 仅在结算状态响应（面板激活时也可能处于其它状态）
            if (GameManager.Instance == null || GameManager.Instance.CurrentGameState != GameState.Result) return;
            if (_buttons.Count == 0) return;

            int prev = _index;
            bool prevKey = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)
                        || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            bool nextKey = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)
                        || Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);

            var gp = Gamepad.current;
            if (gp != null && Time.unscaledTime >= _gpNextMoveTime)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 pad = gp.dpad.ReadValue();
                if (pad.sqrMagnitude > nav.sqrMagnitude) nav = pad;
                if (nav.x < -0.5f || nav.y < -0.5f) { prevKey = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
                else if (nav.x > 0.5f || nav.y > 0.5f) { nextKey = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
            }

            if (prevKey) _index = (_index - 1 + _buttons.Count) % _buttons.Count;
            if (nextKey) _index = (_index + 1) % _buttons.Count;
            if (prev != _index) Highlight();

            bool confirm = Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Space)
                        || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Keypad1)
                        || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (confirm) ActivateSelected();
        }

        #endregion

        #region 4. 公开 API

        // 【做什么】设置胜者（由 UIFlowController 在 OnGameOver 时调用）
        public void SetWinner(int winnerId) => _lastWinnerId = winnerId;

        // 【做什么】设置胜者与统计（KO/MVP）
        public void SetStats(int winnerId, int[] koCountsPerPlayer, int maxPlayers)
        {
            _lastWinnerId = winnerId;
            if (statsText == null) return;

            if (koCountsPerPlayer == null || maxPlayers <= 0)
            {
                statsText.text = string.Empty;
                return;
            }

            // 找 MVP：KO 数最高；若平局，沿用胜者判定思路（命数优先→击飞值小者优）不直接算，此处仅按 KO 排
            int mvp = -1;
            int bestKOs = -1;
            for (int i = 0; i < maxPlayers; i++)
            {
                int k = koCountsPerPlayer[i];
                if (mvp == -1 || k > bestKOs)
                {
                    bestKOs = k;
                    mvp = i;
                }
                else if (k == bestKOs && mvp >= 0)
                {
                    // KO 相同不强制覆盖，保留首个或可按 winnerId 倾斜
                }
            }
            if (mvp < 0) mvp = winnerId >= 0 ? winnerId : 0;

            // 若 KO 全为 0，优先用胜者
            if (bestKOs <= 0 && winnerId >= 0) mvp = winnerId;

            string mvpName = $"P{mvp + 1}";
            int mvpKOs = mvp >= 0 && mvp < koCountsPerPlayer.Length ? koCountsPerPlayer[mvp] : 0;
            statsText.text = $"MVP: {mvpName}  击倒数: {mvpKOs}";
        }

        // 【做什么】返回标题（按钮绑定）
        public void OnReturnToTitle()
        {
            GameManager.Instance.SwitchState(GameState.Title);
        }

        // 【做什么】重赛（按钮绑定）
        public void OnRematch()
        {
            GameManager.Instance.SwitchState(GameState.CharacterSelect);
        }

        #endregion

        #region 5. 导航

        // 【做什么】收集可导航按钮：优先 Inspector 指定，否则自动收集子物体里的可交互 Button
        private void CollectButtons()
        {
            _buttons.Clear();

            if (navButtons != null && navButtons.Length > 0)
            {
                foreach (var b in navButtons) if (b != null) _buttons.Add(b);
                if (_buttons.Count > 0) return;
            }

            foreach (var b in GetComponentsInChildren<Button>(true))
                if (b != null && b.interactable) _buttons.Add(b);
        }

        // 【做什么】把当前索引同步到 EventSystem 选中态（触屏/鼠标也一致）
        private void Highlight()
        {
            if (_index < 0 || _index >= _buttons.Count) return;
            var b = _buttons[_index];
            if (b == null) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
            b.Select();
            // 高亮色改为黄色（与其他按钮选中一致）
            var colors = b.colors;
            colors.selectedColor = Color.yellow;
            b.colors = colors;
        }

        private void ActivateSelected()
        {
            if (_index < 0 || _index >= _buttons.Count) return;
            _buttons[_index]?.onClick.Invoke();
        }

        #endregion
    }
}
