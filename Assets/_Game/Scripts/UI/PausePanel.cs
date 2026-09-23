using UnityEngine;
using UnityEngine.UI;
using SuperSmashLike.Core;

namespace SuperSmashLike.UI
{
    public class PausePanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button moveListButton;
        [SerializeField] private Button moveListBackButton;

        [Header("Move List")]
        [SerializeField] private GameObject moveListPanel;

        #endregion

        #region 2. Unity 生命周期

        private void Start()
        {
            // 【注意】一律空引用守护 + 提示日志，对齐 TitlePanel 的写法
            if (resumeButton != null)
                resumeButton.onClick.AddListener(OnResume);
            if (restartButton != null)
                restartButton.onClick.AddListener(OnRestart);
            if (quitButton != null)
                quitButton.onClick.AddListener(OnQuit);
            if (moveListButton != null) 
                moveListButton.onClick.AddListener(OnMoveList);
            if (moveListBackButton != null) 
                moveListBackButton.onClick.AddListener(OnMoveListBack);
        }

        // 每次暂停面板弹出 → 出招表复位为关闭态（防"上次开着、这次还开着"）
        private void OnEnable()
        {
            if (moveListPanel != null) moveListPanel.SetActive(false);
        }

        #endregion

        #region 3. 按钮动作

        // 【做什么】继续比赛 —— 直接调已有的 ResumeGame（同暂停键逻辑）
        private void OnResume()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.ResumeGame();
        }

        // 【做什么】重新开始 —— 参考 ResultsPanel.OnRematch 的写法，
        //   场景名是否有效需按你项目实际场景确认（项目当前只有一个 BattleTest 场景）
        private void OnRestart()
        {
            GameManager.Instance.ResumeGame();   // 先恢复 timeScale，避免新场景冻结
            GameManager.Instance.SwitchState(GameState.CharacterSelect);
        }

        // 【做什么】打开出招表 —— 注意：绝不调 ResumeGame，游戏要保持暂停
        private void OnMoveList()
        {
            if (moveListPanel != null) moveListPanel.SetActive(true);
        }

        // 【做什么】关闭出招表 → 回到暂停菜单
        private void OnMoveListBack()
        {
            if (moveListPanel != null) moveListPanel.SetActive(false);
        }

        // 【做什么】返回标题 —— 同上思路
        private void OnQuit()
        {
            GameManager.Instance.ResumeGame();   // 先恢复 timeScale，避免标题界面冻结
            GameManager.Instance.SwitchState(GameState.Title);
        }

        #endregion
    }
}