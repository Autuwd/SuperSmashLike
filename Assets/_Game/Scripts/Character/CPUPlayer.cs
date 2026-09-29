using UnityEngine;
using SuperSmashLike.Managers;

// ============================================================
// CPUPlayer — CPU 决策层（M3 · D1 骨架）
// 职责：
//   1. 只做"决策"：决定此刻想干什么（接近/攻击/防御/…）
//   2. 每个决策周期产出一份只读感知快照（Perception）
//   3. 把决策翻译成 FighterController 的公共方法调用
// 架构位置：Character 层，与 FighterController 同层
// 关系：
//   FighterController（动作状态机）= 下层"能不能做"（物理/守卫/状态）
//   CPUPlayer        （决策状态机）= 上层"想做什么"（本文件）
//   【重点】两台状态机【不要合并】——合并后 AI 就能绕过
//     CanTransitionTo 转换表和 CanAct 守卫，直接造出非法状态
//
// 【重点】三层分工，看日志时别混：
//   Animator State  = 表现层（看起来在做什么）
//   FighterState     = 动作状态机（能不能做）
//   CPUState         = 决策状态机（想做什么） ← 本文件
//
// 【重点】铁律：只能调 FighterController 的 public 公共方法，
//   绝不直改 velocity / attackData / isShielding / Transform.position。
//   尤其禁止 StateMachine.TransitionTo()：它是 public，但那是后门，
//   绕过了转换表和守卫（详见交付说明 3.7 末条）
//
// 【对照】为什么用 SmashDebug 两个通道：
//   DebugChannel.State = 本状态机的进入/退出
//   DebugChannel.Input = "选了哪一招"，与人类玩家共用同一开关，便于对比
// ============================================================
namespace SuperSmashLike.Core
{
    // CPU 决策状态
    // 【注意】故意与 FighterState 错开命名（Strike/Guard/Evade/Hop，
    //   而不是 Attack/Defend/Dodge/Jump）：两台状态机都有 Attack/Jump 时，
    //   Debug 日志里一个 "Attack" 到底是哪台机器在说话，排查时极难分辨。
    //   错开命名 = Debug 面板里一眼看出是谁。
    // 【注意】本枚举的数字【不会】写进 Animator，所以顺序可随意改。
    //   这一点与 FighterState 相反（FighterStateMachine.cs:18-19 警告过不能动）。
    public enum CPUState
    {
        Idle,       // 待机——什么都不做，等下一个决策周期
        Approach,   // 接近——朝对手走，进入攻击距离转 Strike
        Strike,     // 攻击——出手一次后立刻退出（单次动作，不是持续状态）
        Retreat,    // 撤离——远离对手，用于被打后拉开距离
        Guard,      // 防御——举盾
        Evade,      // 闪避——【D1 占位】闪避能力项目里还不存在，见交付说明 3.8
        Hop,        // 跳跃/空中调整——起跳、空中后撤
    }

    // 一个决策周期内的只读感知快照
    // 【重点】刻意【不持有】对手引用：只存"我观察到的事实"。
    //   这样决策代码在物理上就没法去改对手的任何字段（防作弊 + 防耦合）。
    // 【对照】这相当于把 CQS 的 Q（Query，只读查询）侧和
    //   C（Command，写输入）侧分开 —— 本结构是纯 Q 侧。
    public readonly struct Perception
    {
        public readonly float Distance;           // 与对手的水平距离
        public readonly float Direction;          // 水平方向符号（+1 右 / -1 左）
        public readonly float HeightDifference;   // 对手相对我的高度差（正 = 对手更高）
        public readonly bool OpponentAttacking;  // 对手是否在攻击硬直中
        public readonly bool OpponentInStun;     // 对手是否被硬直，硬直=我的回合
        public readonly bool SelfStunned;        // 自己是否被硬直（不能行动）
        public readonly bool SelfGrounded;       // 自己是否在地面
        public readonly float SelfDamagePercent;  // 自身累计伤害百分比

        public Perception(float distance, float direction, float hightDifference, bool opponentAttacking,
                          bool opponentInStun ,bool selfStunned, bool selfGrounded, 
                          float selfDamagePercent)
        {
            Distance = distance;
            Direction = direction;
            HeightDifference = hightDifference;
            OpponentAttacking = opponentAttacking;
            OpponentInStun = opponentInStun;
            SelfStunned = selfStunned;
            SelfGrounded = selfGrounded;
            SelfDamagePercent = selfDamagePercent;
        }
    }

    [RequireComponent(typeof(FighterController))]
    public class CPUPlayer : MonoBehaviour
    {
        #region 1. 可调参数（Inspector）

        
        // 【重点】两个都不要改：
        //   ① 别改成每帧决策 —— AI 退化成"输入脚本"，玩家一眼看出机械感；
        //   ② 别改用 unscaledTime —— Hitstop 会设 Time.timeScale = 0
        //      （Hitbox.cs:137），用 unscaledTime 会让 AI 在顿帧期间继续决策，
        //      表现为"顿帧时 AI 还在动"，像穿模出手。
        [SerializeField] private float decisionInterval = 0.2f;     // 决策频率：每 0.2 秒决策一次（5Hz）
        [SerializeField] private float attackRange = 1.6f;          //攻击判定水平距离
        [SerializeField] private float attackHysteresis = 0.2f;     //滞回系数：0.2 = 在 attackRange 上下各留 20% 死区
        [SerializeField] private float attackCooldown = 0.3f;       //出招后间隙。AI 打完要停一下
        [SerializeField] private float highDamageThreshold = 80f;   //自身伤害超过此值就脱离战斗
        [SerializeField] private float safeDistance = 6f;           // 舒适距离
        
        [Header("攻击带垂直范围（实测校准：对手相对我的高度）")]
        [SerializeField] private float verticalRangeAbove = 1.46f;   // 对手在我上方多少仍能打中（实测）
        [SerializeField] private float verticalRangeBelow = 0.96f;   // 对手在我下方多少仍能打中（实测）
        #endregion

        #region 2. 运行时状态

        private FighterController me;              // 自己（缓存，避免每帧 GetComponent）
        private CPUState current = CPUState.Idle;
        private float decisionTimer;
        private FighterController foe;      // 对手引用。只读用，不用来改对手任何字段（Perception 的设计前提）
        private bool closingIn = true;      // 滞回锁存：true = 正在接近，false = 已在攻击带内
        private float strikeStartTime;      // 本次出招的起始时刻
        private float strikeReadyTime;      // 下次可出招的最早时刻
        private bool inBand;                // 本 tick 的攻击带判定结果
        private bool pendingShieldOff;      // 盾意图收，但被硬直吞了，等能收时补

        #endregion

        private void Awake()
        {
            me = GetComponent<FighterController>();

            // 本步唯一的"行为"：证明缓存成功，并让你在 Console 确认挂对了对象。
            // （括号里读 me.name 也顺带消掉了 CS0414，D2 就不需要这行了）
            if (SmashDebug.IsOn(DebugChannel.State))
                SmashDebug.Log(DebugChannel.State, $"CPUPlayer 就绪 | 目标={me.name}");
        }

        private void Update()
        {
            ResolveFoe();

            // 【重点】门禁必须照抄 InputManager 的双条件，再加 respawnLock。
            //   少了它，Title / Result 阶段会给已被 Reset 的角色写 MoveInput。
            if (!CanDecide())
            {
                decisionTimer = 0f;
                return;
            }

            TryFlushShieldOff();  //每Tick都试一次，防止硬直吞盾牌导致连盾

            decisionTimer -= Time.deltaTime;
            if (decisionTimer > 0f) return;
            decisionTimer = decisionInterval;

            Tick();
        }

        // 惰性解析对手
        // 【重点】三条理由，都指向"不能缓存"：
        //   ① Awake 顺序未定义，GameManager.Instance 可能还是 null
        //      （GameManager.cs:71 是唯一赋值点）
        //   ② ActivePlayers 由 MatchManager.StartMatch() 协程填充，
        //      Awake/Start 时列表大概率还是空的
        //   ③ 重生 / 重新注册会改列表内容，缓存的引用会过期
        //   → 所以每帧重找，找不到就跳过本 tick。宁可多算，不可算错。
        private void ResolveFoe()
        {
            if (me == null) return;

            if (GameManager.Instance == null)
            {
                WarnOnce("GameManager.Instance 为 null（场景无 GameManager，或它的 Awake 未跑）");
                return;
            }

            var players = GameManager.Instance.ActivePlayers;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i] != me && players[i].playerID != me.playerID)
                {
                    foe = players[i];
                    return;
                }
            }

            foe = null;
            WarnOnce($"ActivePlayers 共 {players.Count} 人，找不到 playerID != {me.playerID} 的对手（可能还没进战斗态）");
        }

        private bool warned;
        private void WarnOnce(string msg)
        {
            if (warned) return;
            warned = true;
            // 【注意】这里不能用 SmashDebug.LogWarn —— 它第一行也是 if (!IsOn(channel)) return;
            //   而 Settings 未注入时 IsOn 恒 false，会把这条警告一起吞掉。
            Debug.LogWarning($"[CPUPlayer] {msg}");
        }

        // 执行一次决策
        private void Tick()
        {
            // 【重点】感知快照每 tick 造一份新的（struct 值类型，天然不共享可变状态）
            Perception p = Sense();

            if (SmashDebug.IsOn(DebugChannel.Input))
                SmashDebug.Log(DebugChannel.Input,
                    $"感知 dist={p.Distance:F2} dir={p.Direction} 敌攻={p.OpponentAttacking} " +
                    $"敌硬直={p.OpponentInStun} 我硬直={p.SelfStunned} 伤害={p.SelfDamagePercent:F1}%");

            Decide(p);   // 只决定 current
            Act(p);      // 只按 current 执行
        }

        // 【重点】决策集中在这一个函数。
        //   早期版本把判断散在 7 个 TickXxx 里，会出现"状态 A 里判该不该去状态 B"
        //   的双向依赖 —— 改一个状态要同时改另一个，越改越乱。
        private void Decide(Perception p)
        {
            // P0 自己被硬直 → 什么都做不了，站桩
            if (p.SelfStunned) { Enter(CPUState.Idle); return; }

            inBand = InAttackBand(p);      // 【重点】一 tick 唯一调用点

            // P1 安全：对手正在出招且我在攻击带内 → 举盾
            if (p.OpponentAttacking && inBand && CanStartGuard()) { Enter(CPUState.Guard); return; }

            // P2 机会：对手硬直（我的窗口）且在攻击带内 → 出手
            if (!p.OpponentInStun && inBand && CanStartStrike()) { Enter(CPUState.Strike); return; }

            // P3 策略性回避：自身高伤害且离得还不够远 → 拉开
            if (p.SelfDamagePercent >= highDamageThreshold && p.Distance < safeDistance)
            { Enter(CPUState.Retreat); return; }

            // P4 默认 → 接近
            if (Time.time < strikeReadyTime) { Enter(CPUState.Idle); return; }
            Enter(CPUState.Approach);
        }

        // 执行：纯动作，零判断
        private void Act(Perception p)
        {
            switch (current)
            {
                case CPUState.Idle:
                    Stop();
                    break;

                case CPUState.Approach:
                    // 【注意】进带了却在冷却中 → 原地等。
                    //   不判断的话会直接撞进对手怀里（方向永远是"靠近"）。
                    if (inBand && !CanStartStrike()) { Stop(); break; }
                    me.MoveInput = new Vector2(p.Direction, 0f);
                    break;

                case CPUState.Retreat:
                    me.MoveInput = new Vector2(-p.Direction, 0f);
                    break;

                case CPUState.Strike:
                    TickStrike();
                    break;

                case CPUState.Guard:
                    break;   // 盾牌靠 Enter/Exit 成对维持，这里不做动作

                case CPUState.Evade:
                case CPUState.Hop:
                    break;
            }
        }

        private Perception Sense()
        {
            Vector3 a = me.transform.position;
            Vector3 b = foe.transform.position;

            // 【重点】只取 X 轴：这个游戏是 2D 平台格斗，Y 差是跳跃高度不是距离
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float dist = Mathf.Abs(dx);

            //   SelfStunned 只表示"被别人打"，必须排除【自己出招】【自己举盾】
            //   漏了 Attack → AI 在自己挥拳时认为被硬直 → 决策表第一条就把状态打断
            bool selfStunned = !me.StateMachine.CanAct()
                && me.StateMachine.CurrentState != FighterState.Shield
                && me.StateMachine.CurrentState != FighterState.Attack;

            // 【重点】IsInAir() 包含 Knockback，不能用来判"能否行动"
            FighterState fs = foe.StateMachine.CurrentState;
            bool oppAttacking = fs == FighterState.Attack;
            bool oppInStun = fs == FighterState.Hit || fs == FighterState.Stun || fs == FighterState.Knockback;

            return new Perception(
                dist,
                dx > 0f ? 1f : -1f,
                dy,
                oppAttacking,
                oppInStun,
                selfStunned,
                me.IsGrounded,
                me.CurrentDamage);
        }

        // 攻击带判定（带滞回）
        // 【重点】两个阈值夹出一个"死区"，区间内状态不变 —— 这就是滞回。
        //   没有死区时，dist 在 attackRange 附近抖动 → 每 0.2s 进出一次 Strike
        //   → 表现为一顿一顿的抽搐。
        private bool InAttackBand(Perception p)
        {
            float vRange = p.HeightDifference > 0f ? verticalRangeAbove : verticalRangeBelow;
            if (Mathf.Abs(p.HeightDifference) > vRange) return false;

            if (closingIn)
            {
                // 正在接近：必须够近才进带
                if (p.Distance <= attackRange * (1f - attackHysteresis))
                {
                    closingIn = false;
                    return true;
                }
                return false;
            }
            else
            {
                // 已在带内：必须退得够远才出带
                if (p.Distance >= attackRange * (1f + attackHysteresis))
                {
                    closingIn = true;
                    return false;
                }
                return true;
            }
        }

        #region 3. 状态切换

        private void Enter(CPUState next)
        {
            if (current == next) return;
            Exit(current);
            current = next;
            EnterState(next);
        }

        // 【重点】必须区分"进入钩子"和"每 tick 钩子"。
        //   举例：Hop 态进入时只该起跳一次（TryJump 写进 EnterState）；
        //   若塞进 TickHop，就会每 0.2 秒起跳一次 → 连跳，直接击飞自己出界。
        private void EnterState(CPUState state)
        {
            // 【重点】切状态时必须显式清零 MoveInput。
            //   决策 5Hz / 执行 60Hz，MoveInput 是持久值：
            //   "不写" ≠ "停下"，会一直沿用上一次的值 → 停不下来。
            me.MoveInput = Vector2.zero;

            // 【重点】出招写在【进入钩子】，绝不写进 TickStrike。
            //   决策 5Hz vs jab1 总时长 ~0.4s → 放进 TickStrike 会每 0.2s 重出一次。
            if (state == CPUState.Strike) BeginStrike();
            if (state == CPUState.Guard)
            {
                me.SetShielding(true);
                pendingShieldOff = false;
            }

            if (SmashDebug.IsOn(DebugChannel.State))
                SmashDebug.Log(DebugChannel.State, $"CPU 决策 → {state}");
        }

        private void Exit(CPUState state)
        {
            if (state != CPUState.Guard) return;
            pendingShieldOff = true;      // 先记意图
            TryFlushShieldOff();          // 试一次
        }

        private void TryFlushShieldOff()
        {
            if (!pendingShieldOff) return;
            me.SetShielding(false);
            if (me.isShielding) return;   // 还是 true → 硬直吞了，保持意图下轮再试
            pendingShieldOff = false;      // 成功，清意图
        }

        // 【D1 选招表】只给 jab1，够验证"能打"。
        //   InputManager 的选招表是 private，AI 复用不了 —— 这是有意的：
        //   人类靠按键表达意图，AI 靠感知，两者本就该走不同入口。
        //   第 5 步有了决策优先级再扩到 jab2/jab3/tiltSide。
        private void BeginStrike()
        {
            AttackData atk = me.fighterData.jab1;
            strikeStartTime = Time.time;
            strikeReadyTime = Time.time + atk.TotalDuration + attackCooldown;
            me.TryAttack(atk);

            if (SmashDebug.IsOn(DebugChannel.Combat))
                SmashDebug.Log(DebugChannel.Combat,
                    $"CPU 出招 jab1 前摇{atk.startupTime:F2}/判定{atk.activeTime:F2}/后摇{atk.recoveryTime:F2} 合计{atk.TotalDuration:F2}s");
        }

        // 出招预检
        // 【重点】TryAttack 返回 void，且内部多处【静默 return】（:851-852），
        //   调用方拿不到任何反馈 → 失败只能自己预检，否则会对着硬直空气挥空。
        //   这里逐条对齐 TryAttack 的拒绝条件：
        //   ① CanAct()  → 一网打尽 Attack/Shield/Dead/Hit/Stun/Knockback
        //   ② isInKnockback → 独立标志：状态可能已落回 Fall，人还在击飞
        private bool CanStartStrike()
        {
            if (me == null || me.fighterData == null || me.fighterData.jab1 == null) return false;
            if (Time.time < strikeReadyTime) return false;
            if (!me.StateMachine.CanAct()) return false;
            if (me.isInKnockback) return false;
            return true;
        }

        // 战斗门禁：与 InputManager.cs 的 playable 判定保持一致
        private bool CanDecide()
        {
            if (GameManager.Instance == null) return false;
            if (GameManager.Instance.CurrentGameState != GameState.Battle) return false;
            if (MatchManager.Instance == null || !MatchManager.Instance.IsMatchActive) return false;
            if (me == null || foe == null) return false;
            return !me.IsRespawnLocked;
        }

        // 举盾预检：与 FighterController.SetShielding 的守卫名单一一对齐
        private bool CanStartGuard()
        {
            if (me == null) return false;
            FighterState s = me.StateMachine.CurrentState;
            if (s == FighterState.Knockback || s == FighterState.Dead || s == FighterState.Stun
                || s == FighterState.ShieldStun || s == FighterState.Grab || s == FighterState.Grabbed)
                return false;

            return true;
        }

        #endregion

        #region 4. 各状态行为（D1 全部占位，D2 填）
        private void TickStrike()
        {
            AttackData atk = me.fighterData.jab1;
            if (Time.time - strikeStartTime < atk.TotalDuration) return;   // 招式进行中，不写任何输入

            // 【重点】回 Approach 而不是回 Idle：
            //   Approach 会重跑 InAttackBand —— 滞回的【出带】分支在这里第一次被走到。
            //   上一步 TickStrike 是空的，AI 进得去出不来，滞回只能验一半。
            Enter(CPUState.Approach);
        }

        private void Stop() { me.MoveInput = Vector2.zero; }

        #endregion
    }
}
