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
//   1. 控制一场比赛的完整流程（倒计时 → 进行中 → 结束）
//   2. 执行比赛规则（时间制 vs 命数制）
//   3. 管理玩家出生/重生
//   4. 生成 HUD 并把斗士事件转发到 UI
// 架构位置：Managers 层，每个战斗场景一个实例
// 依赖：
//   - GameManager.Instance.gameSettings  → 读规则配置
//   - GameManager.Instance.ActivePlayers → 获取所有玩家
//   - CameraManager                      → 场景引用
//   - BlastZone.OnPlayerOutOfBounds      → 出界 → 击杀 → 重生
//
// 【重点】事件订阅/退订必须严格对称：
//   用 Dictionary<FighterController, Action> 缓存委托，退订时精确移除 ——
//   闭包每次 += 都是新实例，不缓存就退不掉（内存泄漏 + 重复触发）。
// 【重点】HUD 靠 playerID 当数组下标，Prefab 层级命名必须是
//   P1_HUD/DamageDisplay、P2_HUD/StockDisplay 这种约定式路径。
// ============================================================
namespace SuperSmashLike.Managers
{
    public class MatchManager : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("References")]
        public CameraManager cameraManager;      // 场景中的摄像机管理器
        public Transform[] spawnPoints;          // 玩家出生点（从 Scene 拖入）

        [Header("Settings")]
        public float countdownTime = 3f;         // 开局倒计时秒数
        public float gameOverDelay = 3f;         // 结束后延迟（用于播放胜利动画 / 重生等待）

        [Header("HUD")]
        [SerializeField] private GameObject hudPrefab;   // 拖 UI_HUD.prefab（含 DamageDisplay/StockDisplay）

        #endregion

        #region 2. 运行时状态

        public float MatchTimeRemaining { get; private set; }  // 比赛剩余时间
        public bool IsMatchActive { get; private set; }        // 比赛是否进行中

        private DamageDisplay[] _damageDisplays = new DamageDisplay[4];
        private StockDisplay[] _stockDisplays = new StockDisplay[4];
        private GameObject _hudInstance;
        private TextMeshProUGUI centerTimerText;               // 运行时按名字查找

        private GameSettings settings;
        private Coroutine matchCoroutine;
        private bool _isMatchEnding;      // 防止 EndMatch 重入
        private bool _matchStarted;       // 防止重复开赛

        // 委托缓存（订阅/退订对称的关键）
        private readonly Dictionary<FighterController, System.Action<float, FighterController>> _damagedHandlers = new();
        private readonly Dictionary<FighterController, System.Action<FighterController>> _killedHandlers = new();
        private readonly HashSet<FighterController> _registeredFighters = new();   // 防重复注册

        #endregion

        #region 3. 事件

        public System.Action<float> OnTimerUpdated;   // 计时更新（UI 监听刷新显示）
        public System.Action<int> OnPlayerScored;     // 玩家得分（当前无 Invoke 方，功能未实现）
        public System.Action<int> OnGameOver;         // 比赛结束（参数 = 胜者 ID，-1 表示平局/无胜者）

        #endregion

        #region 4. Unity 生命周期

        private void Start()
        {
            settings = GameManager.Instance.gameSettings;
            MatchTimeRemaining = settings.matchTimeSeconds;
            SpawnAndBindHUD();
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

        #endregion

        #region 5. 公开 API

        // 【做什么】开始比赛（启动流程协程）
        public void StartMatch()
        {
            IsMatchActive = false;
            matchCoroutine = StartCoroutine(MatchFlow());
        }

        // 【做什么】结束比赛：广播结果 + 通知 GameManager 切结算 + 清理所有斗士注册
        // 【注意】用快照遍历，避免"枚举期间修改集合"
        public void EndMatch(int winnerID = -1)
        {
            if (_isMatchEnding) return;
            _isMatchEnding = true;

            _matchStarted = false;   // 重置，允许重赛再次开赛

            IsMatchActive = false;
            OnGameOver?.Invoke(winnerID);
            GameManager.Instance.EndMatch();   // 通知 GameManager 切换到结算状态

            var snapshot = new List<FighterController>(GameManager.Instance.ActivePlayers);
            foreach (var f in snapshot)
            {
                if (f != null) GameManager.Instance.UnregisterFighter(f);
            }
            _registeredFighters.Clear();
        }

        // 【做什么】重生指定玩家（对外入口，当前由 OnPlayerOutOfBounds 走协程路径）
        public void RespawnPlayer(FighterController fighter)
        {
            Transform spawn = GetRandomSpawnPoint();
            fighter.Respawn(spawn.position);
        }

        // 【做什么】出界处理入口（由 BlastZone 触发）
        // 【流程】击杀扣命 → 若命数制且还有命则延迟重生
        public void OnPlayerOutOfBounds(FighterController fighter, BlastZone.Side side)
        {
            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match,
                    $"{fighter.name} 掉界 side={side} 状态={fighter.StateMachine.CurrentState} 命数={fighter.remainingStocks}");

            if (fighter.StateMachine.CurrentState == FighterState.Dead) return;

            fighter.Kill();   // 触发 OnKilled 事件 → 扣命 UI

            if (settings.matchMode == GameSettings.MatchMode.Stock && fighter.remainingStocks > 0)
            {
                StartCoroutine(RespawnAfterDelay(fighter));
            }
            else if (SmashDebug.IsOn(DebugChannel.Match))
            {
                SmashDebug.Log(DebugChannel.Match,
                    $"不重生: mode={settings.matchMode} 命数={fighter.remainingStocks}");
            }
        }

        #endregion

        #region 6. 比赛流程协程

        // 【做什么】比赛主流程：倒计时 → 循环检测结束条件 → 结束
        // 【阶段1】倒计时 countdownTime 秒
        // 【阶段2】每帧扣时间；时间制到点结束；命数制存活 ≤1 结束
        private IEnumerator MatchFlow()
        {
            // ===== 阶段1：开局倒计时 =====
            for (int i = (int)countdownTime; i > 0; i--)
            {
                if (SmashDebug.IsOn(DebugChannel.Match))
                    SmashDebug.Log(DebugChannel.Match, $"倒计时 {i}");
                yield return new WaitForSeconds(1f);
            }

            // ===== 阶段2：比赛进行 =====
            IsMatchActive = true;

            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match, "比赛开始");

            while (IsMatchActive)
            {
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
                    var allPlayers = GameManager.Instance.ActivePlayers;
                    var playersInGame = allPlayers.FindAll(p =>
                        p.StateMachine.CurrentState != FighterState.Dead
                        || (p.StateMachine.CurrentState == FighterState.Dead && p.remainingStocks > 0));

                    // 诊断日志：每秒打印一次玩家状态
                    if (Time.frameCount % 60 == 0 && SmashDebug.IsOn(DebugChannel.Match))
                    {
                        SmashDebug.Log(DebugChannel.Match,
                            $"[StockCheck] ActivePlayers={allPlayers.Count} playersInGame={playersInGame.Count}");
                        foreach (var p in allPlayers)
                            SmashDebug.Log(DebugChannel.Match,
                                $"  P{p.playerID}: 状态={p.StateMachine.CurrentState} 命数={p.remainingStocks}");
                    }

                    if (playersInGame.Count <= 1)
                    {
                        if (SmashDebug.IsOn(DebugChannel.Match))
                        {
                            SmashDebug.LogWarn(DebugChannel.Match, $"比赛结束！存活={playersInGame.Count}");
                            foreach (var p in playersInGame)
                                SmashDebug.LogWarn(DebugChannel.Match,
                                    $"  幸存者 P{p.playerID} 状态={p.StateMachine.CurrentState} 命数={p.remainingStocks}");
                        }

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

        // 【做什么】延迟重生：等 gameOverDelay 秒 → 重新注册 → 重绑事件 → Respawn → 同步 UI
        // 【注意】重新注册前先查 _registeredFighters，防止重复注册
        private IEnumerator RespawnAfterDelay(FighterController fighter)
        {
            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match, $"{fighter.name} 等待 {gameOverDelay}s 后重生");

            yield return new WaitForSeconds(gameOverDelay);

            // 只有未注册时才注册（防重复）
            if (!_registeredFighters.Contains(fighter))
            {
                GameManager.Instance.RegisterFighter(fighter);
                _registeredFighters.Add(fighter);
            }

            // 重新订阅事件（先退订再订阅）
            RebindFighterEvents(fighter);

            // 执行重生
            Transform spawn = GetRandomSpawnPoint();
            fighter.Respawn(spawn.position);

            // 重生后：同步 UI
            int id = fighter.playerID;

            if (id >= 0 && id < _stockDisplays.Length && _stockDisplays[id] != null)
            {
                _stockDisplays[id].SetStock(fighter.remainingStocks);

                if (SmashDebug.IsOn(DebugChannel.UI))
                    SmashDebug.Log(DebugChannel.UI, $"P{id} Stock → {fighter.remainingStocks}");
            }

            if (id >= 0 && id < _damageDisplays.Length && _damageDisplays[id] != null)
            {
                _damageDisplays[id].SetPercent(0);      // 伤害归零
                _damageDisplays[id].ResetDisplay();     // 关掉淡入淡出残留
            }

            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match, $"{fighter.name} 重生流程完成");
        }

        #endregion

        #region 7. 事件订阅管理

        // 【做什么】订阅一组斗士的 OnDamaged / OnKilled，并把委托缓存起来
        // 【为什么缓存】闭包每次 += 都是新委托实例，不缓存就退不掉
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

        // 【做什么】退订一组斗士的事件并从缓存移除
        private void UnsubscribeFighterEvents(List<FighterController> players)
        {
            foreach (var f in players)
            {
                if (f == null) continue;

                if (_damagedHandlers.TryGetValue(f, out var dmg)) { f.OnDamaged -= dmg; _damagedHandlers.Remove(f); }
                if (_killedHandlers.TryGetValue(f, out var kill)) { f.OnKilled -= kill; _killedHandlers.Remove(f); }
            }
        }

        // 【做什么】重绑单个斗士的事件（先退订旧委托，再订阅新的）
        private void RebindFighterEvents(FighterController f)
        {
            if (f == null) return;

            if (_damagedHandlers.TryGetValue(f, out var dmg)) f.OnDamaged -= dmg;
            if (_killedHandlers.TryGetValue(f, out var kill)) f.OnKilled -= kill;

            System.Action<float, FighterController> newDmg = (d, atk) => OnFighterDamagedHandler(f, d, atk);
            f.OnDamaged += newDmg;
            _damagedHandlers[f] = newDmg;

            System.Action<FighterController> newKill = victim => OnFighterKilledHandler(victim);
            f.OnKilled += newKill;
            _killedHandlers[f] = newKill;
        }

        #endregion

        #region 8. HUD

        // 【做什么】实例化 HUD 预设体、按约定路径绑定各显示组件、订阅所有事件
        // 【注意】Prefab 层级命名必须严格匹配，代码用 transform.Find 找
        private void SpawnAndBindHUD()
        {
            if (!hudPrefab)
            {
                Debug.LogError("[MatchManager] hudPrefab 未赋值！Inspector 请拖入 UI_HUD.prefab");
                return;
            }

            _hudInstance = Instantiate(hudPrefab);
            _hudInstance.name = "UI_HUD (Runtime)";

            // 初始化注册表
            _registeredFighters.Clear();
            var initPlayers = GameManager.Instance.ActivePlayers;
            foreach (var f in initPlayers) if (f != null) _registeredFighters.Add(f);

            // 自动找中间倒计时（Prefab 里叫 CenterTimer）
            centerTimerText = _hudInstance.transform.Find("CenterTimer")?.GetComponent<TextMeshProUGUI>();
            if (centerTimerText == null)
                Debug.LogWarning("[MatchManager] Prefab 里找不到 CenterTimer 对象或缺少 TextMeshProUGUI");

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

                // 注入 FighterId（UI 靠它认领自己该显示哪个玩家的数据）
                _damageDisplays[i].SetFighterId(i);
                _stockDisplays[i].SetFighterId(i);

                // 初始化命数/倒计时（从 GameSettings 读取）
                _stockDisplays[i].Init(settings.stockCount, settings.matchTimeSeconds, showTimer: false);
            }

            // 订阅自己的事件
            OnTimerUpdated += OnTimerUpdatedHandler;
            OnGameOver += OnGameOverHandler;

            // 订阅现有斗士事件
            SubscribeFighterEvents(GameManager.Instance.ActivePlayers);

            // 后续加入的斗士通过 GameStateChanged 重新订阅
            GameManager.Instance.OnGameStateChanged += OnGameStateChangedHandler;
        }

        // 【做什么】计时刷新：同步到各 StockDisplay 与中间倒计时文本
        private void OnTimerUpdatedHandler(float remaining)
        {
            foreach (var s in _stockDisplays) if (s != null) s.SyncTime(remaining);

            if (centerTimerText != null)
            {
                int m = Mathf.FloorToInt(remaining / 60f);
                int s = Mathf.FloorToInt(remaining % 60f);
                centerTimerText.text = $"{m:00}:{s:00}";
                centerTimerText.color = remaining <= 30f ? Color.red : Color.white;   // 最后 30s 变红
            }
        }

        // 【做什么】比赛结束：停表
        private void OnGameOverHandler(int winnerID)
        {
            foreach (var s in _stockDisplays) if (s != null) s.SetTimerRunning(false);
            if (centerTimerText != null) centerTimerText.text = "00:00";
        }

        // 【做什么】受伤 → 刷新对应玩家的伤害百分比
        private void OnFighterDamagedHandler(FighterController victim, float damage, FighterController attacker)
        {
            int id = victim.playerID;
            if (id < 0 || id >= _damageDisplays.Length || _damageDisplays[id] == null) return;

            _damageDisplays[id].SetPercent(Mathf.RoundToInt(victim.CurrentDamage));

            if (SmashDebug.IsOn(DebugChannel.UI))
                SmashDebug.Log(DebugChannel.UI, $"P{id} Damage → {victim.CurrentDamage}%");
        }

        // 【做什么】被击杀 → 刷新命数显示
        private void OnFighterKilledHandler(FighterController victim)
        {
            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match, $"收到击杀事件: {victim.name} 剩余命数={victim.remainingStocks}");

            int id = victim.playerID;
            if (settings.matchMode != GameSettings.MatchMode.Stock) return;
            if (id < 0 || id >= _stockDisplays.Length || _stockDisplays[id] == null) return;

            _stockDisplays[id].SetStock(victim.remainingStocks);

            if (SmashDebug.IsOn(DebugChannel.UI))
                SmashDebug.Log(DebugChannel.UI, $"P{id} Stock → {victim.remainingStocks}");
        }

        // 【做什么】GameManager 状态变化：进入 Battle 才真正开赛（针对"选完角色才开赛"的流程）
        private void OnGameStateChangedHandler(GameState newState)
        {
            if (newState != GameState.Battle) return;

            if (!_matchStarted)
            {
                StartMatch();
                _matchStarted = true;
            }

            var players = GameManager.Instance.ActivePlayers;
            foreach (var f in players) RebindFighterEvents(f);
        }

        #endregion

        #region 9. 私有工具

        // 【做什么】随机取一个出生点（未配置时退回自身位置）
        private Transform GetRandomSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return transform;
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }

        #endregion
    }
}
