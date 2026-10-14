using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        [SerializeField] private Button trainingButton; // Inspector 拖入 TrainingBtn

        [Header("Navigation")]
        [Tooltip("选中按钮的黄色高亮（与其它菜单一致）")]
        [SerializeField] private Color selectionColor = new Color(1f, 0.85f, 0.2f, 1f);

        #endregion

        #region 2. 运行时

        private Button[] _navButtons;
        private int _navIndex;
        private float _navNextTime;

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
            {
                GameManager.Instance?.CancelTrainingRequest();
                GameManager.Instance?.SwitchState(GameState.CharacterSelect);
            });

            if (trainingButton != null)
            {
                // 训练：走「选人 → 选图 → 训练」流程
                trainingButton.onClick.AddListener(() =>
                    GameManager.Instance?.RequestTraining());
            }
        }

        private void OnEnable()
        {
            _navButtons = null;
            BuildNav();
            _navIndex = 0;
            if (_navButtons.Length > 0)
                SelectNav(0);
        }

        private void Update()
        {
            // 标题界面：按任意确认键/手柄开始，支持方向键选中按钮
            if (GameManager.Instance != null &&
                GameManager.Instance.CurrentGameState != GameState.Title)
                return;

            var gp = Gamepad.current;

            // T：进入训练流程（选人 → 选图 → 训练）
            if (Input.GetKeyDown(KeyCode.T))
            {
                GameManager.Instance?.RequestTraining();
                return;
            }

            // 导航按钮列表（开始 / 训练）
            if (_navButtons == null) BuildNav();
            if (_navButtons.Length == 0) return;

            // 上下切换：方向键/WASD/手柄十字键·摇杆（统一导航）
            int dir = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) ||
                Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dir = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) ||
                     Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dir = 1;

            if (dir == 0 && gp != null && Time.unscaledTime >= _navNextTime)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;
                if (nav.y > 0.5f) dir = -1;
                else if (nav.y < -0.5f) dir = 1;
            }

            if (dir != 0)
            {
                _navIndex = (_navIndex + dir + _navButtons.Length) % _navButtons.Length;
                SelectNav(_navIndex);
                _navNextTime = Time.unscaledTime + 0.18f;
            }

            // 确认：回车/空格/J/小键盘1/手柄A
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) ||
                Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Keypad1) ||
                (gp != null && gp.buttonSouth.wasPressedThisFrame))
            {
                var b = _navButtons[_navIndex];
                if (b != null && b.interactable) b.onClick.Invoke();
            }
        }

        private void BuildNav()
        {
            var list = new System.Collections.Generic.List<Button>(2);
            if (startButton != null) list.Add(startButton);
            if (trainingButton != null) list.Add(trainingButton);
            _navButtons = list.ToArray();

            for (int i = 0; i < _navButtons.Length; i++)
            {
                var b = _navButtons[i];
                if (b == null) continue;

                // 与其它菜单一致：关闭 UGUI 自动导航 + 黄色选中高亮
                var nav = b.navigation;
                nav.mode = Navigation.Mode.None;
                b.navigation = nav;

                b.transition = Selectable.Transition.ColorTint;
                var cb = b.colors;
                cb.normalColor = Color.white;
                cb.selectedColor = selectionColor;
                cb.highlightedColor = selectionColor;
                cb.pressedColor = selectionColor * 0.8f;
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0.05f;
                b.colors = cb;
            }
        }

        // 选中：交给 EventSystem + Button 的黄色 selectedColor（与其它菜单一致）
        private void SelectNav(int i)
        {
            if (_navButtons == null || i < 0 || i >= _navButtons.Length) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_navButtons[i].gameObject);
            else if (_navButtons[i] != null) _navButtons[i].Select();
        }

        #endregion
    }
}
