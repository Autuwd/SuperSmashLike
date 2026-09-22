using SuperSmashLike.Combat;
using SuperSmashLike.InputSystem;
using SuperSmashLike.Managers;
using SuperSmashLike.Stage;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// FighterController — 斗士角色主控制器（整个项目的核心类）
// 职责（8 大块，全部在本类内）：
//   1. 计时器   蓄力 / 攻击兜底 / 连段 / 无敌 / 护盾 / 眩晕 / 抓取超时
//   2. 重力     重力加速度 / 快速下落 / 落地处理 / 受身窗口
//   3. 移动     水平速度（走/跑/空）与状态切换
//   4. 动画     参数同步 / 播放速率 / Action Layer 权重
//   5. 攻击     起手 / 连段推进 / 蓄力 / 特殊攻击 / 取消窗口守卫
//   6. 防御     举盾 / 盾反 / 破盾
//   7. 抓投     抓取 / 挣扎 / 四向投掷
//   8. 受击重生 伤害 / 击飞 / 硬直 / 受身 / 死亡 / 重生
// 架构位置：Character 层核心
// 依赖：GameManager（规则与玩家列表）、DamageSystem（击飞公式）、Projectile（投射物）、
//       FighterData（角色数据）、CameraManager（盾反/受身闪白）
// 关系图：
//   InputManager      → 读输入 → 调用 TryJump / TryAttack / TrySpecial / TryGrab / SetShielding / StartCharge / ReleaseCharge
//   Hitbox            → 命中检测 → 调用 ApplyDamage
//   FighterStateMachine → 管理状态转换 → 本类驱动它
//   MatchManager      → 订阅 OnDamaged / OnKilled
//
// 【重点】本类是全项目唯一的"上帝类"，没有任何 Handler/Manager 拆分。
//   阅读顺序建议：Awake/Start → Update → FixedUpdate → Update* 系列 →
//   Try* 系列 → Apply* 系列 → Perform*/Release* 系列。
// ============================================================
namespace SuperSmashLike.Core
{
    public class FighterController : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Configuration")]
        public bool isFacingRight = true;    // 面朝向（true=朝右），由 SetFacing 镜像 Visual 实现
        public FighterData fighterData;      // 角色数据资产（ScriptableObject）
        public int playerID;                 // 玩家ID（0=P1, 1=P2, …），UI 用它当数组下标

        [Header("References")]
        public Animator animator;            // Animator 引用 → 驱动动画
        public Rigidbody2D rb;               // 刚体 → 物理运动
        public Collider2D mainCollider;      // 主碰撞体（抓取时 IgnoreCollision 用它）
        public Hurtbox hurtbox;              // 受击判定框（子物体）
        public Transform visual;             // 模型父级（Visual），用于镜像翻转
        public GameObject projectilePrefab;  // 特殊攻击投射物预制体（Inspector 拖入）

        [Header("Ground Detection")]
        public Transform groundCheck;            // 脚底检测点（空子物体）
        public float groundCheckRadius = 0.2f;   // 检测半径
        public LayerMask groundLayer;            // 哪些层算"地面"

        [Header("Jump")]
        public int remainingJumps;   // 剩余跳跃次数（大乱斗通常可跳 2 次）
        public bool isJumping;       // 是否正在跳跃
        public bool jumpHeld;        // 跳跃键是否按住
        private bool specialUpUsed;   // 上B空中只能一次，落地后才恢复
        private bool specialUpInStartup;    // SU 起手缓冲期内禁止落地刷新
        private bool isDroppingThrough;   // 落穿中：强制非地面，让重力/状态机正常走 Fall

        [Header("Attack")]
        public AttackData attackData;       // 当前攻击数据（起手/连段推进/缓冲消费共用）
        public bool isAttacking;            // 是否正在攻击动画中
        public float attackTimer;           // 攻击剩余持续时间（当前只写不读，保留供 FrameMeter 扩展）
        public float attackFallbackTimer;   // 攻击兜底计时（独立于 attackTimer，防事件链路断卡死）
        public float specialUpDistance = 12f;  // 上B瞬移高度
        public float farSpecialRange = 2.5f;  // 长按对应远距离位置

        [Header("Charge")]
        public bool isCharging;              // 是否蓄力中
        public float chargeTimer;            // 蓄力进度（秒）
        public float maxChargeTime = 0.8f;   // 满蓄时间
        public float chargeDamageMultiplier = 1.25f;     // 满蓄伤害倍率
        public float chargeKnockbackMultiplier = 1.15f;  // 满蓄击退倍率
        public AttackData chargeBaseData;    // 蓄力基值（smashSide/Up/Down）

        [Header("Combo")]
        public InputBuffer inputBuffer = new InputBuffer();   // 帧号输入缓冲（5 帧窗口）
        public int comboStep;            // 当前连招段（0=Jab1, 1=Jab2, 2=Jab3）
        public float comboWindowStart = 0.3f;   // 连招窗口起点（normalizedTime）
        public float comboWindowEnd = 1.0f;     // 连招窗口终点

        [Header("Special Cancel")]
        public float specialCancelStart = 0.2f;   // 触发判定后开窗
        public float specialCancelEnd = 0.6f;     // 关窗（必须 < 1.0f）

        [Header("Knockback")]
        public Vector2 knockbackVelocity;  // 击飞速度向量（由 DamageSystem 计算）
        public bool isInKnockback;         // 是否处于击飞飞行中

        [Header("Feel")]
        public float gravityScale = 2.2f;         // 角色专属重力倍率（默认×1=标准重力）
        public float fastFallMultiplier = 1.6f;   // 快速下落速度倍率
        public float jumpBufferTime = 0.1f;       // 跳跃缓冲时间（秒）
        public float coyoteTime = 0.1f;           // 土狼时间（秒）：离地后仍可起跳的宽限
        public float doubleTapWindow = 0.25f;     // 双击判定窗口（秒），双击方向键 = 跑步

        [Header("Shield")]
        public float currentShieldHP;  // 护盾当前耐久
        public bool isShielding;       // 是否在举盾

        [Header("Parry")]
        public int parryWindowFrames = 10;           // 盾反窗口：举盾后 10 帧内被命中算盾反
        public float parryStunDuration = 1.2f;       // 攻击者被盾反后的眩晕时长(≥1s)
        public float shieldBreakStunDuration = 1.5f; // 破盾眩晕时长

        [Header("Tech")]
        public float techBounceSpeed = 6f;    // 受身成功小反弹速度
        public float techInvincibleTime = 0.5f;  // 受身无敌时长（秒）

        [Header("Grab")]
        public float grabRange = 1.2f;         // 抓取距离（可 Inspector 调）
        public float grabHoldDistance = 1.0f;  // 被抓方锁在面前的间距
        public int escapeMashThreshold = 8;    // 连按攻击 8 次挣脱

        [Header("Respawn")]
        public bool isInvincible;   // 重生后是否无敌
        public float respawnTimer;  // 无敌剩余时间

        [Header("Stock")]
        public int remainingStocks = 3;   // 剩余命数

        #endregion

        #region 2. 运行时状态

        // ---- 公开只读状态 ----
        public bool IsGrounded { get; private set; }               // 是否在地面（每帧更新）
        public FighterStateMachine StateMachine { get; private set; } = new();
        public float CurrentDamage { get; private set; }           // 当前伤害百分比（0~999%）
        public float CurrentKnockbackSpeed { get; set; }           // 当前击飞速度
        public Vector2 velocity;                                   // 速度向量（x=水平, y=垂直）
        public Vector2 MoveInput { get; set; }                     // 输入方向（由 InputManager 每帧写入）
        public bool TechInputHeld { get; set; }                    // 受身输入（复用护盾键，由 InputManager 写入）

        // 抓取双向引用（必须成对设置、对称清理）
        public FighterController grabTarget;   // 我抓着谁（null = 没抓）
        public FighterController grabber;      // 谁抓着我（null = 没被抓）

        // 连段守卫：本次攻击的判定框是否已打开过（防连段提前取消吞掉判定）
        public bool hitboxActivatedThisAttack;

        // 投掷方向枚举
        public enum ThrowDir { Forward, Back, Up, Down }

        // ---- 私有运行时状态 ----
        private FighterState lastAnimState = (FighterState)(-1);  // 上次同步给 Animator 的状态
        private float jumpBufferTimer;      // 跳跃缓冲剩余时间
        private float coyoteTimer;          // 土狼时间剩余
        private bool isRunning;             // 是否处于跑步状态（双击方向键触发）
        private float lastTapTime = -1f;    // 上次按下方向的时间
        private int lastTapDirection;       // 上次按下的方向（-1/1）
        private int lastTapDir;             // 当前是否在按住方向（0=松开）
        private int shieldStartFrame = -999;// 举盾启动帧号（盾反窗口起点，用帧号不用秒）
        private float stunTimer;            // Stun 剩余时间
        private int escapeMashCount;        // 挣扎连按计数
        private float grabTimer;            // 抓取超时计时器（兜底，防异常断链）
        private bool grabWhiff;             // 本次 Grab 是抓空（没抓到人）
        private bool hasLeftGroundInKnockback;  // 击飞中是否离开过地面（受身窗口判据）
        private int knockbackToken;             // 击飞令牌：连续被击飞时旧协程作废
        private bool stunTechable;              // 摔地硬直是否可被护盾提前起身
        private bool _isKilling;                // Kill 防重入

        private readonly List<Platform> droppedPlatforms = new();  //正穿过的平台

        #endregion

        #region 3. 事件

        public System.Action<FighterState, FighterState> OnStateChanged;   // 状态变化（转发自状态机）
        public System.Action<float, FighterController> OnDamaged;          // 受伤（伤害值, 攻击者）
        public System.Action<FighterController> OnKilled;                  // 被击杀
        public System.Action<FighterController> OnParrySuccess;            // 盾反成功（参数=被反的攻击者）

        #endregion

        #region 4. Unity 生命周期

        // 【做什么】组件引用兜底 + 建立从属关系 + 转发状态机事件
        // 【注意】所有组件引用都要 GetComponent 兜底 —— 漏拖一个就 NRE，
        //   而 NRE 冒泡会让上层逻辑错乱（历史踩坑：mainCollider 为空导致"不能抓取直接击退"）
        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (hurtbox == null) hurtbox = GetComponentInChildren<Hurtbox>();
            if (mainCollider == null) mainCollider = GetComponent<Collider2D>();

            // hurtbox 需要知道属于哪个角色
            if (hurtbox != null) hurtbox.owner = this;

            // 给所有 Hitbox 指定所有者，防止自伤
            var hitboxes = GetComponentsInChildren<Hitbox>(true);
            foreach (var hb in hitboxes) hb.owner = this;

            // 转发状态机事件到外部
            StateMachine.OnStateChanged += (prev, next) => OnStateChanged?.Invoke(prev, next);
        }

        private void Start()
        {
            StateMachine.Initialize(FighterState.Idle);                        // 初始状态 = 待机
            currentShieldHP = GameManager.Instance.gameSettings.shieldMaxHP;   // 满盾
            remainingJumps = fighterData.jumpCount;                            // 满跳跃次数
            specialUpUsed = false;                                             // 恢复上B
            remainingStocks = GameManager.Instance.gameSettings.stockCount;    // 同步剩余命数
            GameManager.Instance.RegisterFighter(this);                        // 向 GameManager 注册
        }

        // 【做什么】每帧主循环：地面检测 → 特殊状态早退 → 计时器 → 重力 → 移动 → 动画 → 朝向
        private void Update()
        {
            // ===== 1. 地面检测与缓冲计时 =====
            bool groundHit = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            IsGrounded = groundHit && !isDroppingThrough;   // 落穿中强制非地面

            // ===== 2. 死亡：只关渲染和碰撞，不关 GameObject =====
            if (StateMachine.CurrentState == FighterState.Dead)
            {
                if (visual != null) visual.gameObject.SetActive(false);
                if (mainCollider != null) mainCollider.enabled = false;
                if (hurtbox != null) hurtbox.gameObject.SetActive(false);
                return;
            }

            // ===== 3. 被抓：锁在抓取者正前方 + 清速度，跳过所有移动/跳跃/转向逻辑 =====
            if (StateMachine.CurrentState == FighterState.Grabbed)
            {
                if (grabber != null)
                {
                    Vector3 anchor = grabber.transform.position
                        + new Vector3(grabber.isFacingRight ? 1f : -1f, 0.5f, 0f) * grabHoldDistance;
                    transform.position = Vector3.Lerp(transform.position, anchor, 15f * Time.deltaTime);
                }
                velocity = Vector2.zero;            // 清惯性
                knockbackVelocity = Vector2.zero;

                // 被抓方也必须把状态写给 Animator，否则 Grabbed 动画永远播不出来
                if (StateMachine.CurrentState != lastAnimState)
                {
                    animator.SetInteger("State", (int)StateMachine.CurrentState);
                    lastAnimState = StateMachine.CurrentState;
                }
                return;
            }

            // ===== 4. 计时器与重力 =====
            UpdateTimers();
            UpdateGravity();

            // ===== 4.5 落穿恢复：完全穿过平台后恢复碰撞 =====
            // 脚下实体探测不受 IgnoreCollision 影响：能同时看到被忽略平台与真实地面/下层平台
            Collider2D groundCol = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

            for (int i = droppedPlatforms.Count - 1; i >= 0; i--)
            {
                var plat = droppedPlatforms[i];
                if (plat == null) { droppedPlatforms.RemoveAt(i); continue; }

                // 角色碰撞体完全离开平台碰撞体（上或下）才恢复，避免恢复瞬间重叠被顶回
                bool fullyBelow = mainCollider.bounds.max.y < plat.Collider.bounds.min.y - 0.05f;
                bool fullyAbove = mainCollider.bounds.min.y > plat.Collider.bounds.max.y + 0.05f;

                // 【预防】着地"在别处"也算恢复：双层平台间距 < 角色身高时，头顶还压在上层
                //   平台内无法 fullyBelow；但脚下已踩到别的实体（下层平台/地面）→ 应恢复。
                //   必须排除"探测到的是当前平台本身"（IgnoreCollision 不影响 OverlapCircle）
                bool standingOnOther = groundCol != null
                    && groundCol.GetComponentInParent<Platform>() != plat;

                if (fullyBelow || fullyAbove || standingOnOther)
                {
                    Physics2D.IgnoreCollision(mainCollider, plat.Collider, false);
                    droppedPlatforms.RemoveAt(i);
                    if (droppedPlatforms.Count == 0) isDroppingThrough = false;
                }
            }

            // ===== 5. 双击方向键检测 → 进入跑步 =====
            int currentDir = MoveInput.x > 0.1f ? 1 : (MoveInput.x < -0.1f ? -1 : 0);

            // 刚按下方向（从"松开"到"按下"的边沿）→ 检测双击
            if (currentDir != 0 && lastTapDir == 0)
            {
                if (currentDir == lastTapDirection && Time.time - lastTapTime < doubleTapWindow)
                    isRunning = true;   // 同方向快速按两次 → 跑
                lastTapTime = Time.time;
                lastTapDirection = currentDir;
            }

            // 反方向 → 退出跑步
            if (currentDir != 0 && lastTapDir != 0 && currentDir != lastTapDirection)
                isRunning = false;

            // 松开方向 → 退出跑步 + 重置双击记忆
            if (currentDir == 0)
            {
                isRunning = false;
                lastTapDir = 0;
            }
            else
            {
                lastTapDir = currentDir;
            }

            // ===== 6. 移动与动画 =====
            UpdateMovement();
            UpdateAnimation();

            // ===== 7. 朝向：移动时按移动方向转向；静止时自动面向对手 =====
            bool canTurn = StateMachine.CurrentState == FighterState.Idle
                || StateMachine.CurrentState == FighterState.Run
                || StateMachine.CurrentState == FighterState.Jump
                || StateMachine.CurrentState == FighterState.Fall;

            if (!canTurn) return;

            if (Mathf.Abs(MoveInput.x) > 0.1f)
            {
                SetFacing(MoveInput.x > 0f);   // 有移动输入 → 按移动方向转向
            }
            else
            {
                // 无移动输入 → 面向最近的对手（对战时保持"锁定对手"的手感）
                Transform target = null;
                var players = GameManager.Instance.ActivePlayers;
                if (players != null)
                {
                    foreach (var p in players)
                        if (p != this) { target = p.transform; break; }
                }
                if (target != null)
                    SetFacing(target.position.x > transform.position.x);
            }
        }

        // 【做什么】物理步：把 velocity / knockbackVelocity 写进刚体
        // 【注意】物理赋值必须放 FixedUpdate —— 固定步长保证稳定性，
        //   放 Update 会因帧率波动导致物理抖动
        private void FixedUpdate()
        {
            if (StateMachine.CurrentState == FighterState.Dead)
                return;

            if (isInKnockback)
            {
                // 击飞中离开过地面 → 标记（否则落地受身永不触发）
                if (!IsGrounded) hasLeftGroundInKnockback = true;

                rb.velocity = knockbackVelocity;
                // 水平/垂直衰减模拟空气阻力
                knockbackVelocity.x *= 0.98f;
                knockbackVelocity.y *= 0.98f;
            }
            else
            {
                rb.velocity = velocity;
            }
        }

        private void OnDestroy()
        {
            StateMachine.OnStateChanged -= (prev, next) => OnStateChanged?.Invoke(prev, next);
        }

        #endregion

        #region 5. 核心私有逻辑（按 Update 调用顺序）

        // 【做什么】统一处理所有计时器：蓄力 / 攻击兜底 / 连段 / 无敌 / 护盾 / 眩晕 / 抓取超时
        private void UpdateTimers()
        {
            // ===== 1. 蓄力计时 =====
            if (isCharging)
            {
                chargeTimer += Time.deltaTime;
                if (chargeTimer >= maxChargeTime)
                {
                    chargeTimer = maxChargeTime;   // 钳住
                    ReleaseCharge();               // 满蓄自动释放
                }
                else
                {
                    chargeTimer = Mathf.Min(chargeTimer, maxChargeTime);
                }
            }

            // 被打断清理：被击飞/眩晕/抓取等打断 → 蓄力作废
            if (isCharging && StateMachine.CurrentState != FighterState.Attack)
                isCharging = false;

            // ===== 2. 攻击兜底计时 =====
            // 正常攻击结束靠动画事件 AttackFinished（动画播到末尾 → HitboxEventRelay → Hitbox → isAttacking=false）。
            // 若事件链路断（状态机重组/转换未配好、动画被中断、relay target 为空）
            // → isAttacking 永远 true → 状态机卡死在 Attack（CanAct()==false 不能移动）。
            // 兜底：attackFallbackTimer 超时（1.2s，覆盖所有攻击动画时长）强制结束攻击。
            // 蓄力期间不跑兜底：蓄力时长由"松手"决定，不是固定时长。
            if (isAttacking && !isCharging)
            {
                attackFallbackTimer -= Time.deltaTime;
                if (attackFallbackTimer <= 0f)
                {
                    comboStep = 0;
                    animator.SetInteger("ComboStep", 0);
                    isAttacking = false;
                    if (StateMachine.CurrentState == FighterState.Attack)
                        StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
                }
            }

            // ===== 3. 连段推进（缓冲消费路径）=====
            bool isJabChain = attackData == fighterData.jab1
                            || attackData == fighterData.jab2
                            || attackData == fighterData.jab3;

            if (isAttacking && isJabChain && inputBuffer.ConsumeAttack() && comboStep < 2
                && IsInComboWindow() && hitboxActivatedThisAttack)
            {
                AdvanceCombo();
            }

            // ===== 4. 重生无敌计时 =====
            if (respawnTimer > 0f)
            {
                respawnTimer -= Time.deltaTime;
                if (respawnTimer <= 0f)
                    isInvincible = false;
            }

            // ===== 5. 护盾耐久：举盾消耗，松盾恢复 =====
            if (isShielding)
            {
                currentShieldHP -= GameManager.Instance.gameSettings.shieldRegenPerSecond * Time.deltaTime;
                if (currentShieldHP <= 0f)
                    BreakShield();
            }
            else if (currentShieldHP < GameManager.Instance.gameSettings.shieldMaxHP)
            {
                currentShieldHP += GameManager.Instance.gameSettings.shieldRegenPerSecond * Time.deltaTime;
            }

            // ===== 6. 眩晕倒计时（含护盾起身）=====
            if (StateMachine.CurrentState == FighterState.Stun && stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;

                if (stunTechable && TechInputHeld)
                {
                    // 摔地硬直中按护盾 → 提前起身
                    stunTechable = false;
                    stunTimer = 0f;
                    StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
                }
                else if (stunTimer <= 0f)
                {
                    StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
                }
            }

            // ===== 7. 抓取超时 =====
            if (StateMachine.CurrentState == FighterState.Grab)
            {
                grabTimer += Time.deltaTime;

                if (grabWhiff)
                {
                    // 抓空：播完挥空动作就回 Idle（不锁 5 秒）
                    if (grabTimer > 0.3f)
                    {
                        grabWhiff = false;
                        grabTimer = 0f;
                        StateMachine.TransitionTo(FighterState.Idle);
                    }
                }
                else if (grabTarget != null && grabTimer > 5f)
                {
                    grabTimer = 0f;
                    var timeoutVictim = grabTarget;   // 先保存引用（ReleaseGrab 会清空）
                    ReleaseGrab();

                    // 恢复物理碰撞（抓取期间是 Ignore 的）
                    if (timeoutVictim != null && mainCollider != null && timeoutVictim.mainCollider != null)
                        Physics2D.IgnoreCollision(mainCollider, timeoutVictim.mainCollider, false);

                    if (SmashDebug.IsOn(DebugChannel.Grab))
                        SmashDebug.Log(DebugChannel.Grab, "抓取超时自动松开");
                }
            }
            else
            {
                grabWhiff = false;
                grabTimer = 0f;
            }
        }

        // 【做什么】重力 / 快速下落 / 落地处理 / 受身窗口判定
        private void UpdateGravity()
        {
            // 空中且不在击飞中 → 累加重力
            if (!IsGrounded && !isInKnockback)
                velocity.y += Physics2D.gravity.y * gravityScale * Time.deltaTime;

            if (isInKnockback && IsGrounded)
            {
                // 受身窗口：击飞落回地面 / 贴地滑行中，按住护盾 → 受身
                if (TechInputHeld)
                {
                    PerformTech();
                    return;
                }

                if (hasLeftGroundInKnockback)
                {
                    // 真击飞落回地面 → 进入可受身硬直
                    if (SmashDebug.IsOn(DebugChannel.Movement))
                        SmashDebug.Log(DebugChannel.Movement,
                            $"受身窗口：落地瞬间 | TechInputHeld={TechInputHeld} | 速度={knockbackVelocity.magnitude:F1}");

                    isInKnockback = false;
                    velocity.y = 0f;
                    StateMachine.TransitionTo(FighterState.Idle);
                    stunTechable = true;      // 标记这次硬直可被护盾起身
                    EnterStun(0.3f);
                }
            }
            // 落地 → 重置垂直速度 + 恢复跳跃次数
            else if (IsGrounded && velocity.y <= 0f)
            {
                if (StateMachine.CurrentState == FighterState.Jump
                    || StateMachine.CurrentState == FighterState.Fall)
                {
                    StateMachine.TransitionTo(FighterState.Idle);
                }

                velocity.y = 0f;
                remainingJumps = fighterData.jumpCount;   // 落地重置跳跃次数
                if (!specialUpInStartup) specialUpUsed = false;  // 落地恢复上B，起手期内不恢复

                // 落地时有缓冲的跳跃意图 → 自动起跳
                if (jumpBufferTimer > 0f)
                    TryPerformJump();
            }

            // 下落速度（大乱斗手感：按住 ↓ 立即速降，不是等重力慢慢爬）
            bool fastFall = MoveInput.y < -0.5f && StateMachine.CurrentState == FighterState.Fall;
            float maxFallSpeed = fastFall
                ? fighterData.fastFallSpeed * fastFallMultiplier   // 速降档：15×1.6 = -24
                : fighterData.fallSpeed;                           // 普通档：-8

            if (fastFall)
            {
                velocity.y = -maxFallSpeed;
            }
            else if (velocity.y < -maxFallSpeed)
            {
                velocity.y = -maxFallSpeed;
            }
        }

        // 【做什么】水平移动与地面/空中状态切换
        // 【注意】非可操作状态下：地面清水平速度（防"攻击中按住方向滑动"），
        //   空中保留惯性（大乱斗风格的空中攻击动量手感）
        private void UpdateMovement()
        {
            if (isInKnockback) return;   // 击飞中不能移动（速度由 FixedUpdate 控制）

            if (!StateMachine.CanAct())
            {
                if (IsGrounded) velocity.x = 0f;
                return;
            }

            // 按地面/空中选择速度档位
            float speed = IsGrounded
                ? (isRunning ? fighterData.runSpeed : fighterData.walkSpeed)
                : fighterData.airSpeed;
            velocity.x = MoveInput.x * speed;

            // 按地面/空中与垂直速度切换状态
            if (IsGrounded)
            {
                StateMachine.TransitionTo(Mathf.Abs(MoveInput.x) > 0.1f
                    ? FighterState.Run : FighterState.Idle);
            }
            else
            {
                StateMachine.TransitionTo(velocity.y > 0f
                    ? FighterState.Jump : FighterState.Fall);
            }
        }

        // 【做什么】把运行时参数同步给 Animator（动画 Blend Tree / 状态转换读这些参数）
        private void UpdateAnimation()
        {
            if (animator == null) return;

            // ===== 1. 播放速率 =====
            // 移动越慢动画越慢；但攻击/举盾/抓取必须锁 1.0 倍速！
            // 原因：animator.speed 是全局缩放（影响所有层），空中攻击时若水平速度低，
            //   动画被压到 0.4 倍速 → "跳起翻转+劈砍"被慢放成"奔跑翻转再攻击"，观感异常。
            bool lockAnimSpeed = isAttacking || isShielding
                  || StateMachine.CurrentState == FighterState.Grab
                  || StateMachine.CurrentState == FighterState.Grabbed;

            animator.speed = lockAnimSpeed
                ? 1f
                : Mathf.Lerp(0.4f, 0.8f, Mathf.Abs(velocity.x) / fighterData.runSpeed);

            // ===== 2. 状态参数（只在变化时写）=====
            // 每帧写会触发 AnyState 重入循环
            if (StateMachine.CurrentState != lastAnimState)
            {
                animator.SetInteger("State", (int)StateMachine.CurrentState);
                lastAnimState = StateMachine.CurrentState;
            }

            // ===== 3. 运动参数 =====
            animator.SetFloat("Speed", Mathf.Abs(velocity.x));      // 速度 → 移动动画 blend
            animator.SetBool("IsGrounded", IsGrounded);
            animator.SetFloat("AirFactor", IsGrounded ? 0f : 1f);   // 0=地面攻击，1=空中攻击
            animator.SetFloat("VerticalSpeed", velocity.y);         // 垂直速度 → 跳跃/下落动画
            animator.SetFloat("Damage", CurrentDamage);             // 伤害值 → 受击表现

            // ===== 4. Action Layer 权重（总开关）=====
            // 该层 m_DefaultWeight = 0，不主动点亮就完全不显示。
            // 进攻击立即抬到 1（消灭起手混合）；退出攻击平滑回落（保留收招过渡）。
            float targetWeight = (isAttacking || isShielding) ? 1f : 0f;
            float cur = animator.GetLayerWeight(1);
            animator.SetLayerWeight(1, targetWeight > cur ? targetWeight : Mathf.Lerp(cur, targetWeight, 10f * Time.deltaTime));
        }

        // 【做什么】实际执行跳跃（按键触发与落地缓冲共用）
        // 【注意】一段跳要求着地或土狼时间内；二段跳空中可跳；二段跳力用 0.85 倍（手感）
        private void TryPerformJump()
        {
            bool isFirstJump = remainingJumps == fighterData.jumpCount;
            bool groundOk = isFirstJump ? (IsGrounded || coyoteTimer > 0f) : true;

            if (!groundOk || remainingJumps <= 0) return;

            velocity.y = fighterData.jumpForce * (isFirstJump ? 1f : 0.85f);
            remainingJumps--;
            StateMachine.TransitionTo(FighterState.Jump);
            isJumping = true;
            jumpBufferTimer = 0f;   // 消耗缓冲
        }

        // 【做什么】硬直结束协程：把击飞惯性交接给正常操控
        // 【注意】用 knockbackToken 作废旧协程 —— 连续被击飞时，上一次的协程必须失效，
        //   否则硬直会被提前结束
        private System.Collections.IEnumerator EndHitstunAfter(float duration)
        {
            int token = knockbackToken;
            yield return new WaitForSeconds(duration);

            if (token != knockbackToken) yield break;   // 期间又被击飞 → 旧协程作废

            if (!isInKnockback) yield break;

            if (!IsGrounded)
            {
                // 空中：惯性交接给 velocity，恢复操控
                velocity = knockbackVelocity;
                knockbackVelocity = Vector2.zero;
                isInKnockback = false;

                // 大乱斗规则：硬直结束 = 恢复空中操控，必给一跳回场。
                // 不能给 jumpCount（"第一跳"要求贴地/土狼，空中会被卡死）
                remainingJumps = Mathf.Max(1, fighterData.jumpCount - 1);
                StateMachine.TransitionTo(FighterState.Fall);
            }
            else
            {
                // 贴地击飞：硬直结束直接回 Idle（没有被推离地面）
                knockbackVelocity = Vector2.zero;
                isInKnockback = false;
                StateMachine.TransitionTo(FighterState.Idle);
            }
        }

        // 【做什么】投掷后延迟恢复物理碰撞（0.6s 足够 victim 飞出身体范围）
        private System.Collections.IEnumerator RestoreCollisionAfterThrow(FighterController victim, float delay = 0.6f)
        {
            yield return new WaitForSeconds(delay);

            if (victim != null && mainCollider != null && victim.mainCollider != null)
                Physics2D.IgnoreCollision(mainCollider, victim.mainCollider, false);
        }

        // 【做什么】脱离抓取 —— 任何路径（投完/挣脱/超时）都走这里
        // 【重点】对称清理：既要处理"我是抓取方"，也要处理"我是被抓方"
        // 【注意】调用方若需要用到对方引用，必须【先存一份】——本方法会清空 grabTarget/grabber
        private void ReleaseGrab()
        {
            // 我是抓取方 → 先清我的目标
            if (grabTarget != null)
            {
                var victim = grabTarget;
                grabTarget = null;
                victim.grabber = null;                  // 清对方反向引用
                victim.escapeMashCount = 0;             // 顺手重置挣扎计数

                if (victim.StateMachine.CurrentState == FighterState.Grabbed)
                    victim.StateMachine.TransitionTo(FighterState.Idle);
                if (StateMachine.CurrentState == FighterState.Grab)
                    StateMachine.TransitionTo(FighterState.Idle);   // 抓取方也要回 Idle
            }

            // 我是被抓方 → 再清抓我的人
            if (grabber != null)
            {
                var holder = grabber;
                grabber = null;
                holder.grabTarget = null;

                if (holder.StateMachine.CurrentState == FighterState.Grab)
                    holder.StateMachine.TransitionTo(FighterState.Idle);
                if (StateMachine.CurrentState == FighterState.Grabbed)
                    StateMachine.TransitionTo(FighterState.Idle);
            }
        }

        #endregion

        #region 6. 公开 API — 输入层调用

        // 【做什么】设置防御状态（由 InputManager 每帧调用，内部判边沿）
        // 【守卫】击飞中/死亡/眩晕/盾硬直/抓取中/被抓中 → 不能举盾
        public void SetShielding(bool active)
        {
            if (StateMachine.CurrentState == FighterState.Knockback
                || StateMachine.CurrentState == FighterState.Dead
                || StateMachine.CurrentState == FighterState.Stun
                || StateMachine.CurrentState == FighterState.ShieldStun
                || StateMachine.CurrentState == FighterState.Grab      // 抓取中不能举盾
                || StateMachine.CurrentState == FighterState.Grabbed)  // 被抓中不能举盾
                return;

            // 只有"盾状态真实变化"才切换 —— 不干扰其他状态
            if (active && StateMachine.CurrentState != FighterState.Shield)
            {
                shieldStartFrame = Time.frameCount;   // 盾启动瞬间才是盾反窗口起点
                isShielding = true;
                StateMachine.TransitionTo(FighterState.Shield);

                if (SmashDebug.IsOn(DebugChannel.Combat))
                    SmashDebug.Log(DebugChannel.Combat, $"举盾 | shieldStartFrame={shieldStartFrame} 帧={Time.frameCount}");
            }
            else if (!active && StateMachine.CurrentState == FighterState.Shield)
            {
                isShielding = false;
                StateMachine.TransitionTo(FighterState.Idle);
            }
        }

        // 【做什么】跳跃入口（由 InputManager 调用）
        // 【注意】即使当前不能跳，也先记下"跳跃意图"（jumpBufferTimer），
        //   落地瞬间会自动补跳 —— 这是格斗游戏的跳跃缓冲手感
        public void TryJump()
        {
            if (!StateMachine.CanAct() || isInKnockback) return;

            jumpBufferTimer = jumpBufferTime;
            TryPerformJump();
        }

        // 【做什么】攻击入口（由 InputManager 调用，传入选好的攻击数据）
        // 【特殊拦截】若正抓着人 → 转成投掷（方向由当前摇杆决定）
        // 【分支】正在攻击中 → 走连段推进 / 缓冲；否则正常起手
        public void TryAttack(AttackData data)
        {
            // 抓取中按攻击 = 投掷
            if (grabTarget != null && StateMachine.CurrentState == FighterState.Grab)
            {
                PerformThrow(GetThrowDirection());
                return;
            }

            if (isInKnockback || isShielding || StateMachine.CurrentState == FighterState.Dead)
                return;

            // ===== 连招：正在攻击中 → 判断能否推进到下一段 =====
            if (isAttacking)
            {
                bool isJabChainNow = attackData == fighterData.jab1
                      || attackData == fighterData.jab2
                      || attackData == fighterData.jab3;

                if (isJabChainNow && comboStep < 2)
                {
                    if (IsInComboWindow() && hitboxActivatedThisAttack)
                    {
                        AdvanceCombo();
                    }
                    else
                    {
                        inputBuffer.BufferAttack();   // 按太早 → 缓冲，窗口到了自动消费
                    }
                }
                return;
            }

            // ===== 正常起手 =====
            inputBuffer.Clear();
            comboStep = 0;
            SetAttackAnim(data);
            animator.SetInteger("ComboStep", 0);
            attackData = data;

            // 把攻击数据广播给所有判定框（引用传递，改一处全改）
            foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                hb.attackData = attackData;

            isAttacking = true;
            hitboxActivatedThisAttack = false;
            attackTimer = attackData.startupTime + attackData.activeTime + attackData.recoveryTime;
            attackFallbackTimer = 1.2f;   // 兜底：动画事件断时 1.2s 后强制结束攻击

            // 下强攻击（TiltDown）在地面时附带小跳，做出"下劈"手感
            if (data == fighterData.tiltDown && IsGrounded)
            {
                velocity.y = fighterData.jumpForce * 0.7f;
                remainingJumps = 0;         // 防止空中二段跳
                IsGrounded = false;         // 立即离地，防止落地后被 Idle 覆盖
            }

            StateMachine.TransitionTo(FighterState.Attack);
        }

        // 【做什么】特殊攻击（投射物型：Archer 箭 / Mage 火球）
        // 【注意】复用 Attack 状态播动画，用 0.5s 快速兜底结束
        public void TrySpecial(AttackData data, bool held = false)
        {
            if (data == null || isInKnockback || StateMachine.CurrentState == FighterState.Dead)
                return;
            if (isShielding) return;

            // 攻击中：只有"该招标记可必杀取消 + 当前在取消窗口内"才放行
            if (isAttacking && !IsInSpecialCancelWindow()) return;

            // ===== 取消路径清理（防 Jab 漏判定式事故：残留激活判定框）=====
            if (isAttacking)
            {
                // 1. 关掉所有还开着的判定框（否则取消后残留激活框 → 错误命中）
                foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                    hb.Deactivate();

                // 2. 清连段状态（对齐 UpdateTimers 兜底的清理）
                comboStep = 0;
                animator.SetInteger("ComboStep", 0);
                inputBuffer.Clear();

                // 3. 攻击数据切换为必杀数据（替换当前攻击）
                attackData = data;
                foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                    hb.attackData = attackData;
            }

            // ===== 分支 1：SU 位移闪现（无投射物） =====
            if (data == fighterData.specialUp)
            {
                if (specialUpUsed) return;  //空中不能二段瞬移
                DoSpecialUp(data);
                return;
            }

            isAttacking = true;
            hitboxActivatedThisAttack = false;

            // ===== 分支 2：投射物型（SN 火球 / SF 气波 / SD 石头） =====
            if (data.projectilePrefab == null)
            {
                if (SmashDebug.IsOn(DebugChannel.Combat))
                    SmashDebug.Log(DebugChannel.Combat, $"特殊攻击 {data.attackName} 缺 projectilePrefab");
                return;
            }

            // 出生点：偏移 x 按朝向翻转；只有石头用远近分档
            float reach = (data == fighterData.specialDown && held) ? farSpecialRange : 1f;
            Vector3 spawnPos = transform.position
                    + new Vector3((isFacingRight ? 1f : -1f) * data.projectileSpawnOffset.x
                    * reach,
                    data.projectileSpawnOffset.y, 0f);

            var proj = Projectile.Spawn(data.projectilePrefab, spawnPos, isFacingRight ? 1 : -1, data, this);
            if (proj != null && data.projectileSpeed > 0f)
                proj.speed = data.projectileSpeed;   // 招式定义的速度优先

            // 播出场动作（火球投掷手 / 踢腿 / 扔石）→ 由 AttackFinished 或 0.5s 兜底收尾
            SetAttackAnim(data);
            StateMachine.TransitionTo(FighterState.Attack);
            attackFallbackTimer = 0.5f;
        }

        // 【做什么】上B：起始闪光 → 垂直瞬移 → 终点闪光，全程短暂无敌
        private void DoSpecialUp(AttackData data)
        {
            isAttacking = true;
            hitboxActivatedThisAttack = false;
            specialUpUsed = true;
            specialUpInStartup = true;  //起手缓冲

            // 1. 起始位置闪光（场景级对象，不跟随角色）
            var startFx = ObjectPooler.Instance.Spawn(data.hitEffectPrefab, transform.position, Quaternion.identity);
            if (startFx != null) ObjectPooler.Instance.Despawn(startFx, 1f);

            StartCoroutine(TeleportUp(data));

            // 5. 占攻击态（无动画，特效即表现）
            SetAttackAnim(data);
            StateMachine.TransitionTo(FighterState.Attack);
            attackFallbackTimer = 0.5f;
        }

        private System.Collections.IEnumerator TeleportUp(AttackData data)
        {
            yield return new WaitForSeconds(0.15f);   // 闪光先亮，角色原地

            // 2. 位移：直接改位置（teleport）+ 清残留速度
            Vector3 targetPos = transform.position + new Vector3(0f, specialUpDistance, 0f);
            transform.position = targetPos;
            velocity.x = 0f;
            velocity.y = fighterData.jumpForce * 0.4f;      // // 上升缓冲，约 5.6 的向上速度
            IsGrounded = false;   // 瞬移到空中，防 Idle 覆盖
            specialUpInStartup = false;   // 已离地，起手期结束

            // 3. 终点闪光
            var endFx = ObjectPooler.Instance.Spawn(data.hitEffectPrefab, targetPos, Quaternion.identity);
            if (endFx != null) ObjectPooler.Instance.Despawn(endFx, 1f);

            // 4. 短暂无敌（复用重生无敌计时：respawnTimer 递减，归零自动关 isInvincible）
            isInvincible = true;
            respawnTimer = 0.2f;
        }

        // 【做什么】抓取入口（由 InputManager 调用）
        // 【流程】前方圆检测 → 筛选可抓目标 → 双向引用 + 双状态转换 + 忽略双方碰撞
        // 【可抓条件】能操作(CanAct) 或 举盾(盾可被抓)；排除击飞/硬直/已被抓/无敌/死亡
        // 【抓空】照样进 Grab 播挥空动作，0.3s 后自动回 Idle
        public void TryGrab()
        {
            // ===== 守卫：抓取方条件 =====
            if (isInKnockback || StateMachine.CurrentState == FighterState.Dead) return;
            if (StateMachine.CurrentState == FighterState.Attack) return;    // 攻击中不能抓
            if (grabTarget != null) return;                                  // 已在抓，别重复
            if (StateMachine.CurrentState == FighterState.Grab) return;      // 已在抓取状态

            // ===== 前方检测：角色面前一个圆 =====
            // 中心点 = 角色位置 + 面向方向 × grabRange（把圆推到身前）
            Vector2 center = (Vector2)transform.position
                + new Vector2(isFacingRight ? 1f : -1f, 0f) * grabRange;
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, grabRange);

            // ===== 找可抓目标 =====
            FighterController victim = null;
            foreach (var hit in hits)
            {
                var hb = hit.GetComponent<Hurtbox>();
                if (hb == null || hb.owner == this) continue;   // 只要对手的受击框

                bool grabbable = hb.owner.StateMachine.CanAct()
                    || hb.owner.StateMachine.CurrentState == FighterState.Shield;   // 盾可以被抓
                if (!grabbable) continue;
                if (hb.owner.isInvincible || hb.owner.StateMachine.CurrentState == FighterState.Grabbed) continue;

                victim = hb.owner;
                break;
            }

            if (victim == null)
            {
                // 抓空：照样进 Grab 播抓取动作（挥空），稍后自动回 Idle
                grabWhiff = true;
                grabTimer = 0f;
                StateMachine.TransitionTo(FighterState.Grab);
                return;
            }

            // ===== 双向引用 + 双状态转换 =====
            isShielding = false;          // 自己（可能从盾抓）收盾
            victim.isShielding = false;   // 对方（举盾被抓）收盾
            grabTarget = victim;
            victim.grabber = this;
            StateMachine.TransitionTo(FighterState.Grab);
            victim.StateMachine.TransitionTo(FighterState.Grabbed);

            // 抓取期间双方物理碰撞忽略（大乱斗规则：被抓者穿过抓取者，不推挤）
            Physics2D.IgnoreCollision(mainCollider, victim.mainCollider, true);

            if (SmashDebug.IsOn(DebugChannel.Grab))
                SmashDebug.Log(DebugChannel.Grab, $"{name} 抓住 {victim.name}");
        }

        // 【做什么】落穿平台：找到脚下最近的 Platform 并让它临时翻转单向
        public void DropThroughPlatform()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                groundCheck.position, groundCheckRadius + 0.05f, groundLayer);

            foreach (var c in hits)
            {
                var plat = c.GetComponentInParent<Platform>();
                if (plat != null)
                {
                    Physics2D.IgnoreCollision(mainCollider, plat.Collider, true);
                    droppedPlatforms.Add(plat);
                    isDroppingThrough = true;                        // 落穿期间不信 groundCheck
                    velocity.y = -fighterData.fastFallSpeed * 0.5f;  // 立即给下坠初速，避免"悬在半空"的顿挫
                    break;
                }
            }
        }

        // 【做什么】蓄力开始（长按确认时由 InputManager 调用）
        // 【注意】复用 Attack 状态来锁移动；用 200/210/220 作为 AttackType
        public void StartCharge(AttackData baseData)
        {
            if (baseData == null || isInKnockback
                || StateMachine.CurrentState == FighterState.Dead
                || StateMachine.CurrentState == FighterState.Grab) return;

            isCharging = true;
            chargeTimer = 0f;
            chargeBaseData = baseData;
            attackData = baseData;
            comboStep = 0;

            isAttacking = true;             // 进入攻击态（锁移动）—— 同时也是 Action Layer 权重开关
            hitboxActivatedThisAttack = false;
            attackFallbackTimer = 1.2f;     // 兜底：动画事件断时 1.2s 后强制结束攻击

            StateMachine.TransitionTo(FighterState.Attack);
            animator.SetInteger("AttackType", GetChargeIndex(baseData));
        }

        // 【做什么】蓄力释放（松手时由 InputManager 调用）
        // 【重点】必须 CloneAttackData 深拷贝再改数值 —— AttackData 是引用类型，
        //   直接改会永久污染 ScriptableObject 资产（下次进游戏伤害就变了）
        public void ReleaseCharge()
        {
            if (!isCharging) return;   // 幂等：被打断后松手不误触发

            // 1. 按蓄力等级算加权伤害/击退
            float lvl = Mathf.Clamp01(chargeTimer / maxChargeTime);
            AttackData final = CloneAttackData(chargeBaseData);
            final.damage = chargeBaseData.damage * Mathf.Lerp(1f, chargeDamageMultiplier, lvl);
            final.knockbackBase = chargeBaseData.knockbackBase
                                * Mathf.Lerp(1f, chargeKnockbackMultiplier, lvl);

            // 2. 正常起手（复刻 TryAttack 后半段）
            isCharging = false;
            comboStep = 0;
            SetAttackAnim(final);
            attackData = final;

            foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                hb.attackData = final;

            isAttacking = true;
            hitboxActivatedThisAttack = false;
            attackTimer = final.startupTime + final.activeTime + final.recoveryTime;
            attackFallbackTimer = 1.2f;
            StateMachine.TransitionTo(FighterState.Attack);
        }

        #endregion

        #region 7. 公开 API — 战斗 / 系统调用

        // 【做什么】受击入口（由 Hitbox.OnTriggerEnter2D 调用）
        // 【流程】无敌/死亡早退 → 盾分支（盾反 / 扣盾 / 破盾）→ 累加伤害 → 计算击飞 → 应用击飞
        public void ApplyDamage(AttackData attack, FighterController attacker)
        {
            if (isInvincible || StateMachine.CurrentState == FighterState.Dead)
                return;

            if (SmashDebug.IsOn(DebugChannel.Combat))
                SmashDebug.Log(DebugChannel.Combat, $"命中！防御方={name} isShielding={isShielding} 帧={Time.frameCount}");

            // ===== 举盾防御：伤害全部由护盾承受，不掉血、不击飞 =====
            if (isShielding)
            {
                if (SmashDebug.IsOn(DebugChannel.Combat))
                    SmashDebug.Log(DebugChannel.Combat, $"进盾分支 帧差={Time.frameCount - shieldStartFrame}");

                // 盾反：举盾后 parryWindowFrames 帧内被命中
                if (Time.frameCount - shieldStartFrame <= parryWindowFrames)
                {
                    if (SmashDebug.IsOn(DebugChannel.Combat))
                        SmashDebug.Log(DebugChannel.Combat, "盾反触发！");
                    PerformParry(attacker);
                    return;    // 不掉血、不掉盾
                }

                currentShieldHP -= DamageSystem.CalculateShieldDamage(attack, currentShieldHP);
                if (currentShieldHP <= 0f)
                    BreakShield();   // 破盾 → 进 Stun 大眩晕
                return;              // 关键：盾防走完直接返回，不再掉血/击飞
            }

            // ===== 应用伤害（含全局缩放系数）=====
            float finalDamage = attack.damage * GameManager.Instance.gameSettings.damageRatio;
            CurrentDamage += finalDamage;
            OnDamaged?.Invoke(finalDamage, attacker);

            // ===== 计算并应用击飞 =====
            float knockSpeed = DamageSystem.CalculateKnockbackVelocity(
                attack, CurrentDamage, fighterData.weight);
            Vector2 knockDir = DamageSystem.CalculateKnockbackDirection(
                attack, transform.position - attacker.transform.position);

            ApplyKnockback(knockDir, knockSpeed);
        }

        // 【做什么】把击飞速度和方向应用到角色上
        // 【参数】direction = 击飞方向；speed = 速度标量；hitstunOverride = 手动指定硬直（<0 表示按公式算）
        public void ApplyKnockback(Vector2 direction, float speed, float hitstunOverride = -1f)
        {
            if (SmashDebug.IsOn(DebugChannel.Combat))
                SmashDebug.Log(DebugChannel.Combat,
                    $"击飞 speed={speed} state={StateMachine.CurrentState} grounded={IsGrounded}");

            knockbackToken++;                  // 作废上一次的硬直协程
            CurrentKnockbackSpeed = speed;
            knockbackVelocity = direction.normalized * speed;
            isInKnockback = true;
            hasLeftGroundInKnockback = false;

            float hitstunDuration = hitstunOverride >= 0f
                ? hitstunOverride
                : DamageSystem.CalculateHitstun(speed);

            StateMachine.TransitionTo(FighterState.Knockback);
            StartCoroutine(EndHitstunAfter(hitstunDuration));
        }

        // 【做什么】执行盾反：攻击方进眩晕，自己无伤且不掉盾（无伤由 ApplyDamage 的 return 保证）
        public void PerformParry(FighterController attacker)
        {
            attacker?.EnterStun(parryStunDuration);

            // 闪白反馈
            var cam = FindObjectOfType<CameraManager>();
            if (cam != null) cam.FlashWhite(0.15f);

            // TODO(M8): 盾反音效 PlayParrySFX()

            OnParrySuccess?.Invoke(attacker);
        }

        // 【做什么】受身（Tech）：清击飞 + 小反弹 + 短暂无敌 + 回可操作状态
        // 【注意】无敌是复用重生无敌计时器（respawnTimer），UpdateTimers 会自动解除
        public void PerformTech()
        {
            if (SmashDebug.IsOn(DebugChannel.Movement))
                SmashDebug.Log(DebugChannel.Movement,
                    $"{name} 受身成功！反弹 y={techBounceSpeed}，无敌 {techInvincibleTime}s");

            var cam = FindObjectOfType<CameraManager>();
            if (cam != null) cam.FlashWhite(0.2f);

            knockbackVelocity = Vector2.zero;
            isInKnockback = false;
            velocity.y = techBounceSpeed;       // 小反弹
            isInvincible = true;
            respawnTimer = techInvincibleTime;
            StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
        }

        // 【做什么】四向投掷
        // 【流程】选数据 → 解除抓取 → 播动画 → 结算伤害 → 按朝向构造方向 → 瞬移受害人 → 应用击飞 → 延迟恢复碰撞
        // 【注意】方向不能复用 CalculateKnockbackDirection（那是"被打过来的方向"），
        //   投掷方向必须按自身朝向构造
        // 【注意】瞬移必须用 rb.position 而非 transform.position（物理系统会把 transform 值拉回校正）
        public void PerformThrow(ThrowDir dir)
        {
            if (grabTarget == null) return;
            FighterController victim = grabTarget;

            AttackData data = dir switch
            {
                ThrowDir.Forward => fighterData.throwForward,
                ThrowDir.Back    => fighterData.throwBack,
                ThrowDir.Up      => fighterData.throwUp,
                ThrowDir.Down    => fighterData.throwDown,
                _                => fighterData.throwForward,   // 兜底：未命名值 → 默认前投
            };
            if (data == null) return;

            // 1) 先解除抓取，让对方状态干净
            ReleaseGrab();

            // 2) 播放投掷动画
            SetAttackAnim(data);
            attackData = data;
            isAttacking = true;
            hitboxActivatedThisAttack = false;
            attackTimer = data.startupTime + data.activeTime + data.recoveryTime;
            attackFallbackTimer = 1.2f;
            StateMachine.TransitionTo(FighterState.Attack);

            // 3) 伤害照常累加
            float finalDamage = data.damage * GameManager.Instance.gameSettings.damageRatio;
            victim.CurrentDamage += finalDamage;
            victim.OnDamaged?.Invoke(finalDamage, this);

            // 后投先转身，让"投掷方向"与朝向一致
            if (dir == ThrowDir.Back) SetFacing(!isFacingRight);

            Vector2 throwDir = dir switch
            {
                ThrowDir.Forward or ThrowDir.Back => isFacingRight ? Vector2.right : Vector2.left,
                ThrowDir.Up   => Vector2.up,
                ThrowDir.Down => Vector2.down,
                _             => Vector2.zero,   // 兜底：防御性归零
            };

            // 4) 瞬移受害人到投掷方向外侧（固定距离，不依赖碰撞体尺寸）
            const float clearDist = 1.5f;   // 足够跨越角色半宽 + 安全间距
            victim.rb.position = (Vector2)rb.position + throwDir * clearDist;

            // 5) 计算击飞并应用
            float knockSpeed = DamageSystem.CalculateKnockbackVelocity(
                data, victim.CurrentDamage, victim.fighterData.weight);
            victim.ApplyKnockback(throwDir, knockSpeed);

            // 6) 投掷后 0.6s 再恢复碰撞（等 victim 飞出身体范围）
            StartCoroutine(RestoreCollisionAfterThrow(victim));
        }

        // 【做什么】进入眩晕（破盾 / 被盾反时调用）
        public void EnterStun(float duration)
        {
            stunTimer = duration;
            StateMachine.TransitionTo(FighterState.Stun);
        }

        // 【做什么】破盾：护盾耐久归零时触发，进大眩晕
        public void BreakShield()
        {
            isShielding = false;
            currentShieldHP = 0f;
            EnterStun(shieldBreakStunDuration);
        }

        // 【做什么】重生：位置复位 + 清状态 + 短时间无敌 + 恢复渲染与碰撞
        // 【注意】必须用 StateMachine.Initialize(Idle) 而不是 TransitionTo ——
        //   Dead 状态无法 TransitionTo(Idle)，会被转换规则拒绝
        public void Respawn(Vector3 position)
        {
            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match, $"{name} 开始重生，位置: {position}");

            transform.position = position;
            CurrentDamage = 0f;
            CurrentKnockbackSpeed = 0f;
            knockbackVelocity = Vector2.zero;
            velocity = Vector2.zero;
            isInKnockback = false;
            isInvincible = true;
            respawnTimer = GameManager.Instance.gameSettings.respawnTime;
            currentShieldHP = GameManager.Instance.gameSettings.shieldMaxHP;
            isShielding = false;
            remainingJumps = fighterData.jumpCount;

            // 强制重置状态机到 Idle（Dead 无法 TransitionTo Idle）
            StateMachine.Initialize(FighterState.Idle);

            if (rb != null) rb.velocity = Vector2.zero;

            // 恢复渲染和碰撞
            if (visual != null) visual.gameObject.SetActive(true);
            if (mainCollider != null) mainCollider.enabled = true;
            if (hurtbox != null) hurtbox.gameObject.SetActive(true);

            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match,
                    $"{name} 重生完成 状态={StateMachine.CurrentState} visual.active={visual?.gameObject.activeSelf}");
        }

        // 【做什么】击杀：扣命 + 进 Dead 状态 + 广播 OnKilled
        // 【注意】_isKilling 防重入 —— 出界可能被多个 BlastZone 同时触发
        public void Kill()
        {
            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match,
                    $"{name} 被击杀 状态={StateMachine.CurrentState} 剩余命数={remainingStocks}");

            if (StateMachine.CurrentState == FighterState.Dead) return;
            if (_isKilling) return;
            _isKilling = true;

            remainingStocks = Mathf.Max(0, remainingStocks - 1);

            if (SmashDebug.IsOn(DebugChannel.Match))
                SmashDebug.Log(DebugChannel.Match, $"扣命后剩余: {remainingStocks}");

            StateMachine.TransitionTo(FighterState.Dead);
            OnKilled?.Invoke(this);

            _isKilling = false;
        }

        // 【做什么】挣扎逃脱（被抓时连按攻击键，由 InputManager 调用）
        // 【注意】先保存抓取者引用再 ReleaseGrab —— ReleaseGrab 会清空 grabber
        public void OnMashGrabEscape()
        {
            if (StateMachine.CurrentState != FighterState.Grabbed) return;

            escapeMashCount++;

            if (SmashDebug.IsOn(DebugChannel.Grab))
                SmashDebug.Log(DebugChannel.Grab, $"挣扎 {escapeMashCount}/{escapeMashThreshold}");

            if (escapeMashCount < escapeMashThreshold) return;

            var escapeHolder = grabber;   // 先保存抓取者引用（ReleaseGrab 会清空）
            ReleaseGrab();

            // 恢复物理碰撞
            if (escapeHolder != null && escapeHolder.mainCollider != null && mainCollider != null)
                Physics2D.IgnoreCollision(escapeHolder.mainCollider, mainCollider, false);
        }

        #endregion

        #region 8. 私有工具方法

        // 【做什么】连段推进到下一段（TryAttack 与 UpdateTimers 两条路径共用）
        // 【副作用】改 comboStep / attackData / 重新广播给所有 Hitbox / 状态机重入 Attack
        private void AdvanceCombo()
        {
            comboStep++;
            hitboxActivatedThisAttack = false;
            animator.SetInteger("ComboStep", comboStep);

            attackData = GetComboAttack(comboStep);
            SetAttackAnim(attackData);

            foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                hb.attackData = attackData;

            attackTimer = attackData.startupTime + attackData.activeTime + attackData.recoveryTime;
            attackFallbackTimer = 1.2f;
            StateMachine.TransitionTo(FighterState.Attack);

            if (SmashDebug.IsOn(DebugChannel.Combat))
                SmashDebug.Log(DebugChannel.Combat, $"Jab{comboStep + 1} 伤害={attackData.damage}");
        }

        // 【做什么】朝向翻转：镜像 Visual 的 X 缩放
        // 【为什么不用旋转】3D 模型旋转会背面朝镜头，必须用负缩放镜像
        // 【注意】只翻符号、保留原有缩放大小
        private void SetFacing(bool faceRight)
        {
            if (faceRight == isFacingRight) return;

            isFacingRight = faceRight;
            if (visual == null) return;

            Vector3 s = visual.localScale;
            s.x = Mathf.Abs(s.x) * (faceRight ? 1f : -1f);
            visual.localScale = s;
        }

        // 【做什么】告诉 Animator 播哪个攻击动画（用 AttackData.animIndex 作为 AttackType）
        private void SetAttackAnim(AttackData data)
        {
            animator.SetInteger("AttackType", data != null ? data.animIndex : 0);
        }

        // 【做什么】判断当前是否在连招窗口内
        // 【为什么用 normalizedTime】对动画帧精确，且自动兼容 Animator 倍速
        private bool IsInComboWindow()
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(1);   // 层 1 = Action Layer
            float t = info.normalizedTime % 1f;
            return t >= comboWindowStart && t <= comboWindowEnd;
        }

        // 【做什么】判断当前攻击是否处于"可必杀取消"窗口内
        // 【为什么和 IsInComboWindow 同构】都读层 1 Action Layer 的动画进度，
        //   自动兼容 Animator 倍速；只是窗口字段不同
        private bool IsInSpecialCancelWindow()
        {
            if(attackData == null || !attackData.canSpecialCancel) return false;
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(1);   // 层 1 = Action Layer
            float t = info.normalizedTime % 1f;
            return t >= specialCancelStart && t <= specialCancelEnd;
        }

        // 【做什么】把蓄力基值映射到 Animator 的 AttackType
        // 【编号规约】挥击 xx → 蓄力 xx×10（SmashSide=20→200, SmashUp=21→210, SmashDown=22→220）
        private int GetChargeIndex(AttackData baseData)
        {
            if (baseData == fighterData.smashUp) return 210;
            if (baseData == fighterData.smashDown) return 220;
            return 200;   // 兜底：侧蓄力（前/后同招）
        }

        // 【做什么】按连段段数取对应的攻击数据
        private AttackData GetComboAttack(int step)
        {
            return step switch
            {
                1 => fighterData.jab2,
                2 => fighterData.jab3,
                _ => fighterData.jab1,
            };
        }

        // 【做什么】按当前摇杆输入决定投掷方向
        // 【优先级】垂直优先（上/下）→ 水平（按与朝向的关系分前/后）→ 无方向默认前投
        // 【注意】相对朝向判定：推的方向与面朝方向同向 = 前投，反向 = 后投
        private ThrowDir GetThrowDirection()
        {
            Vector2 input = MoveInput;            // InputManager 每帧无条件写入，Grab 状态下也有值
            float ax = Mathf.Abs(input.x);
            float ay = Mathf.Abs(input.y);
            const float deadzone = 0.5f;

            // 1. 垂直优先：上下推得明显 → 上投/下投
            if (ay > ax && ay > deadzone)
                return input.y > 0f ? ThrowDir.Up : ThrowDir.Down;

            // 2. 水平：判断推的是"前"还是"后"（相对面朝方向）
            if (ax > deadzone)
            {
                bool pushingForward = (input.x > 0f) == isFacingRight;
                return pushingForward ? ThrowDir.Forward : ThrowDir.Back;
            }

            // 3. 没推方向 → 默认前投（防御性兜底）
            return ThrowDir.Forward;
        }

        // 【做什么】深拷贝一份 AttackData
        // 【为什么必须拷贝】AttackData 是 class（引用类型），蓄力改数值会直接写进
        //   ScriptableObject 资产，导致资产被永久污染。改攻击数值前一律先克隆。
        // 【注意】新增 AttackData 字段时，这里必须同步补充，否则克隆会丢字段
        private AttackData CloneAttackData(AttackData src)
        {
            return new AttackData
            {
                attackName = src.attackName,
                damage = src.damage,
                shieldDamage = src.shieldDamage,
                knockbackAngle = src.knockbackAngle,
                knockbackBase = src.knockbackBase,
                knockbackGrowth = src.knockbackGrowth,
                hitstunOverride = src.hitstunOverride,
                startupTime = src.startupTime,
                activeTime = src.activeTime,
                recoveryTime = src.recoveryTime,
                hitstopDuration = src.hitstopDuration,
                cancelIntoAttacks = src.cancelIntoAttacks,
                canJumpCancel = src.canJumpCancel,
                canSpecialCancel = src.canSpecialCancel,
                projectilePrefab = src.projectilePrefab,
                projectileSpawnOffset = src.projectileSpawnOffset,
                projectileSpeed = src.projectileSpeed,
                hitEffectPrefab = src.hitEffectPrefab,
                hitSound = src.hitSound,
                hitboxOffset = src.hitboxOffset,
                hitboxSize = src.hitboxSize,
                animIndex = src.animIndex,
            };
        }

        #endregion

        #region 9. 调试可视化

        // 【做什么】在 Scene 视图画出地面检测范围（绿=着地 / 红=离地）
        private void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;

            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        #endregion
    }
}
