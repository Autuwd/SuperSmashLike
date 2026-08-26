using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using UnityEngine;

// ============================================================
// BlastZone — 屏幕外边界淘汰区域
// 职责：
//   检测角色是否超出舞台边界 → 触发击杀（Kill）
// 架构位置：Stage 层
// 使用方式：
//   在舞台四周放置 4 个 BlastZone（上/下/左/右）
//   每个 BlastZone 用大 BoxCollider2D 覆盖屏幕外区域
// 大乱斗规则：
//   - 角色被击飞到屏幕外 → 触碰 BlastZone → 失去一条命
//   - 左上右上右下左下各有一个斜向边界线
//   - 简单起见我们用矩形边界
// ============================================================
namespace SuperSmashLike.Stage
{
    public class BlastZone : MonoBehaviour
    {
        [Header("Settings")]
        // 标记这个 BlastZone 对应哪个方向（用于统计击杀方向等）
        public bool isTop;
        public bool isBottom;
        public bool isLeft;
        public bool isRight;

        private MatchManager _matchManager;
        private bool _triggered = false; // 防重入

        public enum Side { Left, Right, Top, Bottom }
        public Side side;

        // 角色出界事件（MatchManager 可以监听此事件来调 Respawn）
        public System.Action<FighterController> OnPlayerOutOfBounds;

        private void Awake()
        {
            _matchManager = FindObjectOfType<MatchManager>();
        }

        // 当碰撞体进入 BlastZone 时触发
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_triggered) return;
            var fighter = other.GetComponentInParent<FighterController>();
            if (fighter != null && fighter.StateMachine.CurrentState != FighterState.Dead)
            {
                _triggered = true;
                _matchManager?.OnPlayerOutOfBounds(fighter, side);
                StartCoroutine(ResetTrigger());
            }
        }

        private System.Collections.IEnumerator ResetTrigger()
        {
            yield return new WaitForSeconds(0.5f);
            _triggered = false;
        }

        // Scene 视图显示边界范围（半透明红色）
        private void OnDrawGizmos()
        {
            BoxCollider2D col = GetComponent<BoxCollider2D>();
            if (col == null) return;

            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawCube(transform.position + (Vector3)col.offset, col.size);
        }
    }
}
