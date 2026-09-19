using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// FrameMeterPrefabBuilder — 帧数表预设体一键生成器
// 职责：把原本 FrameMeter 在 Awake 里代码创建的 UI 结构固化成预设体，
//       方便在 Inspector 里直接调整表现（颜色/字号/位置）
// 架构位置：Editor 层（不进包体）
// 使用方式：菜单 Tools → FrameMeter → Create Prefab
// 产物：Assets/_Game/Prefabs/UI/FrameMeter.prefab
//
// 【注意】生成时的布局算法必须与 FrameMeter.Awake 保持一致，
//   否则生成出来的预设体与运行时重排结果对不上。
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// ============================================================
public static class FrameMeterPrefabBuilder
{
    #region 1. 常量

    private const string PrefabPath = "Assets/_Game/Prefabs/UI/FrameMeter.prefab";

    #endregion

    #region 2. 菜单入口

    [MenuItem("Tools/FrameMeter/Create Prefab")]
    public static void CreatePrefab()
    {
        // ---- 根节点：RectTransform + FrameMeter ----
        var root = new GameObject("FrameMeter", typeof(RectTransform), typeof(FrameMeter));
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(600f, 120f);   // 基准尺寸，可随意改

        // ---- 布局参数（与 FrameMeter.Awake 的算法一致）----
        float myW = 600f, myH = 120f;
        int totalCells = 60;
        float barHeight = myH * 0.55f;
        float cellWidth = (myW - totalCells) / totalCells;
        float barWidth = totalCells * cellWidth;
        float startX = -barWidth / 2f;
        float segFontSize = 14f, fontSize = 20f, textOffsetY = 4f;

        var fm = root.GetComponent<FrameMeter>();
        fm.totalCells = totalCells;

        Vector2 anchor = new Vector2(0.5f, 0.5f);

        // ---- 60 个格子 Cell0..Cell59 ----
        // 命名规约：FrameMeter 靠 transform.Find("Cells/Cell{i}") 找
        for (int i = 0; i < totalCells; i++)
        {
            var go = new GameObject($"Cell{i}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root.transform, false);

            var img = go.GetComponent<Image>();
            img.color = fm.cEmpty;
            img.raycastTarget = false;

            var crt = img.rectTransform;
            crt.anchorMin = crt.anchorMax = anchor;
            crt.sizeDelta = new Vector2(cellWidth - 1f, barHeight);
            crt.anchoredPosition = new Vector2(startX + i * cellWidth + cellWidth * 0.5f, 0f);
        }

        // ---- 3 个段末数字标签 Seg0..Seg2 ----
        for (int s = 0; s < 3; s++)
        {
            var go = new GameObject($"Seg{s}", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(root.transform, false);

            var txt = go.GetComponent<TextMeshProUGUI>();
            txt.fontSize = segFontSize;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;

            var trt = txt.rectTransform;
            trt.anchorMin = trt.anchorMax = anchor;
            trt.sizeDelta = new Vector2(cellWidth * 4f, segFontSize * 1.5f);
            trt.anchoredPosition = new Vector2(-1000f, 0f);   // 先藏到屏幕外
        }

        // ---- 游标 Cursor（白色竖线）----
        {
            var go = new GameObject("Cursor", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root.transform, false);

            var img = go.GetComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;

            var crt = img.rectTransform;
            crt.anchorMin = crt.anchorMax = anchor;
            crt.sizeDelta = new Vector2(3f, barHeight + 6f);
            crt.anchoredPosition = new Vector2(-1000f, 0f);
        }

        // ---- 信息文本 Info ----
        {
            var go = new GameObject("Info", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(root.transform, false);

            var txt = go.GetComponent<TextMeshProUGUI>();
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.raycastTarget = false;

            var irt = txt.rectTransform;
            irt.anchorMin = irt.anchorMax = anchor;
            irt.sizeDelta = new Vector2(barWidth + 60f, fontSize * 2f);
            irt.anchoredPosition = new Vector2(0f, barHeight / 2f + textOffsetY);
        }

        // ---- 保存为预设体 ----
        EnsureFolder("Assets/_Game/Prefabs/UI");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);   // 场景里不要留临时对象

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[FrameMeterPrefabBuilder] 已生成 {PrefabPath}，可在 Inspector 调整表现后拖入场景。");
    }

    #endregion

    #region 3. 私有工具

    // 【做什么】递归确保文件夹存在（AssetDatabase 没有自动建父目录的能力）
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = folder.Substring(0, folder.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
    }

    #endregion
}
