using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// Projectile — 投射物（箭 / 火球通用）
// 职责：
//   1. 直线飞行，超时或命中后自动回收
//   2. 命中时走统一伤害流程 FighterController.ApplyDamage
// 架构位置：Combat 层
// 依赖：ObjectPooler（生成/回收）、AttackData（伤害数据）、FighterController（目标与来源）
// 被谁使用：FighterController.TrySpecial()
//
// 【注意】生成走对象池，不要用 Instantiate —— 特殊攻击是高频操作
// 【注意】命中判定排除 owner 自己，且跳过已死亡目标
// ============================================================
namespace SuperSmashLike.Combat
{
    public class Projectile : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("飞行参数")]
        public float speed = 15f;       // 飞行速度（单位/秒）
        public float lifeTime = 2f;     // 存活时长（秒），超时自动回收

        [Header("抛物线/旋转（直线投射物留 0）")]
        public float launchAngle = 0f;   // 发射角度（度），0=水平
        public float gravity = 0f;       // 重力加速度，0=无弧线
        public float spinSpeed = 0f;     // 每秒旋转角度（Z轴），0=不转
        public float acceleration = 0f;         // 新增：飞行加速度（沿速度方向，单位/秒²）
        public float spinAcceleration = 0f;     // 新增：旋转角加速度（度/秒²），1 = 起步慢 . . . 越转越快

        [Header("命中盒对齐（0 = 保持预制体原样）")]
        public float hitOffsetForward = 0f;        // 碰撞盒向飞行方向前移量
        public Vector2 hitSize = Vector2.zero;     // 命中盒尺寸（zero = 不改）

        #endregion

        #region 2. 运行时状态

        private AttackData attackData;      // 命中时用的攻击数据（由 Spawn 注入）
        private FighterController owner;    // 发射者（用于排除自伤）
        private int dir = 1;                // 飞行方向：1=右, -1=左
        private float timer;                // 已存活时间

        private Vector2 velocity;
        private float angularSpeed;

        #endregion

        #region 3. 公开 API

        // 【做什么】从对象池生成一个投射物并初始化
        // 【参数】prefab = 投射物预制体；pos = 生成位置；dir = 飞行方向；data = 攻击数据；attacker = 发射者
        // 【返回】生成出来的 Projectile 组件（预制体上没挂 Projectile 时会 NRE）
        // 【副作用】会从 ObjectPooler 取对象并激活
        public static Projectile Spawn(GameObject prefab, Vector3 pos, int dir,
            AttackData data, FighterController attacker)
        {
            var obj = ObjectPooler.Instance.Spawn(prefab, pos, Quaternion.identity);
            var p = obj.GetComponent<Projectile>();
            p.Init(data, attacker, dir);
            return p;
        }

        #endregion

        #region 4. 私有逻辑

        // 【做什么】注入运行时参数（每次从池里取出都要重新调用，避免复用残留）
        private void Init(AttackData data, FighterController attacker, int direction)
        {
            attackData = data;
            owner = attacker;
            dir = direction;
            timer = 0f;

            float rad = launchAngle * Mathf.Deg2Rad;
            velocity = new Vector2(Mathf.Cos(rad) * speed * dir, Mathf.Sin(rad) * speed);
            angularSpeed = spinSpeed;

            if(TryGetComponent<ParticleSystem>(out var ps))
            {
                var shape = ps.shape;                          // 气波/投射物通用：发射轴朝飞行方向
                shape.rotation = new Vector3(0f, 90f * Mathf.Sign(dir), 0f);

                var vel = ps.velocityOverLifetime;             // 火球专用：向后方拖尾
                vel.xMultiplier = -Mathf.Abs(vel.xMultiplier) * Mathf.Sign(dir);
            }

            var col = GetComponent<BoxCollider2D>();
            if (col != null && hitSize != Vector2.zero)
            {
                col.offset = new Vector2(dir * hitOffsetForward, col.offset.y);   // 朝飞行方向
                col.size = hitSize;
            }
        }

        private void Update()
        {
            // 先慢后快：沿飞行方向持续加速（0 速保护，防 NaN）
            if (acceleration > 0f && velocity.sqrMagnitude > 0.01f)
                velocity += (Vector2)(velocity.normalized * (acceleration * Time.deltaTime));

            velocity.y -= gravity * Time.deltaTime;  // 抛物线
            angularSpeed += spinAcceleration * Time.deltaTime;  //越转越快
            transform.position += (Vector3)(velocity * Time.deltaTime);
            transform.Rotate(0, 0, angularSpeed * Time.deltaTime);  // 旋转


            // 超时回收
            timer += Time.deltaTime;
            if (timer >= lifeTime) ObjectPooler.Instance.Despawn(gameObject);
        }

        // 【做什么】命中检测：打到对手 → 走统一伤害流程 → 回收自己
        private void OnTriggerEnter2D(Collider2D other)
        {
            var target = other.GetComponentInParent<FighterController>();
            if (target == null || owner == null || target == owner) return;   // 不打自己
            if (target.StateMachine.CurrentState == FighterState.Dead) return;

            target.ApplyDamage(attackData, owner);
            // 特效（可选）：ObjectPooler.Instance.Spawn(impactFx, transform.position, Quaternion.identity)
            ObjectPooler.Instance.Despawn(gameObject);
        }

        // 【做什么】在 Scene 视图中绘制投射物的命中盒（仅编辑器可见）
        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.9f);   // 与 HitboxPreview 同色
            var b = col.bounds;                              // world space，已含位置偏移
            Gizmos.DrawWireCube(b.center, b.size);
        }

        #endregion
    }
}
