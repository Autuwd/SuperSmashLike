using SuperSmashLike.Core;
using UnityEngine;

// ============================================================
// HitboxEventRelay — 动画事件转发器
// 职责：
//   1. 接收挂在 Visual（Animator 所在物体）上的 Animation Event
//   2. 转发给真正跟随武器挥动的 Hitbox（判定框不在 Visual 上）
// 架构位置：Combat 层，挂在 Visual 子物体上
// 依赖：Hitbox（target）
//
// 【为什么需要它】Animation Event 只能调用 Animator 所在物体上组件的方法，
//   但判定框必须挂在武器附近才能跟随挥动 → 中间加一层转发。
// ============================================================
namespace SuperSmashLike.Combat
{
    public class HitboxEventRelay : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Tooltip("拖入判定框物体上的 Hitbox")]
        public Hitbox target;

        #endregion

        #region 2. 运行时状态

        // hitboxSize 未配置的告警只打一次，避免每次攻击刷屏
        private bool _sizeWarned;

        #endregion

        #region 3. 动画事件入口（由 Animation Event 调用）

        // 【做什么】按当前攻击数据把判定框摆到正确位置和大小，然后激活
        // 【副作用】会修改 target 的 localPosition 和 BoxCollider2D.size
        // 【注意】必须先摆位再 Activate —— Activate 会打开 collider，
        //         位置不对会导致判定框出现在错误的地方
        public void Activate()
        {
            if (target == null) return;

            if (!_sizeWarned && target.attackData != null && target.attackData.hitboxSize == Vector2.zero)
            {
                _sizeWarned = true;
                // 配置类错误：保留无条件告警（不走通道开关），否则攻击静默失效很难查
                Debug.LogWarning($"[HitboxEventRelay] {name} 的 attackData.hitboxSize 为 0，攻击将无判定，请检查资产配置！", this);
            }

            if (target.attackData == null || target.owner == null) return;

            // 1. 方向：x 按角色朝向翻转
            float dir = target.owner.isFacingRight ? 1f : -1f;

            // 2. 移动判定框到攻击位置（相对角色根）
            target.transform.localPosition = new Vector3(
                target.attackData.hitboxOffset.x * dir,
                target.attackData.hitboxOffset.y, 0);

            // 3. 设置判定框大小
            if (target.GetComponent<Collider2D>() is BoxCollider2D box)
                box.size = target.attackData.hitboxSize;

            // 4. 最后激活
            target.Activate();
        }

        // 【做什么】关闭判定框（动画事件调用）
        public void Deactivate()
        {
            if (target != null) target.Deactivate();
        }

        // 【做什么】攻击动画播完（动画事件调用）→ 解除 isAttacking
        public void AttackFinished()
        {
            if (target != null) target.AttackFinished();
        }

        #endregion
    }
}
