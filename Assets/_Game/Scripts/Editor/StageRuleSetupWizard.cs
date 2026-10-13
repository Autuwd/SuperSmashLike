using System.Collections.Generic;
using System.IO;
using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using SuperSmashLike.Stage;
using SuperSmashLike.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// StageRuleSetupWizard — M4-D2/D3 一键搭建（编辑器工具）
// 职责：
//   1. 生成 3 个舞台预制体 + 3 个 StageData 资产 + 3 张缩略图
//   2. 在 BattleTest 场景里搭建 StageSelectPanel / RuleSelectPanel
//   3. 自动接线 GameSettings.selectableStages / StageManager / UIFlowController
// 使用：
//   菜单 SuperSmashLike → 30. Setup Stage+Rule Select
//   或 -batchmode -executeMethod StageRuleSetupWizard.Run
// 【注意】可重复执行（幂等）：已存在的舞台/面板会被覆盖重建
// ============================================================
public static class StageRuleSetupWizard
{
    private const string ScenePath = "Assets/_Game/Scenes/BattleTest.unity";
    private const string StagePrefabDir = "Assets/_Game/Prefabs/Stages";
    private const string StageSoDir = "Assets/_Game/ScriptableObjects/Stages";
    private const string ThumbDir = "Assets/_Game/Art/StageUI";
    private const string FontPath = "Assets/_Game/Fonts/MSYH_Dynamic SDF.asset";

    [MenuItem("SuperSmashLike/30. Setup Stage+Rule Select (M4-D2/D3)", false, 30)]
    public static void Setup()
    {
        Run();
    }

    public static void Run()
    {
        EnsureDirs();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) Debug.LogWarning($"[StageRuleSetupWizard] 字体未找到：{FontPath}（TMP 文本会用默认字体）");

        var stages = CreateStages();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[StageRuleSetupWizard] 无法打开场景 {ScenePath}");
            return;
        }

        WireStageManagerAndSettings(stages);
        BuildPanels(font);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[StageRuleSetupWizard] 完成：{stages.Count} 个舞台 + StageSelect/RuleSelect 面板已写入场景。");
    }

    // ==================== 目录 ====================
    #region 目录

    private static void EnsureDirs()
    {
        EnsureDir(StagePrefabDir);
        EnsureDir(StageSoDir);
        EnsureDir(ThumbDir);
    }

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent)) AssetDatabase.CreateFolder(parent, name);
    }

    #endregion

    // ==================== 舞台资产 ====================
    #region 舞台

    // 平台布局编辑：克隆原始 BattleArena.prefab 后，按名字改某平台的 位置/缩放/启用
    private class PlatEdit
    {
        public string name;
        public Vector3 pos;
        public Vector3 scale;
        public bool active;
        public PlatEdit(string n, Vector3 p, Vector3 s, bool a) { name = n; pos = p; scale = s; active = a; }
    }

    private static List<StageData> CreateStages()
    {
        var result = new List<StageData>();
        string basePath = $"{StagePrefabDir}/BattleArena.prefab";

        // ---- 1. 天空竞技场 = 主人手动调过的原始地图，直接引用，不重新生成 ----
        var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
        if (arenaPrefab == null)
            Debug.LogError($"[StageRuleSetupWizard] 找不到原始舞台 {basePath}（天空竞技场将为空）");
        result.Add(WriteStageData("SD_Arena", "天空竞技场", arenaPrefab,
            30f, 20f, new Vector2(-20f, -10f), new Vector2(20f, 15f), new Color(0.30f, 0.46f, 0.78f)));

        // ---- 2. 古罗马斗兽场 = 克隆原始地图改布局（沿用原模型/材质/碰撞器/边界/出生点/层）----
        var colPrefab = CloneStage("Stage_Colosseum", basePath, new[]
        {
            new PlatEdit("MainPlatform",  new Vector3(0f, -2f, 0f),   new Vector3(14f, 0.5f, 1f), true),
            new PlatEdit("LeftPlatform",  new Vector3(-5f, 3.5f, 0f), new Vector3(5f, 0.5f, 1f),  true),
            new PlatEdit("RightPlatform", new Vector3(5f, 3.5f, 0f),  new Vector3(5f, 0.5f, 1f),  true),
            new PlatEdit("TopPlatform",   new Vector3(0f, 7f, 0f),    new Vector3(4f, 0.5f, 1f),  true),
        });
        result.Add(WriteStageData("SD_Colosseum", "古罗马斗兽场", colPrefab,
            30f, 20f, new Vector2(-20f, -10f), new Vector2(20f, 15f), new Color(0.62f, 0.50f, 0.32f)));

        // ---- 3. 高塔对决 = 三层纵向（隐藏原 RightPlatform）----
        var towerPrefab = CloneStage("Stage_Tower", basePath, new[]
        {
            new PlatEdit("MainPlatform",  new Vector3(0f, -3f, 0f),  new Vector3(10f, 0.5f, 1f), true),
            new PlatEdit("LeftPlatform",  new Vector3(0f, 1.5f, 0f), new Vector3(8f, 0.5f, 1f),  true),
            new PlatEdit("RightPlatform", Vector3.zero,              Vector3.one,                false),
            new PlatEdit("TopPlatform",   new Vector3(0f, 6f, 0f),   new Vector3(6f, 0.5f, 1f),  true),
        });
        result.Add(WriteStageData("SD_Tower", "高塔对决", towerPrefab,
            30f, 20f, new Vector2(-20f, -10f), new Vector2(20f, 15f), new Color(0.42f, 0.34f, 0.60f)));

        // 清掉上一版误生成的 Stage_Arena（天空竞技场已改用原始 BattleArena.prefab）
        string orphan = $"{StagePrefabDir}/Stage_Arena.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(orphan) != null) AssetDatabase.DeleteAsset(orphan);

        return result;
    }

    // 克隆源舞台 → 改平台布局 → 另存为新预制体（完整沿用原模型/材质/碰撞器/BlastZone/出生点/层）
    private static GameObject CloneStage(string prefabName, string srcPath, PlatEdit[] edits)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
        if (src == null)
        {
            Debug.LogError($"[StageRuleSetupWizard] 克隆源缺失：{srcPath}");
            return null;
        }

        // 【关键】用 Object.Instantiate（独立副本）而非 PrefabUtility.InstantiatePrefab：
        // 后者得到的是"预制体实例"，另存出来会变成嵌套预制体（只有 95 行、内容全在嵌套实例里）。
        // 独立副本另存 → 展开成完整预制体（与 BattleArena.prefab 同结构，约 1100 行）。
        var inst = Object.Instantiate(src);
        inst.name = prefabName;
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        foreach (var e in edits)
        {
            var t = FindDeep(inst.transform, e.name);
            if (t == null) { Debug.LogWarning($"[StageRuleSetupWizard] {prefabName} 找不到平台 {e.name}"); continue; }
            t.localPosition = e.pos;
            t.localScale = e.scale;
            t.gameObject.SetActive(e.active);
        }

        string path = $"{StagePrefabDir}/{prefabName}.prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(inst, path);
        Object.DestroyImmediate(inst);
        return saved;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static StageData WriteStageData(string soName, string displayName, GameObject prefab,
        float bw, float bh, Vector2 camMin, Vector2 camMax, Color thumbBase)
    {
        var thumb = CreateThumbnail(soName, thumbBase);

        string soPath = $"{StageSoDir}/{soName}.asset";
        var sd = AssetDatabase.LoadAssetAtPath<StageData>(soPath);
        if (sd == null)
        {
            sd = ScriptableObject.CreateInstance<StageData>();
            AssetDatabase.CreateAsset(sd, soPath);
        }

        sd.stageName = displayName;
        sd.stagePrefab = prefab;
        sd.stageThumbnail = thumb;
        sd.cameraBoundsMin = camMin;
        sd.cameraBoundsMax = camMax;
        sd.blastZoneWidth = bw;
        sd.blastZoneHeight = bh;
        EditorUtility.SetDirty(sd);
        return sd;
    }

    private static Sprite CreateThumbnail(string assetName, Color baseCol)
    {
        const int w = 320, h = 180;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = y / (float)(h - 1);
                px[y * w + x] = Color.Lerp(baseCol * 0.55f, baseCol, t);
            }
        }

        // 三条平台条（示意）
        var accent = new Color(0.92f, 0.92f, 0.92f, 0.9f);
        DrawBar(px, w, h, 40, 280, 66, 9, accent);
        DrawBar(px, w, h, 70, 140, 112, 7, accent);
        DrawBar(px, w, h, 180, 250, 112, 7, accent);

        tex.SetPixels(px);
        tex.Apply();

        string path = $"{ThumbDir}/{assetName}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void DrawBar(Color[] px, int w, int h, int x0, int x1, int y, int thick, Color col)
    {
        for (int yy = y; yy < y + thick && yy < h; yy++)
            for (int xx = x0; xx < x1 && xx < w; xx++)
                if (xx >= 0 && yy >= 0) px[yy * w + xx] = col;
    }

    #endregion

    // ==================== 场景接线 ====================
    #region 接线

    private static void WireStageManagerAndSettings(List<StageData> stages)
    {
        var gm = Object.FindObjectOfType<GameManager>();
        var gs = gm != null ? gm.gameSettings : null;
        if (gs != null)
        {
            gs.selectableStages = stages;
            EditorUtility.SetDirty(gs);
        }
        else
        {
            Debug.LogWarning("[StageRuleSetupWizard] 找不到 GameManager.gameSettings，selectableStages 未写入");
        }

        var mm = Object.FindObjectOfType<MatchManager>();
        var cam = Object.FindObjectOfType<CameraManager>();

        // StageRoot：把场景里已有的 BattleArena 实例收进去
        var stageRoot = GameObject.Find("StageRoot");
        if (stageRoot == null) stageRoot = new GameObject("StageRoot");
        var arena = GameObject.Find("BattleArena");
        if (arena != null && arena.transform.parent != stageRoot.transform)
            arena.transform.SetParent(stageRoot.transform, true);

        var sm = Object.FindObjectOfType<StageManager>();
        if (sm == null) sm = stageRoot.AddComponent<StageManager>();
        sm.stageRoot = stageRoot.transform;
        sm.matchManager = mm;
        sm.cameraManager = cam;
        sm.fallbackStagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{StagePrefabDir}/Stage_Arena.prefab");
        EditorUtility.SetDirty(sm);
    }

    #endregion

    // ==================== UI 面板 ====================
    #region UI

    private static void BuildPanels(TMP_FontAsset font)
    {
        var ui = Object.FindObjectOfType<UIFlowController>();
        if (ui == null)
        {
            Debug.LogError("[StageRuleSetupWizard] 找不到 UIFlowController，无法搭建 UI 面板");
            return;
        }

        // 【关键】面板必须挂到"菜单 Canvas"下（与 CharacterSelectPanel 同一个父级）。
        // 不能用 FindObjectOfType<Canvas>() —— 它可能返回 HUD 预制体实例里的 Canvas，
        // 面板会被塞进 HUD 层级；HUD 非战斗态隐藏 → 面板永远看不见（表现为"进状态后卡住"）。
        Transform menuCanvas = null;
        var soUI = new SerializedObject(ui);
        var csProp = soUI.FindProperty("characterSelectPanel");
        var csPanel = csProp != null ? csProp.objectReferenceValue as GameObject : null;
        if (csPanel != null) menuCanvas = csPanel.transform.parent;
        if (menuCanvas == null)
        {
            var cv = Object.FindObjectOfType<Canvas>();
            if (cv != null) menuCanvas = cv.transform;
        }
        if (menuCanvas == null)
        {
            Debug.LogError("[StageRuleSetupWizard] 找不到菜单 Canvas，无法搭建 UI 面板");
            return;
        }
        Debug.Log($"[StageRuleSetupWizard] 面板父节点 = {menuCanvas.name}");

        RemoveExistingPanels();

        var stagePanel = BuildStageSelect(menuCanvas, font);
        var rulePanel = BuildRuleSelect(menuCanvas, font);
        stagePanel.SetActive(false);
        rulePanel.SetActive(false);

        var p1 = soUI.FindProperty("stageSelectPanel");
        var p2 = soUI.FindProperty("ruleSelectPanel");
        if (p1 != null) p1.objectReferenceValue = stagePanel;
        if (p2 != null) p2.objectReferenceValue = rulePanel;
        soUI.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    // 删除场景中已存在的两个面板（无论挂在哪个父级，都能清掉上次错误挂载的残留）
    private static void RemoveExistingPanels()
    {
        foreach (var c in Object.FindObjectsOfType<StageSelectPanel>(true))
            if (c != null && c.gameObject.scene.IsValid()) Object.DestroyImmediate(c.gameObject);
        foreach (var c in Object.FindObjectsOfType<RuleSelectPanel>(true))
            if (c != null && c.gameObject.scene.IsValid()) Object.DestroyImmediate(c.gameObject);
    }

    private static GameObject BuildStageSelect(Transform canvas, TMP_FontAsset font)
    {
        var root = NewUI("StageSelectPanel", canvas);
        Stretch(RT(root));
        AddImage(root, new Color(0.07f, 0.08f, 0.12f, 0.97f));

        AddLabel(root.transform, font, "Title", "选择地图", 52, new Vector2(0f, -70f), new Vector2(900f, 70f), new Vector2(0.5f, 1f), Color.white);

        var thumbs = new Image[3];
        var frames = new Image[3];
        var names = new TextMeshProUGUI[3];
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 320f;
            var frame = NewUI($"ThumbFrame_{i + 1}", root.transform);
            Anchored(RT(frame), new Vector2(0.5f, 0.5f), new Vector2(x, 210f), new Vector2(250f, 150f));
            frames[i] = AddImage(frame, new Color(1f, 1f, 1f, 0.25f));

            var th = NewUI($"Thumb_{i + 1}", frame.transform);
            Stretch(RT(th));
            RT(th).offsetMin = new Vector2(6f, 6f);
            RT(th).offsetMax = new Vector2(-6f, -6f);
            thumbs[i] = AddImage(th, Color.white);

            var nm = NewUI($"ThumbName_{i + 1}", root.transform);
            Anchored(RT(nm), new Vector2(0.5f, 0.5f), new Vector2(x, 120f), new Vector2(300f, 40f));
            names[i] = AddText(nm, "", 26, Color.white, font);
        }

        var preview = NewUI("BigPreview", root.transform);
        Anchored(RT(preview), new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(560f, 315f));
        var previewImg = AddImage(preview, Color.white);

        var nameGo = NewUI("StageName", root.transform);
        Anchored(RT(nameGo), new Vector2(0.5f, 0.5f), new Vector2(0f, -290f), new Vector2(900f, 56f));
        var nameTxt = AddText(nameGo, "", 40, new Color(1f, 0.85f, 0.2f), font);

        var infoGo = NewUI("StageInfo", root.transform);
        Anchored(RT(infoGo), new Vector2(0.5f, 0.5f), new Vector2(0f, -350f), new Vector2(900f, 56f));
        var infoTxt = AddText(infoGo, "", 26, Color.white, font);

        var hintGo = NewUI("Hint", root.transform);
        Anchored(RT(hintGo), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1200f, 44f));
        var hintTxt = AddText(hintGo, "", 24, new Color(0.80f, 0.85f, 0.95f), font);

        var comp = root.AddComponent<StageSelectPanel>();
        var so = new SerializedObject(comp);
        SetObjectArray(so, "thumbnails", thumbs);
        SetObjectArray(so, "thumbFrames", frames);
        SetObjectArray(so, "thumbNames", names);
        so.FindProperty("bigPreview").objectReferenceValue = previewImg;
        so.FindProperty("nameText").objectReferenceValue = nameTxt;
        so.FindProperty("infoText").objectReferenceValue = infoTxt;
        so.FindProperty("hintText").objectReferenceValue = hintTxt;
        so.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }

    private static GameObject BuildRuleSelect(Transform canvas, TMP_FontAsset font)
    {
        var root = NewUI("RuleSelectPanel", canvas);
        Stretch(RT(root));
        AddImage(root, new Color(0.07f, 0.08f, 0.12f, 0.97f));

        AddLabel(root.transform, font, "Title", "比赛规则", 52, new Vector2(0f, -70f), new Vector2(900f, 70f), new Vector2(0.5f, 1f), Color.white);

        var rowTexts = new TextMeshProUGUI[4];
        var rowFrames = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            float y = 170f - i * 80f;
            var frame = NewUI($"Row_{i + 1}", root.transform);
            Anchored(RT(frame), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(900f, 64f));
            rowFrames[i] = AddImage(frame, new Color(0f, 0f, 0f, 0.15f));

            var txt = NewUI("Text", frame.transform);
            Stretch(RT(txt));
            RT(txt).offsetMin = new Vector2(30f, 0f);
            RT(txt).offsetMax = new Vector2(-30f, 0f);
            var t = AddText(txt, "", 32, Color.white, font);
            t.alignment = TextAlignmentOptions.Left;
            rowTexts[i] = t;
        }

        var descGo = NewUI("DifficultyDesc", root.transform);
        Anchored(RT(descGo), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(900f, 36f));
        var descTxt = AddText(descGo, "", 24, new Color(1f, 0.85f, 0.2f), font);

        var advGroup = NewUI("AdvancedGroup", root.transform);
        Anchored(RT(advGroup), new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(900f, 220f));

        var advTexts = new TextMeshProUGUI[3];
        var advFrames = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            float y = 70f - i * 70f;
            var frame = NewUI($"AdvRow_{i + 1}", advGroup.transform);
            Anchored(RT(frame), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(820f, 56f));
            advFrames[i] = AddImage(frame, new Color(0f, 0f, 0f, 0.15f));

            var txt = NewUI("Text", frame.transform);
            Stretch(RT(txt));
            RT(txt).offsetMin = new Vector2(30f, 0f);
            RT(txt).offsetMax = new Vector2(-30f, 0f);
            var t = AddText(txt, "", 28, Color.white, font);
            t.alignment = TextAlignmentOptions.Left;
            advTexts[i] = t;
        }

        var hintGo = NewUI("Hint", root.transform);
        Anchored(RT(hintGo), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1400f, 44f));
        var hintTxt = AddText(hintGo, "", 24, new Color(0.80f, 0.85f, 0.95f), font);

        var comp = root.AddComponent<RuleSelectPanel>();
        var so = new SerializedObject(comp);
        SetObjectArray(so, "rowTexts", rowTexts);
        SetObjectArray(so, "rowFrames", rowFrames);
        SetObjectArray(so, "advTexts", advTexts);
        SetObjectArray(so, "advFrames", advFrames);
        so.FindProperty("advancedGroup").objectReferenceValue = advGroup;
        so.FindProperty("hintText").objectReferenceValue = hintTxt;
        so.FindProperty("difficultyDescText").objectReferenceValue = descTxt;
        so.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }

    #endregion

    // ==================== 小工具 ====================
    #region 小工具

    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        go.layer = uiLayer >= 0 ? uiLayer : 5;   // UI 层，与既有面板一致
        go.transform.SetParent(parent, false);
        return go;
    }

    private static RectTransform RT(GameObject go) => go.GetComponent<RectTransform>();

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void Anchored(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static Image AddImage(GameObject go, Color color)
    {
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size, Color color, TMP_FontAsset font)
    {
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        if (font != null) t.font = font;
        return t;
    }

    private static void AddLabel(Transform parent, TMP_FontAsset font, string name, string text,
        float size, Vector2 pos, Vector2 dim, Vector2 anchor, Color color)
    {
        var go = NewUI(name, parent);
        Anchored(RT(go), anchor, pos, dim);
        AddText(go, text, size, color, font);
    }

    private static void SetObjectArray<T>(SerializedObject so, string prop, T[] values) where T : Object
    {
        var p = so.FindProperty(prop);
        if (p == null) return;
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void RemoveChild(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    #endregion
}
