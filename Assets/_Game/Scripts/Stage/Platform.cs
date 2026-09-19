using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// Platform — 可穿越平台（单向平台）
// 职责：
//   1. 实现大乱斗风格的可穿越平台（从下方跳过可站在上面）
//   2. 支持从上方落下穿过（↓ + 跳跃键 → 落穿平台）
// 架构位置：Stage 层
// 依赖：PlatformEffector2D（Unity 内置组件，实现单向碰撞）
// 被谁使用：FighterController.DropThroughPlatform()
//
// 工作原理：
//   默认用 PlatformEffector2D 的 useOneWay 实现"只从上方碰撞"
//   DropThrough() 时临时关闭 collider → 角色可穿过下落
//   一段时间后自动恢复 → 角色又可站在上面
// ============================================================
namespace SuperSmashLike.Stage
{
    [RequireComponent(typeof(Collider2D))]
    public class Platform : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Settings")]
        public bool isPassThrough = true;          // 是否可穿越（false = 实心平台）
        public float disableCollisionTime = 0.5f;  // 落穿后碰撞禁用时间（秒）

        #endregion

        #region 2. 运行时状态

        private Collider2D platformCollider;   // 平台碰撞体
        private PlatformEffector2D effector;   // 单向碰撞效应器
        private float dropTimer;               // 落穿计时器

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

        // 【做什么】落穿计时结束 → 恢复碰撞
        private void Update()
        {
            if (dropTimer <= 0f) return;

            dropTimer -= Time.deltaTime;
            if (dropTimer <= 0f)
                platformCollider.enabled = true;   // 恢复碰撞 → 又能站立
        }

        #endregion

        #region 4. 公开 API

        // 【做什么】落穿平台：临时关掉碰撞，让角色掉下去
        // 【注意】实现方式是直接 disable collider（不是翻转 effector 角度），
        //   靠 Update 里的计时器恢复
        public void DropThrough()
        {
            if (!isPassThrough || platformCollider == null) return;

            platformCollider.enabled = false;
            dropTimer = disableCollisionTime;
        }

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
