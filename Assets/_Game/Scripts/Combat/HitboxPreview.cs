using System.Reflection;
using UnityEngine;
using SuperSmashLike.Core;        // FighterData / AttackData 在这个命名空间

namespace SuperSmashLike.Combat
{
    // 挂在 AttackHitbox 上（和 Hitbox 同一个物体）
    // 作用：编辑模式下按 AttackData 的真实值画判定框，所见即所得
    //
    //AttackData 是普通可序列化类（不是 ScriptableObject），拖不了引用，
    //    所以这里拖的是 FighterData 资产（ScriptableObject，可以拖），
    //    再用字段名选出要预览的招式。
    public class HitboxPreview : MonoBehaviour
    {
        [Tooltip("拖入角色的 FighterData 资产，例如 FD_Knight.asset")]
        public FighterData data;

        [Tooltip("要预览的招式字段名。可选：jab1 jab2 jab3 / tiltSide tiltUp tiltDown / " +
                 "smashSide smashUp smashDown / aerialNeutral aerialForward aerialBack aerialUp aerialDown / " +
                 "specialNeutral specialSide specialUp specialDown / throwForward throwBack throwUp throwDown")]
        public string attackField = "tiltUp";

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

            //和 HitboxEventRelay.Activate() 同一套算法，保证所见即所得
            Vector3 center = origin + new Vector3(atk.hitboxOffset.x, atk.hitboxOffset.y, 0f);
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.9f);
            Gizmos.DrawWireCube(center, new Vector3(atk.hitboxSize.x, atk.hitboxSize.y, 0.05f));
        }

        // 用反射按字段名取出 AttackData（因为是内嵌类，只能这样拿）
        private static AttackData Find(FighterData d, string field)
        {
            var fi = typeof(FighterData).GetField(field, BindingFlags.Public | BindingFlags.Instance);
            return fi != null ? fi.GetValue(d) as AttackData : null;
        }
    }
}