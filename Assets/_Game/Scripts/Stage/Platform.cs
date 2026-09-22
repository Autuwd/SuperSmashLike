using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// Platform — 可穿越平台（单向平台）
// 职责：
//   1. 实现大乱斗风格的可穿越平台（从下方跳过可站在上面）
//   2. 支持从上方落下穿过（角色侧双击↓ → IgnoreCollision 豁免）
// 架构位置：Stage 层
// 依赖：PlatformEffector2D（Unity 内置组件，实现单向碰撞）
// 被谁使用：FighterController.DropThroughPlatform()
//
// 工作原理：
//   默认用 PlatformEffector2D 的 useOneWay 实现"只从上方碰撞"
//   落穿由角色侧 Physics2D.IgnoreCollision(角色, 本平台, true) 临时豁免
//   角色完全穿过平台后恢复（IgnoreCollision false）
// ============================================================
namespace SuperSmashLike.Stage
{
    [RequireComponent(typeof(Collider2D))]
    public class Platform : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Settings")]
        public bool isPassThrough = true;          // 是否可穿越（false = 实心平台）

        #endregion

        #region 2. 运行时状态

        private Collider2D platformCollider;   // 平台碰撞体
        private PlatformEffector2D effector;   // 单向碰撞效应器

        #endregion

        #region 3. Unity 生命周期

        private void Awake()
        {
            platformCollider = GetComponent<Collider2D>();
            effector = GetComponent<PlatformEffector2D>();
            if (effector == null)
                effector = gameObject.AddComponent<PlatformEffector2D>();   // 漏挂时自动补

            if (!isPassThrough) return;

            // 单向平台配置：只从上方碰撞
            platformCollider.isTrigger = false;      // 单向平台不能用 Trigger
            platformCollider.usedByEffector = true;  // 关键：碰撞体交给 effector 控制
            effector.useOneWay = true;
            effector.surfaceArc = 180f;              // 单向作用弧
        }

        #endregion

        #region 4. 公开 API

        // 【做什么】暴露平台碰撞体
        // 【注意】穿越/恢复由角色侧 Physics2D.IgnoreCollision 按碰撞对处理，
        //   平台不负责开关注销 —— 这样两人同站时只有要下去的人穿过
        public Collider2D Collider => platformCollider;

        #endregion

        #region 5. 调试可视化

        // 【做什么】在 Scene 视图画出平台范围（绿=可穿越 / 灰=实心）
        private void OnDrawGizmos()
        {
            BoxCollider2D col = GetComponent<BoxCollider2D>();
            if (col == null) return;

            Gizmos.color = isPassThrough ? new Color(0f, 1f, 0f, 0.4f) : new Color(0.5f, 0.5f, 0.5f, 0.4f);
            Gizmos.DrawCube(transform.position + (Vector3)col.offset, col.size);
        }

        #endregion
    }
}
