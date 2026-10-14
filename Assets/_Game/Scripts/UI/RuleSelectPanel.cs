using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// RuleSelectPanel — 规则选择界面（M4-D3）
// 行：比赛模式 / 命数 / 时间 / CPU 难度（基础）+ 伤害比例 / 护盾耐久 / 道具（高级）
// 交互：W S 选行 ｜ A D 调值 ｜ F 展开高级 ｜ J/Space/Enter 开始 ｜ K/Esc 返回
// 开始比赛时把选择写入 GameSettings（SO），再切到 Battle
// 架构位置：UI 层
// ============================================================
namespace SuperSmashLike.UI
{
    public class RuleSelectPanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Basic Rows (模式/命数/时间/难度)")]
        [SerializeField] private TextMeshProUGUI[] rowTexts = new TextMeshProUGUI[4];
        [SerializeField] private Image[] rowFrames = new Image[4];

        [Header("Advanced (伤害/护盾/道具)")]
        [SerializeField] private GameObject advancedGroup;
        [SerializeField] private TextMeshProUGUI[] advTexts = new TextMeshProUGUI[3];
        [SerializeField] private Image[] advFrames = new Image[3];

        [Header("Texts")]
        [SerializeField] private TextMeshProUGUI hintText;
        [SerializeField] private TextMeshProUGUI difficultyDescText;

        [Header("Colors")]
        [SerializeField] private Color frameIdle = new Color(0f, 0f, 0f, 0.15f);
        [SerializeField] private Color frameSelected = new Color(1f, 0.85f, 0.2f, 0.55f);

        #endregion

        #region 2. 选项表

        private static readonly int[] StockOptions = { 1, 2, 3, 5, 7 };
        private static readonly int[] TimeOptions = { 1, 2, 3, 5, 8 };       // 分钟
        private static readonly float[] DamageOptions = { 0.5f, 1f, 1.5f, 2f };
        private static readonly float[] ShieldOptions = { 0.5f, 1f, 1.5f };

        #endregion

        #region 3. 运行时状态

        private int _row;
        private GameSettings.MatchMode _mode = GameSettings.MatchMode.Stock;
        private int _stockIdx = 2;      // 3 命
        private int _timeIdx = 2;       // 3 分钟
        private int _difficulty = 1;    // 普通
        private int _damageIdx = 1;     // 1.0x
        private int _shieldIdx = 1;     // 1.0x
        private bool _items;
        private bool _advancedOpen;

        private float _gpNextMoveTime;
        private const float GpRepeat = 0.18f;
        private int RowCount => 4 + (_advancedOpen ? 3 : 0);

        #endregion

        #region 4. 生命周期

        private void OnEnable()
        {
            _advancedOpen = false;
            _row = 0;
            LoadFromSettings();
            Refresh();
        }

        private void Update()
        {
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentGameState != GameState.RuleSelect) return;

            var gp = Gamepad.current;

            bool up = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            bool down = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
            bool left = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            bool right = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);

            // 手柄：上下切行、左右调值（带重复延迟）
            if (gp != null && Time.unscaledTime >= _gpNextMoveTime)
            {
                Vector2 nav = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > nav.sqrMagnitude) nav = dpad;
                if (nav.y > 0.5f) { up = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
                else if (nav.y < -0.5f) { down = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
                else if (nav.x > 0.5f) { right = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
                else if (nav.x < -0.5f) { left = true; _gpNextMoveTime = Time.unscaledTime + GpRepeat; }
            }

            if (up)   { _row = (_row - 1 + RowCount) % RowCount; Refresh(); }
            if (down) { _row = (_row + 1) % RowCount; Refresh(); }
            if (left)  { Adjust(-1); Refresh(); }
            if (right) { Adjust(+1); Refresh(); }

            bool toggleAdv = Input.GetKeyDown(KeyCode.F)
                          || (gp != null && (gp.leftTrigger.wasPressedThisFrame || gp.buttonNorth.wasPressedThisFrame));
            if (toggleAdv)
            {
                _advancedOpen = !_advancedOpen;
                if (!_advancedOpen && _row >= 4) _row = 3;
                Refresh();
            }

            bool confirm = Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Space)
                        || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Keypad1)
                        || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            bool cancel = Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Escape)
                        || Input.GetKeyDown(KeyCode.Keypad2)
                        || (gp != null && gp.buttonEast.wasPressedThisFrame);

            if (confirm) StartMatch();
            else if (cancel) Back();
        }

        #endregion

        #region 5. 逻辑

        private void LoadFromSettings()
        {
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            if (gs == null) return;
            _mode = gs.matchMode;
            _stockIdx = NearestIndex(StockOptions, gs.stockCount);
            _timeIdx = NearestIndex(TimeOptions, Mathf.RoundToInt(gs.matchTimeSeconds / 60f));
            _difficulty = Mathf.Clamp(gs.cpuDifficulty, 0, 3);
            _damageIdx = NearestIndexF(DamageOptions, gs.damageRatio);
            _shieldIdx = NearestIndexF(ShieldOptions, gs.shieldMaxHP / 100f);
            _items = gs.enableItems;
        }

        private void Adjust(int dir)
        {
            switch (_row)
            {
                case 0:  // 比赛模式
                    _mode = (_mode == GameSettings.MatchMode.Stock)
                        ? GameSettings.MatchMode.Time : GameSettings.MatchMode.Stock;
                    break;
                case 1:  // 命数
                    _stockIdx = Wrap(_stockIdx + dir, StockOptions.Length);
                    break;
                case 2:  // 时间
                    _timeIdx = Wrap(_timeIdx + dir, TimeOptions.Length);
                    break;
                case 3:  // CPU 难度
                    _difficulty = Mathf.Clamp(_difficulty + dir, 0, 3);
                    break;
                case 4:  // 伤害比例
                    _damageIdx = Wrap(_damageIdx + dir, DamageOptions.Length);
                    break;
                case 5:  // 护盾耐久
                    _shieldIdx = Wrap(_shieldIdx + dir, ShieldOptions.Length);
                    break;
                case 6:  // 道具
                    _items = !_items;
                    break;
            }
        }

        private void StartMatch()
        {
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            if (gs == null) return;

            // 写入 GameSettings（ScriptableObject，运行时内存态；跨状态保持）
            gs.matchMode = _mode;
            gs.stockCount = StockOptions[_stockIdx];
            gs.matchTimeSeconds = TimeOptions[_timeIdx] * 60f;
            gs.cpuDifficulty = _difficulty;
            gs.damageRatio = DamageOptions[_damageIdx];
            gs.shieldMaxHP = 100f * ShieldOptions[_shieldIdx];
            gs.enableItems = _items;

            GameManager.Instance.StartMatch();   // → GameState.Battle
        }

        private void Back()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.SwitchState(GameState.StageSelect);
        }

        private void Refresh()
        {
            if (advancedGroup != null) advancedGroup.SetActive(_advancedOpen);

            SetText(rowTexts, 0, $"比赛模式：<color=#FFD24D>{(_mode == GameSettings.MatchMode.Stock ? "命数制" : "时间制")}</color>");
            SetText(rowTexts, 1, $"命数：{StockOptions[_stockIdx]} 条");
            SetText(rowTexts, 2, $"时间：{TimeOptions[_timeIdx]} 分钟");
            SetText(rowTexts, 3, $"CPU 难度：<color=#FFD24D>{GameSettings.GetCpuDifficultyName(_difficulty)}</color>");

            SetText(advTexts, 0, $"伤害比例：{DamageOptions[_damageIdx]:0.0}x");
            SetText(advTexts, 1, $"护盾耐久：{ShieldOptions[_shieldIdx]:0.0}x");
            SetText(advTexts, 2, $"道具：{(_items ? "开启" : "关闭")}");

            // 行高亮
            for (int i = 0; i < rowFrames.Length; i++)
                if (rowFrames[i] != null) rowFrames[i].color = (_row == i) ? frameSelected : frameIdle;
            for (int i = 0; i < advFrames.Length; i++)
                if (advFrames[i] != null) advFrames[i].color = (_row == i + 4) ? frameSelected : frameIdle;

            if (difficultyDescText != null)
                difficultyDescText.text = GameSettings.GetCpuDifficultyDesc(_difficulty);
            if (hintText != null)
                hintText.text = "";   // 按键提示改由全局 KeyHintBar 统一显示
        }

        private static void SetText(TextMeshProUGUI[] arr, int i, string txt)
        {
            if (arr != null && i < arr.Length && arr[i] != null) arr[i].text = txt;
        }

        private static int Wrap(int v, int n) => n <= 0 ? 0 : ((v % n) + n) % n;

        private static int NearestIndex(int[] arr, int v)
        {
            int best = 0, bd = int.MaxValue;
            for (int i = 0; i < arr.Length; i++)
            {
                int d = Mathf.Abs(arr[i] - v);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        private static int NearestIndexF(float[] arr, float v)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < arr.Length; i++)
            {
                float d = Mathf.Abs(arr[i] - v);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        #endregion
    }
}
