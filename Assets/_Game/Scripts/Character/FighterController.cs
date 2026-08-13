using UnityEngine;
using SuperSmashLike.Combat;

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

        [Header("Jump")]
        public int remainingJumps;  // 剩余跳跃次数（大乱斗通常可跳2次）
        public bool isJumping;      // 是否正在跳跃
        public bool jumpHeld;       // 跳跃键是否按住

        [Header("Attack")]
        public bool isAttacking;    // 是否正在攻击动画中
        public float attackTimer;   // 攻击剩余持续时间

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

        [Header("Respawn")]
        public bool isInvincible;  // 重生后是否无敌
        public float respawnTimer; // 无敌剩余时间

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

            // 死亡角色不更新
            if (StateMachine.CurrentState == FighterState.Dead)
                return;

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
                || StateMachine.CurrentState == FighterState.FreeMove;
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
                // 击飞状态下：直接设置刚体速度 = 击飞速度
                rb.velocity = knockbackVelocity;
                // 水平衰减模拟空气阻力（每帧速度 * 0.98）
                knockbackVelocity.x *= 0.98f;
                // 速度足够小时结束击飞状态
                if (knockbackVelocity.magnitude < 0.5f)
                {
                    isInKnockback = false;
                    StateMachine.TransitionTo(IsGrounded ? FighterState.Idle : FighterState.Fall);
                }
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
            //事件代替
            //// 攻击计时器：攻击持续时间结束后回到待机
            //if (isAttacking)
            //{
            //    attackTimer -= Time.deltaTime;
            //    if (attackTimer <= 0f)
            //    {
            //        isAttacking = false;
            //        if (StateMachine.CurrentState == FighterState.Attack)
            //            StateMachine.TransitionTo(FighterState.Idle);
            //    }
            //}

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
        }

        // ==================== 重力系统 ====================
        private void UpdateGravity()
        {
            // 空中且不在击飞中 → 增加向下的重力速度
            if (!IsGrounded && !isInKnockback)
                velocity.y += Physics2D.gravity.y * gravityScale * Time.deltaTime;
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
            animator.speed = Mathf.Lerp(0.4f, 0.8f, Mathf.Abs(velocity.x) / fighterData.runSpeed);
            // ★ State 参数只在状态变化时设置（避免每帧 Set 触发 AnyState 重入循环）
            if (StateMachine.CurrentState != lastAnimState)
            {
                animator.SetInteger("State", (int)StateMachine.CurrentState);
                lastAnimState = StateMachine.CurrentState;
            }
            animator.SetFloat("Speed", Mathf.Abs(velocity.x));      // 速度→移动动画blend
            animator.SetBool("IsGrounded", IsGrounded);             // 是否在地面
            animator.SetFloat("VerticalSpeed", velocity.y);         // 垂直速度→跳跃/下落动画
            animator.SetFloat("Damage", CurrentDamage);             // 伤害值→受击表情/动作变化
        }

        // ==================== 公开方法 ====================

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
        public void TryAttack(AttackData attackData)
        {
            if (!StateMachine.CanAct() || isAttacking || isInKnockback)
                return;

            //把本次攻击数据同步给所有 Hitbox（AttackData 是内嵌类，不能拖拽，只能运行时赋值）
            foreach (var hb in GetComponentsInChildren<Hitbox>(true))
                hb.attackData = attackData;

            isAttacking = true;
            // 攻击总时长 = 前摇 + 判定帧 + 后摇
            attackTimer = attackData.startupTime + attackData.activeTime + attackData.recoveryTime;
            StateMachine.TransitionTo(FighterState.Attack);
        }

        // 【受击】由 Hitbox.OnTriggerEnter2D 调用
        // 流程：计算伤害 → 累加百分比 → 计算击飞 → 应用击飞
        public void ApplyDamage(AttackData attack, FighterController attacker)
        {
            if (isInvincible || StateMachine.CurrentState == FighterState.Dead)
                return;

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
        public void ApplyKnockback(Vector2 direction, float speed)
        {
            CurrentKnockbackSpeed = speed;
            knockbackVelocity = direction.normalized * speed;
            isInKnockback = true;

            // 计算受击硬直时间（速度越快硬直越长）
            float hitstunDuration = DamageSystem.CalculateHitstun(speed);
            StateMachine.TransitionTo(FighterState.Knockback);

            // 硬直结束后进入 FreeMove（可受控但还飞着）
            StartCoroutine(EndHitstunAfter(hitstunDuration));
        }

        // 硬直结束协程
        private System.Collections.IEnumerator EndHitstunAfter(float duration)
        {
            yield return new WaitForSeconds(duration);

            if (isInKnockback && knockbackVelocity.magnitude > 1f)
            {
                // 硬直结束但还在飞 → 进入自由移动状态（可DI）
                StateMachine.TransitionTo(FighterState.FreeMove);
            }
        }

        // 【防御开关】由 InputManager 调用（按住/松开）
        public void SetShielding(bool active)
        {
            if (StateMachine.CurrentState == FighterState.Knockback
                || StateMachine.CurrentState == FighterState.Dead)
                return;

            // 只有"盾状态真实变化"才切换——不干扰其他状态！
            if (active && StateMachine.CurrentState != FighterState.Shield)
            {
                isShielding = true;
                StateMachine.TransitionTo(FighterState.Shield);
            }
            else if (!active && StateMachine.CurrentState == FighterState.Shield)
            {
                isShielding = false;
                StateMachine.TransitionTo(FighterState.Idle);
            }
        }

        // 【破盾】护盾耐久归零时触发
        public void BreakShield()
        {
            isShielding = false;
            currentShieldHP = 0f;
            StateMachine.TransitionTo(FighterState.Stun);  // 破盾后大眩晕
        }

        // 【重生】被击杀后在出生点复活
        // 重置所有状态 + 短时间无敌
        public void Respawn(Vector3 position)
        {
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

            StateMachine.TransitionTo(FighterState.Idle);

            if (rb != null)
                rb.velocity = Vector2.zero;
        }

        // 【击杀】角色出界或生命归零时调用
        public void Kill()
        {
            StateMachine.TransitionTo(FighterState.Dead);
            OnKilled?.Invoke(this);
            GameManager.Instance.UnregisterFighter(this);
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
