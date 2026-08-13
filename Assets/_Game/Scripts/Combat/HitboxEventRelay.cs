// ============================================================
// HitboxEventRelay — 动画事件转发器
// 用途：动画事件只能调用 Animator 所在物体（Visual）上组件的方法，
//       但判定框（Hitbox）需要挂在武器附近跟随挥动。
//       本脚本放在 Visual 上，把动画事件转发给判定框上的 Hitbox。
// 架构位置：Combat 层
// ============================================================
using UnityEngine;

namespace SuperSmashLike.Combat
{
    public class HitboxEventRelay : MonoBehaviour
    {
        [Tooltip("拖入判定框物体上的 Hitbox")]
        public Hitbox target;

        // 由攻击动画的 Animation Event 调用
        public void Activate() { if (target != null) target.Activate(); }
        public void Deactivate() { if (target != null) target.Deactivate(); }
        public void AttackFinished() { if (target != null) target.AttackFinished(); }
    }
}