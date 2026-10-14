using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// TrainingMenuPanel — 训练菜单（Esc 开关）
// 视觉/交互沿用暂停菜单：垂直按钮列 + 黄色选中高亮；行文字显示当前开关状态
//   0 继续（关闭菜单）
//   1 CPU（木桩）   开/关
//   2 无敌          开/关
//   3 慢动作        开/关
//   4 判定框        开/关
//   5 帧数据面板    开/关
//   6 HUD           开/关
//   7 重置位置
//   8 退出训练
// 导航：↑↓ / W S / 手柄左摇杆·十字键 移动
//   J / 小键盘1 / 回车 / 空格 / 手柄A 确认
//   K / 小键盘2 / 手柄B 关闭（Esc 由 TrainingModeController 统一 toggle）
// ============================================================
namespace SuperSmashLike.Training
{
    public class TrainingMenuPanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Refs")]
        public TrainingModeController controller;
        public TrainingBoxOverlay boxOverlay;
        public TrainingFrameDataPanel framePanel;

        [Header("Rows（按顺序）0继续 1CPU 2无敌 3慢动作 4判定框 5帧数据 6HUD 7重置 8退出")]
        public Button[] rowButtons = new Button[9];
        public TextMeshProUGUI[] rowLabels = new TextMeshProUGUI[9];

        [Header("Nav")]
        [Tooltip("选中高亮色（黄色，与暂停菜单一致）")]
        public Color selectionColor = new Color(1f, 0.85f, 0.2f, 1f);
        public float navRepeatDelay = 0.18f;

        #endregion

        #region 2. 运行时状态

        private int _index;
        private float _nextNav;

        #endregion

        #region 3. 生命周期

        private void OnEnable()
        {
            _index = 0;
            ApplyRowColors();
            ApplyRowClicks();
            Refresh();
            SelectRow(0);
        }

        // 【做什么】给每行按钮绑定 onClick（鼠标点击也能用，不只是键盘索引）
        private void ApplyRowClicks()
        {
            if (rowButtons == null) return;
            for (int i = 0; i < rowButtons.Length; i++)
            {
                var b = rowButtons[i];
                if (b == null) continue;
                int idx = i;
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => Activate(idx));
            }
        }

        private void Update()
        {
            var gp = Gamepad.current;

            // ---- 关闭（Esc 由控制器 toggle，这里只管 K/小键盘2/手柄B）----
            if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Keypad2)
                || (gp != null && gp.buttonEast.wasPressedThisFrame))
            {
                if (controller != null) controller.SetMenu(false);
                return;
            }

            // ---- 确认 / 切换 ----
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Keypad1)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)
                || (gp != null && gp.buttonSouth.wasPressedThisFrame))
            {
                Activate(_index);
                return;
            }

            // ---- 上下移动 ----
            int dir = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = 1;

            if (dir == 0 && gp != null && Time.unscaledTime >= _nextNav)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;
                if (nav.y > 0.5f) dir = -1;
                else if (nav.y < -0.5f) dir = 1;
            }

            if (dir != 0 && rowButtons != null && rowButtons.Length > 0)
            {
                _index = (_index + dir + rowButtons.Length) % rowButtons.Length;
                SelectRow(_index);
                _nextNav = Time.unscaledTime + navRepeatDelay;
            }
        }

        #endregion

        #region 4. 行操作

        private void Activate(int i)
        {
            switch (i)
            {
                case 0: if (controller != null) controller.SetMenu(false); break;
                case 1: if (controller != null) controller.SetCpuEnabled(!controller.CpuEnabled); break;
                case 2: if (controller != null) controller.Invincible = !controller.Invincible; break;
                case 3: if (controller != null) controller.SetSlowMo(!controller.SlowMoActive); break;
                case 4: if (boxOverlay != null) boxOverlay.showBoxes = !boxOverlay.showBoxes; break;
                case 5: ToggleFrameData(); break;
                case 6: if (controller != null) controller.SetHudVisible(!controller.HudVisible); break;
                case 7: if (controller != null) controller.ResetBothPublic(); break;
                case 8: if (controller != null) controller.ExitTraining(); break;
            }
            Refresh();
        }

        // 【做什么】切换帧数据面板 —— 只切它的文本子物体
        // 【注意】TrainingFrameDataPanel 挂在 HUD 根上，直接关 gameObject 会连整个 HUD 一起关
        //   （随后被 TrainingModeController 立刻重新打开 → 看起来"关了还在"）
        private void ToggleFrameData()
        {
            var go = FrameDataTarget();
            if (go != null) go.SetActive(!go.activeSelf);
        }

        private bool FrameDataVisible()
        {
            var go = FrameDataTarget();
            return go != null && go.activeSelf;
        }

        private GameObject FrameDataTarget()
        {
            if (framePanel == null) return null;
            return framePanel.frameText != null ? framePanel.frameText.gameObject : framePanel.gameObject;
        }

        // 【做什么】按当前各模块真实状态刷新每行文字
        public void Refresh()
        {
            if (rowLabels == null) return;
            SetLabel(0, "继续");
            SetLabel(1, $"CPU（木桩）      {(controller != null && controller.CpuEnabled ? "开" : "关")}");
            SetLabel(2, $"无敌             {(controller != null && controller.Invincible ? "开" : "关")}");
            SetLabel(3, $"慢动作           {(controller != null && controller.SlowMoActive ? "开" : "关")}");
            SetLabel(4, $"判定框           {(boxOverlay != null && boxOverlay.showBoxes ? "开" : "关")}");
            SetLabel(5, $"帧数据面板       {(FrameDataVisible() ? "开" : "关")}");
            SetLabel(6, $"HUD              {(controller != null && controller.HudVisible ? "开" : "关")}");
            SetLabel(7, "重置位置");
            SetLabel(8, "退出训练");
        }

        private void SetLabel(int i, string s)
        {
            if (rowLabels.Length > i && rowLabels[i] != null) rowLabels[i].text = s;
        }

        // 【做什么】给每个行按钮设置黄色选中高亮（与暂停菜单一致），并关闭自动导航
        private void ApplyRowColors()
        {
            if (rowButtons == null) return;
            for (int i = 0; i < rowButtons.Length; i++)
            {
                var b = rowButtons[i];
                if (b == null) continue;

                var nav = b.navigation;
                nav.mode = Navigation.Mode.None;   // 由本脚本接管导航
                b.navigation = nav;

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

        private void SelectRow(int i)
        {
            if (rowButtons == null || rowButtons.Length == 0) return;
            if (i < 0 || i >= rowButtons.Length) return;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(rowButtons[i].gameObject);
        }

        #endregion
    }
}
