using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// Platform — 可穿越平台
// 职责：
//   1. 实现大乱斗风格的可穿越平台（从下方跳过可站在上面）
//   2. 支持从上方落下穿过（按下+跳跃 => 落穿平台）
// 架构位置：Stage 层
// 工作原理：
//   默认 isTrigger = true → 角色可从下方穿过
//   DropThrough() 被调用时 → 临时关闭 Trigger → 角色可穿过下落
//   一段时间后自动恢复 Trigger → 角色又可站在上面
// ============================================================
namespace SuperSmashLike.Stage
{
    [RequireComponent(typeof(Collider2D))]
    public class Platform : MonoBehaviour
    {
        [Header("Settings")]
        public bool isPassThrough = true;       // 是否可穿越
        public float disableCollisionTime = 0.5f;  // 落穿后碰撞禁用时间

        private Collider2D platformCollider;  // 平台碰撞体
        private PlatformEffector2D effector;
        private float dropTimer;              // 落穿计时器

        private void Awake()
        {
            platformCollider = GetComponent<Collider2D>();
            effector = GetComponent<PlatformEffector2D>();
            if (effector == null)
                effector = gameObject.AddComponent<PlatformEffector2D>();

            if (isPassThrough)
            {
                // 单向平台：只从上方碰撞
                platformCollider.isTrigger = false;      // 不能是 Trigger
                platformCollider.usedByEffector = true;  // ← 关键：碰撞体交给 effector 控制
                effector.useOneWay = true;
                //effector.useOneWayGrouping = true;     // 不需要分组，单平台各自判断
                effector.surfaceArc = 180f;
            }
        }

        private void Update()
        {
            if (dropTimer > 0f)
            {
                dropTimer -= Time.deltaTime;
                if (dropTimer <= 0f)
                    platformCollider.enabled = true;   // 恢复碰撞 → 又能站立
            }
        }

        // 落穿：翻转 effector 角度 180° → 反向单向 → 角色从上方也能穿过
        public void DropThrough()
        {
            if (!isPassThrough || platformCollider == null) return;
            platformCollider.enabled = false;   // 关键：直接关掉碰撞
            dropTimer = disableCollisionTime;
        }

        // Scene 视图显示平台范围
        private void OnDrawGizmos()
        {
            BoxCollider2D col = GetComponent<BoxCollider2D>();
            if (col == null) return;

            Gizmos.color = isPassThrough ? new Color(0f, 1f, 0f, 0.4f) : new Color(0.5f, 0.5f, 0.5f, 0.4f);
            Gizmos.DrawCube(transform.position + (Vector3)col.offset, col.size);
        }
    }
}
