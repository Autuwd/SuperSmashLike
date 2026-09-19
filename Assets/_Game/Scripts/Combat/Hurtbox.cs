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
        #region 1. Inspector 配置

        [HideInInspector] public FighterController owner;  // 属于哪个斗士（由 FighterController 在 Awake 赋值）

        [Header("Debug")]
        public Color activeColor = new(1f, 0.5f, 0f, 0.3f);  // 半透明橙色（Scene 视图）

        #endregion

        #region 2. 运行时状态

        private Collider2D hurtCollider;

        #endregion

        #region 3. Unity 生命周期

        private void Awake()
        {
            hurtCollider = GetComponent<Collider2D>();
            if (hurtCollider != null)
                hurtCollider.isTrigger = true;  // 设为 Trigger，避免影响物理碰撞
        }

        #endregion

        #region 4. 调试可视化

        // 【做什么】在 Scene 视图画出受击框范围，方便肉眼确认判定区
        // 【注意】collider 可能还没被 Awake 赋值（编辑模式），所以这里兜底再取一次
        private void OnDrawGizmos()
        {
            if (hurtCollider == null) hurtCollider = GetComponent<Collider2D>();
            Gizmos.color = activeColor;

            if (hurtCollider is BoxCollider2D box)
                Gizmos.DrawCube(transform.position + (Vector3)box.offset, box.size);
            else if (hurtCollider is CircleCollider2D circle)
                Gizmos.DrawSphere(transform.position + (Vector3)circle.offset, circle.radius);
        }

        #endregion
    }
}
