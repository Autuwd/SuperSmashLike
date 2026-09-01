using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using UnityEngine;

namespace SuperSmashLike.UI
{
    /// <summary>
    /// UI 流程控制器：监听 GameManager 状态切换对应面板；
    /// 并充当 MatchManager → ResultsPanel 的桥梁，转发胜者。
    ///
    /// 修复说明（2026-09-01）：
    /// 订阅从 OnEnable 移到 Start —— Unity 保证所有物体 Awake 先于任何 Start，
    /// 因此 Start 里 GameManager.Instance 必然已就绪，杜绝 "Instance 为 null" 的 NRE。
    /// </summary>
    public class UIFlowController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject titlePanel;           // 标题面板（可选）
        [SerializeField] private GameObject characterSelectPanel; // 选人面板
        [SerializeField] private GameObject resultsPanel;         // 结算面板
        [SerializeField] private GameObject hud;                  // 战斗 HUD

        [Header("Match")]
        [SerializeField] private MatchManager matchManager;       // 场景里的 MatchManager 实例

        private ResultsPanel _resultsPanel;
        private bool _subscribed = false;

        private void OnEnable()
        {
            // 首次启用：若已在 Start 后，直接补订（面板可能晚于 UIFlowController 启用）
            if (_subscribed) Subscribe();
        }

        private void Start()
        {
            Subscribe();
            SetAllActive(false);
            if (GameManager.Instance != null)
                HandleStateChanged(GameManager.Instance.CurrentGameState);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStateChanged += HandleStateChanged;
            }
            else
            {
                Debug.LogWarning("[UIFlow] GameManager.Instance 为 null，状态订阅跳过——请确认场景中有 GameManager。");
            }

            if (matchManager != null)
            {
                matchManager.OnGameOver += HandleGameOver;
            }
            else
            {
                Debug.LogWarning("[UIFlow] matchManager 未拖入 Inspector，胜者转发不生效。");
            }

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged -= HandleStateChanged;

            if (matchManager != null)
                matchManager.OnGameOver -= HandleGameOver;

            _subscribed = false;
        }

        // 转发胜者到 ResultsPanel
        private void HandleGameOver(int winnerId)
        {
            if (!_resultsPanel)
                _resultsPanel = resultsPanel ? resultsPanel.GetComponent<ResultsPanel>() : null;
            if (_resultsPanel) _resultsPanel.SetWinner(winnerId);
        }

        private void HandleStateChanged(GameState state)
        {
            // 一次只显示一个主面板，HUD 单独控制
            bool showSelect = state == GameState.CharacterSelect;
            bool showResult = state == GameState.Result;
            bool showTitle = state == GameState.Title;
            bool showHUD = state == GameState.Battle || state == GameState.Paused;

            if (titlePanel)             titlePanel.SetActive(showTitle);
            if (characterSelectPanel)   characterSelectPanel.SetActive(showSelect);
            if (resultsPanel)           resultsPanel.SetActive(showResult);
            if (hud)                    hud.SetActive(showHUD);
        }

        private void SetAllActive(bool active)
        {
            if (titlePanel)             titlePanel.SetActive(active);
            if (characterSelectPanel)   characterSelectPanel.SetActive(active);
            if (resultsPanel)           resultsPanel.SetActive(active);
            if (hud)                    hud.SetActive(active);
        }
    }
}
