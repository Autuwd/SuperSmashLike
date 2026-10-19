using System.IO;
using SuperSmashLike.Core;
using SuperSmashLike.Stage;
using UnityEditor;
using UnityEngine;

// ============================================================
// ForestStageWizard — M7-D1 森林舞台一键搭建（编辑器工具）
// 职责：
//   1. 克隆 BattleArena.prefab → 改森林布局（宽主台 22 / 双侧台 / 高顶台）
//   2. 换 4 张森林材质 + 背景装饰（Synty PNB_Meadow_Forest：树/草/灌木/岩石/河流）
//   3. 收紧出界边界（±36，比 Arena ±40 略小）与出生点，写入 Stage_Forest.prefab
//   4. 生成 SD_Forest.asset（相机边界 ±20/±12）+ SD_Forest.png 缩略图
// 使用：
//   菜单 SuperSmashLike → 31. Setup Forest Stage (M7-D1)
//   或 -batchmode -executeMethod ForestStageWizard.Build
// 【注意】可重复执行（幂等）：重跑会覆盖重建全部产物
// 【注意】本向导只生成资产；选图接入（GameSettings.selectableStages / UI）属 M7-D3
//
// 【设计注】层高按跳跃力实测标定：
//   重力 = 9.81 × gravityScale(2.2) ≈ 21.6；跳跃初速 11~14（非首跳 ×0.85）
//   最弱角色 Paladin：单跳高 11²/(2×21.6) ≈ 2.80，双跳合计 ≈ 4.82
//   Arena 已验证爬升 4.0 → 本舞台：
//     主台顶面 -0.5 → 侧台顶面 3.5（爬升 4.0，与 Arena 一致，全角色可达）
//     侧台 → 顶台顶面 7.0（爬升 3.5）
// ============================================================
public static class ForestStageWizard
{
    private const string SrcArenaPath = "Assets/_Game/Prefabs/Stages/BattleArena.prefab";
    private const string ForestPrefabPath = "Assets/_Game/Prefabs/Stages/Stage_Forest.prefab";
    private const string StageSoDir = "Assets/_Game/ScriptableObjects/Stages";
    private const string ThumbDir = "Assets/_Game/Art/StageUI";
    private const string MatDir = "Assets/_Game/Prefabs/Material";
    private const string PnbPrefabDir = "Assets/Synty/PolygonNatureBiomes/PNB_Meadow_Forest/Prefabs";

    [MenuItem("SuperSmashLike/31. Setup Forest Stage (M7-D1)", false, 31)]
    public static void Setup() => Build();

    public static void Build()
    {
        EnsureDir(StageSoDir);
        EnsureDir(ThumbDir);
        EnsureDir(MatDir);

        // ---- 1. 森林材质（4 张，Standard shader 纯色，与既有平台材质同规格）----
        var matMain = EnsureMaterial("Forest_Main_Mat", new Color(0.42f, 0.30f, 0.18f));   // 木头（主台）
        var matVine = EnsureMaterial("Forest_Vine_Mat", new Color(0.30f, 0.48f, 0.22f));   // 藤蔓（左侧）
        var matStump = EnsureMaterial("Forest_Stump_Mat", new Color(0.36f, 0.24f, 0.15f)); // 树桩（右侧）
        var matTop = EnsureMaterial("Forest_Top_Mat", new Color(0.45f, 0.55f, 0.30f));     // 苔藓（顶台/背景地）

        // ---- 2. 克隆源舞台（独立副本，避免嵌套预制体）----
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(SrcArenaPath);
        if (src == null)
        {
            Debug.LogError($"[ForestStageWizard] 克隆源缺失：{SrcArenaPath}");
            return;
        }
        var inst = Object.Instantiate(src);
        // 完全解包：确保产物是独立普通预制体（避免保存成 BattleArena 的 Variant）
        if (PrefabUtility.IsPartOfPrefabInstance(inst))
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = "Stage_Forest";
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        // ---- 3. 平台布局 ----
        SetPlatform(inst.transform, "MainPlatform", new Vector3(0f, -2f, 0f), new Vector3(22f, 3f, 1f), matMain);
        SetPlatform(inst.transform, "LeftPlatform", new Vector3(-8f, 3.25f, 0f), new Vector3(4f, 0.5f, 1f), matVine);
        SetPlatform(inst.transform, "RightPlatform", new Vector3(8f, 3.25f, 0f), new Vector3(4f, 0.5f, 1f), matStump);
        SetPlatform(inst.transform, "TopPlatform", new Vector3(0f, 6.75f, 0f), new Vector3(6f, 0.5f, 1f), matTop);

        // ---- 4. 出界边界（±36；顺带修正 side 方向标记）----
        SetBlastZone(inst.transform, "BlastZone_Top", new Vector3(0f, 36f, 0f), new Vector2(74f, 2f), BlastZone.Side.Top);
        SetBlastZone(inst.transform, "BlastZone_Bottom", new Vector3(0f, -36f, 0f), new Vector2(74f, 2f), BlastZone.Side.Bottom);
        SetBlastZone(inst.transform, "BlastZone_Left", new Vector3(-36f, 0f, 0f), new Vector2(2f, 72f), BlastZone.Side.Left);
        SetBlastZone(inst.transform, "BlastZone_Right", new Vector3(36f, 0f, 0f), new Vector2(2f, 72f), BlastZone.Side.Right);

        // ---- 5. 出生点 ----
        SetSpawnPoint(inst.transform, "SpawnPoint_1", new Vector3(-4f, 4f, 0f));
        SetSpawnPoint(inst.transform, "SpawnPoint_2", new Vector3(4f, 4f, 0f));
        SetSpawnPoint(inst.transform, "SpawnPoint_3", new Vector3(-2f, 9f, 0f));
        SetSpawnPoint(inst.transform, "SpawnPoint_4", new Vector3(2f, 9f, 0f));

        // ---- 6. 背景装饰 ----
        Decorate(inst.transform, matTop);

        var saved = PrefabUtility.SaveAsPrefabAsset(inst, ForestPrefabPath);
        Object.DestroyImmediate(inst);

        // ---- 7. StageData + 缩略图 ----
        var thumb = CreateThumbnail("SD_Forest", new Color(0.28f, 0.50f, 0.30f));
        string soPath = $"{StageSoDir}/SD_Forest.asset";
        var sd = AssetDatabase.LoadAssetAtPath<StageData>(soPath);
        if (sd == null)
        {
            sd = ScriptableObject.CreateInstance<StageData>();
            AssetDatabase.CreateAsset(sd, soPath);
        }
        sd.stageName = "迷雾森林";
        sd.stagePrefab = saved;
        sd.stageThumbnail = thumb;
        sd.cameraBoundsMin = new Vector2(-20f, -12f);
        sd.cameraBoundsMax = new Vector2(20f, 12f);
        sd.blastZoneWidth = 28f;
        sd.blastZoneHeight = 18f;
        EditorUtility.SetDirty(sd);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ForestStageWizard] 完成：Stage_Forest.prefab + SD_Forest.asset + SD_Forest.png（出界 ±36 / 相机 ±20,±12）");
    }

    // ==================== 装饰 ====================
    #region 装饰

    // 装饰坐标说明（z 轴：+z 远离相机，机位 (0,6,-12) 俯角 40°）
    // 素材默认尺寸实测（缩放前）：Birch 树高 10.6 / Meadow 树丛 15.2 / Fruit 树 6.2
    //   草簇 ~1x1 / 大草皮 34x34 / 灌木 2.9 / 小石 0.3 / 悬崖 8.1 / 水面 5x10
    private static void Decorate(Transform root, Material matGround)
    {
        // 背景大地：大立方体充当远处森林地面（顶面 -3.0）
        var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bg.name = "BG_Ground";
        var col = bg.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        bg.GetComponent<MeshRenderer>().sharedMaterial = matGround;
        bg.transform.SetParent(root, false);
        bg.transform.localPosition = new Vector3(0f, -5.5f, 7f);
        bg.transform.localScale = new Vector3(52f, 5f, 8f);

        // 河流：水面 5x10 → (6.5,1,0.6) ≈ 32x6 横贯带
        AddDeco(root, "SM_Env_Water_Plane_01", new Vector3(0f, -2.92f, 4.8f), 0f, new Vector3(6.5f, 1f, 0.6f));

        // 背景林（两排，z 6.5~9.5）
        AddDeco(root, "SM_Env_Tree_Birch_03", new Vector3(-19.5f, -3f, 9.5f), 5f, 1.0f);
        AddDeco(root, "SM_Env_Tree_Birch_01", new Vector3(-16f, -3f, 7.5f), 15f, 0.9f);
        AddDeco(root, "SM_Env_Tree_Birch_02", new Vector3(-12.5f, -3f, 8.5f), -20f, 0.8f);
        AddDeco(root, "SM_Env_Tree_Meadow_01", new Vector3(-9.5f, -3f, 8.8f), 0f, 0.5f);
        AddDeco(root, "SM_Env_Tree_Fruit_01", new Vector3(-5.5f, -3f, 7.8f), 10f, 1.3f);
        AddDeco(root, "SM_Env_Tree_Birch_03", new Vector3(-1.5f, -3f, 9.2f), -25f, 0.85f);
        AddDeco(root, "SM_Env_Tree_Fruit_02", new Vector3(2f, -3f, 8.2f), 30f, 1.2f);
        AddDeco(root, "SM_Env_Tree_Meadow_02", new Vector3(6.5f, -3f, 8.8f), 0f, 0.5f);
        AddDeco(root, "SM_Env_Tree_Birch_01", new Vector3(10.5f, -3f, 8f), 15f, 0.8f);
        AddDeco(root, "SM_Env_Tree_Fruit_03", new Vector3(13.5f, -3f, 7.5f), -10f, 1.25f);
        AddDeco(root, "SM_Env_Tree_Birch_02", new Vector3(16.5f, -3f, 8.5f), 25f, 0.9f);
        AddDeco(root, "SM_Env_Tree_Fruit_01", new Vector3(19.5f, -3f, 9.5f), -5f, 1.35f);

        // 两侧悬崖剪影
        AddDeco(root, "SM_Env_Rock_Cliff_01", new Vector3(-21f, -3f, 8f), 120f, 1.2f);
        AddDeco(root, "SM_Env_Rock_Cliff_01", new Vector3(21f, -3f, 8f), 240f, 1.0f);

        // 背景地面低层植被（前排）
        AddDeco(root, "SM_Env_Bush_01", new Vector3(-13f, -3f, 4.8f), 0f, 1.0f);
        AddDeco(root, "SM_Env_Bush_02", new Vector3(11f, -3f, 4.8f), 40f, 0.9f);
        AddDeco(root, "SM_Env_Grass_Tall_Clump_01", new Vector3(-10f, -3f, 5.2f), 0f, 1.8f);
        AddDeco(root, "SM_Env_Grass_Tall_Clump_02", new Vector3(7f, -3f, 5.0f), 20f, 1.6f);
        AddDeco(root, "SM_Env_Grass_Large_01", new Vector3(-6f, -3f, 6.2f), 0f, 0.28f);
        AddDeco(root, "SM_Env_Grass_Large_01", new Vector3(4f, -3f, 6.4f), 60f, 0.3f);
        AddDeco(root, "SM_Env_Wildflowers_01", new Vector3(-2.5f, -3f, 5.0f), 0f, 2.2f);
        AddDeco(root, "SM_Env_Wildflowers_01", new Vector3(14.5f, -3f, 5.0f), 0f, 2.0f);

        // 平台上表面点缀（沿后缘 z≈0.25，不挡对局视线）
        AddDeco(root, "SM_Env_Grass_Tall_Clump_02", new Vector3(-9.7f, -0.5f, 0.28f), 0f, 1.6f);
        AddDeco(root, "SM_Env_Grass_Med_Clump_01", new Vector3(-6.8f, -0.5f, 0.25f), 0f, 1.8f);
        AddDeco(root, "SM_Env_Grass_Tall_Clump_03", new Vector3(6.3f, -0.5f, 0.25f), 0f, 1.7f);
        AddDeco(root, "SM_Env_Bush_01", new Vector3(9.3f, -0.5f, 0.28f), 0f, 0.75f);
        AddDeco(root, "SM_Env_Wildflowers_01", new Vector3(-2.8f, -0.5f, 0.22f), 0f, 2.0f);
        AddDeco(root, "SM_Env_Flowers_Flat_01", new Vector3(2.6f, -0.5f, 0.22f), 0f, 1.6f);
        AddDeco(root, "SM_Env_Rock_Small_01", new Vector3(-10.6f, -0.5f, 0.25f), 0f, 5f);
        AddDeco(root, "SM_Env_Rock_Small_01", new Vector3(10.6f, -0.5f, 0.25f), 0f, 4.5f);

        // 侧台 / 顶台点缀
        AddDeco(root, "SM_Env_Grass_Short_Clump_01", new Vector3(-9f, 3.5f, 0.2f), 0f, 1.5f);
        AddDeco(root, "SM_Env_Grass_Med_Clump_02", new Vector3(8.8f, 3.5f, 0.2f), 0f, 1.6f);
        AddDeco(root, "SM_Env_Wildflowers_01", new Vector3(0.8f, 7f, 0.2f), 0f, 1.8f);
    }

    private static void AddDeco(Transform root, string prefabName, Vector3 pos, float rotY, float scale)
        => AddDeco(root, prefabName, pos, rotY, Vector3.one * scale);

    private static void AddDeco(Transform root, string prefabName, Vector3 pos, float rotY, Vector3 scale)
    {
        string path = $"{PnbPrefabDir}/{prefabName}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"[ForestStageWizard] 素材未找到：{path}");
            return;
        }
        var go = Object.Instantiate(prefab, root);
        go.name = prefabName;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
        go.transform.localScale = scale;
    }

    #endregion

    // ==================== 平台 / 边界 / 出生点 ====================
    #region 布局

    private static void SetPlatform(Transform root, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var t = FindDeep(root, name);
        if (t == null)
        {
            Debug.LogWarning($"[ForestStageWizard] 找不到平台 {name}");
            return;
        }
        t.localPosition = pos;
        t.localScale = scale;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null && mat != null) mr.sharedMaterial = mat;
    }

    private static void SetBlastZone(Transform root, string name, Vector3 pos, Vector2 colSize, BlastZone.Side side)
    {
        var t = FindDeep(root, name);
        if (t == null)
        {
            Debug.LogWarning($"[ForestStageWizard] 找不到出界边界 {name}");
            return;
        }
        t.localPosition = pos;
        var col = t.GetComponent<BoxCollider2D>();
        if (col != null) col.size = colSize;
        var bz = t.GetComponent<BlastZone>();
        if (bz != null)
        {
            bz.side = side;
            bz.isTop = side == BlastZone.Side.Top;
            bz.isBottom = side == BlastZone.Side.Bottom;
            bz.isLeft = side == BlastZone.Side.Left;
            bz.isRight = side == BlastZone.Side.Right;
        }
    }

    private static void SetSpawnPoint(Transform root, string name, Vector3 pos)
    {
        var t = FindDeep(root, name);
        if (t == null)
        {
            Debug.LogWarning($"[ForestStageWizard] 找不到出生点 {name}");
            return;
        }
        t.localPosition = pos;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    #endregion

    // ==================== 资产工具 ====================
    #region 资产

    private static Material EnsureMaterial(string matName, Color color)
    {
        string path = $"{MatDir}/{matName}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.color = color;
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent)) AssetDatabase.CreateFolder(parent, name);
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
}
