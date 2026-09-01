using SuperSmashLike.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SuperSmashLike.UI
{
    public class TitlePanel : MonoBehaviour
    {
        [SerializeField] private Button startButton;   // Inspector 拖入 StartBtn

        private void Start()
        {
            if (startButton == null)
            {
                Debug.LogWarning("[TitlePanel] startButton 未拖入 Inspector");
                return;
            }

            // 点击 → 进入选人阶段
            startButton.onClick.AddListener(() =>
                GameManager.Instance?.SwitchState(GameState.CharacterSelect));
        }
    }
}