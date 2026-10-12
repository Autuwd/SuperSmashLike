using SuperSmashLike.Core;
using UnityEngine;
using UnityEngine.InputSystem;
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

        [Header("Start Button Blink")]
        [Tooltip("标题界面\"开始游戏\"按钮的呼吸闪烁颜色")]
        [SerializeField] private Color startBlinkColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private float blinkSpeed = 6f;

        #endregion

        #region 2. 运行时

        private Graphic _startBlinkGraphic;
        private Color _startOrigColor;

        #endregion

        #region 3. Unity 生命周期

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

            // 记录原色
            _startBlinkGraphic = startButton.targetGraphic != null
                ? startButton.targetGraphic
                : startButton.GetComponent<Graphic>();
            if (_startBlinkGraphic != null)
            {
                _startOrigColor = _startBlinkGraphic.color;
                // 用纯色过渡（避开 Button 自身状态色打架）
                startButton.transition = Selectable.Transition.None;
            }
        }

        private void OnEnable()
        {
            if (startButton != null)
                startButton.Select();

            if (_startBlinkGraphic != null)
                _startBlinkGraphic.color = _startOrigColor;
        }

        private void Update()
        {
            // 标题界面：按任意确认键/手柄开始，支持方向键选中按钮
            if (GameManager.Instance != null &&
                GameManager.Instance.CurrentGameState != GameState.Title)
                return;

            // 黄色呼吸闪烁（一直闪烁，提示可操作）
            if (_startBlinkGraphic != null)
            {
                float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * blinkSpeed);
                Color c = Color.Lerp(startBlinkColor * 0.35f, startBlinkColor, t);
                c.a = 1f;
                _startBlinkGraphic.color = c;
            }

            var kbSubmit = Input.GetKeyDown(KeyCode.Return) ||
                           Input.GetKeyDown(KeyCode.Space) ||
                           Input.GetKeyDown(KeyCode.J);

            var gp = Gamepad.current;
            bool gpSubmit = gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.buttonWest.wasPressedThisFrame);

            if (kbSubmit || gpSubmit)
            {
                if (startButton != null && startButton.interactable)
                {
                    // 触发按钮点击（兼容 EventSystem 选中状态）
                    startButton.onClick.Invoke();
                }
            }
        }

        #endregion
    }
}
