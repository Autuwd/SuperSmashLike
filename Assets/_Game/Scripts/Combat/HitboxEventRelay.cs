// ============================================================
// HitboxEventRelay — 动画事件转发器
// 用途：动画事件只能调用 Animator 所在物体（Visual）上组件的方法，
//       但判定框（Hitbox）需要挂在武器附近跟随挥动。
//       本脚本放在 Visual 上，把动画事件转发给判定框上的 Hitbox。
// 架构位置：Combat 层
// ============================================================
using SuperSmashLike.Core;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;

namespace SuperSmashLike.Combat
{
    public class HitboxEventRelay : MonoBehaviour
    {
        [Tooltip("拖入判定框物体上的 Hitbox")]
        public Hitbox target;

        // 由攻击动画的 Animation Event 调用
        public void Activate()
        {
            if (target == null) return;

            if (target.attackData.hitboxSize == Vector2.zero)
            {
                Debug.LogWarning($"[Hitbox] {name} hitboxSize 为 0，检查资产配置！", this);
            }

            // 1. 从 target 拿攻击数据和所属角色
            float dir = target.owner.isFacingRight ? 1f : -1f;

            // 2. 移动判定框到攻击位置（相对角色根，x 按朝向翻转）
            target.transform.localPosition = new Vector3(
                target.attackData.hitboxOffset.x * dir,
                target.attackData.hitboxOffset.y, 0);

            // 3. 设置判定框大小（collider 在判定框物体上）
            if (target.GetComponent<Collider2D>() is BoxCollider2D box)
                box.size = target.attackData.hitboxSize;

            // 4. 最后激活
            target.Activate();
        }
        public void Deactivate() { if (target != null) target.Deactivate(); }
        public void AttackFinished() { if (target != null) target.AttackFinished(); }
    }
}