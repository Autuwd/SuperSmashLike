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
        private float dropTimer;              // 落穿计时器

        private void Awake()
        {
            platformCollider = GetComponent<Collider2D>();
            if (isPassThrough)
                platformCollider.isTrigger = true;  // Trigger → 角色可穿过
        }

        private void Update()
        {
            if (dropTimer > 0f)
            {
                dropTimer -= Time.deltaTime;
                if (dropTimer <= 0f)
                    platformCollider.isTrigger = true;  // 恢复可站立
            }
        }

        // 落穿：由 InputManager 检测到"下+跳跃"时调用
        public void DropThrough()
        {
            if (!isPassThrough) return;
            platformCollider.isTrigger = false;   // 临时禁用 Trigger → 角色碰到平台会卡住
            dropTimer = disableCollisionTime;     // 启动计时器
        }

        // 当角色穿过平台后恢复 Trigger 状态
        private void OnTriggerExit2D(Collider2D other)
        {
            if (isPassThrough && dropTimer > 0f && other.GetComponent<FighterController>() != null)
            {
                platformCollider.isTrigger = true;  // 角色已穿过，恢复可站立
            }
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
