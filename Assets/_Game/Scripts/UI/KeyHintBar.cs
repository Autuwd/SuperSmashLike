using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// KeyHintBar — 全局底部按键提示条
// 职责：所有界面底部常显"当前可用按键"提示；键鼠/手柄自动切换两套文案；
//       角色选择 / 地图选择等特殊界面额外显示"随机"等专属提示。
// 架构位置：UI 层（挂 Canvas 下，常驻）
// 依赖：GameManager（读当前状态决定提示内容）
// ============================================================
namespace SuperSmashLike.UI
{
    public class KeyHintBar : MonoBehaviour
    {
        [Header("UI")]
        public TextMeshProUGUI hintText;
        [Tooltip("灰色条背景（游戏进行中会与提示一起隐藏）")]
        public Image background;

        [Header("Colors")]
        public Color normalColor = new Color(1f, 1f, 1f, 0.85f);

        private bool _usingGamepad;

        // 【做什么】外部强制显示（如训练菜单打开时，需要底部按键提示）
        public static bool ForceShow;

        private void Update()
        {
            HandleEsc();
            DetectDevice();

            var s = GameManager.Instance != null ? GameManager.Instance.CurrentGameState : GameState.Boot;
            bool show = ForceShow
                     || (s != GameState.Battle && s != GameState.BattleLoading && s != GameState.Result && s != GameState.Training);

            // 游戏进行中：连面板一起隐藏，不挡视野
            if (background != null) background.enabled = show;
            if (hintText != null) hintText.enabled = show;
            if (!show || hintText == null) return;

            hintText.color = normalColor;
            hintText.text = BuildHint();
        }

        // 【做什么】Esc 兜底：普通对战打开/关闭暂停菜单
        //   挂在常驻 Canvas 上（不依赖 GameManager 的 Update）
        // 【问题①修复 2026-10-17】切换与去重统一走 GameManager.TogglePause（唯一静态冷却）；
        //   本类不再自持 0.2s 冷却 —— 否则与输入系统 Pause 动作的通道互不知情、同帧互抵净零。
        private bool _escWasDown;

        private void HandleEsc()
        {
            // 主判据：旧版 Input 读 Esc（本项目 activeInputHandler=Both，旧的可用）；
            // 备用：新 Input System 的 escapeKey 边缘检测。二者任一触发即可。
            bool legacyEsc = false;
            try { legacyEsc = Input.GetKeyDown(KeyCode.Escape); } catch { /* 纯 InputSystem 工程时忽略 */ }

            bool newEsc = false, pauseKey = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool down = kb.escapeKey.isPressed;
                newEsc = down && !_escWasDown;
                _escWasDown = down;
                pauseKey = kb.pKey.wasPressedThisFrame;   // 备用暂停键 P（Esc 在本机被吞时的替代）
            }

            if (!legacyEsc && !newEsc && !pauseKey) return;

            GameManager.Instance?.TogglePause();
        }

        private static bool _loggedNoKb;

        // 【做什么】按最近的输入设备切换 键鼠 / 手柄 两套提示
        private void DetectDevice()
        {
            var gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame ||
                    gp.buttonWest.wasPressedThisFrame || gp.buttonNorth.wasPressedThisFrame ||
                    gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame ||
                    gp.startButton.wasPressedThisFrame || gp.dpad.ReadValue().sqrMagnitude > 0.25f ||
                    gp.leftStick.ReadValue().sqrMagnitude > 0.25f)
                    _usingGamepad = true;
            }

            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame)
                _usingGamepad = false;
        }

        private string BuildHint()
        {
            var s = GameManager.Instance != null ? GameManager.Instance.CurrentGameState : GameState.Boot;

            switch (s)
            {
                case GameState.Title:
                    return _usingGamepad
                        ? "十字键/摇杆 选择       A 确认"
                        : "↑↓ 选择       J / 小键盘1 / 回车 确认";

                case GameState.CharacterSelect:
                    return _usingGamepad
                        ? "摇杆/十字键 选择       A 确认       B 取消       X 出招表       LT 随机"
                        : "WASD / 方向键 选择       J / 小键盘1 确认       K / 小键盘2 取消       L / 小键盘3 随机";

                case GameState.StageSelect:
                    return _usingGamepad
                        ? "摇杆/十字键 切换       A 确认       B 返回"
                        : "←→ / A D 切换       J / 小键盘1 确认       K / 小键盘2 返回";

                case GameState.RuleSelect:
                    return _usingGamepad
                        ? "摇杆/十字键 选择       A 确认       B 返回       Y 高级"
                        : "↑↓←→ 选择       J / 小键盘1 确认       K / 小键盘2 返回       F 高级";

                case GameState.Paused:
                    return _usingGamepad
                        ? "十字键/摇杆 选择       A 确认       B 返回"
                        : "↑↓ 选择       J / 小键盘1 确认       K / 小键盘2 返回";

                case GameState.Training:
                    return _usingGamepad
                        ? "LB/RB 切换页      十字键/摇杆 选择      A 确认      B 关闭"
                        : "Q/E 切换页      ↑↓ 选择      J/小键盘1 确认      K/小键盘2 关闭";

                case GameState.Battle:
                case GameState.BattleLoading:
                case GameState.Result:
                    return "";   // 游戏开始后不再显示底部按键提示

                default:
                    return "";
            }
        }
    }
}
