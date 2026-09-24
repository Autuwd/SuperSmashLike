using System.Reflection;
using UnityEngine;
using SuperSmashLike.Core;        // FighterData / AttackData 在这个命名空间
using SuperSmashLike.Combat;      // FighterController / HitboxEventRelay 在这个命名空间

// ============================================================
// HitboxPreview — 判定框场景预览（挂在 AttackHitbox 上，和 Hitbox 同物体）
// 职责：
//   1. 编辑模式下按 AttackData 的真实值画出判定框，做到所见即所得
//   2. 未配置判定框时画红点提醒
// 架构位置：Combat 层（仅编辑器可视化，运行时无行为）
//
// 【注意】AttackData 是普通可序列化类（不是 ScriptableObject），拖不了引用，
//   所以这里拖的是 FighterData 资产，再用【字段名】选出要预览的招式。
// 【注意】绘制算法必须与 HitboxEventRelay.Activate() 保持一致，
//   否则"预览看到的"和"实际打到的"会对不上。
// ============================================================
namespace SuperSmashLike.Combat
{
    public class HitboxPreview : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Tooltip("拖入角色的 FighterData 资产，例如 FD_Knight.asset")]
        public FighterData data;

        [Tooltip("要预览的招式字段名。可选：jab1 jab2 jab3 / tiltSide tiltUp tiltDown / " +
                 "smashSide smashUp smashDown / aerialNeutral aerialForward aerialBack aerialUp aerialDown / " +
                 "specialNeutral specialSide specialUp specialDown / grab / throwForward throwBack throwUp throwDown")]
        public string attackField = "tiltUp";

        #endregion

        #region 2. 调试可视化

        // 【做什么】在 Scene 视图画出当前招式配置的判定框
        private void OnDrawGizmosSelected()
        {
            if (data == null) return;

            AttackData atk = Find(data, attackField);
            if (atk == null) return;

            Vector3 origin = transform.parent != null ? transform.parent.position : transform.position;

            // ===== 分支 1：抓取（代码驱动，无 hitbox 配置）=====
            // 与 FighterController.TryGrab 同算法：方框中心 = 角色 + 朝向
            // 预览默认按右朝向画；翻转是运行时行为
            if (attackField == "grab")
            {
                var fc = GetComponentInParent<FighterController>();
                Vector2 size = fc != null ? fc.grabBoxSize : new Vector2(1.8f, 1.2f);
                Vector2 off = fc != null ? fc.grabBoxOffset : new Vector2(0.9f, 0.6f);
                Gizmos.DrawWireCube(origin + new Vector3(off.x, off.y, 0f), size);   // 默认右朝向预览
                return;
            }

            // ===== 分支 2：投射物招（判定在弹体上，不画近战框）=====
            // 注意用 projectileSpawnOffset，不是 hitboxOffset（FighterController L951-954 读的是它）
            if (atk.projectilePrefab != null)
            {
                // 弹体出生点（默认右朝向预览）
                Vector3 spawn = origin + new Vector3(atk.projectileSpawnOffset.x, atk.projectileSpawnOffset.y, 0f);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(spawn, 0.12f);

                // 弹体碰撞范围：从预制体读真实 Collider2D（所见即所得）
                var col = atk.projectilePrefab.GetComponent<Collider2D>();
                if (col != null)
                {
                    Gizmos.color = new Color(1f, 0.4f, 0f, 0.9f);
                    Vector3 colCenter = spawn + new Vector3(col.offset.x, col.offset.y, 0f);
                    if (col is BoxCollider2D box)
                        Gizmos.DrawWireCube(colCenter, new Vector3(box.size.x, box.size.y, 0.05f));
                    else if (col is CircleCollider2D circ)
                        Gizmos.DrawWireSphere(colCenter, circ.radius);
                }

                // 飞行方向指示（默认右；SD 石头有远近分档 farSpecialRange，此处画默认 1f）
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(spawn, spawn + new Vector3(1.5f, 0f, 0f));
                return;
            }

            // ===== 分支 3：近战招（原有逻辑，不动）=====
            if (atk.hitboxSize == Vector2.zero)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(origin + new Vector3(atk.hitboxOffset.x, atk.hitboxOffset.y, 0f), 0.15f);
                return;
            }

            Vector3 center = origin + new Vector3(atk.hitboxOffset.x, atk.hitboxOffset.y, 0f);
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.9f);
            Gizmos.DrawWireCube(center, new Vector3(atk.hitboxSize.x, atk.hitboxSize.y, 0.05f));
        }

        #endregion

        #region 3. 私有工具

        // 【做什么】用反射按字段名从 FighterData 里取出 AttackData
        // 【为什么用反射】AttackData 是内嵌类、字段名由配置决定，编译期拿不到
        // 【返回】找不到字段或类型不符时返回 null
        private static AttackData Find(FighterData d, string field)
        {
            var fi = typeof(FighterData).GetField(field, BindingFlags.Public | BindingFlags.Instance);
            return fi != null ? fi.GetValue(d) as AttackData : null;
        }

        #endregion
    }
}
