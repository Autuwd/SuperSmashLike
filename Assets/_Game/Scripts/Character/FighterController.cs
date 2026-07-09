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
        public FighterData fighterData;  // 角色数据资产（ScriptableObject）
        public int playerID;             // 玩家ID（0=P1, 1=P2, …）

        [Header("References")]
        public Animator animator;        // Animator 引用 → 驱动动画
        public Rigidbody2D rb;           // 刚体 → 物理运动
        public Collider2D mainCollider;  // 主碰撞体
        public Hurtbox hurtbox;          // 受击判定框（子物体）

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
            if (hurtbox != null) hurtbox.owner = this;  // hurtbox 需要知道属于哪个角色

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

            // 死亡角色不更新
            if (StateMachine.CurrentState == FighterState.Dead)
                return;

            UpdateTimers();      // 攻击计时器、无敌计时器、护盾消耗/恢复
            UpdateGravity();     // 重力加速度
            UpdateMovement();    // 水平移动 + 地面/空中状态切换
            UpdateAnimation();   // 同步参数到 Animator
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
            // 攻击计时器：攻击持续时间结束后回到待机
            if (isAttacking)
            {
                attackTimer -= Time.deltaTime;
                if (attackTimer <= 0f)
                {
                    isAttacking = false;
                    if (StateMachine.CurrentState == FighterState.Attack)
                        StateMachine.TransitionTo(FighterState.Idle);
                }
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
        }

        // ==================== 重力系统 ====================
        private void UpdateGravity()
        {
            // 空中且不在击飞中 → 增加向下的重力速度
            if (!IsGrounded && !isInKnockback)
                velocity.y += Physics2D.gravity.y * Time.deltaTime;
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
            }
        }

        // ==================== 移动系统 ====================
        private void UpdateMovement()
        {
            // 非可操作状态或击飞中 → 不能移动
            if (!StateMachine.CanAct() || isInKnockback)
                return;

            // 根据地面/空中选择不同的速度
            float speed = IsGrounded ? fighterData.walkSpeed : fighterData.airSpeed;
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

            animator.SetFloat("Speed", Mathf.Abs(velocity.x));      // 速度→移动动画blend
            animator.SetBool("IsGrounded", IsGrounded);             // 是否在地面
            animator.SetFloat("VerticalSpeed", velocity.y);         // 垂直速度→跳跃/下落动画
            animator.SetFloat("Damage", CurrentDamage);             // 伤害值→受击表情/动作变化
            animator.SetInteger("State", (int)StateMachine.CurrentState);  // 当前状态→状态机转换
        }

        // ==================== 公开方法 ====================

        // 【跳跃】由 InputManager 调用
        // 大乱斗风格：可多段跳（二段跳）；空中和地面都可跳
        public void TryJump()
        {
            if (!StateMachine.CanAct() || isInKnockback)
                return;

            if (remainingJumps > 0)
            {
                // 一段跳用 full jump force，二段跳略弱（85%）
                velocity.y = fighterData.jumpForce * (remainingJumps == fighterData.jumpCount ? 1f : 0.85f);
                remainingJumps--;
                StateMachine.TransitionTo(FighterState.Jump);
                isJumping = true;
            }
        }

        // 【攻击】由 InputManager 调用
        // 传入 AttackData 决定用哪个攻击动作
        public void TryAttack(AttackData attackData)
        {
            if (!StateMachine.CanAct() || isAttacking || isInKnockback)
                return;

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

            isShielding = active;
            StateMachine.TransitionTo(active ? FighterState.Shield : FighterState.Idle);
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
