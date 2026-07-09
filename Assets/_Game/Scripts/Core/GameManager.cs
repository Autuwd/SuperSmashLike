using UnityEngine;
using System.Collections.Generic;

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
    // 场景流转: Boot → Title → CharacterSelect → Battle → Result
    public enum GameState
    {
        Boot,              // 启动/加载界面
        Title,             // 标题画面
        CharacterSelect,   // 选人界面（多人依次选择）
        BattleLoading,     // 加载战斗场景
        Battle,            // 战斗中（核心玩法进行中）
        Paused,            // 暂停（Time.timeScale = 0）
        Result,            // 结算画面（显示排名/统计数据）
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
        public bool skipToBattle;           // 调试用：勾选后跳过菜单直接进入战斗

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
            // 单例初始化：如果已有实例则销毁自己
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);  // 场景切换时不销毁
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
            Debug.Log($"[GameManager] State: {newState}");
        }

        public void StartMatch()
        {
            SwitchState(GameState.Battle);
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
