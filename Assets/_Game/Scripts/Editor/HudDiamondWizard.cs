using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// HudDiamondWizard — 把 UI_HUD 的 P1/P2 玩家信息块改成菱形样式
//   菱形中央 = 角色头像；上方 = 击飞百分比；下方 = 命数（都常显）
// 使用：菜单 SuperSmashLike → 31. Rebuild HUD Diamond
//      或 -batchmode -executeMethod HudDiamondWizard.Run
// 说明：保留 P1_HUD/DamageDisplay、P1_HUD/StockDisplay 节点名与组件
//       （MatchManager 按这两条路径绑定），只调整内部布局/新增头像。
// ============================================================
public static class HudDiamondWizard
{
    private const string HudPath = "Assets/_Game/Prefabs/UI/UI_HUD.prefab";

    [MenuItem("SuperSmashLike/31. Rebuild HUD Diamond (M4-HUD)", false, 31)]
    public static void Run()
    {
        var root = PrefabUtility.LoadPrefabContents(HudPath);
        if (root == null) { Debug.LogError($"[HudDiamond] 找不到 {HudPath}"); return; }
        try
        {
            foreach (var hudName in new[] { "P1_HUD", "P2_HUD" })
            {
                var hud = root.transform.Find(hudName);
                if (hud == null) { Debug.LogWarning($"[HudDiamond] 找不到 {hudName}"); continue; }
                BuildDiamond(hud);
            }
            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
            Debug.Log("[HudDiamond] 完成：P1/P2 菱形信息块已重建");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildDiamond(Transform hud)
    {
        var dd = hud.Find("DamageDisplay");
        var sd = hud.Find("StockDisplay");
        if (dd == null || sd == null)
        {
            Debug.LogWarning($"[HudDiamond] {hud.name} 缺 DamageDisplay/StockDisplay，跳过");
            return;
        }

        // 旧进度条隐藏（菱形样式不需要）
        SetActiveIf(dd, "BarBG", false);
        SetActiveIf(dd, "BarFill", false);

        // 击飞百分比：菱形上方
        var pct = dd.Find("PercentText");
        if (pct != null)
        {
            Anchored(pct.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(240f, 70f));
            var t = pct.GetComponent<TextMeshProUGUI>();
            if (t != null) { t.fontSize = 52; t.alignment = TextAlignmentOptions.Center; t.color = Color.white; }
        }

        // 命数：菱形下方
        var sk = sd.Find("StockText");
        if (sk != null)
        {
            Anchored(sk.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(240f, 60f));
            var t = sk.GetComponent<TextMeshProUGUI>();
            if (t != null) { t.fontSize = 44; t.alignment = TextAlignmentOptions.Center; t.color = Color.white; }
        }
        SetActiveIf(sd, "TimerText", false);

        // 菱形底板（旋转 45° 的方块）
        var diamond = EnsureImage(dd, "DiamondBG", new Vector2(0f, 0f), new Vector2(150f, 150f), new Color(0f, 0f, 0f, 0.45f));
        diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        diamond.transform.SetAsFirstSibling();

        // 角色头像：菱形中央（不旋转，保持角色正立）
        var portrait = EnsureImage(dd, "Portrait", new Vector2(0f, 0f), new Vector2(96f, 96f), Color.white);

        // 绑定到 DamageDisplay.portrait
        var comp = dd.GetComponent<DamageDisplay>();
        if (comp != null)
        {
            var so = new SerializedObject(comp);
            so.FindProperty("portrait").objectReferenceValue = portrait;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ==================== 小工具 ====================

    private static void SetActiveIf(Transform parent, string name, bool active)
    {
        var t = parent.Find(name);
        if (t != null) t.gameObject.SetActive(active);
    }

    private static void Anchored(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static Image EnsureImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var t = parent.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform));
        if (go.transform.parent != parent) go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        Anchored(go, new Vector2(0.5f, 0.5f), pos, size);
        return img;
    }
}
