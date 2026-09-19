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

        #endregion

        #region 2. 运行时状态

        private AttackData attackData;      // 命中时用的攻击数据（由 Spawn 注入）
        private FighterController owner;    // 发射者（用于排除自伤）
        private int dir = 1;                // 飞行方向：1=右, -1=左
        private float timer;                // 已存活时间

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
        }

        private void Update()
        {
            // 直线飞行（用 Translate 而非物理，投射物不需要碰撞体推挤）
            transform.Translate(Vector2.right * dir * speed * Time.deltaTime);

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

        #endregion
    }
}
