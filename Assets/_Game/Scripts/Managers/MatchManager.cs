using SuperSmashLike.Core;
using SuperSmashLike.Stage;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// ============================================================
// MatchManager — 比赛管理器
// 职责：
//   1. 控制一场比赛的完整流程（倒计时→进行中→结束）
//   2. 执行比赛规则（时间制 vs 命数制）
//   3. 管理玩家出生/重生
// 架构位置：Managers 层，每个场景一个实例
// 依赖：
//   - GameManager.Instance.gameSettings → 读取规则配置
//   - GameManager.Instance.ActivePlayers → 获取所有玩家
//   - CameraManager → 更新摄像机
//   - BlastZone.OnPlayerOutOfBounds → 触发击杀→重生
// ============================================================
namespace SuperSmashLike.Managers
{
    public class MatchManager : MonoBehaviour
    {
        [Header("References")]
        public CameraManager cameraManager;      // 场景中的摄像机管理器
        public Transform[] spawnPoints;          // 玩家出生点（从 Scene 拖入）

        [Header("Settings")]
        public float countdownTime = 3f;         // 开局倒计时秒数
        public float gameOverDelay = 3f;         // 结束后延迟（用于播放胜利动画）

        public float MatchTimeRemaining { get; private set; }  // 比赛剩余时间
        public bool IsMatchActive { get; private set; }        // 比赛是否进行中

        public System.Action<float> OnTimerUpdated;   // 计时更新事件（UI 监听此事件刷新显示）
        public System.Action<int> OnPlayerScored;     // 玩家得分事件
        public System.Action<int> OnGameOver;         // 比赛结束事件（参数=胜者ID）

[Header("HUD")]
        [SerializeField] private GameObject hudPrefab;           // 拖 UI_HUD.prefab（含 DamageDisplay/StockDisplay）

        private DamageDisplay[] _damageDisplays = new DamageDisplay[4];
        private StockDisplay[] _stockDisplays = new StockDisplay[4];
        private GameObject _hudInstance;

        // === 新增：委托缓存 + 重生常量 + 注册去重 ===
        private readonly Dictionary<FighterController, System.Action<float, FighterController>> _damagedHandlers = new();
        private readonly Dictionary<FighterController, System.Action<FighterController>> _killedHandlers = new();
        private readonly HashSet<FighterController> _registeredFighters = new(); // 防重复注册
        private TextMeshProUGUI centerTimerText;              // 运行时赋值
        private GameSettings settings;
        private Coroutine matchCoroutine;
        private bool _isMatchEnding = false; // 防止 EndMatch 重入

        private const float RespawnInvincibilityTime = 3f;    // 与 GameSettings.respawnTime 对齐

        

        private void Start()
        {
            settings = GameManager.Instance.gameSettings;
            MatchTimeRemaining = settings.matchTimeSeconds;
            StartMatch();
            SpawnAndBindHUD();
        }

        // 开始比赛（启动协程控制比赛流程）
        public void StartMatch()
        {
            IsMatchActive = false;
            matchCoroutine = StartCoroutine(MatchFlow());
        }

        // 比赛流程协程
        // 阶段1：倒计时（3秒）
        // 阶段2：比赛进行（循环检测结束条件）
        // 阶段3：比赛结束
        private IEnumerator MatchFlow()
        {
            // === 阶段1：开局倒计时 ===
            for (int i = (int)countdownTime; i > 0; i--)
            {
                Debug.Log($"Countdown: {i}");
                yield return new WaitForSeconds(1f);
            }

            // === 阶段2：比赛进行 ===
            IsMatchActive = true;
            Debug.Log("Match Started!");

            while (IsMatchActive)
            {
                // 更新时间
                MatchTimeRemaining -= Time.deltaTime;
                OnTimerUpdated?.Invoke(MatchTimeRemaining);

                // 时间制：时间到 → 结束
                if (settings.matchMode == GameSettings.MatchMode.Time && MatchTimeRemaining <= 0f)
                {
                    EndMatch();
                    yield break;
                }

                // 命数制：存活玩家 ≤ 1 → 结束
                if (settings.matchMode == GameSettings.MatchMode.Stock)
                {
                    var playersInGame = GameManager.Instance.ActivePlayers
                        .FindAll(p =>
                            p.StateMachine.CurrentState != FighterState.Dead
                            || (p.StateMachine.CurrentState == FighterState.Dead && p.remainingStocks > 0)
                        );

                    if (playersInGame.Count <= 1)
                    {
                        yield return new WaitForSeconds(gameOverDelay);
                        int winnerID = -1;
                        if (playersInGame.Count == 1) winnerID = playersInGame[0].playerID;
                        EndMatch(winnerID);
                        yield break;
                    }
                }

                yield return null;
            }
        }

// 结束比赛
        public void EndMatch(int winnerID = -1)
        {
            if (_isMatchEnding) return; _isMatchEnding = true;

            IsMatchActive = false;
            OnGameOver?.Invoke(winnerID);
            GameManager.Instance.EndMatch();  // 通知 GameManager 切换到结算状态

            // === 清理所有 Fighter 注册 ===
            foreach (var f in GameManager.Instance.ActivePlayers)
            {
                if (f != null) GameManager.Instance.UnregisterFighter(f);
            }
            _registeredFighters.Clear();
        }

        // 重生玩家（由 BlastZone 触发击杀后调用）
        public void RespawnPlayer(FighterController fighter)
        {
            Transform spawn = GetRandomSpawnPoint();
            fighter.Respawn(spawn.position);
        }

        private Transform GetRandomSpawnPoint()
        {
            if (spawnPoints.Length == 0) return transform;
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }

        // === HUD 新增：核心方法 ===
        private void SpawnAndBindHUD()
        {
            if (!hudPrefab)
            {
                Debug.LogError("[MatchManager] hudPrefab 未赋值！Inspector 请拖入 UI_HUD.prefab");
                return;
            }

            _hudInstance = Instantiate(hudPrefab);
            _hudInstance.name = "UI_HUD (Runtime)";

            // === 新增：初始化注册表 ===
            _registeredFighters.Clear();
            var initPlayers = GameManager.Instance.ActivePlayers;
            foreach (var f in initPlayers) if (f != null) _registeredFighters.Add(f);

            // === 新增：自动找中间倒计时（Prefab 里叫 CenterTimer）===
            centerTimerText = _hudInstance.transform.Find("CenterTimer")?.GetComponent<TextMeshProUGUI>();
            if (centerTimerText == null)
            {
                Debug.LogWarning("[MatchManager] Prefab 里找不到 CenterTimer 对象或缺少 TextMeshProUGUI");
            }

            // 只绑定双人 P1=0, P2=1
            for (int i = 0; i < 2; i++)
            {
                string pName = i == 0 ? "P1_HUD" : "P2_HUD";
                Transform pRoot = _hudInstance.transform.Find(pName);
                if (!pRoot)
                {
                    Debug.LogError($"[MatchManager] 找不到 {pName}，检查 Prefab 层级命名");
                    continue;
                }

                _damageDisplays[i] = pRoot.Find("DamageDisplay")?.GetComponent<DamageDisplay>();
                _stockDisplays[i] = pRoot.Find("StockDisplay")?.GetComponent<StockDisplay>();

                if (!_damageDisplays[i] || !_stockDisplays[i])
                {
                    Debug.LogError($"[MatchManager] {pName} 缺少 DamageDisplay/StockDisplay 组件");
                    continue;
                }

                // 注入 FighterId
                _damageDisplays[i].SetFighterId(i);
                _stockDisplays[i].SetFighterId(i);

                // 初始化命数/倒计时（从 GameSettings 读取）
                _stockDisplays[i].Init(settings.stockCount, settings.matchTimeSeconds, showTimer: false);
            }

            // 订阅 MatchManager 自己的事件
            OnTimerUpdated += OnTimerUpdatedHandler;
            OnGameOver += OnGameOverHandler;

            // 订阅现有 Fighter 事件（用闭包捕获 fighter）
            SubscribeFighterEvents(GameManager.Instance.ActivePlayers);

            // 后续加入的 Fighter（重生）通过 GameStateChanged 重新订阅
            GameManager.Instance.OnGameStateChanged += OnGameStateChangedHandler;
        }

        private void SubscribeFighterEvents(List<FighterController> players)
        {
            foreach (var f in players)
            {
                if (f == null) continue;
                System.Action<float, FighterController> dmgHandler = (dmg, attacker) => OnFighterDamagedHandler(f, dmg, attacker);
                f.OnDamaged += dmgHandler;
                _damagedHandlers[f] = dmgHandler;

                System.Action<FighterController> killHandler = victim => OnFighterKilledHandler(victim);
                f.OnKilled += killHandler;
                _killedHandlers[f] = killHandler;
            }
        }

        private void UnsubscribeFighterEvents(List<FighterController> players)
        {
            foreach (var f in players)
            {
                if (f == null) continue;
                if (_damagedHandlers.TryGetValue(f, out var dmg)) { f.OnDamaged -= dmg; _damagedHandlers.Remove(f); }
                if (_killedHandlers.TryGetValue(f, out var kill)) { f.OnKilled -= kill; _killedHandlers.Remove(f); }
            }
        }


        private void OnCenterTimerUpdated(float remaining)
        {
            if (centerTimerText != null)
            {
                int m = Mathf.FloorToInt(remaining / 60f);
                int s = Mathf.FloorToInt(remaining % 60f);
                centerTimerText.text = $"{m:00}:{s:00}";
                // 最后 30s 变红
                centerTimerText.color = remaining <= 30f ? Color.red : Color.white;
            }
        }

        private void OnFighterDamagedHandler(FighterController victim, float damage, FighterController attacker)
        {
            int id = victim.playerID;
            if (id >= 0 && id < _damageDisplays.Length && _damageDisplays[id] != null)
            {
                _damageDisplays[id].SetPercent(Mathf.RoundToInt(victim.CurrentDamage));
                Debug.Log($"[HUD] P{id} Damage → {victim.CurrentDamage}%");
            }
        }

        // === 新增：BlastZone 调用入口 ===
        public void OnPlayerOutOfBounds(FighterController fighter, BlastZone.Side side)
        {
            Debug.Log($"[OnPlayerOutOfBounds] {fighter.name} 掉界, side={side}, state={fighter.StateMachine.CurrentState}, stocks={fighter.remainingStocks}");
            if (fighter.StateMachine.CurrentState == FighterState.Dead) return;

            fighter.Kill(); // 触发 OnKilled 事件 → 扣命 UI

            if (settings.matchMode == GameSettings.MatchMode.Stock && fighter.remainingStocks > 0)
            {
                Debug.Log($"[OnPlayerOutOfBounds] 启动重生协程 for {fighter.name}");
                StartCoroutine(RespawnAfterDelay(fighter));
            }
            else
            {
                Debug.Log($"[OnPlayerOutOfBounds] 不重生: mode={settings.matchMode}, stocks={fighter.remainingStocks}");
            }
        }

        private IEnumerator RespawnAfterDelay(FighterController fighter)
        {
            Debug.Log($"[RespawnAfterDelay] {fighter.name} 等待 {gameOverDelay}s 后重生...");
            yield return new WaitForSeconds(gameOverDelay); // 3s 等待

            Debug.Log($"[RespawnAfterDelay] {fighter.name} 延迟结束，开始重生流程");
            
            // 只有未注册时才注册（防重复）
            if (!_registeredFighters.Contains(fighter))
            {
                Debug.Log($"[RespawnAfterDelay] 注册 Fighter 到 GameManager");
                GameManager.Instance.RegisterFighter(fighter);
                _registeredFighters.Add(fighter);
            }
            else
            {
                Debug.Log($"[RespawnAfterDelay] Fighter 已在注册表中，跳过注册");
            }
            
            // 重新订阅事件
            Debug.Log($"[RespawnAfterDelay] 重新绑定事件");
            RebindFighterEvents(fighter);

            // 执行重生
            Transform spawn = GetRandomSpawnPoint();
            Debug.Log($"[RespawnAfterDelay] 重生位置: {spawn.position}");
            fighter.Respawn(spawn.position);

            // 重生后：同步 Stock UI
            int id = fighter.playerID;
            if (id >= 0 && id < _stockDisplays.Length && _stockDisplays[id] != null)
            {
                _stockDisplays[id].SetStock(fighter.remainingStocks);
                Debug.Log($"[RespawnAfterDelay] 同步 Stock UI: P{id} -> {fighter.remainingStocks}");
            }
            if (id >= 0 && id < _damageDisplays.Length && _damageDisplays[id] != null)
            {
                _damageDisplays[id].SetPercent(0); // 伤害归零
                _damageDisplays[id].ResetDisplay(); // 关掉淡入淡出残留
                Debug.Log($"[RespawnAfterDelay] 重置 Damage UI: P{id}");
            }
            
            Debug.Log($"[RespawnAfterDelay] {fighter.name} 重生流程完成");
        }

        private void RebindFighterEvents(FighterController f)
        {
            if (f == null) return;
            if (_damagedHandlers.TryGetValue(f, out var dmg)) f.OnDamaged -= dmg;
            if (_killedHandlers.TryGetValue(f, out var kill)) f.OnKilled -= kill;

            System.Action<float, FighterController> newDmg = (dmg, atk) => OnFighterDamagedHandler(f, dmg, atk);
            f.OnDamaged += newDmg;
            _damagedHandlers[f] = newDmg;

            System.Action<FighterController> newKill = victim => OnFighterKilledHandler(victim);
            f.OnKilled += newKill;
            _killedHandlers[f] = newKill;
        }

        private void OnFighterKilledHandler(FighterController victim)
        {
            Debug.Log($"[OnFighterKilledHandler] 收到击杀事件: {victim.name}, stocks={victim.remainingStocks}");
            int id = victim.playerID;
            if (settings.matchMode == GameSettings.MatchMode.Stock && id >= 0 && id < _stockDisplays.Length && _stockDisplays[id] != null)
            {
                // 统一用 SetStock 同步剩余命数
                _stockDisplays[id].SetStock(victim.remainingStocks);
                Debug.Log($"[HUD] P{id} Stock → {victim.remainingStocks}");
            }
        }

        private void OnGameStateChangedHandler(GameState newState)
        {
            if (newState == GameState.Battle)
            {
                var players = GameManager.Instance.ActivePlayers;
                foreach (var f in players) RebindFighterEvents(f);
            }
        }

        // 中间倒计时同步
        private void OnTimerUpdatedHandler(float remaining)
        {
            foreach (var s in _stockDisplays) if (s != null) s.SyncTime(remaining);
            if (centerTimerText != null)
            {
                int m = Mathf.FloorToInt(remaining / 60f);
                int s = Mathf.FloorToInt(remaining % 60f);
                centerTimerText.text = $"{m:00}:{s:00}";
                centerTimerText.color = remaining <= 30f ? Color.red : Color.white;
            }
        }

        private void OnGameOverHandler(int winnerID)
        {
            foreach (var s in _stockDisplays) if (s != null) s.SetTimerRunning(false);
            if (centerTimerText != null) centerTimerText.text = "00:00";
        }

private void OnDestroy()
        {
            if (matchCoroutine != null) StopCoroutine(matchCoroutine);

            OnTimerUpdated -= OnTimerUpdatedHandler;
            OnGameOver -= OnGameOverHandler;
            GameManager.Instance.OnGameStateChanged -= OnGameStateChangedHandler;
            UnsubscribeFighterEvents(_registeredFighters.ToList());
            _registeredFighters.Clear();
        }
    }
}
