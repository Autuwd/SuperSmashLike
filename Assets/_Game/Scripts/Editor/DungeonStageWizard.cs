using System.IO;
using SuperSmashLike.Core;
using SuperSmashLike.Stage;
using UnityEditor;
using UnityEngine;

// ============================================================
// DungeonStageWizard — M7-D2 地牢舞台一键搭建（编辑器工具）
// 职责：
//   1. 克隆 BattleArena.prefab → 改地牢布局（石砖主台 16×4 / 双侧高台 / 中阶台）
//   2. 换 4 张地牢材质 + 背景装饰（Synty PolygonDungeon：柱/灯/残阶/石堆/符文柱）
//   3. 收紧出界边界（±20，比森林 ±36 更近）与出生点，写入 Stage_Dungeon.prefab
//   4. 生成 SD_Dungeon.asset（相机边界 ±18/±11）+ SD_Dungeon.png 缩略图
// 使用：
//   菜单 SuperSmashLike → 32. Setup Dungeon Stage (M7-D2)
//   或 -batchmode -executeMethod DungeonStageWizard.Build
// 【注意】可重复执行（幂等）：重跑会覆盖重建全部产物
// 【注意】本向导只生成资产；选图接入（GameSettings.selectableStages / UI）属 M7-D3
// 【注意】素材包内无"锁链"预制体（已检索确认），用残阶/断柱/石堆强化废墟氛围
//
// 【设计注】层高按跳跃力实测标定（沿用 Forest 结论）：
//   重力 = 9.81 × gravityScale(2.2) ≈ 21.6；最弱角色 Paladin 双跳合计爬升 ≈ 4.82
//   三阶爬升链：主台顶面 0 → 中台顶面 3 → 侧高台顶面 6（每跳 3.0，全角色可达）
//   主台 16×4 厚石砖（实心，可站立）；中台/侧台单向可穿（继承克隆源的 Platform 配置）
//   出界 ±20（森林 ±36 更近，增加危机感）；相机 ±18/±11（贴合收紧，压迫感）
// ============================================================
public static class DungeonStageWizard
{
    private const string SrcArenaPath = "Assets/_Game/Prefabs/Stages/BattleArena.prefab";
    private const string DungeonPrefabPath = "Assets/_Game/Prefabs/Stages/Stage_Dungeon.prefab";
    private const string StageSoDir = "Assets/_Game/ScriptableObjects/Stages";
    private const string ThumbDir = "Assets/_Game/Art/StageUI";
    private const string MatDir = "Assets/_Game/Prefabs/Material";
    private const string EnvPrefabDir = "Assets/Synty/PolygonDungeon/Prefabs/Environments";

    [MenuItem("SuperSmashLike/32. Setup Dungeon Stage (M7-D2)", false, 32)]
    public static void Setup() => Build();

    public static void Build()
    {
        EnsureDir(StageSoDir);
        EnsureDir(ThumbDir);
        EnsureDir(MatDir);

        // ---- 1. 地牢材质（4 张，Standard shader 纯色，与既有平台材质同规格）----
        var matMain = EnsureMaterial("Dungeon_Main_Mat", new Color(0.30f, 0.30f, 0.34f));   // 深灰石砖（主台）
        var matSide = EnsureMaterial("Dungeon_Side_Mat", new Color(0.36f, 0.32f, 0.28f));   // 暖灰石（双侧高台）
        var matCenter = EnsureMaterial("Dungeon_Center_Mat", new Color(0.30f, 0.30f, 0.33f)); // 灰石（中台）
        var matBg = EnsureMaterial("Dungeon_BG_Mat", new Color(0.14f, 0.13f, 0.17f));       // 暗色背景地面

        // ---- 2. 克隆源舞台（独立副本，避免嵌套预制体）----
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(SrcArenaPath);
        if (src == null)
        {
            Debug.LogError($"[DungeonStageWizard] 克隆源缺失：{SrcArenaPath}");
            return;
        }
        var inst = Object.Instantiate(src);
        // 完全解包：确保产物是独立普通预制体（避免保存成 BattleArena 的 Variant）
        if (PrefabUtility.IsPartOfPrefabInstance(inst))
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = "Stage_Dungeon";
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        // ---- 3. 平台布局（主台实心；中台/侧台单向可穿，继承克隆源配置）----
        SetPlatform(inst.transform, "MainPlatform", new Vector3(0f, -2f, 0f), new Vector3(16f, 4f, 1f), matMain);
        SetPlatform(inst.transform, "LeftPlatform", new Vector3(-6.5f, 5.5f, 0f), new Vector3(3f, 1f, 1f), matSide);
        SetPlatform(inst.transform, "RightPlatform", new Vector3(6.5f, 5.5f, 0f), new Vector3(3f, 1f, 1f), matSide);
        SetPlatform(inst.transform, "TopPlatform", new Vector3(0f, 2.5f, 0f), new Vector3(4f, 1f, 1f), matCenter);
        // 中台改名（Arena 的"顶部台"在地牢里是中间踏板）
        var center = FindDeep(inst.transform, "TopPlatform");
        if (center != null) center.name = "CenterPlatform";

        // ---- 4. 出界边界（±20；顺带修正 side 方向标记）----
        SetBlastZone(inst.transform, "BlastZone_Top", new Vector3(0f, 20f, 0f), new Vector2(42f, 2f), BlastZone.Side.Top);
        SetBlastZone(inst.transform, "BlastZone_Bottom", new Vector3(0f, -20f, 0f), new Vector2(42f, 2f), BlastZone.Side.Bottom);
        SetBlastZone(inst.transform, "BlastZone_Left", new Vector3(-20f, 0f, 0f), new Vector2(2f, 42f), BlastZone.Side.Left);
        SetBlastZone(inst.transform, "BlastZone_Right", new Vector3(20f, 0f, 0f), new Vector2(2f, 42f), BlastZone.Side.Right);

        // ---- 5. 出生点 ----
        SetSpawnPoint(inst.transform, "SpawnPoint_1", new Vector3(-4f, 4f, 0f));
        SetSpawnPoint(inst.transform, "SpawnPoint_2", new Vector3(4f, 4f, 0f));
        SetSpawnPoint(inst.transform, "SpawnPoint_3", new Vector3(-6.5f, 8f, 0f));
        SetSpawnPoint(inst.transform, "SpawnPoint_4", new Vector3(6.5f, 8f, 0f));

        // ---- 6. 背景装饰 ----
        Decorate(inst.transform, matBg);

        var saved = PrefabUtility.SaveAsPrefabAsset(inst, DungeonPrefabPath);
        Object.DestroyImmediate(inst);

        // ---- 7. StageData + 缩略图 ----
        var thumb = CreateThumbnail("SD_Dungeon", new Color(0.32f, 0.31f, 0.35f));
        string soPath = $"{StageSoDir}/SD_Dungeon.asset";
        var sd = AssetDatabase.LoadAssetAtPath<StageData>(soPath);
        if (sd == null)
        {
            sd = ScriptableObject.CreateInstance<StageData>();
            AssetDatabase.CreateAsset(sd, soPath);
        }
        sd.stageName = "幽暗地牢";
        sd.stagePrefab = saved;
        sd.stageThumbnail = thumb;
        sd.cameraBoundsMin = new Vector2(-18f, -11f);
        sd.cameraBoundsMax = new Vector2(18f, 11f);
        sd.blastZoneWidth = 24f;
        sd.blastZoneHeight = 16f;
        sd.ambientLight = new Color(0.62f, 0.60f, 0.68f);
        EditorUtility.SetDirty(sd);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DungeonStageWizard] 完成：Stage_Dungeon.prefab + SD_Dungeon.asset + SD_Dungeon.png（出界 ±20 / 相机 ±18,±11）");
    }

    // ==================== 装饰 ====================
    #region 装饰

    // 装饰坐标说明（z 轴：+z 远离相机，机位 (0,6,-12) 俯角 40°）
    // 素材尺寸实测（缩放前，高×宽）：
    //   Square_06 高 5.01 / Square_02 高 3.01 / Square_05 高 2.83 / Square_03 高 1.97
    //   Square_08 高 0.82 / Stone_Lantern_01 高 1.91 / Railing_Pillar_01 高 1.22 / _03 高 1.92
    //   Stairs_Broken_01 4.99×1.76（轴心下沉 0.99）/ _02 4.96×1.65（轴心下沉 1.52）
    //   RockPile_Pillar_01/_02 高 2.41 / Rune_Pillar_03 高 2.38
    //   Pillar_Broken_Pile_01 (0.70,0.57,1.40) / _02 (2.08,1.17,2.73)
    // 地面顶面 -4（与主台底面齐平），装饰均以 locY=-4 为站立基准
    private static void Decorate(Transform root, Material matBG)
    {
        // 背景大地板：大立方体充当幽暗地面（顶面 -4）
        var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bg.name = "BG_Floor";
        var col = bg.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        bg.GetComponent<MeshRenderer>().sharedMaterial = matBG;
        bg.transform.SetParent(root, false);
        bg.transform.localPosition = new Vector3(0f, -6.5f, 6.5f);
        bg.transform.localScale = new Vector3(52f, 5f, 8f);

        // 背景高柱列（z 8.5~9.5）
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_06", new Vector3(-17f, -4f, 9.0f), 0f, 1.5f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_06", new Vector3(17f, -4f, 9.0f), 0f, 1.3f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_02", new Vector3(-11f, -4f, 8.5f), 15f, 1.6f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_02", new Vector3(11f, -4f, 8.5f), -15f, 1.6f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_05", new Vector3(-6.5f, -4f, 9.5f), -10f, 1.4f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_05", new Vector3(6.5f, -4f, 9.5f), 10f, 1.4f);

        // 后墙扶栏柱列（z 9.3）
        AddDeco(root, "Misc/SM_Env_Railing_Pillar_03", new Vector3(-3.2f, -4f, 9.3f), 0f, 1.2f);
        AddDeco(root, "Misc/SM_Env_Railing_Pillar_03", new Vector3(3.2f, -4f, 9.3f), 0f, 1.2f);
        AddDeco(root, "Misc/SM_Env_Railing_Pillar_01", new Vector3(-8.2f, -4f, 9.3f), 0f, 1.2f);
        AddDeco(root, "Misc/SM_Env_Railing_Pillar_01", new Vector3(8.2f, -4f, 9.3f), 0f, 1.2f);

        // 符文柱（中景幽光柱，z 7.8）
        AddDeco(root, "Rocks/SM_Env_Rune_Pillar_03", new Vector3(-4.5f, -4f, 7.8f), 0f, 1.4f);
        AddDeco(root, "Rocks/SM_Env_Rune_Pillar_03", new Vector3(4.5f, -4f, 7.8f), 0f, 1.4f);

        // 残阶（中景斜置，z 6.8；轴心下沉按实测补偿）
        AddDeco(root, "Floors/SM_Env_Stairs_Broken_01", new Vector3(-17.5f, -2.61f, 6.8f), -20f, 1.4f);
        AddDeco(root, "Floors/SM_Env_Stairs_Broken_02", new Vector3(17.5f, -2.02f, 6.8f), 200f, 1.3f);

        // 石灯笼（火把替身，z 5.6~6.0）
        AddDeco(root, "Misc/SM_Env_Stone_Lantern_01", new Vector3(-9f, -4f, 6.0f), 0f, 1.1f);
        AddDeco(root, "Misc/SM_Env_Stone_Lantern_01", new Vector3(9f, -4f, 6.0f), 0f, 1.1f);
        AddDeco(root, "Misc/SM_Env_Stone_Lantern_01", new Vector3(-13f, -4f, 5.6f), -20f, 0.9f);
        AddDeco(root, "Misc/SM_Env_Stone_Lantern_01", new Vector3(13f, -4f, 5.6f), 20f, 0.9f);

        // 石堆（z 5.8~6.6）
        AddDeco(root, "Rocks/SM_Env_RockPile_Pillar_01", new Vector3(-7f, -4f, 6.6f), 30f, 1.0f);
        AddDeco(root, "Rocks/SM_Env_RockPile_Pillar_02", new Vector3(7.5f, -4f, 6.4f), -40f, 1.2f);
        AddDeco(root, "Rocks/SM_Env_RockPile_Pillar_02", new Vector3(-15f, -4f, 5.8f), 80f, 0.9f);
        AddDeco(root, "Rocks/SM_Env_RockPile_Pillar_01", new Vector3(15f, -4f, 5.9f), -70f, 0.9f);

        // 断柱残堆（两翼，z 7.2~7.5；残堆轴心略低于 0，按实测补偿）
        AddDeco(root, "Pillars/SM_Env_Pillar_Broken_Pile_01", new Vector3(-20f, -3.96f, 7.5f), 30f, 1.3f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Broken_Pile_02", new Vector3(20.5f, -3.83f, 7.2f), -40f, 1.2f);

        // 断短柱（近中景，z 5.5~5.6）
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_03", new Vector3(-11.5f, -4f, 5.6f), 0f, 1.0f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Square_03", new Vector3(11.5f, -4f, 5.5f), 0f, 1.0f);

        // 平台面点缀（后缘 z 0.25~0.30，不挡对局视线）
        AddDeco(root, "Pillars/SM_Env_Pillar_Broken_Pile_01", new Vector3(-6.5f, 0f, 0.30f), 0f, 0.45f);
        AddDeco(root, "Pillars/SM_Env_Pillar_Broken_Pile_01", new Vector3(6.5f, 0f, 0.30f), 0f, 0.45f);
        AddDeco(root, "Misc/SM_Env_Stone_Lantern_01", new Vector3(-6.5f, 6f, 0.25f), 0f, 0.55f);
        AddDeco(root, "Misc/SM_Env_Stone_Lantern_01", new Vector3(6.5f, 6f, 0.25f), 0f, 0.55f);
    }

    private static void AddDeco(Transform root, string prefabRelPath, Vector3 pos, float rotY, float scale)
        => AddDeco(root, prefabRelPath, pos, rotY, Vector3.one * scale);

    private static void AddDeco(Transform root, string prefabRelPath, Vector3 pos, float rotY, Vector3 scale)
    {
        string path = $"{EnvPrefabDir}/{prefabRelPath}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"[DungeonStageWizard] 素材未找到：{path}");
            return;
        }
        var go = Object.Instantiate(prefab, root);
        go.name = prefab.name;
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
            Debug.LogWarning($"[DungeonStageWizard] 找不到平台 {name}");
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
            Debug.LogWarning($"[DungeonStageWizard] 找不到出界边界 {name}");
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
            Debug.LogWarning($"[DungeonStageWizard] 找不到出生点 {name}");
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

        // 四条平台条（主台 + 中台 + 双侧高台，示意三阶布局）
        var accent = new Color(0.92f, 0.92f, 0.92f, 0.9f);
        DrawBar(px, w, h, 40, 280, 66, 9, accent);    // 主台（宽）
        DrawBar(px, w, h, 135, 185, 89, 6, accent);   // 中台（中）
        DrawBar(px, w, h, 75, 125, 112, 7, accent);   // 左侧高台
        DrawBar(px, w, h, 195, 245, 112, 7, accent);  // 右侧高台

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
