using SuperSmashLike.Core;
using UnityEngine;

// ============================================================
// VisualRootMotion — 根运动接管
// 职责：
//   1. 默认丢弃全部根运动（不位移、不旋转）→ 解决抓取旋转 + 攻击漂移
//   2. 只对 keepVerticalFor 白名单里的招式放行"垂直"分量 → 保留跳跃下劈的起伏
// 架构位置：Character 层，挂在 Visual（Animator 所在物体）上
// 依赖：FighterController（读 isAttacking / attackData）
//
// 【重点】必须挂在【和 Animator 同一个 GameObject】上（也就是 Visual）！
//   Unity 只在 Animator 所在的 GameObject 上调用 OnAnimatorMove，
//   挂在 FighterController（根节点）上不会生效。
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// ============================================================
public class VisualRootMotion : MonoBehaviour
{
    #region 1. Inspector 配置

    [Tooltip("需要保留根运动垂直位移的招式名（对应 AttackData.attackName）")]
    public string[] keepVerticalFor = { "TiltDown" };

    #endregion

    #region 2. 运行时状态

    private Animator animator;
    private FighterController owner;

    #endregion

    #region 3. Unity 生命周期

    private void Awake()
    {
        animator = GetComponent<Animator>();
        owner = GetComponentInParent<FighterController>();
    }

    // 【做什么】根运动回调 —— 默认什么都不做 = 根运动被完全丢弃
    // 【注意】Unity 只在 Animator 所在物体上调用此回调
    private void OnAnimatorMove()
    {
        if (animator == null || owner == null) return;
        if (!owner.isAttacking || owner.attackData == null) return;

        foreach (var n in keepVerticalFor)
        {
            if (owner.attackData.attackName != n) continue;

            // 只取垂直分量（下劈的起伏）；水平/纵深一律丢弃
            Vector3 d = animator.deltaPosition;
            transform.localPosition += new Vector3(0f, d.y, 0f);
            return;
        }
    }

    // 【做什么】非攻击状态把位移平滑归零，防止根运动残留累积
    private void LateUpdate()
    {
        if (owner != null && !owner.isAttacking)
            transform.localPosition = Vector3.Lerp(transform.localPosition, Vector3.zero, 12f * Time.deltaTime);
    }

    #endregion
}
