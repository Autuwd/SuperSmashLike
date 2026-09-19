using System.Reflection;
using UnityEngine;
using SuperSmashLike.Core;        // FighterData / AttackData 在这个命名空间

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
                 "specialNeutral specialSide specialUp specialDown / throwForward throwBack throwUp throwDown")]
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

            // 没配判定框 → 画个红点提醒
            if (atk.hitboxSize == Vector2.zero)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(origin + new Vector3(atk.hitboxOffset.x, atk.hitboxOffset.y, 0f), 0.15f);
                return;
            }

            // 与 HitboxEventRelay.Activate() 同一套算法，保证所见即所得
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
