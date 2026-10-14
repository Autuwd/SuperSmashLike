using SuperSmashLike.Core;
using TMPro;
using UnityEngine;

// ============================================================
// DamagePopup — 训练模式伤害飘字
// 职责：命中时在受击者头顶弹出一个伤害数值，向上飘并淡出后自销毁
// 架构位置：Training 层（仅训练模式使用，由 TrainingHud 触发）
// 坐标：世界坐标 → 屏幕坐标 → Canvas 局部坐标（除以 scaleFactor，兼容 CanvasScaler）
// ============================================================
namespace SuperSmashLike.Training
{
    public class DamagePopup : MonoBehaviour
    {
        private TextMeshProUGUI _text;
        private RectTransform _rect;
        private Vector2 _startAnchored;
        private float _life;
        private float _duration = 0.8f;
        private float _riseDistance = 60f;

        // 【做什么】在 Canvas 上生成一个伤害飘字
        // 【参数】victim 用于取世界坐标；damage 显示数值；font 复用已有 TMP 字体
        public static DamagePopup Spawn(Canvas canvas, FighterController victim, float damage, TMP_FontAsset font, Color color)
        {
            if (canvas == null || victim == null) return null;

            Camera cam = Camera.main;
            Vector2 anchoredPos;
            if (cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(victim.transform.position + Vector3.up * 1.5f);
                float sf = canvas.scaleFactor <= 0f ? 1f : canvas.scaleFactor;
                anchoredPos = new Vector2(sp.x / sf, sp.y / sf);
            }
            else
            {
                anchoredPos = Vector2.zero;
            }

            var go = new GameObject("DamagePopup", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = new Vector2(0f, 0f);   // 以 Canvas 左下角为基准，配合屏幕像素坐标
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(160f, 60f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = Mathf.RoundToInt(damage).ToString();
            if (font != null) text.font = font;
            text.fontSize = 40f;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.enableWordWrapping = false;

            var popup = go.AddComponent<DamagePopup>();
            popup._text = text;
            popup._rect = rect;
            popup._startAnchored = anchoredPos;
            return popup;
        }

        private void Update()
        {
            // 用 unscaledDeltaTime：慢动作/暂停时飘字仍按真实速度播放
            _life += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_life / _duration);

            if (_rect != null)
                _rect.anchoredPosition = _startAnchored + new Vector2(0f, _riseDistance * t);

            if (_text != null)
            {
                var c = _text.color;
                c.a = 1f - t * t;   // 越到后面淡出越快
                _text.color = c;
            }

            if (_life >= _duration) Destroy(gameObject);
        }
    }
}
