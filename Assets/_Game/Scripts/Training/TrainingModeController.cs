using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SuperSmashLike.Training
{
    public class TrainingModeController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("P1 斗士（训练时操作方）")]
        public FighterController player1;
        [Tooltip("P2 斗士（通常设为 CPU）")]
        public FighterController player2;

        [Header("Slow Motion")]
        [Tooltip("按住 LeftShift 时的基础时间缩放")]
        [Range(0.05f, 1f)] public float slowMoScale = 0.5f;

        [Header("HUD")]
        [Tooltip("训练用 HUD 根节点（可选，用于 H 键开关）")]
        public GameObject trainingHudRoot;

        [Header("Menu")]
        [Tooltip("训练菜单面板（Esc 开关）")]
        public GameObject menuPanel;

        private bool _slowMoActive;
        private bool _cpuEnabled = true;
        private bool _hudVisible = true;
        private bool _inputSoloApplied;
        private bool _menuOpen;
        private FrameMeter[] _frameMeters;   // 场景中的帧数表（仅训练显示）
        [System.NonSerialized] public bool FrameDataOn = true;   // 帧数表/帧数据 统一开关

        // ---- 菜单 / 外部读取的状态 ----
        public bool MenuOpen => _menuOpen;
        public bool CpuEnabled => _cpuEnabled;
        public bool HudVisible => _hudVisible;
        public bool SlowMoActive => _slowMoActive;
        public bool Invincible
        {
            get => player1 != null && player1.debugInvincible;
            set { if (player1 != null) player1.debugInvincible = value; }
        }

        private void Awake()
        {
            // 默认隐藏：只有进入训练模式才显示（否则训练 HUD 会在任意状态常驻）
            if (trainingHudRoot != null)
                trainingHudRoot.SetActive(false);
            if (menuPanel != null)
                menuPanel.SetActive(false);
            _menuOpen = false;
        }

        private void Update()
        {
            if (GameManager.Instance == null) return;

            bool inTraining = GameManager.Instance.CurrentGameState == GameState.Training;
            if (trainingHudRoot != null)
                trainingHudRoot.SetActive(inTraining && _hudVisible);

            // 帧数表：仅训练模式显示，且受"帧数据"统一开关控制
            if (_frameMeters == null) _frameMeters = FindObjectsOfType<FrameMeter>(true);
            bool showFrame = inTraining && FrameDataOn;
            for (int i = 0; i < _frameMeters.Length; i++)
            {
                var fm = _frameMeters[i];
                if (fm == null) continue;
                if (fm.gameObject.activeSelf != showFrame) fm.gameObject.SetActive(showFrame);
            }

            // 训练时让玩家接管全部设备（手柄/方向键/小键盘/键盘都操作 P1）；P2 是 CPU 不抢输入
            if (inTraining != _inputSoloApplied)
            {
                _inputSoloApplied = inTraining;
                ApplySoloInput(inTraining);
            }
            if (!inTraining)
            {
                if (_menuOpen) SetMenu(false);   // 离开训练 → 收起菜单
                return;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            // Esc：开关训练菜单（不再直接返回标题）
            if (kb.escapeKey.wasPressedThisFrame)
            {
                ToggleMenu();
                return;
            }

            // 菜单打开时：热键交给菜单处理，避免误触发
            if (_menuOpen) return;

            // R：重置双方位置+伤害
            if (kb.rKey.wasPressedThisFrame)
                ResetBoth();

            // T：CPU 开关（P2）
            if (kb.tKey.wasPressedThisFrame)
                SetCpuEnabled(!_cpuEnabled);

            // I：调试无敌（P1）
            if (kb.iKey.wasPressedThisFrame)
                Invincible = !Invincible;

            // 慢动作：只由菜单开关控制（不再用 Shift 热键，避免与开关状态冲突）
            // 帧数表开关（菜单"帧数据"行会改它）
            // H：HUD 开关
            if (kb.hKey.wasPressedThisFrame)
                SetHudVisible(!_hudVisible);
        }

        // 【做什么】训练模式让 P1 同时接受 键盘P1 + 键盘P2(方向键/小键盘) + 手柄
        private void ApplySoloInput(bool solo)
        {
            if (player1 == null) return;
            var im = player1.GetComponent<SuperSmashLike.InputSystem.InputManager>();
            if (im != null) im.SetOwnAllSchemes(solo);
        }

        // ---- 菜单 / 外部调用 API ----

        public void SetCpuEnabled(bool enabled)
        {
            _cpuEnabled = enabled;
            ToggleCpu(enabled);
            RefreshMenu();
        }

        public void SetHudVisible(bool visible)
        {
            _hudVisible = visible;
            if (trainingHudRoot != null)
                trainingHudRoot.SetActive(visible);
            RefreshMenu();
        }

        public void SetSlowMo(bool active)
        {
            if (active == _slowMoActive) return;
            _slowMoActive = active;
            if (GameManager.Instance != null)
            {
                GameManager.Instance.BaseTimeScale = active ? slowMoScale : 1f;
                GameManager.Instance.ApplyTimeScale();
            }
            RefreshMenu();
        }

        public void ResetBothPublic()
        {
            ResetBoth();
            RefreshMenu();
        }

        // 【做什么】退出训练：恢复时标 + 停训练协程 + 回标题
        public void ExitTraining()
        {
            if (GameManager.Instance == null) return;
            SetMenu(false);
            // 恢复 CPU（避免训练时把 P2 冻成木桩后影响到后续正常对战）
            _cpuEnabled = true;
            ToggleCpu(true);
            GameManager.Instance.BaseTimeScale = 1f;
            Time.timeScale = 1f;
            GameManager.Instance.ApplyTimeScale();
            if (MatchManager.Instance != null)
                MatchManager.Instance.StopTrainingMatch();
            GameManager.Instance.SwitchState(GameState.Title);
        }

        // 【做什么】开关训练菜单；打开时冻结（timeScale=0），关闭时恢复 BaseTimeScale
        public void ToggleMenu() => SetMenu(!_menuOpen);

        public void SetMenu(bool open)
        {
            _menuOpen = open;
            SuperSmashLike.UI.KeyHintBar.ForceShow = open;   // 菜单打开时显示底部按键提示
            if (menuPanel != null) menuPanel.SetActive(open);

            // 菜单打开时禁用 P1 输入，防止菜单按键被战斗输入一起消费
            var im = player1 != null ? player1.GetComponent<SuperSmashLike.InputSystem.InputManager>() : null;
            if (im != null) im.enabled = !open;

            if (GameManager.Instance != null)
                Time.timeScale = open ? 0f : GameManager.Instance.BaseTimeScale;

            RefreshMenu();
        }

        private void RefreshMenu()
        {
            if (menuPanel == null) return;
            var panel = menuPanel.GetComponent<TrainingMenuPanel>();
            if (panel != null) panel.Refresh();
        }

        private void ResetBoth()
        {
            if (GameManager.Instance == null) return;
            var mm = FindObjectOfType<MatchManager>();
            if (mm != null)
            {
                if (player1 != null)
                    player1.ResetToSpawn(mm.GetSpawnPositionFor(player1), zeroDamage: true);
                if (player2 != null)
                    player2.ResetToSpawn(mm.GetSpawnPositionFor(player2), zeroDamage: true);
                return;
            }

            if (player1 != null) player1.ResetToSpawn(new Vector3(-1.5f, 0f, 0f), true);
            if (player2 != null) player2.ResetToSpawn(new Vector3(1.5f, 0f, 0f), true);
        }

        private void ToggleCpu(bool enabled)
        {
            if (player2 == null) return;
            var cpu = player2.GetComponent<CPUPlayer>();
            if (cpu != null)
            {
                cpu.enabled = enabled;
                cpu.SetAiFrozen(!enabled);
                cpu.SetAttackEnabled(enabled);
                if (!enabled)
                    cpu.ForceIdle();
            }
            else
            {
                player2.enabled = enabled;
            }
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentGameState == GameState.Training)
            {
                GameManager.Instance.BaseTimeScale = 1f;
                GameManager.Instance.ApplyTimeScale();
            }
            _slowMoActive = false;
            _menuOpen = false;
            if (menuPanel != null) menuPanel.SetActive(false);
        }
    }
}