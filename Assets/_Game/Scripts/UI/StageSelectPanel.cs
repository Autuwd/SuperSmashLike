using SuperSmashLike.Core;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// StageSelectPanel — 地图选择界面（M4-D2）
// 交互：←→/A D 切换舞台 ｜ J/Space/Enter 确认 ｜ K/Esc 返回
// 依赖：GameSettings（读可选舞台 / 写选中舞台）、GameManager（切状态）
// 架构位置：UI 层
// ============================================================
namespace SuperSmashLike.UI
{
    public class StageSelectPanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Thumbnails (3)")]
        [SerializeField] private Image[] thumbnails = new Image[3];      // 3 个缩略图
        [SerializeField] private Image[] thumbFrames = new Image[3];     // 3 个高亮边框
        [SerializeField] private TextMeshProUGUI[] thumbNames = new TextMeshProUGUI[3];

        [Header("Preview")]
        [SerializeField] private Image bigPreview;                        // 大图预览
        [SerializeField] private TextMeshProUGUI nameText;                // 舞台名称
        [SerializeField] private TextMeshProUGUI infoText;                // 尺寸 / 出界边界
        [SerializeField] private TextMeshProUGUI hintText;                // 操作提示

        [Header("Colors")]
        [SerializeField] private Color frameIdle = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private Color frameSelected = new Color(1f, 0.85f, 0.2f, 1f);

        #endregion

        #region 2. 运行时状态

        private readonly List<StageData> _stages = new List<StageData>();
        private int _index;
        private float _gpNextMoveTime;
        private const float GpRepeat = 0.18f;

        #endregion

        #region 3. 生命周期

        private void OnEnable()
        {
            Build();
            Refresh();
        }

        private void Update()
        {
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentGameState != GameState.StageSelect) return;
            if (_stages.Count == 0) return;

            int prev = _index;

            bool left = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            bool right = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);

            var gp = Gamepad.current;
            if (gp != null && Time.unscaledTime >= _gpNextMoveTime)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;
                if (nav.x > 0.5f) { right = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
                else if (nav.x < -0.5f) { left = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
            }

            if (left)  _index = (_index - 1 + _stages.Count) % _stages.Count;
            if (right) _index = (_index + 1) % _stages.Count;
            if (prev != _index) Refresh();

            bool confirm = Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Space)
                        || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Keypad1)
                        || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            bool cancel = Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Escape)
                        || Input.GetKeyDown(KeyCode.Keypad2)
                        || (gp != null && gp.buttonEast.wasPressedThisFrame);

            if (confirm) Confirm();
            else if (cancel) Back();
        }

        #endregion

        #region 4. 数据与刷新

        private void Build()
        {
            _stages.Clear();
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            if (gs != null)
            {
                var list = gs.GetSelectableStages();
                if (list != null)
                    foreach (var s in list) if (s != null) _stages.Add(s);
            }

            // 默认对齐到当前已选舞台
            int start = 0;
            var sel = gs != null ? gs.GetSelectedStage() : null;
            if (sel != null) { int i = _stages.IndexOf(sel); if (i >= 0) start = i; }
            _index = Mathf.Clamp(start, 0, Mathf.Max(0, _stages.Count - 1));
        }

        private void Refresh()
        {
            for (int i = 0; i < thumbnails.Length; i++)
            {
                bool has = i < _stages.Count;
                if (thumbnails.Length > i && thumbnails[i] != null)
                {
                    thumbnails[i].enabled = has;
                    thumbnails[i].sprite = has ? _stages[i].stageThumbnail : null;
                    thumbnails[i].color = has ? Color.white : new Color(1f, 1f, 1f, 0.08f);
                }
                if (thumbNames.Length > i && thumbNames[i] != null)
                    thumbNames[i].text = has ? _stages[i].stageName : "";
                if (thumbFrames.Length > i && thumbFrames[i] != null)
                    thumbFrames[i].color = (has && i == _index) ? frameSelected : frameIdle;
            }

            if (_stages.Count == 0)
            {
                if (nameText != null) nameText.text = "无舞台";
                if (infoText != null) infoText.text = "";
                if (bigPreview != null) bigPreview.enabled = false;
                return;
            }

            var sd = _stages[_index];
            if (nameText != null) nameText.text = sd.stageName;
            if (bigPreview != null)
            {
                bigPreview.enabled = sd.stageThumbnail != null;
                bigPreview.sprite = sd.stageThumbnail;
            }
            if (infoText != null)
                infoText.text = $"尺寸：{SizeLabel(sd)}    出界边界：{sd.blastZoneWidth:F0} × {sd.blastZoneHeight:F0}";
            if (hintText != null)
                hintText.text = $"{_index + 1} / {_stages.Count}";   // 按键提示改由全局 KeyHintBar 显示
        }

        private string SizeLabel(StageData sd)
        {
            float w = sd.blastZoneWidth;
            if (w <= 26f) return "小型";
            if (w <= 32f) return "中型";
            return "大型";
        }

        #endregion

        #region 5. 操作

        private void Confirm()
        {
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            if (gs == null || _stages.Count == 0) return;

            gs.SetSelectedStage(_stages[_index]);   // 锁定
            // 训练流程：跳过规则选择，直接进训练
            if (GameManager.Instance.trainingRequested)
                GameManager.Instance.StartTraining();
            else
                GameManager.Instance.SwitchState(GameState.RuleSelect);
        }

        private void Back()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.SwitchState(GameState.CharacterSelect);
        }

        #endregion
    }
}
