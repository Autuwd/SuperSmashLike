using SuperSmashLike.Combat;
using SuperSmashLike.InputSystem;
using SuperSmashLike.Managers;
using SuperSmashLike.Stage;
using UnityEngine;

// ============================================================
// FighterController — 斗士角色主控制器（整个项目的核心类）
// 职责：
//   1. 整合输入、物理、状态机、动画、战斗系统的中枢
//   2. 每帧处理：计时器更新 → 重力 → 移动 → 动画
//   3. 对外提供：跳跃/攻击/受击/防御/击飞/重生/死亡 等公共方法
// 架构位置：Character 层核心
// 关系图：
//   InputManager → 读取输入 → 调用 FighterController.TryJump/TryAttack/SetShielding
//   Hitbox → 命中检测 → 调用 FighterController.ApplyDamage
//   StateMachine → 管理状态转换 → FighterController 驱动它
//   DamageSystem → 静态工具方法 → 被 ApplyDamage 调用
// ============================================================
namespace SuperSmashLike.Core
{
    public class FighterController : MonoBehaviour
    {
        // ==================== Inspector 配置区 ====================
        [Header("Configuration")]
        public bool isFacingRight = true; //面朝向翻转
        public FighterData fighterData;  // 角色数据资产（ScriptableObject）
        public int playerID;             // 玩家ID（0=P1, 1=P2, …）

        [Header("References")]
        public Animator animator;        // Animator 引用 → 驱动动画
        public Rigidbody2D rb;           // 刚体 → 物理运动
        public Collider2D mainCollider;  // 主碰撞体
        public Hurtbox hurtbox;          // 受击判定框（子物体）
        public Transform visual;         // 模型父级（Visual），用于镜像翻转
        public GameObject projectilePrefab;  // 特殊攻击投射物预制体（Inspector 拖入）

        [Header("Ground Detection")]
        public Transform groundCheck;        // 脚底检测点（空子物体）
        public float groundCheckRadius = 0.2f;  // 检测半径
        public LayerMask groundLayer;        // 哪些层算"地面"
        public bool IsGrounded { get; private set; }  // 是否在地面（每帧更新）

        // ==================== 运行时状态 ====================
        public FighterStateMachine StateMachine { get; private set; } = new();
        public float CurrentDamage { get; private set; }  // 当前伤害百分比（0~999%）
        public float CurrentKnockbackSpeed { get; set; }   // 当前击飞速度
        public Vector2 velocity;                           // 速度向量（x=水平, y=垂直）
        public Vector2 MoveInput { get; set; }             // 输入方向（由 InputManager 写入）
        private FighterState lastAnimState = (FighterState)(-1);   // 上次同步的状态

        public bool TechInputHeld { get; set; }

        [Header("Jump")]
        public int remainingJumps;  // 剩余跳跃次数（大乱斗通常可跳2次）
        public bool isJumping;      // 是否正在跳跃
        public bool jumpHeld;       // 跳跃键是否按住

        [Header("Attack")]
        public AttackData attackData;   // 当前攻击数据（起手/连招推进/缓冲消费共用）
        public bool isAttacking;    // 是否正在攻击动画中
        public float attackTimer;   // 攻击剩余持续时间
        public float attackFallbackTimer;  // 攻击兜底计时（独立于 attackTimer，防事件链路断卡死）

        [Header("Charge")]   // 蓄力（09-15 新增，Smash 收敛为长按机制）
        public bool isCharging;              // 是否蓄力中
        public float chargeTimer;            // 蓄力进度（秒）
        public float maxChargeTime = 0.8f;   // 满蓄时间
        public float chargeDamageMultiplier = 1.25f;     // 满蓄伤害倍率
        public float chargeKnockbackMultiplier = 1.15f;  // 满蓄击退倍率
        public AttackData chargeBaseData;    // 蓄力基值（smashSide/Up/Down）

        [Header("Combo")]
        //public bool bufferedAttack;      // 攻击输入缓冲
        public InputBuffer inputBuffer = new InputBuffer();   // 帧号输入缓冲（5帧窗口）
        public int comboStep;            // 当前连招段（0=Jab1, 1=Jab2, 2=Jab3）
        public float comboWindowStart = 0.3f;   // 连招窗口起点（normalizedTime）
        public float comboWindowEnd = 1.0f;    // 连招窗口终点

        [Header("Knockback")]
        public Vector2 knockbackVelocity;  // 击飞速度向量（由 DamageSystem 计算）
        public bool isInKnockback;         // 是否处于击飞飞行中

        [Header("Feel")]
        public float gravityScale = 2.2f;   // 角色专属重力倍率（默认×1=标准重力）
        public float fastFallMultiplier = 1.6f;   // 快速下落速度倍率
        public float jumpBufferTime = 0.1f;       // 跳跃缓冲时间（秒）
        public float coyoteTime = 0.1f;           // 土狼时间（秒）
        private float jumpBufferTimer = 0f;
        private float coyoteTimer = 0f;
        public float doubleTapWindow = 0.25f;   // 双击判定窗口（秒）
        private bool isRunning = false;          // 是否处于跑步状态
        private float lastTapTime = -1f;         // 上次按下方向的时间
        private int lastTapDirection = 0;        // 上次按下的方向（-1/1）
        private int lastTapDir = 0;              // 当前是否在按住方向（0=松开）

        [Header("Shield")]
        public float currentShieldHP;  // 护盾当前耐久
        public bool isShielding;       // 是否在举盾

        [Header("Parry")]
        public int parryWindowFrames = 10;          // 盾反窗口：10帧
        public float parryStunDuration = 1.2f;     // 攻击者被盾反后的眩晕时长(≥1s)
        public float shieldBreakStunDuration = 1.5f; // 破盾眩晕时长（D1 缺口，顺手补）
        public System.Action<FighterController> OnParrySuccess;  // 盾反成功事件
        private int shieldStartFrame = -999;       // 举盾启动帧号（用帧号！）
        private float stunTimer = 0f;              // Stun 剩余时间

        [Header("Tech")]
        public float techBounceSpeed = 6f;         // 受身成功小反弹速度
        public float techInvincibleTime = 0.5f;    // 受身无敌时长（秒）

        [Header("Grab")]
        public float grabRange = 1.2f;                // 抓取距离（可 Inspector 调）
        public float grabHoldDistance = 1.0f;        // 被抓方锁在面前的间距

        // 运行时状态（和 grabTarget 一起放这）：
        [Header("Grab State")]  // 或并进 Grab 区
        public FighterController grabTarget;   // 我抓着谁（null = 没抓）
        public FighterController grabber;      // 谁抓着我（null = 没被抓）

        [Header("Respawn")]
        public bool isInvincible;  // 重生后是否无敌
        public float respawnTimer; // 无敌剩余时间

        // === 剩余命数 ===
        [Header("Stock")]
        public int remainingStocks = 3;

        // 挣扎计数器（Inspector 调阈值）
        public int escapeMashThreshold = 8;   // 连按攻击 8 次挣脱
        private int escapeMashCount = 0;
        private float grabTimer = 0f;  // 抓取超时计时器（兜底，防止异常断链）
        private bool grabWhiff = false;   // 本次 Grab 是抓空（没抓到人）

        private bool hasLeftGroundInKnockback; // 击飞中是否离开过地面（用于 FreeMove 过渡）

        public bool hitboxActivatedThisAttack;   // 本次攻击判定框是否已打开（防连段提前取消吞判定）
        private int knockbackToken;              // 击飞令牌：连续被击飞时旧协程作废
        private bool stunTechable;               // 摔地硬直是否可被护盾提前起身

        public enum ThrowDir { Forward, Back, Up, Down }

        // ==================== 事件 ====================
        public System.Action<FighterState, FighterState> OnStateChanged;  // 状态变化
        public System.Action<float, FighterController> OnDamaged;          // 受伤 (伤害值, 攻击者)
        public System.Action<FighterController> OnKilled;                  // 被击杀

        // ==================== 初始化 ====================
        private void Awake()
        {
            // 自动获取组件引用（如果 Inspector 没拖的话）
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (hurtbox == null) hurtbox = GetComponentInChildren<Hurtbox>();
            if (mainCollider == null) mainCollider = GetComponent<Collider2D>();
            // hurtbox 需要知道属于哪个角色
            if (hurtbox != null) hurtbox.owner = this;
            // 修复：给所有 Hitbox 指定所有者，防止自伤
            var hitboxes = GetComponentsInChildren<Hitbox>(true);
            foreach (var hb in hitboxes) hb.owner = this;

            // 转发状态机事件到外部
            StateMachine.OnStateChanged += (prev, next) => OnStateChanged?.Invoke(prev, next);
        }

        private void Start()
        {
            StateMachine.Initialize(FighterState.Idle);          // 初始状态 = 待机
            currentShieldHP = GameManager.Instance.gameSettings.shieldMaxHP;  // 满盾
            remainingJumps = fighterData.jumpCount;             // 满跳跃次数
            // === 同步剩余命数 ===
            remainingStocks = GameManager.Instance.gameSettings.stockCount;
            GameManager.Instance.RegisterFighter(this);         // 向 GameManager 注册
        }

        // ==================== 每帧更新 ====================
        // Update 处理逻辑（状态判断、计时器、输入响应）
        private void Update()
        {
            // 用地面的一个小圆来检测是否在地面上
            // Physics2D.OverlapCircle 检测 groundCheck 位置是否有 groundLayer 的碰撞体
            IsGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            // 土狼时间：着地刷新，离地后倒计时（0.1s 内仍可跳）
            coyoteTimer = IsGrounded ? coyoteTime : coyoteTimer - Time.deltaTime;
            // 跳跃缓冲倒计时
            if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

            if (StateMachine.CurrentState == FighterState.Dead)
            {
                // 只关渲染和碰撞，不关 GameObject
                if (visual != null) visual.gameObject.SetActive(false);
                if (mainCollider != null) mainCollider.enabled = false;
                if (hurtbox != null) hurtbox.gameObject.SetActive(false);
                return;
            }

            // 被抓状态：锁在抓取者正前方 + 清速度
            if (StateMachine.CurrentState == FighterState.Grabbed)
            {
                if (grabber != null)
                {
                    Vector3 anchor = grabber.transform.position
                        + new Vector3(grabber.isFacingRight ? 1f : -1f, 0.5f, 0f) * grabHoldDistance;
                    transform.position = Vector3.Lerp(transform.position, anchor, 15f * Time.deltaTime);
                }
                velocity = Vector2.zero;   // 清惯性
                knockbackVelocity = Vector2.zero;

                //被抓方也必须把状态写给 Animator，否则 Grabbed 动画永远播不出来
                if (StateMachine.CurrentState != lastAnimState)
                {
                    animator.SetInteger("State", (int)StateMachine.CurrentState);
                    lastAnimState = StateMachine.CurrentState;
                }

                return;                    // ← 重点：被抓期间不进移动/跳跃/转向逻辑！
            }

            UpdateTimers();      // 攻击计时器、无敌计时器、护盾消耗/恢复
            UpdateGravity();     // 重力加速度
            // 快速下落：下落中按住 ↓ 加速（更快落地）
            bool fastFallHeld = MoveInput.y < -0.5f;
            //Debug.Log($"DEBUG: state={StateMachine.CurrentState} moveY={MoveInput.y:F2} velY={velocity.y:F2} grounded={IsGrounded}");
            if (StateMachine.CurrentState == FighterState.Fall && fastFallHeld && velocity.y < -4f)
            {
                velocity.y = Mathf.Min(velocity.y, -fighterData.fastFallSpeed * fastFallMultiplier);
            }
            // ===== 双击方向键检测 → 进入跑步 =====
            int currentDir = MoveInput.x > 0.1f ? 1 : (MoveInput.x < -0.1f ? -1 : 0);

            // 刚按下方向（从"松开"到"按下"的边沿）→ 检测双击
            if (currentDir != 0 && lastTapDir == 0)
            {
                if (currentDir == lastTapDirection && Time.time - lastTapTime < doubleTapWindow)
                    isRunning = true;   // 同方向快速按两次 → 跑！
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
            UpdateMovement();    // 水平移动 + 地面/空中状态切换
            UpdateAnimation();   // 同步参数到 Animator

            // 朝向逻辑：自由状态下，移动时按移动方向转向；静止时面向对手
            bool canTurn = StateMachine.CurrentState == FighterState.Idle
                || StateMachine.CurrentState == FighterState.Run
                || StateMachine.CurrentState == FighterState.Jump
                || StateMachine.CurrentState == FighterState.Fall;
            if (canTurn)
            {
                // 1. 移动输入优先 → 按移动方向转向
                // 移动转向（原第 169-177 行整段替换）：
                if (Mathf.Abs(MoveInput.x) > 0.1f)
                {
                    SetFacing(MoveInput.x > 0f);   // ← 直接调，SetFacing 内部自己判断
                }
                // 2. 无移动输入 → 自动面向对手（对战时保持"锁定对手"）
                else
                {
                    Transform target = null;
                    var players = GameManager.Instance.ActivePlayers;
                    if (players != null)
                    {
                        foreach (var p in players)
                            if (p != this) { target = p.transform; break; }
                    }
                    // 面向对手（原第 188-196 行，target 判空之后）：
                    if (target != null)
                    {
                        SetFacing(target.position.x > transform.position.x);
                    }
                }
            }
        }

        // FixedUpdate 处理物理（刚体速度赋值）
        // 和 Update 分离是为了物理稳定性（固定时间步长）
        private void FixedUpdate()
        {
            if (StateMachine.CurrentState == FighterState.Dead)
                return;

            if (isInKnockback)
            {
                // 击飞中离开过地面 → 标记（否则落地受身永不触发！）
                if (!IsGrounded) hasLeftGroundInKnockback = true;

                // 击飞状态下：直接设置刚体速度 = 击飞速度
                rb.velocity = knockbackVelocity;
                // 水平衰减模拟空气阻力（每帧速度 * 0.98）
                knockbackVelocity.x *= 0.98f;
                knockbackVelocity.y *= 0.98f;
            }
            else
            {
                // 正常状态下：刚体速度 = velocity
                rb.velocity = velocity;
            }
        }

        // ==================== 计时器系统 ====================
        private void UpdateTimers()
        {
            // 攻击兜底计时器（08-17 新增）：
            // 正常攻击结束靠动画事件 AttackFinished（动画播到 0.77s 触发 → HitboxEventRelay → Hitbox → isAttacking=false）。
            // 若事件链路断（状态机重组后转换/Motion 未配好、动画被中断、relay target 为空）
            // → isAttacking 永远 true → 状态机卡死在 Attack（CanAct()==false 不能移动）。
            // 兜底：attackFallbackTimer 超时（1.2s，覆盖所有攻击动画时长）强制结束攻击。
            // 事件正常时 AttackFinished 早已置 false，此计时器不触发，双保险。
            
            // 蓄力计时（09-15 新增）
            if (isCharging)
            {
                chargeTimer += Time.deltaTime;
                if (chargeTimer >= maxChargeTime)
                {
                    chargeTimer = maxChargeTime;  // 钳住
                    ReleaseCharge();              // ★ 满蓄自动释放
                }
                else
                {
                    chargeTimer = Mathf.Min(chargeTimer, maxChargeTime);
                }
            }

            // 被打断清理（统一兜底）
            if (isCharging && StateMachine.CurrentState != FighterState.Attack)
            {
                isCharging = false;   // 被击飞/眩晕/抓取等打断 → 蓄力作废
            }

            if (isAttacking && !isCharging)    // 蓄力期间不跑兜底：蓄力时长由"松手"决定，不是固定时长
            {
                attackFallbackTimer -= Time.deltaTime;
                if (attackFallbackTimer <= 0f)
                {
                    comboStep = 0;
                    animator.SetInteger("ComboStep", 0);
                    isAttacking = false;
                    if (StateMachine.CurrentState == FighterState.Attack)
                        StateMachine.TransitionTo(FighterState.Idle);
                }
            }

            bool isJabChain = attackData == fighterData.jab1
                            || attackData == fighterData.jab2 
                            || attackData == fighterData.jab3;
            if (isAttacking && isJabChain && inputBuffer.ConsumeAttack() && comboStep < 2 && IsInComboWindow() && hitboxActivatedThisAttack)
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
                Debug.Log($"Jab{comboStep + 1} 伤害={attackData.damage}");
            }


            // 重生无敌计时器：一段时间后解除无敌
            if (respawnTimer > 0f)
            {
                respawnTimer -= Time.deltaTime;
                if (respawnTimer <= 0f)
                    isInvincible = false;
            }

            // 护盾系统：举盾消耗耐久，松盾恢复耐久
            if (isShielding)
            {
                currentShieldHP -= GameManager.Instance.gameSettings.shieldRegenPerSecond * Time.deltaTime;
                if (currentShieldHP <= 0f)
                    BreakShield();  // 破盾 → 眩晕
            }
            else if (currentShieldHP < GameManager.Instance.gameSettings.shieldMaxHP)
            {
                currentShieldHP += GameManager.Instance.gameSettings.shieldRegenPerSecond * Time.deltaTime;
            }

            // Stun 倒计时（D2 补：原 D1 破盾 Stun 无退出 → 永久定身）
            if (StateMachine.CurrentState == FighterState.Stun && stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                if (stunTechable && TechInputHeld)
                {
                    stunTechable = false;
                    stunTimer = 0f;
                    StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
                }
                else if (stunTimer <= 0f)
                {
                    StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
                }
            }

            // 抓取计时（区分：抓空 / 抓到人）
            if (StateMachine.CurrentState == FighterState.Grab)
            {
                grabTimer += Time.deltaTime;

                if (grabWhiff)
                {
                    // ★ 抓空：播完挥空动作就回 Idle（不锁 5 秒）
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
                    var timeoutVictim = grabTarget;   // ★ 先保存引用（ReleaseGrab 会清空）
                    ReleaseGrab();
                    // ★ 恢复物理碰撞（抓取期间是 Ignore 的）
                    if (timeoutVictim != null && mainCollider != null && timeoutVictim.mainCollider != null)
                        Physics2D.IgnoreCollision(mainCollider, timeoutVictim.mainCollider, false);
                    Debug.Log("[Grab] 超时自动松开");
                }
            }
            else
            {
                grabWhiff = false;
                grabTimer = 0f;
            }
        }

        // ==================== 重力系统 ====================
        private void UpdateGravity()
        {
            // 空中且不在击飞中 → 增加向下的重力速度
            if (!IsGrounded && !isInKnockback)
                velocity.y += Physics2D.gravity.y * gravityScale * Time.deltaTime;

            if (isInKnockback && IsGrounded)
            {
                // 受身窗口：任何击飞落回地面 / 贴地滑行中，按住护盾 → 受身
                if (TechInputHeld)
                {
                    PerformTech();
                    return;
                }

                if (hasLeftGroundInKnockback)
                {
                    // 真击飞落回地面 → 受身窗口！
                    Debug.Log($"[受身诊断] 落地瞬间 | TechInputHeld={TechInputHeld} | 速度={knockbackVelocity.magnitude:F1}");
                    isInKnockback = false;
                    velocity.y = 0f;
                    StateMachine.TransitionTo(FighterState.Idle);
                    stunTechable = true;         // 标记这次硬直可被护盾起身
                    EnterStun(0.3f);
                }
            }
            // 落地 → 重置垂直速度 + 恢复跳跃次数
            else if (IsGrounded && velocity.y <= 0f)
            {
                // 从跳/落状态→落地，切换到待机
                if (StateMachine.CurrentState == FighterState.Jump
                    || StateMachine.CurrentState == FighterState.Fall)
                {
                    StateMachine.TransitionTo(FighterState.Idle);
                }
                velocity.y = 0f;
                remainingJumps = fighterData.jumpCount;  // 重置跳跃次数
                // 落地时有缓冲的跳跃意图 → 自动起跳
                if (jumpBufferTimer > 0f)
                    TryPerformJump();
            }
            // 下落速度上限：fallSpeed 越大落得越快（上限越松）
            if (velocity.y < -fighterData.fallSpeed)
                velocity.y = -fighterData.fallSpeed;
        }

        // ==================== 移动系统 ====================
        private void UpdateMovement()
        {
            // 击飞中 → 不能移动（击飞速度由 FixedUpdate 单独控制）
            if (isInKnockback)
                return;

            // 攻击/受击/眩晕等非可操作状态 → 不能移动
            // 地面：水平速度清零，防止"攻击中按住方向键滑动"
            // 空中：保留惯性（空中攻击的动量手感，大乱斗风格）
            if (!StateMachine.CanAct())
            {
                if (IsGrounded)
                    velocity.x = 0f;
                return;
            }

            // 根据地面/空中选择不同的速度
            float speed = IsGrounded
                ? (isRunning ? fighterData.runSpeed : fighterData.walkSpeed)
                : fighterData.airSpeed;
            velocity.x = MoveInput.x * speed;

            // 根据是否在地面和垂直速度切换状态
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

        // ==================== 动画系统 ====================
        // 将运行时参数同步给 Animator Controller
        // Animator 中用这些参数做 Blend Tree / 状态转换
        private void UpdateAnimation()
        {
            if (animator == null) return;
            // 动画播放速率 = 移动速度 / 最大速度 → 走路慢放、跑动正常
            // ⚠️ 修复(08-17)：攻击/举盾时锁定 1.0 倍速！
            // animator.speed 是全局缩放（影响所有层），空中攻击时若水平速度低
            // 动画被压到 0.4 倍速 → LightLeaping01 的"跳起翻转+劈砍"被慢放成
            // "奔跑翻转再攻击"，过程极长且与姿态混合观感异常。
            bool lockAnimSpeed = isAttacking || isShielding
                  || StateMachine.CurrentState == FighterState.Grab
                  || StateMachine.CurrentState == FighterState.Grabbed;

            animator.speed = lockAnimSpeed
                ? 1f
                : Mathf.Lerp(0.4f, 0.8f, Mathf.Abs(velocity.x) / fighterData.runSpeed);

            // ★ State 参数只在状态变化时设置（避免每帧 Set 触发 AnyState 重入循环）
            if (StateMachine.CurrentState != lastAnimState)
            {
                animator.SetInteger("State", (int)StateMachine.CurrentState);
                lastAnimState = StateMachine.CurrentState;
            }
            animator.SetFloat("Speed", Mathf.Abs(velocity.x));      // 速度→移动动画blend
            animator.SetBool("IsGrounded", IsGrounded);             // 是否在地面
            animator.SetFloat("AirFactor", IsGrounded ? 0f : 1f);   // 0=地面攻击，1=空中攻击
            animator.SetFloat("VerticalSpeed", velocity.y);         // 垂直速度→跳跃/下落动画
            animator.SetFloat("Damage", CurrentDamage);             // 伤害值→受击表情/动作变化

            // Action Layer（索引 1）权重：攻击/举盾时抬起，结束归零
            float targetWeight = (isAttacking || isShielding) ? 1f : 0f;
            float cur = animator.GetLayerWeight(1);
            // 进入攻击 → 立即 1（消灭起手混合）；退出攻击 → 平滑回落（保留收招过渡）
            animator.SetLayerWeight(1, targetWeight > cur ? targetWeight : Mathf.Lerp(cur, targetWeight, 10f * Time.deltaTime));
        }

        // ==================== 公开方法 ====================

        // 【防御开关】由 InputManager 调用（按住/松开）
        public void SetShielding(bool active)
        {
            if (StateMachine.CurrentState == FighterState.Knockback
                || StateMachine.CurrentState == FighterState.Dead
                || StateMachine.CurrentState == FighterState.Stun
                || StateMachine.CurrentState == FighterState.ShieldStun
                || StateMachine.CurrentState == FighterState.Grab      // ★ 抓取中不能举盾
                || StateMachine.CurrentState == FighterState.Grabbed)  // ★ 被抓中不能举盾
                return;

            // 只有"盾状态真实变化"才切换——不干扰其他状态！
            if (active && StateMachine.CurrentState != FighterState.Shield)
            {
                shieldStartFrame = Time.frameCount;   //盾启动瞬间才是窗口起点
                Debug.Log($"[D] 举盾! shieldStartFrame={shieldStartFrame}, 帧={Time.frameCount}");   // Log0
                isShielding = true;
                StateMachine.TransitionTo(FighterState.Shield);
            }
            else if (!active && StateMachine.CurrentState == FighterState.Shield)
            {
                isShielding = false;
                StateMachine.TransitionTo(FighterState.Idle);
            }
        }

        // 朝向翻转：镜像 Visual 的 X 缩放（3D 模型旋转会背面朝镜头，必须用镜像）
        private void SetFacing(bool faceRight)
        {
            if (faceRight == isFacingRight) return;
            isFacingRight = faceRight;
            if (visual == null) return;
            Vector3 s = visual.localScale;
            s.x = Mathf.Abs(s.x) * (faceRight ? 1f : -1f);   // 保留原有缩放大小，只翻符号
            visual.localScale = s;
        }

        // 用 AttackType 参数告诉 Animator 播哪个攻击动画
        private void SetAttackAnim(AttackData data)
        {
            animator.SetInteger("AttackType", data != null ? data.animIndex : 0);
        }

        // ===== 蓄力开始（长按确认时由 InputManager 调用）=====
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

            isAttacking = true;             // 进入攻击态（锁移动）
            hitboxActivatedThisAttack = false;
            attackFallbackTimer = 1.2f;     // 兜底：动画事件断时 1.2s 后强制结束攻击

            StateMachine.TransitionTo(FighterState.Attack);   // 复用攻击态：锁移动 ✓
            animator.SetInteger("AttackType", GetChargeIndex(baseData));           // TODO 动画：蓄力姿势
        }

        // 【跳跃】由 InputManager 调用
        // 大乱斗风格：可多段跳（二段跳）；空中和地面都可跳
        public void TryJump()
        {
            if (!StateMachine.CanAct() || isInKnockback)
                return;

            jumpBufferTimer = jumpBufferTime;  // 记录本次按键意图（即使暂时不能跳）
            TryPerformJump();
        }

        // 实际执行跳跃（按键与落地缓冲共用）
        private void TryPerformJump()
        {
            bool isFirstJump = remainingJumps == fighterData.jumpCount;
            // 一段跳需要着地或土狼时间内；二段跳空中可跳
            bool groundOk = isFirstJump ? (IsGrounded || coyoteTimer > 0f) : true;
            if (groundOk && remainingJumps > 0)
            {
                velocity.y = fighterData.jumpForce * (isFirstJump ? 1f : 0.85f);
                remainingJumps--;
                StateMachine.TransitionTo(FighterState.Jump);
                isJumping = true;
                jumpBufferTimer = 0f;   // 消耗缓冲
            }
        }

        // 【攻击】由 InputManager 调用
        // 传入 AttackData 决定用哪个攻击动作
        public void TryAttack(AttackData data)
        {
            if (grabTarget != null && StateMachine.CurrentState == FighterState.Grab)
            {
                PerformThrow(GetThrowDirection());   // 传方向枚举
                return;
            }

            if (isInKnockback || isShielding || StateMachine.CurrentState == FighterState.Dead)
                return;

            // ===== 连招：正在攻击中 → 判断能否推进到下一段 =====
            if (isAttacking)
            {
                //守卫标识
                bool isJabChainNow = attackData == fighterData.jab1
                      || attackData == fighterData.jab2
                      || attackData == fighterData.jab3;

                if (isJabChainNow && comboStep < 2)
                {
                    if (IsInComboWindow() && hitboxActivatedThisAttack)
                    {
                        comboStep++;
                        hitboxActivatedThisAttack = false;
                        SetAttackAnim(attackData);
                        animator.SetInteger("ComboStep", comboStep);
                        attackData = GetComboAttack(comboStep);
                        SetAttackAnim(attackData);
                        foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                            hb.attackData = attackData;
                        attackTimer = attackData.startupTime + attackData.activeTime + attackData.recoveryTime;
                        attackFallbackTimer = 1.2f;
                        StateMachine.TransitionTo(FighterState.Attack);
                        Debug.Log($"Jab{comboStep + 1} 伤害={attackData.damage}");
                    }
                    else
                    {
                        inputBuffer.BufferAttack();   // 按太早 → 缓冲
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
            foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                hb.attackData = attackData;
            isAttacking = true;
            hitboxActivatedThisAttack = false;
            attackTimer = attackData.startupTime + attackData.activeTime + attackData.recoveryTime;
            attackFallbackTimer = 1.2f;   // 兜底：动画事件断时 1.2s 后强制结束攻击

            if (data == fighterData.tiltDown && IsGrounded)
            {
                velocity.y = fighterData.jumpForce * 0.7f;   // ★ 起跳初速，调手感
                remainingJumps = 0;                          // 防止空中二段跳
                IsGrounded = false;                          // 立即离地，防止落地后被 Idle 覆盖
            }

            StateMachine.TransitionTo(FighterState.Attack);
        }

        // 【特殊攻击】投射物型（Archer 箭 / Mage 火球）
        public void TrySpecial(AttackData data)
        {
            if (data == null || isInKnockback || StateMachine.CurrentState == FighterState.Dead)
                return;
            if (isAttacking || isShielding) return;   // 攻击/举盾中不能发

            // 生成投射物（位置：角色前方胸口高度，方向按朝向）
            Vector3 spawnPos = transform.position + new Vector3(isFacingRight ? 0.8f : -0.8f, 1.0f, 0f);
            var proj = Projectile.Spawn(
                projectilePrefab, spawnPos, isFacingRight ? 1 : -1, data, this);
            if (proj == null) return;

            StateMachine.TransitionTo(FighterState.Attack);   // 复用攻击状态播动画
            attackFallbackTimer = 0.5f;   // 快速结束
        }

        // 【抓取】由 InputManager 调用
        public void TryGrab()
        {
            // === 守卫：抓取方条件 ===
            if (isInKnockback || StateMachine.CurrentState == FighterState.Dead) return;
            if (StateMachine.CurrentState == FighterState.Attack) return;   // 攻击中不能抓
            if (grabTarget != null) return;                                 // 已在抓，别重复
            if (StateMachine.CurrentState == FighterState.Grab) return;      // 被抓中不能抓

            // === 前方检测：角色面前一个圆 ===
            // 中心点 = 角色位置 + 面向方向 × grabRange（把圆推到身前）
            Vector2 center = (Vector2)transform.position
                + new Vector2(isFacingRight ? 1f : -1f, 0f) * grabRange;
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, grabRange);

            // === 找可抓目标 ===
            FighterController victim = null;
            foreach (var hit in hits)
            {
                var hb = hit.GetComponent<Hurtbox>();
                if (hb == null || hb.owner == this) continue;   // 只要对手的受击框

                // 受害者可抓条件（大乱斗规则）：
                //   能操作(CanAct) 或 举盾(盾可被抓！) → 可抓
                //   击飞中/硬直中/已被抓/无敌/死亡 → 不可抓
                bool grabbable = hb.owner.StateMachine.CanAct()
                    || hb.owner.StateMachine.CurrentState == FighterState.Shield;
                if (!grabbable) continue;
                if (hb.owner.isInvincible || hb.owner.StateMachine.CurrentState == FighterState.Grabbed) continue;

                victim = hb.owner;
                break;
            }
            if (victim == null)
            {
                // ★ 抓空：照样进 Grab 播抓取动作（挥空），稍后自动回 Idle
                grabWhiff = true;
                grabTimer = 0f;
                StateMachine.TransitionTo(FighterState.Grab);
                return;
            }

            // === 双向引用 + 双状态转换 ===
            isShielding = false;                              // ★ 自己（可能从盾抓）收盾
            victim.isShielding = false;                       // ★ 对方（举盾被抓）收盾
            grabTarget = victim;
            victim.grabber = this;
            StateMachine.TransitionTo(FighterState.Grab);
            victim.StateMachine.TransitionTo(FighterState.Grabbed);

            // 抓取期间双方物理碰撞忽略（大乱斗规则：被抓者穿过抓取者，不推挤）
            Physics2D.IgnoreCollision(mainCollider, victim.mainCollider, true);
            Debug.Log($"[Grab] {name} 抓住 {victim.name}");
        }

        // 落穿：找到脚下最近的 Platform 并让它翻转单向
        public void DropThroughPlatform()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                groundCheck.position, groundCheckRadius + 0.05f, groundLayer);
            foreach (var c in hits)
            {
                var plat = c.GetComponentInParent<Platform>();
                if (plat != null) { plat.DropThrough(); break; }
            }
        }

        private bool IsInComboWindow()
        {
            // normalizedTime 对动画帧精确，且自动兼容 Animator 倍速
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(1);
            float t = info.normalizedTime % 1f;
            return t >= comboWindowStart && t <= comboWindowEnd;
        }

        // 编号规则：挥击 xx → 蓄力 xx×10（SmashSide=20→200, SmashUp=21→210, SmashDown=22→220）
        private int GetChargeIndex(AttackData baseData)
        {
            if (baseData == fighterData.smashUp) return 210;   // 上蓄力
            if (baseData == fighterData.smashDown) return 220;   // 下蓄力
            return 200;                                          // 兜底：侧蓄力（前/后同招）
        }

        private AttackData GetComboAttack(int step)
        {
            return step switch
            {
                1 => fighterData.jab2,
                2 => fighterData.jab3,
                _ => fighterData.jab1,
            };
        }

        private ThrowDir GetThrowDirection()
        {
            Vector2 input = MoveInput;            // InputManager 每帧无条件写入，Grab 状态下也有值 ✅
            float ax = Mathf.Abs(input.x);
            float ay = Mathf.Abs(input.y);
            const float deadzone = 0.5f;          // 摇杆/方向键死区

            // 1. 垂直优先：上下推得明显 → 上投/下投
            if (ay > ax && ay > deadzone)
                return input.y > 0f ? ThrowDir.Up : ThrowDir.Down;

            // 2. 水平：判断推的是"前"还是"后"（相对面朝方向！）
            if (ax > deadzone)
            {
                // 推的方向 与 面朝方向 同向 = 前投；反向 = 后投
                bool pushingForward = (input.x > 0f) == isFacingRight;
                return pushingForward ? ThrowDir.Forward : ThrowDir.Back;
            }

            // 3. 没推方向 → 默认前投（防御性兜底，避免 null/坏输入）
            return ThrowDir.Forward;
        }


        // 【受击】由 Hitbox.OnTriggerEnter2D 调用
        // 流程：计算伤害 → 累加百分比 → 计算击飞 → 应用击飞
        public void ApplyDamage(AttackData attack, FighterController attacker)
        {
            if (isInvincible || StateMachine.CurrentState == FighterState.Dead)
                return;

            Debug.Log($"[D] 命中! 防御={name}, isShielding={isShielding}, 帧={Time.frameCount}");   // Log1

            // ===== 举盾防御：伤害全部由护盾承受，不掉血、不击飞 =====
            if (isShielding)
            {
                Debug.Log($"[D] 进盾分支, 帧差={Time.frameCount - shieldStartFrame}");   // Log2

                // M1-D2 Parry：举盾 4 帧内被命中 → 盾反
                if (Time.frameCount - shieldStartFrame <= parryWindowFrames)
                {
                    Debug.Log($"[D] Parry触发!");   // Log3
                    PerformParry(attacker);
                    return;    // 不掉血、不掉盾
                }

                currentShieldHP -= DamageSystem.CalculateShieldDamage(attack, currentShieldHP);
                if (currentShieldHP <= 0f)
                    BreakShield();   // 破盾 → 进 Stun 大眩晕
                return;              // ← 关键！盾防走完直接返回，不再掉血/击飞
            }

            // 应用伤害（含全局缩放系数）
            float finalDamage = attack.damage * GameManager.Instance.gameSettings.damageRatio;
            CurrentDamage += finalDamage;
            OnDamaged?.Invoke(finalDamage, attacker);

            // 使用 DamageSystem 的静态方法计算击飞速度和方向
            float knockSpeed = DamageSystem.CalculateKnockbackVelocity(
                attack, CurrentDamage, fighterData.weight);
            Vector2 knockDir = DamageSystem.CalculateKnockbackDirection(
                attack, transform.position - attacker.transform.position);

            ApplyKnockback(knockDir, knockSpeed);
        }

        // 【应用击飞】将击飞速度和方向应用到角色上
        public void ApplyKnockback(Vector2 direction, float speed, float hitstunOverride = -1f)
        {
            Debug.Log($"[击飞] speed={speed} state={StateMachine.CurrentState} grounded={IsGrounded}");

            knockbackToken++;
            CurrentKnockbackSpeed = speed;
            knockbackVelocity = direction.normalized * speed;
            isInKnockback = true;
            hasLeftGroundInKnockback = false;

            // 计算受击硬直时间（速度越快硬直越长）
            float hitstunDuration = hitstunOverride >= 0f 
                ? hitstunOverride
                : DamageSystem.CalculateHitstun(speed);
            StateMachine.TransitionTo(FighterState.Knockback);

            // 硬直结束后进入 FreeMove（可受控但还飞着）
            StartCoroutine(EndHitstunAfter(hitstunDuration));
        }

        // 硬直结束协程
        private System.Collections.IEnumerator EndHitstunAfter(float duration)
        {
            int token = knockbackToken;
            yield return new WaitForSeconds(duration);

            if (token != knockbackToken) yield break;     // 期间又被击飞 → 旧协程作废（防硬直提前结束）

            if (isInKnockback)
            {
                if (!IsGrounded)
                {
                    velocity = knockbackVelocity;      // 空中：惯性交接 → Fall 恢复操控
                    knockbackVelocity = Vector2.zero;
                    isInKnockback = false;
                    // 大乱斗规则：硬直结束 = 恢复空中操控，必有一跳回场。
                    // 不能 = jumpCount（"第一跳"要求贴地/土狼，空中会被卡死）
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
        }

        //执行格挡反击：攻击方进入眩晕，自己无伤且不掉盾
        public void PerformParry(FighterController attacker)
        {
            // 自身：无伤 + 不掉盾（上面的 return 已保证，这里只做成功反馈）
            // 攻击方：进 Stun
            attacker?.EnterStun(parryStunDuration);
            // 闪白
            var cam = FindObjectOfType<CameraManager>();
            if (cam != null) cam.FlashWhite(0.15f);
            // 声音位预留（M8 音效接入时补）
            // TODO(M8): PlayParrySFX()
            OnParrySuccess?.Invoke(attacker);
        }

        //空中恢复
        public void PerformTech()
        {
            // 日志打印
            Debug.Log($"[Tech] {name} 受身成功！反弹 y={techBounceSpeed}，无敌 {techInvincibleTime}s");

            // 视觉闪光（复用现成 API，CameraManager 已有 FlashWhite）
            var cam = FindObjectOfType<CameraManager>();
            if (cam != null) cam.FlashWhite(0.2f);

            // 1. 清击飞
            knockbackVelocity = Vector2.zero;
            isInKnockback = false;
            // 2. 小反弹（数值挂 Inspector）
            velocity.y = techBounceSpeed;
            // 3. 短暂无敌（复用现有计时器 L294-299 自动解除）
            isInvincible = true;
            respawnTimer = techInvincibleTime;
            // 4. 回可操作状态
            StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
        }

        //投掷
        public void PerformThrow(ThrowDir dir)
        {
            if (grabTarget == null) return;
            FighterController victim = grabTarget;
            AttackData data = dir switch
            {
                ThrowDir.Forward => fighterData.throwForward,
                ThrowDir.Back => fighterData.throwBack,
                ThrowDir.Up => fighterData.throwUp,
                ThrowDir.Down => fighterData.throwDown,
                _ => fighterData.throwForward,   // ★ 兜底：未命名值 → 默认前投
            };
            if (data == null) return;

            //1）先解除抓取让对方状态干净
            ReleaseGrab();

            //2）播放投掷动画
            SetAttackAnim(data);
            attackData = data;
            isAttacking = true;
            hitboxActivatedThisAttack = false;
            attackTimer = data.startupTime + data.activeTime + data.recoveryTime;
            attackFallbackTimer = 1.2f;
            StateMachine.TransitionTo(FighterState.Attack);

            //3）伤害+击退
            // 伤害照常累加
            float finalDamage = data.damage * GameManager.Instance.gameSettings.damageRatio;
            victim.CurrentDamage += finalDamage;
            victim.OnDamaged?.Invoke(finalDamage, this);
            // ⚠️ 坑：不能复用 CalculateKnockbackDirection（它按"打过来的方向"算）
            // 投掷方向必须按自身朝向固定构造：

            if (dir == ThrowDir.Back) SetFacing(!isFacingRight);

            Vector2 throwDir = dir switch
            {
                ThrowDir.Forward or ThrowDir.Back => isFacingRight ? Vector2.right : Vector2.left,
                ThrowDir.Up => Vector2.up,
                ThrowDir.Down => Vector2.down,
                _ => Vector2.zero,               // ★ 兜底：防御性归零
            };

            // —— 瞬移受害人到投掷方向外侧（固定距离，不依赖碰撞体尺寸）——
            float clearDist = 1.5f;   // 足够跨越角色半宽 + 安全间距，不会被挡
            victim.rb.position = (Vector2)rb.position + throwDir * clearDist;

            float knockSpeed = DamageSystem.CalculateKnockbackVelocity(
                data, victim.CurrentDamage, victim.fighterData.weight);
            victim.ApplyKnockback(throwDir, knockSpeed);

            // 投掷后 0.6s 再恢复碰撞（等 victim 飞出身体范围）
            StartCoroutine(RestoreCollisionAfterThrow(victim));

            // 投完脱离（唯一出口：另一方回 Idle）
            ReleaseGrab();
        }

        // 投掷/抓取结束后恢复碰撞（0.6s 足够 victim 飞出身体范围）
        private System.Collections.IEnumerator RestoreCollisionAfterThrow(FighterController victim, float delay = 0.6f)
        {
            yield return new WaitForSeconds(delay);
            if (victim != null && mainCollider != null && victim.mainCollider != null)
                Physics2D.IgnoreCollision(mainCollider, victim.mainCollider, false);
        }

        // 任何脱离场景（投完/挣脱/超时）都走这：对称清理双方
        private void ReleaseGrab()
        {
            // 我是抓取方 → 先清我的目标
            if (grabTarget != null)
            {
                var victim = grabTarget;
                grabTarget = null;
                victim.grabber = null;                       // 清对方反向引用
                victim.escapeMashCount = 0;                  // 顺手重置挣扎计数
                if (victim.StateMachine.CurrentState == FighterState.Grabbed)
                    victim.StateMachine.TransitionTo(FighterState.Idle);
                if (StateMachine.CurrentState == FighterState.Grab)
                    StateMachine.TransitionTo(FighterState.Idle);   // ★ 抓取方也要回 Idle！
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

        // ===== 蓄力释放（松手时由 InputManager 调用）=====
        public void ReleaseCharge()
        {
            if (!isCharging) return;   // 幂等：被打断后松手不误触发

            // 1. 按蓄力等级算加权伤害/击退 —— ★ 必须 new 副本，不能改原数据！
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

        public void EnterStun(float duration)
        {
            stunTimer = duration;
            StateMachine.TransitionTo(FighterState.Stun);
        }

        // 【破盾】护盾耐久归零时触发
        public void BreakShield()
        {
            isShielding = false;
            currentShieldHP = 0f;
            //StateMachine.TransitionTo(FighterState.Stun);  // 破盾后大眩晕
            EnterStun(shieldBreakStunDuration);
        }

        // 【重生】被击杀后在出生点复活
        // 重置所有状态 + 短时间无敌
        public void Respawn(Vector3 position)
        {
            Debug.Log($"[Respawn] {name} 开始重生，位置: {position}");
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

            // 强制重置状态机到 Idle（避免 Dead 无法 TransitionTo Idle）
            StateMachine.Initialize(FighterState.Idle);

            if (rb != null) rb.velocity = Vector2.zero;

            // === 恢复渲染和碰撞 ===
            if (visual != null) visual.gameObject.SetActive(true);
            if (mainCollider != null) mainCollider.enabled = true;
            if (hurtbox != null) hurtbox.gameObject.SetActive(true);

            Debug.Log($"[Respawn] {name} 重生完成，状态: {StateMachine.CurrentState}, visual.active={visual?.gameObject.activeSelf}");
        }

        // 【击杀】角色出界或生命归零时调用
        public void Kill()
        {
            Debug.Log($"[Kill] {name} 被击杀, 当前状态: {StateMachine.CurrentState}, 剩余命数: {remainingStocks}");
            if (StateMachine.CurrentState == FighterState.Dead) return;
            if (_isKilling) return; _isKilling = true; // 防重入
            
            // === 扣命 ===
            remainingStocks = Mathf.Max(0, remainingStocks - 1);
            Debug.Log($"[Kill] 扣命后剩余: {remainingStocks}");

            StateMachine.TransitionTo(FighterState.Dead);
            OnKilled?.Invoke(this);
            //GameManager.Instance.UnregisterFighter(this);
            _isKilling = false;
        }
        private bool _isKilling = false;

        public void OnMashGrabEscape()
        {
            if (StateMachine.CurrentState != FighterState.Grabbed) return;
            escapeMashCount++;
            Debug.Log($"[Escape] 挣扎 {escapeMashCount}/{escapeMashThreshold}");
            if (escapeMashCount >= escapeMashThreshold)
            {
                var escapeHolder = grabber;   // ★ 先保存抓取者引用（ReleaseGrab 会清空）
                ReleaseGrab();
                // ★ 恢复物理碰撞
                if (escapeHolder != null && escapeHolder.mainCollider != null && mainCollider != null)
                    Physics2D.IgnoreCollision(escapeHolder.mainCollider, mainCollider, false);
            }
        }

        // AttackData 是 class（引用类型），深拷贝一份
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
                hitEffectPrefab = src.hitEffectPrefab,
                hitSound = src.hitSound,
                hitboxOffset = src.hitboxOffset,
                hitboxSize = src.hitboxSize,
                animIndex = src.animIndex,
            };
        }

        // ==================== 可视化调试 ====================
        private void OnDrawGizmosSelected()
        {
            // 在 Scene 视图中显示地面检测范围
            if (groundCheck != null)
            {
                Gizmos.color = IsGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
            }
        }

        private void OnDestroy()
        {
            StateMachine.OnStateChanged -= (prev, next) => OnStateChanged?.Invoke(prev, next);
        }
    }
}
