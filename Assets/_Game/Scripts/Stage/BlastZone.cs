using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using UnityEngine;

// ============================================================
// BlastZone — 屏幕外边界淘汰区域
// 职责：
//   检测角色是否超出舞台边界 → 通知 MatchManager 走击杀流程
// 架构位置：Stage 层
// 使用方式：
//   在舞台四周放置 4 个 BlastZone（上/下/左/右）
//   每个 BlastZone 用大 BoxCollider2D 覆盖屏幕外区域
// 大乱斗规则：
//   角色被击飞到屏幕外 → 触碰 BlastZone → 失去一条命
//   （真实大乱斗是斜向边界线，本项目简化为矩形边界）
//
// 【注意】_triggered 是防重入标记 —— 同一次出界可能被多个 BlastZone 同时触发
// 【注意】isTop/isBottom/isLeft/isRight 四个 bool 与 side 枚举语义重复，
//   当前代码只用 side；那 4 个 bool 属于历史遗留（保留仅为 Inspector 可读性）
// ============================================================
namespace SuperSmashLike.Stage
{
    public class BlastZone : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Settings")]
        // 标记这个 BlastZone 对应哪个方向（历史遗留，当前逻辑只用 side）
        public bool isTop;
        public bool isBottom;
        public bool isLeft;
        public bool isRight;

        // 出界方向（传给 MatchManager 用于统计/诊断）
        public enum Side { Left, Right, Top, Bottom }
        public Side side;

        #endregion

        #region 2. 运行时状态

        private MatchManager _matchManager;
        private bool _triggered;   // 防重入：一次出界只处理一次

        // 角色出界事件（预留：外部可订阅，当前 MatchManager 是直接调方法）
        public System.Action<FighterController> OnPlayerOutOfBounds;

        #endregion

        #region 3. Unity 生命周期

        private void Awake()
        {
            _matchManager = FindObjectOfType<MatchManager>();
        }

        #endregion

        #region 4. 触发逻辑

        // 【做什么】角色进入边界 → 通知 MatchManager 处理出界
        // 【注意】已死亡的角色不再触发（重生/淘汰流程中可能仍在边界内）
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_triggered) return;

            var fighter = other.GetComponentInParent<FighterController>();
            if (fighter == null || fighter.StateMachine.CurrentState == FighterState.Dead) return;

            _triggered = true;
            _matchManager?.OnPlayerOutOfBounds(fighter, side);
            StartCoroutine(ResetTrigger());
        }

        // 【做什么】0.5s 后解除防重入（足够让出界流程走完）
        private System.Collections.IEnumerator ResetTrigger()
        {
            yield return new WaitForSeconds(0.5f);
            _triggered = false;
        }

        #endregion

        #region 5. 调试可视化

        // 【做什么】在 Scene 视图画出边界范围（半透明红色）
        private void OnDrawGizmos()
        {
            BoxCollider2D col = GetComponent<BoxCollider2D>();
            if (col == null) return;

            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawCube(transform.position + (Vector3)col.offset, col.size);
        }

        #endregion
    }
}
