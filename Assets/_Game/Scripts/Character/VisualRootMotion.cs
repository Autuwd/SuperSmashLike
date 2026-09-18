using SuperSmashLike.Core;
using UnityEngine;

// ============================================================
// VisualRootMotion
// ★ 必须挂在【和 Animator 同一个 GameObject】上（也就是 Visual）！
//   Unity 只在 Animator 所在的 GameObject 上调用 OnAnimatorMove，
//   挂在 FighterController（根节点）上不会生效。
//
// 作用：接管根运动 ——
//   默认全部丢弃（不位移、不旋转）→ 解决抓取旋转 + 攻击漂移
//   只对指定招式放行"垂直"分量 → 保留跳跃下劈的起伏
// ============================================================
public class VisualRootMotion : MonoBehaviour
{
    [Tooltip("需要保留根运动垂直位移的招式名（对应 AttackData.attackName）")]
    public string[] keepVerticalFor = { "TiltDown" };

    private Animator animator;
    private FighterController owner;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        owner = GetComponentInParent<FighterController>();
    }

    // Unity 只在 Animator 所在的 GameObject 上调用这个回调
    private void OnAnimatorMove()
    {
        // ★ 默认什么都不做 = 根运动被完全丢弃（不漂移、不旋转）
        if (animator == null || owner == null) return;
        if (!owner.isAttacking || owner.attackData == null) return;

        foreach (var n in keepVerticalFor)
        {
            if (owner.attackData.attackName == n)
            {
                // 只取垂直分量（下劈的起伏）；水平/纵深丢弃
                Vector3 d = animator.deltaPosition;
                transform.localPosition += new Vector3(0f, d.y, 0f);
                return;
            }
        }
    }

    // 非攻击时把位移平滑归零（防止累积）
    private void LateUpdate()
    {
        if (owner != null && !owner.isAttacking)
            transform.localPosition = Vector3.Lerp(transform.localPosition, Vector3.zero, 12f * Time.deltaTime);
    }
}