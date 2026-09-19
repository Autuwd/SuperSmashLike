using UnityEngine;

// ============================================================
// ShieldVisual — 护盾视觉（纯表现层）
// 职责：
//   1. 举盾时显示护盾，松盾/破盾时隐藏
//   2. 耐久越低盾越小（血条式反馈，满盾 1.0 → 残盾 0.15）
// 架构位置：Character 层，纯表现，不参与任何战斗逻辑
// 依赖：FighterController（读 isShielding / currentShieldHP）、GameSettings（读 shieldMaxHP）
//
// 挂载方式：Fighter 根物体下新建子物体 ShieldVisual
//   → 加 SpriteRenderer（默认材质即可，内置管线天然透明队列）
//   → 挂本脚本 → Inspector 把 Fighter 引用拖到 fighter 字段
//   → Order in Layer = +1，localPosition.z = -0.3
//
// 【注意】必须挂在 Fighter 根级，不能挂在 Visual 下 ——
//   Visual 会被 SetFacing 做 X 轴负缩放镜像，护盾会跟着翻成负缩放。
// ============================================================
namespace SuperSmashLike.Core
{
    public class ShieldVisual : MonoBehaviour
    {
        #region 1. Inspector 配置

        public FighterController fighter;    // 所属角色（Inspector 拖入）

        #endregion

        #region 2. 运行时状态

        private SpriteRenderer sr;
        private float shieldMaxHP = 100f;    // 兜底值，Start 里从 GameSettings 覆盖
        private float scaleVel;              // SmoothDamp 的速度缓存

        #endregion

        #region 3. Unity 生命周期

        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();   // 漏挂时自动补
        }

        private void Start()
        {
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            if (gs != null) shieldMaxHP = gs.shieldMaxHP;
        }

        // 【做什么】每帧同步护盾的显隐与大小
        private void Update()
        {
            if (fighter == null || sr == null) return;

            // 显隐：举盾 → 显示；松盾/破盾 → 隐藏
            bool visible = fighter.isShielding;
            if (sr.enabled != visible)
                sr.enabled = visible;

            // 大小：耐久比例映射到缩放，最低 0.15 倍保证还看得见
            float hpRatio = Mathf.Clamp01(fighter.currentShieldHP / shieldMaxHP);
            float targetScale = hpRatio <= 0f ? 0f : Mathf.Max(0.15f, hpRatio);
            float s = Mathf.SmoothDamp(transform.localScale.x, targetScale, ref scaleVel, 0.05f);
            transform.localScale = Vector3.one * s;

            // 透明度固定（信息量交给缩放表达，透明度保持明亮可见）
            Color c = sr.color;
            c.a = 0.65f;
            sr.color = c;
        }

        #endregion
    }
}
