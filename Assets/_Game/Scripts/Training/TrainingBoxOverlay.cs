using System.Collections.Generic;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;
using UnityEngine;

// ============================================================
// TrainingBoxOverlay — 训练模式判定框游戏内可视化
// 职责：在 Game 视图（不只是 Scene 视图）常开绘制判定框：
//   Hitbox  → 红（激活）/ 绿（未激活）
//   Hurtbox → 橙（半透明）
// 实现：OnRenderObject + GL 画线（LoadOrtho + viewport 坐标），
//   仅在训练模式绘制；世界坐标经 Camera.WorldToScreenPoint → viewport 换算。
// 说明：Scene 视图的 OnDrawGizmos（Hitbox/Hurtbox）仍然保留，本组件不替代它。
// ============================================================
namespace SuperSmashLike.Training
{
    public class TrainingBoxOverlay : MonoBehaviour
    {
        [Tooltip("是否显示判定框")]
        public bool showBoxes = true;

        [Tooltip("判定框刷新间隔（秒）；判定的开/关实时读取，这里只控制重新搜集对象的频率")]
        public float refreshInterval = 0.5f;

        private Material _lineMat;
        private float _nextRefresh;
        private readonly List<Hitbox> _hitboxes = new();
        private readonly List<Hurtbox> _hurtboxes = new();

        private void OnRenderObject()
        {
            if (!showBoxes) return;
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentGameState != GameState.Training) return;

            Camera cam = Camera.main;
            if (cam == null || Camera.current != cam) return;   // 只画一次，避免多相机重复

            if (_lineMat == null) CreateMaterial();
            if (_lineMat == null) return;

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
                RefreshLists();
            }

            _lineMat.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.LINES);

            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (hb == null) continue;
                var col = hb.GetComponent<Collider2D>();
                if (col == null) continue;
                DrawBox(cam, col, hb.IsActive ? hb.activeColor : hb.inactiveColor);
            }

            for (int i = 0; i < _hurtboxes.Count; i++)
            {
                var hu = _hurtboxes[i];
                if (hu == null) continue;
                var col = hu.GetComponent<Collider2D>();
                if (col == null) continue;
                DrawBox(cam, col, hu.activeColor);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void CreateMaterial()
        {
            var sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            _lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _lineMat.SetInt("_ZWrite", 0);
            _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        }

        private void RefreshLists()
        {
            _hitboxes.Clear();
            _hurtboxes.Clear();
            _hitboxes.AddRange(Object.FindObjectsOfType<Hitbox>());
            _hurtboxes.AddRange(Object.FindObjectsOfType<Hurtbox>());
        }

        private static void DrawBox(Camera cam, Collider2D col, Color color)
        {
            Vector2 center;
            Vector2 size;

            if (col is BoxCollider2D box)
            {
                center = col.transform.TransformPoint(box.offset);
                size = Vector2.Scale(box.size, col.transform.lossyScale);
            }
            else if (col is CircleCollider2D circle)
            {
                center = col.transform.TransformPoint(circle.offset);
                float r = circle.radius * Mathf.Abs(col.transform.lossyScale.x);
                size = new Vector2(r * 2f, r * 2f);
            }
            else
            {
                return;
            }

            Vector2 h = size * 0.5f;
            Vector3 c0 = new Vector3(center.x - h.x, center.y - h.y, 0f);
            Vector3 c1 = new Vector3(center.x + h.x, center.y - h.y, 0f);
            Vector3 c2 = new Vector3(center.x + h.x, center.y + h.y, 0f);
            Vector3 c3 = new Vector3(center.x - h.x, center.y + h.y, 0f);

            GL.Color(color);
            Line(cam, c0, c1);
            Line(cam, c1, c2);
            Line(cam, c2, c3);
            Line(cam, c3, c0);
        }

        private static void Line(Camera cam, Vector3 a, Vector3 b)
        {
            GL.Vertex(ToViewport(cam, a));
            GL.Vertex(ToViewport(cam, b));
        }

        private static Vector3 ToViewport(Camera cam, Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            return new Vector3(sp.x / Screen.width, sp.y / Screen.height, 0f);
        }

        private void OnDestroy()
        {
            if (_lineMat != null) Destroy(_lineMat);
        }
    }
}
