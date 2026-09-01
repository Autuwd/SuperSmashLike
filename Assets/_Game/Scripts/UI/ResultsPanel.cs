using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SuperSmashLike.Core;

namespace SuperSmashLike.UI
{
    /// <summary>
    /// 结算面板：展示胜者/排名，提供返回标题与重赛按钮。
    /// </summary>
    public class ResultsPanel : MonoBehaviour
    {
        [Header("Results UI")]
        [SerializeField] private TextMeshProUGUI resultTitle;     // "P1 获胜！" / "平局"
        [SerializeField] private TextMeshProUGUI statsText;       // 统计数据占位

        private int _lastWinnerId = -1;

        private void OnEnable()
        {
            // 读 MatchManager 缓存的胜者（最佳实践：由 MatchManager 或 GameManager 存"战绩"）
            // 示例用 GameManager 静态字段，也可从 MatchManager 取。
            GameManager gm = GameManager.Instance;
            resultTitle.text = (_lastWinnerId >= 0)
                ? $"P{_lastWinnerId + 1} 获胜！"
                : "平局";
        }

        // 由 UIFlowController 或外部在 OnGameOver 时调用
        public void SetWinner(int winnerId) => _lastWinnerId = winnerId;

        public void OnReturnToTitle()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("TitleScene"); // 按实际场景名
            GameManager.Instance.SwitchState(GameState.Title);
        }

        public void OnRematch()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("BattleScene"); // 按实际场景名
            GameManager.Instance.SwitchState(GameState.CharacterSelect);
        }
    }
}