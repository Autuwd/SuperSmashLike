using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using System.Reflection;
using UnityEngine;

// ============================================================
// UIFlowController — UI 流程控制器
// 职责：
//   1. 监听 GameManager 状态切换，显示对应面板（标题/选人/结算/HUD）
//   2. 充当 MatchManager → ResultsPanel 的桥梁，转发胜者
// 架构位置：UI 层
// 依赖：GameManager（状态源）、MatchManager（比赛结束事件）、ResultsPanel（结算显示）
//
// 修复说明（2026-09-01）：
//   订阅从 OnEnable 移到 Start —— Unity 保证所有物体 Awake 先于任何 Start，
//   因此 Start 里 GameManager.Instance 必然已就绪，杜绝 "Instance 为 null" 的 NRE。
// ============================================================
namespace SuperSmashLike.UI
{
    public class UIFlowController : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Panels")]
        [SerializeField] private GameObject titlePanel;            // 标题面板（可选）
        [SerializeField] private GameObject characterSelectPanel;  // 选人面板
        [SerializeField] private GameObject stageSelectPanel;      // 地图选择面板（M4-D2）
        [SerializeField] private GameObject ruleSelectPanel;       // 规则选择面板（M4-D3）
        [SerializeField] private GameObject resultsPanel;          // 结算面板
        [SerializeField] private GameObject hud;                   // 战斗 HUD
        [SerializeField] private GameObject pausePanel;            // 暂停面板

        [Header("Match")]
        [SerializeField] private MatchManager matchManager;        // 场景里的 MatchManager 实例

        #endregion

        #region 2. 运行时状态

        private ResultsPanel _resultsPanel;
        private bool _subscribed;

        #endregion

        #region 3. Unity 生命周期

        private void OnEnable()
        {
            // 首次启用：若已在 Start 之后，直接补订（面板可能晚于 UIFlowController 启用）
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

        #endregion

        #region 4. 订阅管理

        // 【做什么】订阅 GameManager 状态变化与 MatchManager 结束事件
        // 【注意】用 _subscribed 保证幂等（OnEnable 与 Start 都会调）
        private void Subscribe()
        {
            if (_subscribed) return;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStateChanged += HandleStateChanged;
            }
            else if (SmashDebug.IsOn(DebugChannel.UI))
            {
                SmashDebug.LogWarn(DebugChannel.UI,
                    "GameManager.Instance 为 null，状态订阅跳过 —— 请确认场景中有 GameManager。");
            }

            if (matchManager != null)
            {
                matchManager.OnGameOver += HandleGameOver;
            }
            else if (SmashDebug.IsOn(DebugChannel.UI))
            {
                SmashDebug.LogWarn(DebugChannel.UI, "matchManager 未拖入 Inspector，胜者转发不生效。");
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

        #endregion

        #region 5. 事件处理

        // 【做什么】比赛结束 → 把胜者转发给结算面板
        private void HandleGameOver(int winnerId)
        {
            if (!_resultsPanel)
                _resultsPanel = resultsPanel ? resultsPanel.GetComponent<ResultsPanel>() : null;

            if (_resultsPanel)
            {
                // 读取 KO 统计：MatchManager 的 _koCounts 是 private，改为 internal 或通过属性
                // 简化：直接反射读取（临时方案，保持改动最小）
                var mm = MatchManager.Instance;
                int[] koArr = new int[4];
                if (mm != null)
                {
                    var f = mm.GetType().GetField("_koCounts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f != null)
                    {
                        var arr = f.GetValue(mm) as int[];
                        if (arr != null) System.Array.Copy(arr, koArr, koArr.Length);
                    }
                }
                _resultsPanel.SetStats(winnerId, koArr, 4);
            }
        }

        // 【做什么】游戏状态变化 → 一次只显示一个主面板，HUD 单独控制
        private void HandleStateChanged(GameState state)
        {
            bool showTitle = state == GameState.Title;
            bool showSelect = state == GameState.CharacterSelect;
            bool showStage = state == GameState.StageSelect;
            bool showRule = state == GameState.RuleSelect;
            bool showResult = state == GameState.Result;
            bool showPause = state == GameState.Paused;
            bool showHUD = state == GameState.Battle || state == GameState.Paused;

            if (titlePanel)           titlePanel.SetActive(showTitle);
            if (characterSelectPanel) characterSelectPanel.SetActive(showSelect);
            if (stageSelectPanel)     stageSelectPanel.SetActive(showStage);
            if (ruleSelectPanel)      ruleSelectPanel.SetActive(showRule);
            if (resultsPanel)         resultsPanel.SetActive(showResult);
            if (pausePanel)           pausePanel.SetActive(showPause);
            if (hud)                  hud.SetActive(showHUD);
        }

        #endregion

        #region 6. 私有工具

        // 【做什么】把所有面板一次性关掉（Start 时的初始态）
        private void SetAllActive(bool active)
        {
            if (titlePanel)           titlePanel.SetActive(active);
            if (characterSelectPanel) characterSelectPanel.SetActive(active);
            if (stageSelectPanel)     stageSelectPanel.SetActive(active);
            if (ruleSelectPanel)      ruleSelectPanel.SetActive(active);
            if (resultsPanel)         resultsPanel.SetActive(active);
            if (hud)                  hud.SetActive(active);
        }

        #endregion
    }
}
