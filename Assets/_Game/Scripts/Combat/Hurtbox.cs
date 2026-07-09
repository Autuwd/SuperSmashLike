using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// Hurtbox — 受击判定框
// 职责：
//   1. 挂在角色身体上的碰撞体，标记哪些部位可被攻击命中
//   2. 被 Hitbox 的 OnTriggerEnter2D 检测到 → 触发伤害
//   3. 本身是被动检测的，没有主动行为
// 架构位置：Combat 层
// 使用方式：
//   在角色身体各部位（躯干/头/手脚）加 BoxCollider2D + Hurtbox
//   FighterController 在 Awake 时自动设置 Hurtbox.owner = 自己
// 对比 Hitbox：
//   Hitbox  = 攻击方 → 主动检测别人
//   Hurtbox = 受击方 → 被动被别人检测
// ============================================================
namespace SuperSmashLike.Combat
{
    public class Hurtbox : MonoBehaviour
    {
        [HideInInspector] public FighterController owner;  // 属于哪个斗士（由 FighterController 赋值）

        [Header("Debug")]
        public Color activeColor = new(1f, 0.5f, 0f, 0.3f);  // 半透明橙色

        private Collider2D hurtCollider;

        private void Awake()
        {
            hurtCollider = GetComponent<Collider2D>();
            if (hurtCollider != null)
                hurtCollider.isTrigger = true;  // 设为 Trigger 避免影响物理碰撞
        }

        // Scene 视图可视化
        private void OnDrawGizmos()
        {
            if (hurtCollider == null) hurtCollider = GetComponent<Collider2D>();
            Gizmos.color = activeColor;

            if (hurtCollider is BoxCollider2D box)
                Gizmos.DrawCube(transform.position + (Vector3)box.offset, box.size);
            else if (hurtCollider is CircleCollider2D circle)
                Gizmos.DrawSphere(transform.position + (Vector3)circle.offset, circle.radius);
        }
    }
}
