using UnityEngine;
using System.Collections.Generic;
using SuperSmashLike.Managers;
using SuperSmashLike.Stage;

// ============================================================
// GameManager — 全局游戏状态管理器（单例）
// 职责：
//   1. 管理整个游戏的生命周期状态（标题→选人→战斗→结算）
//   2. 持有所有活跃的 FighterController 引用列表
//   3. 持有全局游戏规则配置（GameSettings）
// 架构位置：Core 层的最顶层，所有模块通过 GameManager.Instance 访问
// ============================================================
namespace SuperSmashLike.Core
{
    // 游戏流程状态枚举
    // 场景流转: Boot → Title → CharacterSelect → StageSelect → RuleSelect → Battle → Result
    public enum GameState
    {
        Boot,              // 启动/加载界面
        Title,             // 标题画面
        CharacterSelect,   // 选人界面（多人依次选择）
        StageSelect,       // 地图选择界面（单人流程 · M4-D2）
        RuleSelect,        // 规则选择界面（单人流程 · M4-D3）
        BattleLoading,     // 加载战斗场景
        Battle,            // 战斗中（核心玩法进行中）
        Paused,            // 暂停（Time.timeScale = 0）
        Result,            // 结算画面（显示排名/统计数据）
        Training,          // 训练模式
    }

    public class GameManager : MonoBehaviour
    {
        // 单例模式：整个游戏生命周期只有一个 GameManager
        // 通过 GameManager.Instance 全局访问
        public static GameManager Instance { get; private set; }

        [Header("References")]
        public GameSettings gameSettings;   // 从 Unity Inspector 拖入的全局规则配置资产
        public GameObject fighterPrefab;    // 斗士角色的预制体模板

        [Header("Debug")]
        public DebugSettings debugSettings;   // 调试开关配置资产（拖入 DS_Debug.asset）
        public bool skipToBattle;           // 调试用：勾选后跳过菜单直接进入战斗

        [Header("Players / Devices")]
        [Tooltip("人类玩家数量（1 = 单人，隐藏设备选择，默认键鼠+手柄皆可）")]
        public int playerCount = 1;
        [Tooltip("AI 数量")]
        public int aiCount = 1;
        [Tooltip("每个玩家的操作方式：0=键盘P1区  1=键盘P2区  2=手柄")]
        public int[] playerDevice = { 0, 2, 0, 2 };

        // 【做什么】取某玩家选定的操作方式（越界回退：P1=键盘P1区 / P2=手柄）
        public int GetDeviceForPlayer(int pid)
            => (pid >= 0 && pid < playerDevice.Length) ? playerDevice[pid] : (pid == 1 ? 2 : 0);

        [Header("Time Scale")]
        [Tooltip("基础时间缩放（非暂停时生效，用于训练慢动作）")]
        public float baseTimeScale = 1f;

        public float BaseTimeScale
        {
            get => baseTimeScale;
            set => baseTimeScale = Mathf.Clamp(value, 0f, 10f);
        }

        // 【做什么】外部冻结标记：训练菜单打开等非 Paused 状态的冻结置位，ApplyTimeScale 一律尊重
        // 【为什么】时标单一所有权：慢动作 / Hitstop 结束等路径都必须走 ApplyTimeScale，
        //   否则会把菜单要求的 0 覆盖回 BaseTimeScale，出现菜单开着游戏却恢复流动的问题
        public bool externalFreeze;

        // 当前游戏阶段，其他模块通过这个属性来判断能做什么
        public GameState CurrentGameState { get; private set; }

        // 玩家人数（由 activePlayers 列表长度推导）
        public int PlayerCount => activePlayers.Count;

        // 当前场景中所有活跃斗士的只读列表
        // CameraManager 用它来跟踪所有玩家位置
        // MatchManager 用它来判断存活人数
        public List<FighterController> ActivePlayers => activePlayers;

        // 内部持有的活跃斗士列表（在 Inspector 中只读显示）
        [SerializeField] private List<FighterController> activePlayers = new();

        // 状态变更事件：其他模块监听此事件来做响应
        // 例如：UI 模块监听 → 切换显示对应界面
        public System.Action<GameState> OnGameStateChanged;

        private void Awake()
        {
            // 【必须先做】注入调试配置 —— 其他模块在 Start/Update 里会用到
            // 若未拖入资产，SmashDebug 会视为"全部通道关闭"，只保留 LogError
            SmashDebug.Settings = debugSettings;

            // 单例初始化：如果已有实例则销毁自己
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);  // 场景切换时不销毁

            // 【M8-D5】把「游戏设置页」保存的默认偏好种入本局 GameSettings
            ApplyGameplayDefaultsFromStore();
        }

        // 【做什么】用 SettingsStore 里保存的默认偏好（规则模式 / 命数 / CPU 难度）初始化 GameSettings
        // 【为什么】设置页改的三个默认值原本是"死数据"——只落盘没有任何消费方，
        //   导致"改完默认命数进战斗还是 3 条"。这里在启动时把它种入 GameSettings，
        //   规则选择界面 LoadFromSettings() 随后读到的就是玩家保存的偏好（玩家仍可在该界面临时改）。
        // 【注意】只播种这三个"默认偏好"字段；damageRatio / shieldMaxHP / enableItems 等
        //   每局细项仍由规则界面自行维护，避免覆盖设计上的默认手感。
        private void ApplyGameplayDefaultsFromStore()
        {
            if (gameSettings == null) return;        // 未拖入资产：保持 ScriptableObject 自身值
            if (!SettingsStore.Loaded) return;       // 尚未读盘：不猜值

            var gp = SettingsStore.Data != null ? SettingsStore.Data.gameplay : null;
            if (gp == null) return;

            gameSettings.matchMode = (GameSettings.MatchMode)Mathf.Clamp(gp.defaultMatchMode, 0, 1);
            gameSettings.stockCount = Mathf.Clamp(gp.defaultStock, 1, 3);
            gameSettings.cpuDifficulty = Mathf.Clamp(gp.defaultCpuDifficulty, 0, 3);
        }

        private void Start()
        {
            // 调试模式直接进入战斗，否则从标题画面开始
            if (skipToBattle)
                StartMatch();
            else
                SwitchState(GameState.Title);
        }

        // 切换游戏状态的核心方法，会触发 OnGameStateChanged 事件
        public void SwitchState(GameState newState)
        {
            CurrentGameState = newState;
            OnGameStateChanged?.Invoke(newState);

            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match, $"GameManager 状态 → {newState}");
        }

        public bool IsCombatState(bool includeTraining = true)
        {
            var s = CurrentGameState;
            return s == GameState.Battle || (includeTraining && s == GameState.Training);
        }

        public void ApplyTimeScale()
        {
            if (CurrentGameState == GameState.Paused || externalFreeze)
            {
                Time.timeScale = 0f;
                return;
            }
            Time.timeScale = BaseTimeScale;
        }

        public void StartMatch()
        {
            SwitchState(GameState.Battle);
        }

        // 【做什么】是否请求走"训练流程"（标题→选人→选图→训练，跳过规则选择）
        public bool trainingRequested { get; private set; }

        // 【做什么】从标题进入训练流程：置标记 → 去选人（再选图 → 训练）
        public void RequestTraining()
        {
            trainingRequested = true;
            SwitchState(GameState.CharacterSelect);
        }

        public void CancelTrainingRequest() => trainingRequested = false;

        public void StartTraining()
        {
            trainingRequested = false;
            SwitchState(GameState.Training);
            if (StageManager.Instance != null)
                StageManager.Instance.LoadSelectedStage();

            // M6-D1：训练开局同样按选人结果重建斗士（训练不走 MatchManager 的 Battle 状态分支）
            FighterSpawnService.SpawnForCurrentMatch();

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ResetMatchData();
                MatchManager.Instance.RebindAllFighterEvents();   // 新实例补订阅（Battle 路径由状态分支尾部覆盖）
                MatchManager.Instance.StartMatch();
            }
        }

        public void EndMatch()
        {
            SwitchState(GameState.Result);
        }

        // 暂停：只有战斗状态才能暂停，通过将 Time.timeScale 设为 0 冻结物理和Update
        public void PauseGame()
        {
            if (CurrentGameState == GameState.Battle)
            {
                SwitchState(GameState.Paused);
                Time.timeScale = 0f;
            }
        }

        // 恢复：Time.timeScale 回到 1
        public void ResumeGame()
        {
            if (CurrentGameState == GameState.Paused)
            {
                SwitchState(GameState.Battle);
                Time.timeScale = 1f;
            }
        }

        // 【做什么】输入层统一的"暂停/恢复切换"入口（问题①修复 2026-10-17）
        // 【为什么必须集中】Esc 会经由多条通道同帧抵达：
        //   ① InputManager.OnPauseCtx（Pause 动作，KeyboardP1/P2 两个实例各收一次）
        //   ② KeyHintBar.HandleEsc（旧版 Input.GetKeyDown 兜底）
        //   此前各通道各自维护静态冷却、互不知情 → 同一帧"先暂停、再恢复"，净零。
        //   现在收敛为本类唯一的静态冷却：一次按键周期只允许切换一次。
        private static float s_lastPauseToggleTime = -10f;

        public void TogglePause()
        {
            if (Time.unscaledTime - s_lastPauseToggleTime < 0.2f) return;
            s_lastPauseToggleTime = Time.unscaledTime;

            if (CurrentGameState == GameState.Battle) PauseGame();
            else if (CurrentGameState == GameState.Paused) ResumeGame();
        }

        // FighterController 在 Start() 中调用此方法注册自己
        public void RegisterFighter(FighterController fighter)
        {
            if (!activePlayers.Contains(fighter))
                activePlayers.Add(fighter);
        }

        // FighterController 在死亡或销毁时调用此方法注销
        public void UnregisterFighter(FighterController fighter)
        {
            activePlayers.Remove(fighter);
        }
    }
}
