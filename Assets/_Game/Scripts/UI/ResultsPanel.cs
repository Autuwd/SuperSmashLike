using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SuperSmashLike.Core;

// ============================================================
// ResultsPanel — 结算面板
// 职责：
//   1. 展示胜者（由 UIFlowController 转发 MatchManager.OnGameOver 的结果）
//   2. 提供"返回标题"与"重赛"按钮
// 架构位置：UI 层
// 依赖：GameManager（切状态）、SceneManager（切场景）
//
// 【注意】OnReturnToTitle / OnRematch 里的场景名是硬编码占位
//   （"TitleScene" / "BattleScene"），项目实际只有 BattleTest 一个场景，
//   多场景流程尚未搭建，调用会失败。
// ============================================================
namespace SuperSmashLike.UI
{
    public class ResultsPanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Results UI")]
        [SerializeField] private TextMeshProUGUI resultTitle;   // "P1 获胜！" / "平局"
        [SerializeField] private TextMeshProUGUI statsText;     // 统计数据占位（尚未填充）

        #endregion

        #region 2. 运行时状态

        private int _lastWinnerId = -1;   // -1 = 平局/无胜者

        #endregion

        #region 3. Unity 生命周期

        // 【做什么】面板显示时按胜者 ID 刷新标题
        private void OnEnable()
        {
            if (resultTitle == null) return;

            resultTitle.text = _lastWinnerId >= 0
                ? $"P{_lastWinnerId + 1} 获胜！"
                : "平局";
        }

        #endregion

        #region 4. 公开 API

        // 【做什么】设置胜者（由 UIFlowController 在 OnGameOver 时调用）
        public void SetWinner(int winnerId) => _lastWinnerId = winnerId;

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
    }
}
