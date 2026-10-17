using SuperSmashLike.Managers;
using SuperSmashLike.Stage;
using UnityEngine;
using UnityEngine.Profiling;

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
        Evade,      // 闪避——起跳 + 反向输入（引擎无 dodge API，只能这样组合，见 CanStartEvade）
        Hop,        // 跳跃/空中调整——台边求生：朝台心走，掉到台面以下就起跳抢高度
        Flank,      // 绕后——跳越过对手，从背后进攻（D5：决策丰富化）
    }

    // AI 难度档位（D2-①）
    // 【注意】显式给值 0/1/2/3，不靠声明顺序 —— 以后往中间插档位，
    //   不会让 Inspector 里已选好的难度静默变成另一档。
    public enum Difficulty 
    { 
        Easy = 0, 
        Normal = 1, 
        Hard = 2, 
        Expert = 3 
    }

    // 【新增 · D5】出招意图：Decide 决定"这一拳是为什么打的"，ChooseStrike 据此挑槽位。
    //   【重点】战略（要不要打）与选招（用哪一招）分离 —— 改难度只需动 Decide/Profile，
    //   不必碰选招表；反之换招式表也不必碰决策链。
    public enum StrikeIntent
    {
        Neutral,    // 常规对拼：双方干净时的轻快起手
        Chase,      // 追击：对手被击飞/硬直，压上去收残
        Punish,     // 择时惩罚：读到对手攻击后摇，抓最重的一拳
        Pressure,   // 压制：对手举盾时打盾（破盾值优先）
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
        // 【新增 · D2】边缘求生（D2-③）。刻意也放进快照而不是让 Decide 现算：
        //   "我离台边多近"是【观察到的事实】，不是决策结果。放进快照后
        //   Decide/Act 只读 p.SelfNearEdge，就不会出现"状态 A 里判该不该去状态 B"
        //   的双向依赖（同文件下方"决策集中在这一个函数"铁律）。
        public readonly bool SelfNearEdge;       // 自己是否已进入台边危险带
        public readonly float SelfTowardCenterX; // 该往台心走的方向（+1 右 / -1 左），不危险时为 0

        // 【新增 · D5】决策丰富化的扩展感知（读招/绕后/压制/终结判断所需）。
        //   同 D2 的 SelfNearEdge 一样，全部是"观察到的事实"（纯只读），
        //   不是决策结果 —— 保证 Decide 里不会出现"状态 A 判该不该去状态 B"的双向依赖。
        public readonly bool OpponentRecovering;    // 对手处于【攻击后摇】窗口（我的惩罚时机）
        public readonly bool OpponentShielding;     // 对手正在举盾（压制/抓取机会）
        public readonly bool OpponentGrounded;      // 对手是否在地面（空对地/对空选招用）
        public readonly float OpponentDamagePercent; // 对手累计伤害百分比（终结技判定用）
        public readonly bool OpponentNearEdge;      // 对手靠近台边/台面之下（施压与击杀机会）
        public readonly bool IAmBehindOpponent;     // 我是否站在对手背后（相对其朝向）

        public Perception(float distance, float direction, float hightDifference, bool opponentAttacking,
                          bool opponentInStun ,bool selfStunned, bool selfGrounded,
                          float selfDamagePercent, bool selfNearEdge, float selfTowardCenterX,
                          bool opponentRecovering, bool opponentShielding, bool opponentGrounded,
                          float opponentDamagePercent, bool opponentNearEdge, bool iAmBehindOpponent)
        {
            Distance = distance;
            Direction = direction;
            HeightDifference = hightDifference;
            OpponentAttacking = opponentAttacking;
            OpponentInStun = opponentInStun;
            SelfStunned = selfStunned;
            SelfGrounded = selfGrounded;
            SelfDamagePercent = selfDamagePercent;
            SelfNearEdge = selfNearEdge;
            SelfTowardCenterX = selfTowardCenterX;
            OpponentRecovering = opponentRecovering;
            OpponentShielding = opponentShielding;
            OpponentGrounded = opponentGrounded;
            OpponentDamagePercent = opponentDamagePercent;
            OpponentNearEdge = opponentNearEdge;
            IAmBehindOpponent = iAmBehindOpponent;
        }
    }

    [System.Serializable]
    public struct AIProfile
    {
        [Tooltip("反应延迟：决策间隔秒数，越大越迟钝")]
        public float decisionInterval;
        [Tooltip("出招后间隙秒数，越小越激进")]
        public float attackCooldown;
        [Tooltip("攻击欲望 0~1：进带后不一定要出手")]
        [Range(0f, 1f)] public float strikeChance;
        [Tooltip("防御倾向 0~1：对手出招时不一定举盾")]
        [Range(0f, 1f)] public float guardChance;
        [Tooltip("闪避概率 0~1")]
        [Range(0f, 1f)] public float evadeChance;

        // ---- 【新增 · D5】行为门控：难度差 = 行为差，不是频率差 ----
        //   低难度这些门≈0：放过破绽、干等对手收盾、被后摇惩罚也不会抓、从不绕后；
        //   高难度几乎全开：会追击收残、会读后摇惩罚、会压盾、会绕后。
        [Tooltip("追击概率 0~1：对手被硬直/被击飞时继续压上（低难度≈0 = 放过破绽）")]
        [Range(0f, 1f)] public float chaseChance;
        [Tooltip("绕后概率 0~1：近身时偶尔跳越过对手，从背后进攻")]
        [Range(0f, 1f)] public float flankChance;
        [Tooltip("压制概率 0~1：对手举盾时持续打盾（低难度 = 干等对手收盾）")]
        [Range(0f, 1f)] public float pressureChance;
        [Tooltip("择时/读招概率 0~1：越高越会读对手后摇抓惩罚、且不胡乱对拼")]
        [Range(0f, 1f)] public float punishChance;
        [Tooltip("终结倾向 0~1：对手伤害高时改用击退最强的 smash 收头")]
        [Range(0f, 1f)] public float finisherBias;
    }

    [RequireComponent(typeof(FighterController))]
    public class CPUPlayer : MonoBehaviour
    {
        #region 1. 可调参数（Inspector）


        // 【重点】决策间隔 / 出招间隙的唯一真源 = AIProfile（见下方 profile 属性）。
        //   不要再留独立的 decisionInterval / attackCooldown —— 两份真源会静默打架：
        //   改 Inspector 那个不生效、或改 profile 那个被覆盖，而两边"看起来"都像生效了。
        //   默认 Hard 档 = 0.2s / 0.3s，与删掉的那两个默认值完全一致 → 行为零变化。
        [SerializeField] private Difficulty difficulty = Difficulty.Hard;
        [Tooltip("勾选后，进入战斗时用 GameSettings.cpuDifficulty（规则选择界面）覆盖上面的档位")]
        [SerializeField] private bool difficultyFromSettings = true;
        [SerializeField] private float attackRange = 1.6f;          // 攻击判定水平距离
        [SerializeField] private float attackHysteresis = 0.2f;     //滞回系数：0.2 = 在 attackRange 上下各留 20% 死区
        [SerializeField] private float highDamageThreshold = 80f;   //自身伤害超过此值就脱离战斗
        [SerializeField] private float safeDistance = 6f;           // 舒适距离

        [Header("调试开关")]
        [Tooltip("是否允许 CPU 主动交战（进攻+防御）。关掉后 AI 只接近/撤离，不出招不出盾，方便测试人类一侧（投掷/受击/KO）")]
        [SerializeField] private bool attackEnabled = true;

        [Tooltip("完全冻结 CPU：不做任何决策、不写 MoveInput。测试人类一侧时最干净")]
        [SerializeField] private bool aiFrozen = false;

        [Header("攻击带垂直范围（实测校准：对手相对我的高度）")]
        [SerializeField] private float verticalRangeAbove = 1.46f;   // 对手在我上方多少仍能打中（实测）
        [SerializeField] private float verticalRangeBelow = 0.96f;   // 对手在我下方多少仍能打中（实测）

        [Header("边缘求生（D2-③）")]
        [Tooltip("进入 Hop 的台边危险带宽度：距左右安全界或台面高度小于此值就判危险")]
        [SerializeField] private float edgeDangerMargin = 3f;
        [Tooltip("退出 Hop 的安全带宽度。【注意】必须【大于】edgeDangerMargin，否则会在边界反复进出（无滞回 = 抽搐）")]
        [SerializeField] private float edgeSafeMargin = 7f;
        [Tooltip("Hop 最长持续秒数，超时强制回 Approach —— 防止 Hop 变成永久状态把 AI 卡死")]
        [SerializeField] private float hopMaxDuration = 2f;

        [Header("闪避（D2-④）")]
        [Tooltip("闪避持续秒数：这段时间按住远离对手的方向")]
        [SerializeField] private float evadeDuration = 0.25f;

        [Header("远程 / 近战（D2-⑤）")]
        [Tooltip("远程角色停步距离：比这个近就不再靠近，改为原地放投射物或出 jab")]
        [SerializeField] private float rangedHoldDistance = 4.5f;
        [Tooltip("远程角色启用投射物技的最小距离。【注意】应【小于】rangedHoldDistance，两者之间形成【停下风筝】的区间")]
        [SerializeField] private float rangedFireMinDistance = 3.5f;

        [Header("行为丰富化（D5）")]
        [Tooltip("绕后（Flank）最长持续秒数，超时强制回 Approach —— 与 hopMaxDuration 同一兜底思路")]
        [SerializeField] private float flankMaxDuration = 1.2f;
        [Tooltip("允许发起绕后的最大水平距离：只有近身才跳越，太远跳不过去")]
        [SerializeField] private float flankTriggerDistance = 3f;
        [Tooltip("终结技判定阈值：对手伤害达到此值 + finisherBias 命中 → 改用击退最强的横 smash 收头")]
        [SerializeField] private float finisherDamagePercent = 80f;
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

        // 【M6-D5 预留】有效档位 = 难度档 × 角色调参档（FighterData.aiTuning）。
        // 【重点】难度可被 GameSettings 在运行时覆盖 → 不缓存，每次按需合成，避免失效同步问题。
        //   aiTuning 全默认 1.0（或 null）→ 返回值与 Profiles[difficulty] 完全一致，行为零变化。
        private AIProfile profile
        {
            get
            {
                AIProfile baseProfile = Profiles[(int)difficulty];
                if (me == null || me.fighterData == null || me.fighterData.aiTuning == null)
                    return baseProfile;   // 未配置调参 → 与旧版行为完全一致
                return ApplyTuning(baseProfile, me.fighterData.aiTuning);
            }
        }

        // 【M6-D5 预留】把角色调参倍率叠加到难度档上（框架预留：六角色当前全默认 = 无差异）。
        //   chance 类钳制 [0,1]；时间类钳制下限 0.05s。
        private static AIProfile ApplyTuning(AIProfile b, FighterData.AITuning t)
        {
            return new AIProfile
            {
                decisionInterval = Mathf.Max(0.05f, b.decisionInterval * t.decisionIntervalMul),
                attackCooldown   = Mathf.Max(0.05f, b.attackCooldown * t.attackCooldownMul),
                strikeChance     = Clamp01(b.strikeChance * t.strikeChanceMul),
                guardChance      = Clamp01(b.guardChance * t.guardChanceMul),
                evadeChance      = Clamp01(b.evadeChance * t.evadeChanceMul),
                chaseChance      = Clamp01(b.chaseChance * t.chaseChanceMul),
                flankChance      = Clamp01(b.flankChance * t.flankChanceMul),
                pressureChance   = Clamp01(b.pressureChance * t.pressureChanceMul),
                punishChance     = Clamp01(b.punishChance * t.punishChanceMul),
                finisherBias     = Clamp01(b.finisherBias * t.finisherBiasMul),
            };
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // ---- 出招选招（D2-⑤ 远程/近战）----
        // 【重点】以前 Strike 硬编码 fighterData.jab1，远程角色永远只会贴身挥拳。
        //   现在把"这次出哪一招"提前选好存进 pendingStrike，
        //   BeginStrike / TickStrike / 出招冻结判定全部只读它 —— 单一真源。
        private AttackData pendingStrike;        // 本次 Strike 选定的招式（jab1 或投射物特殊技）
        private AttackData myRangedSpecial;      // 本角色的投射物特殊技（仅 Ranged 定位才解析；null = 无）
        private bool isRangedFighter;            // 是否远程定位（= FighterData.aiArchetype == Ranged）
        // 【新增 · D2-⑤】远程射击带的滞回状态（语义同 InAttackBand 的 closingIn：
        //   true = 已在带外侧，需要靠近才进带）。与 closingIn 分开是因为两条带
        //   阈值不同，共用一个状态会互相污染出带判据。
        private bool rangedClosingIn;

        // ---- 边缘求生（D2-③）----
        private bool safeBoundsResolved;  // 安全区是否已从 BlastZone 解析成功
        private int boundsRetryCount;     // 解析失败的重试次数（防止每帧 FindObjectsOfType 拖死）
        private float safeMinX, safeMaxX, safeMinY;  // 舞台安全区（左右 BlastZone 内壁 + 底部 BlastZone 内壁）
        private float stageCenterX;       // 舞台中心 X（左右安全界的中点）
        private float hopStartTime;        // 本次 Hop 的起始时刻（超时兜底用）
        private float evadeStartTime;      // 本次 Evade 的起始时刻

        // ---- 【新增 · D5】行为丰富化运行时状态 ----
        private StrikeIntent pendingIntent = StrikeIntent.Neutral;  // 本次出招的意图（BeginStrike 据此计数）
        private float flankStartTime;      // 本次 Flank 的起始时刻（超时兜底用）
        private float flankSide0;          // 发起绕后时对手所在的侧向（+1 右 / -1 左）；方向翻转 = 已越过
        private float flankCooldownUntil;  // 下次可绕后的最早时刻（避免连续跳越抽搐）
        private bool lastPickWasFinisher;  // 本次选招是否走了"终结技"分支（计数用）

        // 【注意】只缓存【一个】方向量。指向台心的方向不在这里缓存 ——
        //   它已经由 Perception.SelfTowardCenterX 提供了（TickHop 读 p 即可）。
        //   留两份真源就是本文件上方警告过的那种"静默打架"：改一处忘另一处，
        //   表现为 AI 朝错误方向跑且极难复现。故此处只留 curDirection。
        private float curDirection;       // 本 tick 指向对手的方向（+1 右 / -1 左）

        // 四档预设（D2-① 全部内容 + D5 行为门控）
        // 【D5·难度设计】前 5 项是"节奏/频率"（D2 已有），后 5 项是"行为种类"——
        //   简单档后 5 项几乎为 0：不追击、不绕后、不压盾、不读招、不终结；
        //   专家档全部拉满：肉眼可辨"迟钝 vs 积极"，且差异不来自频率。
        private static readonly AIProfile[] Profiles =
        {
            new AIProfile { decisionInterval = 0.50f, attackCooldown = 0.80f, strikeChance = 0.35f, guardChance = 0.15f, evadeChance = 0.05f,
                            chaseChance = 0.00f, flankChance = 0.00f, pressureChance = 0.05f, punishChance = 0.00f, finisherBias = 0.00f }, // 简单
            new AIProfile { decisionInterval = 0.32f, attackCooldown = 0.50f, strikeChance = 0.60f, guardChance = 0.35f, evadeChance = 0.15f,
                            chaseChance = 0.35f, flankChance = 0.10f, pressureChance = 0.30f, punishChance = 0.30f, finisherBias = 0.30f }, // 普通
            new AIProfile { decisionInterval = 0.20f, attackCooldown = 0.30f, strikeChance = 0.80f, guardChance = 0.60f, evadeChance = 0.30f,
                            chaseChance = 0.65f, flankChance = 0.30f, pressureChance = 0.60f, punishChance = 0.60f, finisherBias = 0.55f }, // 困难
            new AIProfile { decisionInterval = 0.12f, attackCooldown = 0.18f, strikeChance = 0.95f, guardChance = 0.80f, evadeChance = 0.50f,
                            chaseChance = 0.90f, flankChance = 0.55f, pressureChance = 0.90f, punishChance = 0.90f, finisherBias = 0.80f }, // 专家
        };

        // ---- M6-D5 行为计数（只读公开，供无头冒烟取证"难度差 ≠ 频率差"）----
        //   每局开打清零（见 HandleStateChanged_ApplyDifficulty）。
        public int statChase { get; private set; }      // 追击出招次数
        public int statPunish { get; private set; }     // 择时惩罚出招次数
        public int statPressure { get; private set; }   // 压制打盾出招次数
        public int statFlank { get; private set; }      // 绕后发起次数
        public int statFinisher { get; private set; }   // 终结技（smash 收头）次数
        public int statNeutral { get; private set; }    // 常规对拼出招次数

        // 【注意】public 是刻意的：行为计数要在【不打扰对局】的前提下被测试/冒烟脚本清零取样
        //   （D5 验收证据：Easy 与 Expert 的行为计数对比，见 完整学习计划.md D5 行）。
        public void ResetStats()
        {
            statChase = 0;
            statPunish = 0;
            statPressure = 0;
            statFlank = 0;
            statFinisher = 0;
            statNeutral = 0;
        }

        #endregion

        private void Awake()
        {
            me = GetComponent<FighterController>();

            ResolveLoadout();
            EnsureSafeBounds();

            // 本步唯一的"行为"：证明缓存成功，并让你在 Console 确认挂对了对象。
            // （括号里读 me.name 也顺带消掉了 CS0414，D2 就不需要这行了）
            if (SmashDebug.IsOn(DebugChannel.State))
                SmashDebug.Log(DebugChannel.State, $"CPUPlayer 就绪 | 目标={me.name} | 类型={(isRangedFighter ? "远程" : "近战")}");
        }

        // M4-D3：订阅战斗开始，用规则选择界面设定的难度覆盖档位
        private void Start()
        {
            ApplyDifficultyFromSettings();
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged += HandleStateChanged_ApplyDifficulty;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged -= HandleStateChanged_ApplyDifficulty;
        }

        private void HandleStateChanged_ApplyDifficulty(GameState state)
        {
            if (state == GameState.Battle)
            {
                ApplyDifficultyFromSettings();
                ResetStats();   // 【新增 · D5】每局开打清零行为计数（无头冒烟据此对比难度行为差）
            }
        }

        // 【做什么】从全局规则读取 CPU 难度（0=简单 … 3=疯狂）
        private void ApplyDifficultyFromSettings()
        {
            if (!difficultyFromSettings) return;
            var gs = (GameManager.Instance != null) ? GameManager.Instance.gameSettings : null;
            if (gs == null) return;
            difficulty = (Difficulty)Mathf.Clamp(gs.cpuDifficulty, 0, 3);
        }

        private void Update()
        {
            ResolveFoe();

            // 【调试】完全冻结：连决策都不跑。
            //   必须显式清 MoveInput —— 冻结前若停在 Approach，MoveInput 里还留着方向，
            //   人为"停住决策"但角色照走（决策 5Hz / 物理 60Hz，不写 ≠ 停下）。
            if (aiFrozen)
            {
                // 冻结前正在举盾就先收盾，否则它会一直举着盾挡住你的抓取测试
                if (current == CPUState.Guard) { Exit(CPUState.Guard); Enter(CPUState.Idle); }
                TryFlushShieldOff();
                me.MoveInput = Vector2.zero;
                return;
            }

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
            decisionTimer = profile.decisionInterval;

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
            // 【新增】安全区惰性重试：Awake 时场景可能还没生成 BlastZone
            //   （对象激活顺序未定义），这里每 tick 补一次解析机会。
            //   内部有次数上限，不会变成每帧 FindObjectsOfType。
            if (!safeBoundsResolved) EnsureSafeBounds();

            // 【重点】感知快照每 tick 造一份新的（struct 值类型，天然不共享可变状态）
            Perception p = Sense();

            if (SmashDebug.IsOn(DebugChannel.Input))
                SmashDebug.Log(DebugChannel.Input,
                    $"感知 dist={p.Distance:F2} dir={p.Direction} 敌攻={p.OpponentAttacking} " +
                    $"敌硬直={p.OpponentInStun} 我硬直={p.SelfStunned} 伤害={p.SelfDamagePercent:F1}% " +
                    $"台边={(p.SelfNearEdge ? "危险→" + p.SelfTowardCenterX : "安全")}");

            Decide(p);   // 只决定 current
            Act(p);      // 只按 current 执行
        }

        // 【重点】决策集中在这一个函数。
        //   早期版本把判断散在 7 个 TickXxx 里，会出现"状态 A 里判该不该去状态 B"
        //   的双向依赖 —— 改一个状态要同时改另一个，越改越乱。
        private void Decide(Perception p)
        {
            // 【重点】出招进行中 → 决策机冻结，让 Act 走 TickStrike 把出招生命周期走完。
            //   不加这条：P4 的冷却判断会在下一个 tick 把 Strike 踢成 Idle，
            //   TickStrike 永远是死代码，出招节奏退化成固定 0.5s 节拍器。
            // 【注意】读 pendingStrike 而不是硬编码 jab1 —— 远程角色出的是投射物技，
            //   总时长不同，冻结窗口必须跟着选中的那一招走。
            if (current == CPUState.Strike)
            {
                AttackData atk = pendingStrike;
                if (atk != null && Time.time - strikeStartTime < atk.TotalDuration)
                { return; }
            }

            if (p.SelfStunned) 
            { 
                Enter(CPUState.Idle); 
                return; 
            }

            inBand = InAttackBand(p);

            // 【新增 · D2-⑤】远程射击带要和近战带【分开】判，不能合并成一个 inBand：
            //   近战带外沿 1.6*1.2=1.92，而投射物技门槛是 3.5，两者互斥。
            //   只留 inBand 的话远程角色永远走不到 ChooseStrike 的投射物分支。
            bool inRangedBand = InRangedBand(p);

            // 【新增】目标已离开台面 → 不追击，优先回台心/自保
            if (safeBoundsResolved && foe != null)
            {
                Vector3 t = foe.transform.position;
                FighterState tfs = foe.StateMachine.CurrentState;
                bool targetOffStage = t.y <= safeMinY + edgeDangerMargin + 1.5f
                                    || t.x <= safeMinX + edgeDangerMargin + 0.5f
                                    || t.x >= safeMaxX - edgeDangerMargin - 0.5f
                                    || tfs == FighterState.Dead;
                if (targetOffStage && !p.SelfNearEdge)
                {
                    Enter(CPUState.Hop);
                    return;
                }
            }

            // 【新增 · D2-③】边缘求生优先级【最高】，压在所有战斗反应之上。
            //   理由：Retreat 会往远离对手的方向走、Approach 会往对手方向走，
            //   当两者方向恰好都指着台外时，这两个状态就是【自杀指令】。
            //   站在台边时先回台心，比挡一下、挥一拳重要得多。
            // 【注意】故意不受 attackEnabled 约束：调试开关关掉进攻不代表允许掉出界。
            // 边缘求生：不仅自己靠边危险时回台心；也强制在底部极靠近时尝试回归（更容易跳返场）
            bool nearBottom = me.transform.position.y <= safeMinY + edgeDangerMargin + 0.8f;
            if (p.SelfNearEdge || nearBottom)
            {
                Enter(CPUState.Hop);
                return;
            }

            // 【新增 · D2-④】闪避。排在举盾【之前】是刻意的：
            //   闪避是"这一下我挡不住"的反应，比举盾更强；两者的概率互相独立，
            //   所以专家档（evade 0.50 / guard 0.80）的实际分布是
            //   闪避 50% + 举盾 40% + 什么也不做 10%，三种反应都有，不会退化成只会举盾。
            if (attackEnabled && p.OpponentAttacking && inBand
                && CanJumpNow() && Rand01() < profile.evadeChance)
            {
                Enter(CPUState.Evade);
                return;
            }

            if (attackEnabled && p.OpponentAttacking && inBand && CanStartGuard() && Rand01() < profile.guardChance) 
            { 
                Enter(CPUState.Guard); 
                return; 
            }

            // ================= 【新增 · D5】行为丰富化决策链 =================
            // 【重点】顺序即优先级：退避反应 > 绕后 > 意图出招 > 保底。
            //   为什么"退避反应"排在出招之前：对手招式已经压到脸上时，
            //   先拉开比硬拼安全（硬拼由下面的 Neutral 意图兜底，两者概率独立）。
            //   为什么"高伤撤退"降到出招之后：撤退只在"对手没有便宜可占"时才合理；
            //   对手正在收招（我的惩罚窗口）时，打一拳比跑更安全。

            // 【读招】只有档位命中 punishChance 时，AI 才"看得懂"对手的窗口。
            //   低难度 punishChance=0 → 永远 readsThreat=false → 不惩罚、不闪避、不择时。
            //   【重点】这就是"迟钝 vs 积极"的差异来源，不是攻击频率差异。
            bool readsThreat = Rand01() < profile.punishChance;

            // ---- ① 退避反应：读到对手压制手段 → 主动拉开（而不是只会举盾）----
            if (attackEnabled && p.OpponentAttacking && inBand && readsThreat && Rand01() < 0.5f)
            {
                Enter(CPUState.Retreat);
                return;
            }

            // ---- ② 绕后：近身时跳越过对手，从背后进攻 ----
            //   条件刻意苛刻：对手靠台边不绕（会把自己送出台）、我在台边不绕、
            //   对手硬直/出招中不绕（直接打更划算）、冷却未到不绕（防连续跳越抽搐）。
            // 【重点】D5 修正：这里【不能】再要求 inBand。inBand 的外沿只有
            //   attackRange*(1+attackHysteresis)=1.6*1.2=1.92，而 flankTriggerDistance=3.0。
            //   一旦同时要求两者，3.0 就成了【永远不生效的死参数】，绕后只能在贴脸1.92内触发。
            //   实测（双 CPU 对打 22s）flank 恒为 0，正是这条冗余门造成的。
            //   距离交给 flankTriggerDistance 单独把关；高度差单独把关（防隔着一层楼跳越）。
            bool canFlank = attackEnabled && p.SelfGrounded
                            && p.Distance <= flankTriggerDistance
                            && Mathf.Abs(p.HeightDifference) <= verticalRangeAbove
                            && !p.OpponentInStun && !p.OpponentAttacking
                            && !p.IAmBehindOpponent && !p.OpponentNearEdge && !p.SelfNearEdge
                            && Time.time >= flankCooldownUntil && CanJumpNow();
            if (canFlank && Rand01() < profile.flankChance)
            {
                Enter(CPUState.Flank);
                return;
            }

            // ---- ③ 认意图：按优先级定意图（Pressure > Punish > Chase > Neutral）----
            // 【重点】D5 修正：意图判定必须与"够不够得着"【解耦】。
            //   旧版把整条意图链塞在 (inBand || inRangedBand) 门内 → 而 Chase/Punish
            //   的触发前提恰恰是"对手被硬直/被击飞"，那正是【被击飞出台（>1.92）】的时刻
            //   → 那两个分支永远跑不到。实测双 CPU 对打 22s（对手累计吃 88 伤害、
            //   反复进入 Hit/Knockback）chase/punish 全为 0，只有中性兜底在涨。
            //   现在：先看对手状态定意图，再问距离够不够打。
            StrikeIntent intent = StrikeIntent.Neutral;
            bool hasIntent = false;

            if (p.OpponentShielding && Rand01() < profile.pressureChance)
            {
                intent = StrikeIntent.Pressure;          // 对手举盾 → 打盾
                hasIntent = true;
            }
            else if ((p.OpponentRecovering || p.OpponentInStun) && readsThreat
                     && Rand01() < profile.punishChance)
            {
                intent = StrikeIntent.Punish;            // 对手收招/硬直 → 抓惩罚
                hasIntent = true;
            }
            else if (p.OpponentInStun || p.OpponentRecovering)
            {
                if (Rand01() < profile.chaseChance)
                {
                    intent = StrikeIntent.Chase;         // 对手被击飞 → 追击收残
                    hasIntent = true;
                }
            }
            else if (!p.OpponentAttacking || !readsThreat)
            {
                intent = StrikeIntent.Neutral;           // 双方干净 → 常规对拼
                hasIntent = true;
            }

            // ---- ③b 够得着就打 ----
            //   【重点】门槛必须是 (inBand || inRangedBand)：inBand 外沿仅 1.92，
            //   而 rangedFireMinDistance=3.5，两者互斥；只判 inBand 时
            //   ChooseStrike 的远程分支是【永远为假的死代码】，远程角色从不射击。
            bool inStrikeRange = inBand || inRangedBand;
            if (hasIntent && attackEnabled && inStrikeRange)
            {
                AttackData chosen = ChooseStrike(p, intent);
                if (chosen != null && CanStartStrike(chosen) && Rand01() < profile.strikeChance)
                {
                    pendingStrike = chosen;
                    pendingIntent = intent;
                    Enter(CPUState.Strike);
                    return;
                }
            }

            // ---- ③c 窗口在、但够不着 → 主动贴近 ----
            //   Chase/Punish 的落地靠这一步：没有它，AI 在窗口期会掉到下面的通用兜底
            //   （Idle/Hop），窗口白白浪费。只对非中性意图生效：中性本来就走 Approach。
            if (hasIntent && attackEnabled && !inStrikeRange && intent != StrikeIntent.Neutral)
            {
                Enter(CPUState.Approach);
                return;
            }

            // ---- ④ 保底：自己伤害过高且对手没有便宜可占 → 撤离 ----
            if (p.SelfDamagePercent >= highDamageThreshold && p.Distance < safeDistance)
            {
                Enter(CPUState.Retreat);
                return;
            }
            if (Time.time < strikeReadyTime)
            {
                Enter(CPUState.Idle);
                return;
            }
            // 如果对手在高处（平台上），尝试跳起追击；否则接近
            if (foe != null && foe.transform.position.y - me.transform.position.y > 0.6f && CanJumpNow())
            {
                Enter(CPUState.Hop);
                return;
            }
            Enter(CPUState.Approach);
        }

        private float Rand01() => UnityEngine.Random.value;

        // 执行：纯动作，零判断
        private void Act(Perception p)
        {
            switch (current)
            {
                case CPUState.Idle:
                    Stop();
                    break;

                case CPUState.Approach:
                    // 【安全守卫】目标离台 → 放弃追击，回台心
                    if (safeBoundsResolved && foe != null)
                    {
                        Vector3 t = foe.transform.position;
                        FighterState tfs = foe.StateMachine.CurrentState;
                        bool targetOffStage = t.y <= safeMinY + edgeDangerMargin + 1.5f
                                            || t.x <= safeMinX + edgeDangerMargin + 0.5f
                                            || t.x >= safeMaxX - edgeDangerMargin - 0.5f
                                            || tfs == FighterState.Dead;
                        if (targetOffStage)
                        {
                            Enter(CPUState.Hop);
                            break;
                        }
                    }
                    // 【新增 · D2-⑤】远程角色进入射程就停步风筝。
                    if (isRangedFighter && p.Distance <= rangedHoldDistance) { Stop(); break; }
                    if (inBand && (!attackEnabled || Time.time < strikeReadyTime)) { Stop(); break; }
                    // 高度差：对手在上方，优先跳跃而不是水平走
                    if (foe != null && foe.transform.position.y - me.transform.position.y > 0.8f && CanJumpNow())
                    {
                        me.TryJump();
                    }
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

                // 【新增 · D2-④】闪避：按住远离对手的方向，落地/超时即收
                case CPUState.Evade:
                    TickEvade(p);
                    break;

                // 【新增 · D2-③】边缘求生：朝台心走，回到安全带或超时即收
                case CPUState.Hop:
                    TickHop(p);
                    break;

                // 【新增 · D5】绕后：跳过对手，落到背后再出手
                case CPUState.Flank:
                    TickFlank(p);
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
            bool selfStunned = me.isInKnockback || me.IsRespawnLocked
                || (!me.StateMachine.CanAct()
                    && me.StateMachine.CurrentState != FighterState.Shield
                    && me.StateMachine.CurrentState != FighterState.Attack);

            // 【重点】IsInAir() 包含 Knockback，不能用来判"能否行动"
            FighterState fs = foe.StateMachine.CurrentState;
            bool oppAttacking = fs == FighterState.Attack;
            bool oppInStun = fs == FighterState.Hit || fs == FighterState.Stun || fs == FighterState.Knockback;

            // 【新增 · D2-③】边缘感知：把"我离台边多近、该往哪走"也算成【观察到的事实】，
            //   而不是让 Decide 现算。这样 Decide/Act 只读 p.SelfNearEdge 就行，
            //   不会出现"状态 A 里判该不该去状态 B"的双向依赖（同文件上方铁律）。
            bool nearEdge = false;
            float towardCenterX = 0f;
            if (safeBoundsResolved)
            {
                bool nearSide = a.x <= safeMinX + edgeDangerMargin
                             || a.x >= safeMaxX - edgeDangerMargin;
                // 只把"掉到台面以下"算危险。【不】把"跳太高"算危险：
                //   顶部 BlastZone 内壁在 y≈14，而台面在 y≈0，中间全是正常战斗高度，
                //   若把顶部也按同一 margin 判定，AI 一跳起来就误判危险、转头往台心跑。
                bool belowStage = a.y <= safeMinY + edgeDangerMargin;
                nearEdge = nearSide || belowStage;
                if (nearEdge) towardCenterX = a.x < stageCenterX ? 1f : -1f;
            }

            // 缓存给 EnterState 用（进入钩子拿不到 Perception 参数）
            curDirection = dx > 0f ? 1f : -1f;

            // 【新增 · D5】扩展感知（读招/绕后/压制/终结）—— 同样只记"观察到的事实"
            bool oppShielding = foe.isShielding;
            bool oppGrounded = foe.IsGrounded;
            float oppDamagePercent = foe.CurrentDamage;
            bool oppRecovering = ComputeOpponentRecovering(foe);

            bool oppNearEdge = false;
            if (safeBoundsResolved)
            {
                oppNearEdge = b.x <= safeMinX + edgeDangerMargin
                           || b.x >= safeMaxX - edgeDangerMargin
                           || b.y <= safeMinY + edgeDangerMargin;
            }

            // 我是否在对手【背后】：相对其朝向判断（同一 x 靠前 = 正面，靠后 = 背面）
            // 【修正 10-16】原式两个分支写反了：对手朝右时其正面在 +x，我 x 更大 = 站在【正前方】，
            //   原式却判成"背后" → canFlank 的 !IAmBehindOpponent 在正面接近时恒为 false，
            //   绕后分支永远不可达（实测被动对手 + Expert 20s，flank 恒 0）。
            bool iAmBehind = foe.isFacingRight ? a.x < b.x : a.x > b.x;

            return new Perception(
                dist,
                curDirection,
                dy,
                oppAttacking,
                oppInStun,
                selfStunned,
                me.IsGrounded,
                me.CurrentDamage,
                nearEdge,
                towardCenterX,
                oppRecovering,
                oppShielding,
                oppGrounded,
                oppDamagePercent,
                oppNearEdge,
                iAmBehind);
        }

        // 【新增 · D5】读招：对手是否处于【攻击后摇】—— 我的惩罚窗口。
        // 【重点】判据必须落在"判定段之后"，不能只看 isAttacking：
        //   isAttacking 覆盖整段招式（前摇+判定+后摇），若只看它，
        //   惩罚窗口会把对手的前摇也算进来 —— AI 会主动撞进对方判定里挨打。
        // 【注意】normalizedTime 是【循环计数】（0..1..2..），必须取小数部分；
        //   直接把整数当比例会得到"永远为真/永远为假"的静默错误。
        // 【注意】层号做降级：本工程攻击态在第 1 层；若工程只有单层动画层，退回第 0 层。
        private bool ComputeOpponentRecovering(FighterController f)
        {
            if (f == null) return false;
            if (!f.isAttacking) return false;

            AttackData atk = f.attackData;
            if (atk == null || atk.TotalDuration <= 0.01f) return true;   // 数据缺失 → 保守视为可惩罚

            if (f.animator == null) return false;

            int layer = f.animator.layerCount > 1 ? 1 : 0;
            float normalized = f.animator.GetCurrentAnimatorStateInfo(layer).normalizedTime % 1f;
            if (normalized < 0f) normalized += 1f;

            float recoverStart = (atk.startupTime + atk.activeTime) / atk.TotalDuration;
            return normalized >= recoverStart;
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

        // 【新增 · D2-⑤】远程射击带（同样带滞回）
        // 【重点】为什么不能复用 InAttackBand：
        //   InAttackBand 的外沿是 attackRange*(1+hysteresis) = 1.6*1.2 = 1.92，
        //   而投射物技的门槛是 Distance >= rangedFireMinDistance = 3.5 —— 互斥。
        //   若出招分支只判 inBand，ChooseStrike 的 `Distance >= 3.5` 分支
        //   就是【永远为假的死代码】：远程角色只在贴脸距离用 jab，永远不发射。
        //   症状是"远程角色不打弹"，但没有任何报错。
        private bool InRangedBand(Perception p)
        {
            if (!isRangedFighter || myRangedSpecial == null) return false;

            // 高度差判据与 InAttackBand 同一套，否则会"隔着一层楼打出子弹"
            float vRange = p.HeightDifference > 0f ? verticalRangeAbove : verticalRangeBelow;
            if (Mathf.Abs(p.HeightDifference) > vRange) return false;

            if (rangedClosingIn)
            {
                // 带外侧：近到 hold 以内才进带
                if (p.Distance <= rangedHoldDistance) { rangedClosingIn = false; return true; }
                return false;
            }
            else
            {
                // 带内：退到 hold*1.2 以外才出带（复用 attackHysteresis 作为滞回系数）
                if (p.Distance >= rangedHoldDistance * (1f + attackHysteresis))
                { rangedClosingIn = true; return false; }
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

            // 【新增 · D2-④】闪避 = 起跳 + 反向输入。
            //   引擎【没有】dodge/roll API（FighterData.airDodgeCount 是"未实现"字段），
            //   只能这样组合。CanJumpNow() 自检预算的原因见该方法注释。
            if (state == CPUState.Evade)
            {
                evadeStartTime = Time.time;
                if (CanJumpNow()) me.TryJump();
                me.MoveInput = new Vector2(-curDirection, 0f);
            }

            // 【新增 · D5】绕后进入钩子：记录起点与侧向 → 起跳越顶 → 朝对手推进。
            //   【注意】起跳只在进入钩子里做一次（写进 TickFlank 会变成每 tick 一跳）。
            if (state == CPUState.Flank)
            {
                flankStartTime = Time.time;
                flankSide0 = curDirection;
                statFlank++;
                if (CanJumpNow()) me.TryJump();
                me.MoveInput = new Vector2(curDirection, 0f);
            }

            // 【新增 · D2-③】边缘求生进入钩子：只【记录】起始时刻，不在这里起跳。
            //   贴地时朝台心走就够（这个舞台左右是 BlastZone 而非悬崖，地面能走回来）；
            //   只有"已经掉到台面以下"才需要跳 —— 那件事交给 TickHop 按需做，
            //   避免每进一次 Hop 就白白消耗一跳。
            if (state == CPUState.Hop)
            {
                hopStartTime = Time.time;
                // 进入边缘求生时，优先起跳尝试返场
                if (CanJumpNow())
                    me.TryJump();
            }

            if (SmashDebug.IsOn(DebugChannel.State))
                SmashDebug.Log(DebugChannel.State, $"CPU 决策 → {state}");
        }

        private void Exit(CPUState state)
        {
            // 【新增】招式数据随状态一起丢弃。留着会有一个隐患：
            //   下一次进 Strike 若因任何原因没走到 pendingStrike = chosen，
            //   BeginStrike 就会拿【上一次】的招式当这一招打（且总时长算错）。
            if (state == CPUState.Strike)
            {
                pendingStrike = null;
                pendingIntent = StrikeIntent.Neutral;   // 【D5】意图随招式一起丢弃，防污染下一次
            }

            // 【新增 · D5】绕后结束 → 进入冷却：避免每次近身都跳越（抽搐式绕后）
            if (state == CPUState.Flank)
                flankCooldownUntil = Time.time + profile.attackCooldown;

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

        // 出招执行
        // 【D1 选招表】近战只给 jab1，够验证"能打"。
        //   InputManager 的选招表是 private，AI 复用不了 —— 这是有意的：
        //   人类靠按键表达意图，AI 靠感知，两者本就该走不同入口。
        // 【新增 · D2-⑤】远程角色的投射物技走 TrySpecial 而不是 TryAttack ——
        //   引擎只在 TrySpecial 里处理 projectilePrefab 分支（FighterController.cs:1031），
        //   把投射物技喂给 TryAttack 不会生成任何子弹。
        private void BeginStrike()
        {
            AttackData atk = pendingStrike != null ? pendingStrike : me.fighterData.jab1;
            if (atk == null)
            {
                // 理论上不可达：CanStartStrike(atk) 已挡掉 null。
                //   留这行是为了不给"静态分析看不出防御"留隐患。
                Debug.LogWarning("[CPUPlayer] BeginStrike 无可用招式，本次出招作废");
                return;
            }

            strikeStartTime = Time.time;
            strikeReadyTime = Time.time + atk.TotalDuration + profile.attackCooldown;

            // 【新增 · D5】行为计数：难度行为差的无头取证来源
            //   （频率差异看 decisionInterval/attackCooldown，行为差异就看这 6 个计数）
            switch (pendingIntent)
            {
                case StrikeIntent.Chase:    statChase++;    break;
                case StrikeIntent.Punish:   statPunish++;   break;
                case StrikeIntent.Pressure: statPressure++; break;
                default:                    statNeutral++;  break;
            }
            if (lastPickWasFinisher) statFinisher++;

            if (atk == myRangedSpecial) me.TrySpecial(atk);
            else me.TryAttack(atk);

            if (SmashDebug.IsOn(DebugChannel.Combat))
                SmashDebug.Log(DebugChannel.Combat,
                    $"CPU 出招 {atk.attackName} 前摇{atk.startupTime:F2}/判定{atk.activeTime:F2}/后摇{atk.recoveryTime:F2} 合计{atk.TotalDuration:F2}s");
        }

            // 出招预检
        private bool CanStartStrike(AttackData atk)
        {
            if (me == null || me.fighterData == null) return false;
            if (atk == null) return false;
            if (Time.time < strikeReadyTime) return false;
            if (!me.StateMachine.CanAct()) return false;
            if (me.isInKnockback) return false;
            if (me.isAttacking || me.isShielding) return false;
            return true;
        }

        // 选招：把"意图"翻译成 FighterData 里的具体槽位（D5 起从 1 招扩到整张槽位表）
        // 【重点】本函数【不做战略判断】：要不要打、打谁、为什么打，全部由 Decide 决定后
        //   以 intent 参数带进来；这里只回答"这个意图该用哪一招"。
        //   战略/选招分离后，改难度只动 Decide + Profile，换招式表只动这里，互不牵连。
        // 【重点】AttackData 是【引用类型】（FighterData.cs:163 class 而非 struct），
        //   所以比较用 == 是比引用 —— 恰好是我们要的"是不是同一招"。
        private AttackData ChooseStrike(Perception p, StrikeIntent intent)
        {
            FighterData fd = me.fighterData;
            if (fd == null) return null;

            lastPickWasFinisher = false;

            // ---- ① 远程：射程外优先投射物技（D2-⑤ 行为保持不变）----
            if (myRangedSpecial != null && p.Distance >= rangedFireMinDistance)
                return myRangedSpecial;

            // ---- ② 空中：对空用上挑型、对地用下砸型、平飞用前 A ----
            if (!me.IsGrounded)
            {
                if (p.HeightDifference > 0.9f && fd.aerialUp != null) return fd.aerialUp;
                if (p.HeightDifference < -0.6f && fd.aerialDown != null) return fd.aerialDown;
                if (fd.aerialForward != null) return fd.aerialForward;
                if (fd.aerialNeutral != null) return fd.aerialNeutral;
            }
            else if (p.HeightDifference > 0.9f)
            {
                // 地面但对手明显在上方（站在平台上）→ 用上挑打浮空对手
                if (fd.tiltUp != null) return fd.tiltUp;
                if (fd.smashUp != null) return fd.smashUp;
            }

            // ---- ③ 地面：终结判定优先 ----
            //   对手伤害已进必杀区间 + 档位有终结意识 → 换击退最强的横 smash 收头。
            //   低难度 finisherBias=0 → 永远不触发，只会常规蹭伤害（= 收不掉人）。
            bool finisher = p.OpponentDamagePercent >= finisherDamagePercent
                            && Rand01() < profile.finisherBias;
            lastPickWasFinisher = finisher;
            if (finisher && fd.smashSide != null) return fd.smashSide;

            // ---- ④ 按意图挑招 ----
            switch (intent)
            {
                case StrikeIntent.Pressure: return PickPressure(fd);
                case StrikeIntent.Punish:   return PickHeaviest(fd);
                case StrikeIntent.Chase:    return PickChase(fd, p);
                default:                    return PickNeutral(fd);
            }
        }

        // 压制：对手举盾 → 破盾值最高者（盾破 = 长硬直 = 免费一套）
        private AttackData PickPressure(FighterData fd)
            => Fallback(Best(new[] { fd.smashSide, fd.smashUp, fd.smashDown, fd.tiltSide, fd.tiltDown, fd.jab1 },
                             a => a.shieldDamage), fd);

        // 惩罚：对手在硬直/后摇里，必中 → 直接用伤害最高的地面招
        private AttackData PickHeaviest(FighterData fd)
            => Fallback(Best(new[] { fd.smashSide, fd.smashUp, fd.smashDown, fd.tiltSide, fd.tiltUp, fd.tiltDown, fd.jab1 },
                             a => a.damage), fd);

        // 追击：对手被击飞时多在空中 → 优先能覆盖上方的招；对手落地才用常规重招
        private AttackData PickChase(FighterData fd, Perception p)
        {
            if (p.HeightDifference > 0.6f || !p.OpponentGrounded)
                return Fallback(Best(new[] { fd.aerialForward, fd.aerialUp, fd.tiltUp, fd.smashUp, fd.jab1 },
                                     a => a.damage), fd);
            return PickHeaviest(fd);
        }

        // 常规对拼：轻快起手（保持 D2 手感：jab1 → 横 tilt → 下 tilt 小跳）
        private AttackData PickNeutral(FighterData fd)
        {
            if (fd.jab1 != null) return fd.jab1;
            if (fd.tiltSide != null) return fd.tiltSide;
            if (fd.tiltDown != null) return fd.tiltDown;
            return Fallback(null, fd);
        }

        // 在候选槽位里挑"打分"最高的一招；全部为 null 时返回 null
        // 【注意】显式钉住"第一个非空候选也要被选中"（best == null 时无条件接管），
        //   否则打分若为负（理论上不该有），会出现"有招却选出 null"。
        private AttackData Best(AttackData[] candidates, System.Func<AttackData, float> score)
        {
            AttackData best = null;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Length; i++)
            {
                AttackData atk = candidates[i];
                if (atk == null) continue;
                float s = score(atk);
                if (best == null || s > bestScore) { bestScore = s; best = atk; }
            }
            return best;
        }

        // 兜底链：选中的招 → jab1 → 投射物技（保证任何角色都有招可出）
        private AttackData Fallback(AttackData picked, FighterData fd)
        {
            if (picked != null) return picked;
            if (fd != null && fd.jab1 != null) return fd.jab1;
            return myRangedSpecial;
        }

        // 跳跃预算自检
        // 【重点】为什么不能盲目调 me.TryJump()：
        //   FighterController.cs:922 的实现是【先记 jumpBufferTimer，再试起跳】，
        //   而 TryPerformJump(:798) 在条件不满足时【静默 return】——
        //   于是"没跳成"却把补跳缓冲留下了（落地瞬间自动弹一下）。
        //   决策 5Hz，若每 tick 无脑调一次，AI 落地就会被缓冲反复弹跳。
        //   所以调用前必须自己确认"这次真的跳得起来"。
        // 【注意】coyoteTimer 是 private，贴地判定只能用 IsGrounded 近似，
        //   会漏掉那 0.1s 的土狼窗口 —— 宁可少跳一次，也不能留脏缓冲。
        private bool CanJumpNow()
        {
            if (me == null || me.fighterData == null) return false;
            if (me.remainingJumps <= 0) return false;
            if (me.isInKnockback) return false;
            if (!me.StateMachine.CanAct()) return false;
            return true; // 不限制一段跳必须贴地，允许空中也跳（二段跳）
        }

        // 战斗门禁：与 InputManager.cs 的 playable 判定保持一致
        private bool CanDecide()
        {
            if (GameManager.Instance == null) return false;
            if (!GameManager.Instance.IsCombatState(true)) return false;   // Battle 或 Training
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

        #region 4. 各状态行为

        private void TickStrike()
        {
            // 【新增】读 pendingStrike 而非硬编码 jab1：远程角色出的是投射物技，总时长不同。
            //   null 视为"招式已结束"，直接收 —— 防御性写法，正常路径不会为 null。
            AttackData atk = pendingStrike;
            if (atk != null && Time.time - strikeStartTime < atk.TotalDuration) return;   // 招式进行中，不写任何输入

            // 【重点】回 Approach 而不是回 Idle：
            //   Approach 会重跑 InAttackBand —— 滞回的【出带】分支在这里第一次被走到。
            //   （招式进行中由上一行提前 return 处理，此时不写任何输入）
            Enter(CPUState.Approach);
        }

        // 闪避最短按住时间：起跳后至少维持这么久的后撤输入。
        // 【重点】为什么需要它：Enter(Evade) 内部已调 TryJump()，但 p 是【起跳前】
        //   拍的感知快照，其 SelfGrounded 仍为 true。若直接拿它当"已落地"判据，
        //   闪避会在进入的【同一个 tick】就退出，MoveInput 还没生效就转回 Approach
        //   —— 表现为"闪避完全不发生"，且日志里看不出任何异常。
        private const float kEvadeMinHold = 0.08f;

        // 【新增 · D2-④】闪避行为
        //   进入钩子已经起跳并给了反向输入，这里只负责"什么时候收"。
        //   三个收手条件缺一不可：
        //   ① 最短按住 kEvadeMinHold —— 见上面常量注释，否则闪避 0 帧
        //   ② 落地就收 —— 继续按住后撤会退化成 Retreat，闪避就变成了普通逃跑
        //   ③ 超时就收 —— 万一跳起来卡在对手身上（踩头判定等），不会永远按着
        private void TickEvade(Perception p)
        {
            float held = Time.time - evadeStartTime;
            // 【重点】落地判据读【实时】me.IsGrounded，不读快照 p.SelfGrounded。
            //   快照是起跳前采的，起跳后必然还是 true —— 拿它判落地等于永远立刻收手。
            if (held >= kEvadeMinHold && me.IsGrounded)
            {
                Enter(CPUState.Approach);
                return;
            }
            if (held >= evadeDuration)
            {
                Enter(CPUState.Approach);
                return;
            }
            me.MoveInput = new Vector2(-p.Direction, 0f);
        }

        // 【新增 · D2-③】边缘求生行为
        //   朝台心走；掉到台面以下时按需起跳抢高度。
        private void TickHop(Perception p)
        {
            // 回安全区就收。判据用【滞回】的宽安全带（edgeSafeMargin）而不是
            //   感知用的窄危险带（edgeDangerMargin）：两者相等时 AI 会在边界
            //   一进一出地抽搐（这与 InAttackBand 用双阈值夹死区是同一个道理）。
            if (IsSafeFromEdge()) { Enter(CPUState.Approach); return; }

            // 超时兜底：绝不让 Hop 变成永久状态把 AI 卡死。
            //   例如双方都在台边互相把对方推出去，会一直触发 Hop。
            if (Time.time - hopStartTime >= hopMaxDuration) { Enter(CPUState.Approach); return; }

            // 边缘求生：如果自己在空中（掉出平台）或快要掉下去，优先起跳抢回高度；贴地时朝台心走
            bool needJump = me.IsGrounded == false && me.transform.position.y <= safeMinY + 1.5f && CanJumpNow();
            // 自己还在地面但已经很靠近底部危险带（快掉下平台边缘）也尝试起跳
            if (!needJump && me.IsGrounded && me.transform.position.y <= safeMinY + edgeDangerMargin + 0.5f && CanJumpNow())
                needJump = true;
            if (needJump)
            {
                me.TryJump();
            }

            // 朝台心移动；如果没能起跳也继续往台心走
            me.MoveInput = new Vector2(p.SelfTowardCenterX, 0f);
        }

        // 【新增 · D5】绕后行为
        //   设计：跳越过对手 → 落到对手【背后】→ 回身打。
        //   【重点】不缓存"是否已越过"这种可推导状态：直接比较当前侧向与发起侧向 ——
        //   多缓存一份真源就是本文件反复警告的"静默打架"隐患。
        private void TickFlank(Perception p)
        {
            // ① 自己靠台边危险 → 立刻放弃绕后回台心（绕后是进攻，不能自杀）
            if (me.IsGrounded && !IsSafeFromEdge()) { Enter(CPUState.Approach); return; }

            // ② 超时兜底：绝不让 Flank 变成永久状态（与 hopMaxDuration 同一思路）
            if (Time.time - flankStartTime >= flankMaxDuration) { Enter(CPUState.Approach); return; }

            // ③ 侧向翻转 = 已越过对手 → 空中出手（前 A 覆盖下坠时的打击面）
            bool crossed = p.Direction != flankSide0;
            if (crossed)
            {
                if (!me.IsGrounded)
                {
                    AttackData atk = me.fighterData != null ? me.fighterData.aerialForward : null;
                    if (atk != null && CanStartStrike(atk))
                    {
                        pendingStrike = atk;
                        pendingIntent = StrikeIntent.Neutral;
                        Enter(CPUState.Strike);
                        return;
                    }
                }
                // 落地了还没打出去 → 交回常规接近（下一 tick 会重新决策）
                Enter(CPUState.Approach);
                return;
            }

            // ④ 还没越过且在下落 → 补第二跳抢高度（否则跳不过对手、白绕一次）
            if (!me.IsGrounded && me.velocity.y < 0f
                && me.transform.position.y <= foe.transform.position.y + 0.5f
                && CanJumpNow())
            {
                me.TryJump();
            }

            me.MoveInput = new Vector2(p.Direction, 0f);
        }

        // 【重点】X 和 Y 必须是【与】关系，不能是【或】。
        //   写成 `if (xSafe) return true; if (ySafe) return true;` 的话，
        //   站台上 y≈0 而 safeMinY≈-10、edgeSafeMargin=7 → 0 > -3 恒成立
        //   → 每个 tick 都判"已安全" → Hop 在进入的【下一 tick 就退出】，
        //   AI 站在台边一步都不往台心走，整个 D2-③ 静默失效。
        //   只有"X 已回到安全带 【且】 Y 已回到台面高度"才算真的安全。
        //   Y 的退出阈值复用【窄】的 edgeDangerMargin —— 因为 Sense() 进危险带的
        //   判据就是 y <= safeMinY + edgeDangerMargin，退出条件必须是它的严格反命题；
        //   若误用 edgeSafeMargin(7)，就要求爬到 y > 3 才开始撤，那是跳跃高度不是台面。
        private bool IsSafeFromEdge()
        {
            Vector3 pos = me.transform.position;
            bool xSafe = pos.x > safeMinX + edgeSafeMargin && pos.x < safeMaxX - edgeSafeMargin;
            bool ySafe = pos.y > safeMinY + edgeDangerMargin;
            return xSafe && ySafe;
        }

        private void Stop() { me.MoveInput = Vector2.zero; }

        #endregion

        #region 5. 角色类型 / 舞台边界解析

        // 【新增 · D2-⑤；判据重构 · M6-D5 补】判定自己是远程还是近战角色
        // 【重点】判据 = FighterData.aiArchetype（显式配置），【不再】用"有没有弹道"反推。
        //   旧判据（specialNeutral/specialSide 带 projectilePrefab）在 M6 六个角色上【全部为真】，
        //   后果是"六个角色全被判远程、AI 一律在 4.5 距离放风筝、从不贴身"，
        //   D2-⑤ 的"Archer 远程 / Barbarian 近战"差异化整体失效 —— 数据无法表达设计意图。
        // 【重点】"哪一招才是投射物"仍然必须按 projectilePrefab 找：引擎的投射物分支
        //   （FighterController 里按 projectilePrefab 判）用的就是这个判据；
        //   但它【只在 Ranged 定位下启用】—— Melee 角色即使数据里挂着弹道，AI 也不会拿去远程放。
        private void ResolveLoadout()
        {
            myRangedSpecial = null;
            isRangedFighter = false;
            if (me == null || me.fighterData == null) return;

            if (me.fighterData.aiArchetype != AIArchetype.Ranged) return;   // 近战定位：到此为止

            if (me.fighterData.specialNeutral != null && me.fighterData.specialNeutral.projectilePrefab != null)
                myRangedSpecial = me.fighterData.specialNeutral;
            else if (me.fighterData.specialSide != null && me.fighterData.specialSide.projectilePrefab != null)
                myRangedSpecial = me.fighterData.specialSide;

            // 【注意】定位写了 Ranged 却没挂任何弹道 = 数据配错。此时若把 isRangedFighter 置 true：
            //   InRangedBand 会因 myRangedSpecial==null 恒 false（永远进不了射击带），
            //   而 Approach 又会因 isRangedFighter 在 4.5 距离停步 → 变成一个"站着挨打、永不出招"的沙包。
            //   故【降级为近战行为】+ 告警，保证 AI 至少会打人。
            if (myRangedSpecial == null)
            {
                if (SmashDebug.IsOn(DebugChannel.State))
                    SmashDebug.LogWarn(DebugChannel.State,
                        $"CPUPlayer 配置异常 | {me.name} 的 aiArchetype=Ranged 但没找到带弹道的必杀技 | 已降级为近战行为");
                return;
            }

            isRangedFighter = true;

            // 【注意·陷阱】specialUp（上B）绝对不能拿来做边缘求生：
            //   DoSpecialUp 的落点是 transform.position + (0, specialUpDistance, 0)
            //   （FighterController.cs:1086）——【纯垂直】瞬移，不朝对手也不朝台心。
            //   在台边用它只会把自己垂直顶向顶部 BlastZone，等于自杀。
            //   且 specialUpUsed 是 private，AI 无法查询是否已用过，
            //   重复调 TrySpecial(specialUp) 会静默 no-op（:1025），白白浪费决策。
            //   → 故 AI 禁用上B，此处显式说明以免后人误加。
        }

        // BlastZone 解析重试上限：决策 5Hz × 30 次 ≈ 6 秒。
        //   超过就接受出生点推算的保守范围，不再每 tick 扫场景。
        private const int kBoundsMaxRetry = 30;

        // 【新增 · D2-③】从 BlastZone 反推舞台安全区
        // 【重点】BlastZone 没有公开的边界数值 —— 只有 4 个历史遗留 bool 和 side 枚举
        //   （BlastZone.cs:29-36 注释自称"历史遗留"），真正的边界只存在于它身上的
        //   BoxCollider2D 里。所以只能自己 GetComponent 读出来。
        // 【重点】取的是【内壁】而不是中心：
        //   左边界区的【右】壁才是安全左界，右边界区的【左】壁才是安全右界。
        //   底部边界区的【上】壁才是安全高度。
        private void EnsureSafeBounds()
        {
            if (safeBoundsResolved) return;

            // 【注意】重试上限：Awake 时场景对象激活顺序未定义，可能读不到 BlastZone。
            //   但也不能无限重试 —— FindObjectsOfType 每 tick 调一次会把性能拖垮。
            // 【重点】超限后必须【永久接受】保守估算（置 resolved），不能就这么返回：
            //   Sense() 的边缘判定以 safeBoundsResolved 为门槛，不置它就等于
            //   "边缘求生整个功能静默失效" —— 而这正是最需要它生效的降级场景。
            //   宁可边界粗一点，也不能让 AI 不知道自己会掉出界。
            if (boundsRetryCount > kBoundsMaxRetry)
            {
                FallbackSafeBounds();
                safeBoundsResolved = true;
                return;
            }
            boundsRetryCount++;

            BlastZone[] zones = FindObjectsOfType<BlastZone>();
            if (zones == null || zones.Length == 0) return;

            float left = float.NaN, right = float.NaN, bottom = float.NaN;
            for (int i = 0; i < zones.Length; i++)
            {
                BoxCollider2D col = zones[i].GetComponent<BoxCollider2D>();
                if (col == null) continue;

                Vector3 c = zones[i].transform.position;
                float zoneMinX = c.x + col.offset.x - col.size.x * 0.5f;
                float zoneMaxX = c.x + col.offset.x + col.size.x * 0.5f;
                float zoneMinY = c.y + col.offset.y - col.size.y * 0.5f;
                float zoneMaxY = c.y + col.offset.y + col.size.y * 0.5f;

                switch (zones[i].side)
                {
                    case BlastZone.Side.Left:   left = zoneMaxX; break;   // 左区右壁 = 安全左界
                    case BlastZone.Side.Right:  right = zoneMinX; break;  // 右区左壁 = 安全右界
                    case BlastZone.Side.Bottom: bottom = zoneMaxY; break; // 底区上壁 = 安全高度
                    default: break;   // Top 不参与：顶部高不算危险（见 Sense 注释）
                }
            }

            if (float.IsNaN(left) || float.IsNaN(right) || float.IsNaN(bottom))
            {
                FallbackSafeBounds();
                return;
            }

            safeMinX = left;
            safeMaxX = right;
            safeMinY = bottom;
            stageCenterX = (left + right) * 0.5f;
            safeBoundsResolved = true;

            if (SmashDebug.IsOn(DebugChannel.State))
                SmashDebug.Log(DebugChannel.State,
                    $"CPU 台边安全区 x∈[{safeMinX:F1}, {safeMaxX:F1}] y≥{safeMinY:F1} 中心x={stageCenterX:F1}");
        }

        // 退化路径：场景缺 BlastZone 时，用出生点位置推一个保守范围
        // 【注意】此时【不】置 safeBoundsResolved —— 下个 tick 还会再试真值。
        private void FallbackSafeBounds()
        {
            float cx = 0f;
            if (MatchManager.Instance != null && MatchManager.Instance.spawnPoints != null)
            {
                Transform[] sps = MatchManager.Instance.spawnPoints;
                float sum = 0f; int n = 0;
                for (int i = 0; i < sps.Length; i++)
                {
                    if (sps[i] == null) continue;
                    sum += sps[i].position.x; n++;
                }
                if (n > 0) cx = sum / n;
            }

            stageCenterX = cx;
            safeMinX = cx - 20f;
            safeMaxX = cx + 20f;
            safeMinY = -10f;
            WarnOnce("未能从 BlastZone 解析台边边界，暂用出生点推算的保守范围（x±20 / y≥-10）");
        }

        public void SetAiFrozen(bool frozen)
        {
            aiFrozen = frozen;
        }

        public void SetAttackEnabled(bool enabled)
        {
            attackEnabled = enabled;
        }

        public void ForceIdle()
        {
            var fc = GetComponent<FighterController>();
            if (fc != null && fc.StateMachine != null)
            {
                fc.StateMachine.TransitionTo(fc.IsGrounded ? FighterState.Idle : FighterState.Fall);
            }
            // 【关键】立即清零移动输入：否则冻结前最后一帧写入的方向会残留 → 木桩继续滑行
            if (fc != null) fc.MoveInput = Vector2.zero;
            strikeReadyTime = 0f;
            decisionTimer = 0f;
        }

        #endregion
    }
}
