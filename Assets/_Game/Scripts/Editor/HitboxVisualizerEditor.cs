using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// HitboxVisualizerEditor — 判定框可视化拖拽编辑器
// 用途：在 Scene 视图直接拖拽判定框修改位置/大小，
//       修改实时写入 FighterData 资产（Ctrl+Z 可撤销）。
// 操作：
//   中心方块 = 拖拽移动判定框（改 hitboxOffset）
//   角落方块 = 拖拽缩放判定框（改 hitboxSize）
//   红色文字 = 攻击名称
// ============================================================
[CustomEditor(typeof(HitboxVisualizer))]
public class HitboxVisualizerEditor : Editor
{
    private static GUIStyle labelStyle;

    private void OnSceneGUI()
    {
        var viz = (HitboxVisualizer)target;
        var fc = viz.GetComponent<FighterController>();
        if (fc == null || fc.fighterData == null) return;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(EditorStyles.label);
            labelStyle.normal.textColor = Color.red;
            labelStyle.fontStyle = FontStyle.Bold;
        }

        foreach (var pair in viz.GetAllAttacks())
        {
            string fieldName = pair.Key;
            AttackData ad = pair.Value;

            Vector3 center = fc.transform.position + new Vector3(ad.hitboxOffset.x, ad.hitboxOffset.y, 0);
            Vector3 half = new Vector3(ad.hitboxSize.x * 0.5f, ad.hitboxSize.y * 0.5f, 0);

            // ===== 1. 中心方块手柄：拖拽 = 移动判定框 =====
            EditorGUI.BeginChangeCheck();
            var fmh_44_25_639226830838294050 = Quaternion.identity; Vector3 newCenter = Handles.FreeMoveHandle(
                GUIUtility.GetControlID(FocusType.Passive),
                center, 0.15f, Vector3.zero, Handles.CubeHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(fc.fighterData, $"Move {fieldName} hitbox");
                Vector3 local = newCenter - fc.transform.position;
                ad.hitboxOffset = new Vector2(local.x, local.y);   // 忽略 z
                EditorUtility.SetDirty(fc.fighterData);            // 标记资产已修改
            }

            // ===== 2. 角落方块手柄：拖拽 = 缩放判定框 =====
            Vector3 corner = center + half;
            EditorGUI.BeginChangeCheck();
            var fmh_58_25_639226830838314517 = Quaternion.identity; Vector3 newCorner = Handles.FreeMoveHandle(
                GUIUtility.GetControlID(FocusType.Passive),
                corner, 0.12f, Vector3.zero, Handles.CubeHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(fc.fighterData, $"Resize {fieldName} hitbox");
                Vector3 diff = newCorner - center;
                ad.hitboxSize = new Vector2(
                    Mathf.Max(0.1f, diff.x),   // 最小 0.1 防止拖成负数
                    Mathf.Max(0.1f, diff.y));
                EditorUtility.SetDirty(fc.fighterData);
            }

            // ===== 3. 名称标签 =====
            string display = HitboxVisualizer.DisplayNames.TryGetValue(fieldName, out var name)
                ? name : fieldName;
            Handles.Label(center + Vector3.up * (ad.hitboxSize.y * 0.5f + 0.25f), display, labelStyle);
        }
    }
}