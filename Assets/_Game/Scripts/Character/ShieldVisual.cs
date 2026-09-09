using UnityEngine;

// ============================================================
// ShieldVisual - 护盾视觉（M1-D1）
// 职责：
//   1. 举盾时显示半透明护盾，松盾/破盾时隐藏
//   2. 耐久越低盾越透明（血条式反馈）
// 架构位置：Character 层，纯表现，不参与任何战斗逻辑
// 挂载方式：Fighter 根物体下新建子物体 ShieldVisual
//   → 加 SpriteRenderer（默认材质即可，内置管线天然透明队列）
//   → 挂本脚本 → Inspector 把 Fighter 引用拖到 fighter 字段
//   → Order in Layer = +1，localPosition.z = -0.3
// ============================================================
namespace SuperSmashLike.Core
{
    public class ShieldVisual : MonoBehaviour
    {
        public FighterController fighter;    // 所属角色（Inspector 拖入）
        private SpriteRenderer sr;
        private float shieldMaxHP = 100f;    // 兜底值，Start 里从 GameSettings 覆盖

        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        }

        private void Start()
        {
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            if (gs != null) shieldMaxHP = gs.shieldMaxHP;
        }

        private void Update()
        {
            if (fighter == null || sr == null) return;

            // 举盾 → 显示；松盾/破盾 → 隐藏
            bool visible = fighter.isShielding;
            if (sr.enabled != visible)
                sr.enabled = visible;

            // 耐久越低盾越透明：满盾 0.65 透明度，残盾最低 0.15
            float hpRatio = Mathf.Clamp01(fighter.currentShieldHP / shieldMaxHP);
            Color c = sr.color;
            c.a = 0.65f * Mathf.Max(0.15f, hpRatio);
            sr.color = c;
        }
    }
}