using UnityEngine;
using UnityEngine.UI;

// ============================================================
// SlidingToggle — 滑动开关（UGUI Toggle 的滑块左右滑动 + 开关高亮）
// 用法：挂在带 Toggle 的物体上；handle 指向滑块 RectTransform；
//      onColor/offColor 为槽/滑块高亮色（开=黄，关=灰）。
// ============================================================
namespace SuperSmashLike.UI
{
    [RequireComponent(typeof(Toggle))]
    public class SlidingToggle : MonoBehaviour
    {
        [Header("滑块")]
        public RectTransform handle;
        [Tooltip("开时滑块的 x（相对槽中心，靠右）")]
        public float onX = 22f;
        [Tooltip("关时滑块的 x（靠左）")]
        public float offX = -22f;
        [Tooltip("滑动速度系数")]
        public float speed = 14f;

        [Header("颜色")]
        public Image slotImage;
        public Image handleImage;
        public Color onColor = new Color(1f, 0.85f, 0.2f, 1f);
        public Color offColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        private Toggle _toggle;

        private void Awake()
        {
            _toggle = GetComponent<Toggle>();
        }

        private void OnEnable()
        {
            if (_toggle == null) _toggle = GetComponent<Toggle>();
            ApplyImmediate();
        }

        private void Update()
        {
            if (handle == null) return;

            float target = _toggle != null && _toggle.isOn ? onX : offX;
            var p = handle.anchoredPosition;
            p.x = Mathf.MoveTowards(p.x, target, Mathf.Abs(onX - offX) * speed * Time.unscaledDeltaTime);
            handle.anchoredPosition = p;

            var c = _toggle != null && _toggle.isOn ? onColor : offColor;
            if (slotImage != null) slotImage.color = new Color(c.r, c.g, c.b, 0.35f);
            if (handleImage != null) handleImage.color = c;
        }

        // 【做什么】立即到位（不打滑），用于初始化/外部设值
        public void ApplyImmediate()
        {
            if (handle != null)
            {
                var p = handle.anchoredPosition;
                p.x = _toggle != null && _toggle.isOn ? onX : offX;
                handle.anchoredPosition = p;
            }
            var c = _toggle != null && _toggle.isOn ? onColor : offColor;
            if (slotImage != null) slotImage.color = new Color(c.r, c.g, c.b, 0.35f);
            if (handleImage != null) handleImage.color = c;
        }

        // 【做什么】外部同步（不触发回调）
        public void SetValue(bool on)
        {
            if (_toggle != null) _toggle.SetIsOnWithoutNotify(on);
            ApplyImmediate();
        }
    }
}
