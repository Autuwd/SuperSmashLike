using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// Hitbox — 攻击判定框
// 职责：
//   1. 挂在角色手脚/武器上的碰撞体，检测是否命中对手
//   2. 由 Animation Event 控制激活/停用时机（匹配攻击动画的判定帧）
//   3. 命中后调用 FighterController.ApplyDamage 处理伤害和击飞
//   4. 实现 Hitstop（打击暂停）—— 命中时短暂冻结时间，增强打击感
// 架构位置：Combat 层
// 使用方式：
//   在角色子物体上加 BoxCollider2D + Hitbox 脚本
//   在攻击动画中用 Animation Event 调用 Activate() / Deactivate()
// ============================================================
namespace SuperSmashLike.Combat
{
    public class Hitbox : MonoBehaviour
    {
        [Header("Settings")]
        public AttackData attackData;                    // 这个攻击框对应的攻击数据
        [HideInInspector] public FighterController owner; // 所属角色（由 FighterController 在 Awake 中赋值）

        [Header("Debug")]
        public bool showGizmos = true;
        public Color activeColor = Color.red;       // 激活时红色
        public Color inactiveColor = Color.green;   // 未激活时绿色

        public bool IsActive { get; private set; }
        public System.Action<FighterController> OnHit;  // 命中回调

        private Collider2D hitCollider;

        private void Awake()
        {
            hitCollider = GetComponent<Collider2D>();
            if (hitCollider != null)
                hitCollider.enabled = false;  // 默认禁用，由动画事件激活
        }

        // 由 Animation Event 调用 → 开启判定框
        public void Activate()
        {
            IsActive = true;
            if (hitCollider != null)
                hitCollider.enabled = true;
        }

        // 由 Animation Event 调用 → 关闭判定框
        public void Deactivate()
        {
            IsActive = false;
            if (hitCollider != null)
                hitCollider.enabled = false;
        }

        // 由攻击动画末尾的 Animation Event 调用（Hitbox 在 Visual 上，事件能调到）
        public void AttackFinished()
        {
            if (owner != null && owner.isAttacking &&
                owner.StateMachine.CurrentState == FighterState.Attack)
            {
                owner.isAttacking = false;
                owner.StateMachine.TransitionTo(FighterState.Idle);
            }
        }

        // 命中检测：当 Hitbox 碰撞体与 Hurtbox 碰撞体接触时触发
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsActive) return;

            Hurtbox hurtbox = other.GetComponent<Hurtbox>();
            // 确认碰撞到的是受击框，且不是自己（避免自伤）
            if (hurtbox != null && hurtbox.owner != owner)
            {
                // 对目标斗士应用伤害
                hurtbox.owner.ApplyDamage(attackData, owner);
                // 命中特效（在 AttackData 资产里配）
                if (attackData.hitEffectPrefab != null)
                {
                    GameObject fx = Instantiate(attackData.hitEffectPrefab, other.transform.position, Quaternion.identity);
                    Destroy(fx, 0.1f);   // 1.5 秒后自动清理
                }
                OnHit?.Invoke(hurtbox.owner);

                // Hitstop：时间暂停一小段时间，增强打击感
                // 这是格斗游戏常见的"hit freeze"技术
                if (GameManager.Instance.gameSettings.hitstopScale > 0f)
                    StartCoroutine(HitstopRoutine());
            }
        }

        // Hitstop 协程：暂停时间 → 等待 → 恢复时间
        // 注意：使用 WaitForSecondsRealtime 而非 WaitForSeconds
        // 因为 Time.timeScale = 0 时 WaitForSeconds 不会计时
        private System.Collections.IEnumerator HitstopRoutine()
        {
            Time.timeScale = 0f;  // 冻结游戏时间
            float duration = attackData.hitstopDuration * GameManager.Instance.gameSettings.hitstopScale;
            yield return new WaitForSecondsRealtime(duration);  // 使用真实时间等待
            Time.timeScale = 1f;  // 恢复游戏时间
        }

        // 在 Scene 视图中绘制 Hitbox 范围，方便调试
        private void OnDrawGizmos()
        {
            if (!showGizmos) return;
            Gizmos.color = IsActive ? activeColor : inactiveColor;

            if (hitCollider is BoxCollider2D box)
                Gizmos.DrawWireCube(transform.position + (Vector3)box.offset, box.size);
            else if (hitCollider is CircleCollider2D circle)
                Gizmos.DrawWireSphere(transform.position + (Vector3)circle.offset, circle.radius);
        }
    }
}
