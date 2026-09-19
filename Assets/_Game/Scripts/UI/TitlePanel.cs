using SuperSmashLike.Core;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// TitlePanel — 标题面板
// 职责：标题界面的"开始"按钮 → 切到选人阶段
// 架构位置：UI 层
// 依赖：GameManager（切状态）
// ============================================================
namespace SuperSmashLike.UI
{
    public class TitlePanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [SerializeField] private Button startButton;   // Inspector 拖入 StartBtn

        #endregion

        #region 2. Unity 生命周期

        private void Start()
        {
            if (startButton == null)
            {
                if (SmashDebug.IsOn(DebugChannel.UI))
                    SmashDebug.LogWarn(DebugChannel.UI, "startButton 未拖入 Inspector，标题界面无法开始游戏");
                return;
            }

            // 点击 → 进入选人阶段
            startButton.onClick.AddListener(() =>
                GameManager.Instance?.SwitchState(GameState.CharacterSelect));
        }

        #endregion
    }
}
