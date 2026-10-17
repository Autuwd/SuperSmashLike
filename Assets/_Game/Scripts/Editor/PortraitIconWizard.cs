using System;
using System.IO;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// PortraitIconWizard — M6-D3c：生成 Rogue / Paladin 角色头像（头肩特写图标）
// 使用：菜单 SuperSmashLike → 45. Generate Portrait Icons (M6-D3)
//      或 -batchmode -executeMethod PortraitIconWizard.Run
//
// 步骤（幂等，可重复执行）：
//   ① 渲染：临时实例化 Fighter_Rogue / Fighter_Paladin（挪到远处脱离场景杂物），
//      以 Head 骨骼为锚做头肩特写，256×256(8xAA) 渲染 → 64×64 降采样 → PNG 写盘
//   ② 导入：按 Knight.png 同规格配置 Sprite 导入（Single / PPU100 / Clamp / Bilinear / 无 Mipmap）
//   ③ 回填：FD_Rogue / FD_Paladin.portraitIcon ← 新 Sprite（SetDirty + SaveAssets）
//   ④ 校验：[Icon] OK/FAIL —— 文件存在 / 导入为 Sprite / 尺寸 64×64 / FD 引用非空
//
// 【风格基线】现有图标（Knight/Archer/Barbarian/Mage）：头肩特写、正面视角、
//   平底色 RGB(49,49,49)、24bpp。尺寸统一按 64×64（Knight 79×76 为个例）。
// 【取景】锚点 = Head 骨骼世界坐标；FOV 30°、距离 1.2m、光轴略俯 3°；头顶留少量边距。
// 【注意】渲染前隐藏 ShieldVisual（举盾表现层，不隐藏会在画面里出红盘）；
//   临时对象（实例/相机/灯/RT）全部 finally 清理，场景无残留。
// ============================================================
public static class PortraitIconWizard
{
    private const string IconDir = "Assets/_Game/Art/Icon";
    private const string FdDir = "Assets/_Game/ScriptableObjects/Characters/";

    // 渲染参数（对齐现有图标规格）
    private const int RenderSize = 256;          // 渲染分辨率（8x AA 超采样）
    private const int FinalSize = 64;            // 最终图标尺寸
    private const float CamFov = 30f;            // 垂直视场角
    private const float CamDistance = 1.2f;      // 相机到 Head 锚点距离
    private const float CamUpOffset = 0.02f;     // 相机高于锚点
    private const float LookDownOffset = -0.05f; // 注视点低于锚点（略俯视）
    private static readonly Color BgColor = new Color(49f / 255f, 49f / 255f, 49f / 255f, 1f);

    private struct Target
    {
        public string tag;         // 日志标签
        public string prefabPath;  // 斗士预制体
        public string iconName;    // 输出 PNG 名（不含扩展名）
        public string fdName;      // FighterData 资产名
    }

    private static readonly Target[] Targets =
    {
        new Target { tag = "Rogue",   prefabPath = "Assets/_Game/Prefabs/Fighters/Fighter_Rogue.prefab",   iconName = "Rogue",   fdName = "FD_Rogue" },
        new Target { tag = "Paladin", prefabPath = "Assets/_Game/Prefabs/Fighters/Fighter_Paladin.prefab", iconName = "Paladin", fdName = "FD_Paladin" },
    };

    [MenuItem("SuperSmashLike/45. Generate Portrait Icons (M6-D3)", false, 45)]
    public static void Run()
    {
        Debug.Log("[Icon] ===== 开始生成角色头像（Rogue / Paladin）=====");

        // ① 渲染 + 写盘
        foreach (Target t in Targets) Step1_Render(t);

        // ② 导入设置 + ③ 回填
        AssetDatabase.Refresh();
        foreach (Target t in Targets) Step2_ConfigureImporter(t);
        foreach (Target t in Targets) Step3_Assign(t);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ④ 校验
        Step4_Verify();

        Debug.Log("[Icon] ===== 生成结束 =====");
    }

    // ============================================================
    // ① 渲染：头肩特写 → PNG
    // ============================================================
    private static void Step1_Render(Target t)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(t.prefabPath);
        if (prefab == null) { Debug.LogError($"[Icon] ① {t.tag} 预制体缺失：{t.prefabPath}"); return; }

        string outPath = $"{IconDir}/{t.iconName}.png";
        GameObject instance = null, camGO = null, lightGO = null;
        Camera cam = null;
        RenderTexture rt = null, rtSmall = null;
        Texture2D tex = null;

        try
        {
            // 摆在远离场景内容处：仅本物体落在相机远裁剪面内
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(new Vector3(5000f, -5000f, 0f), Quaternion.identity);

            // 隐藏举盾表现层（渲染时会被画面捕获）
            Transform shield = instance.transform.Find("ShieldVisual");
            if (shield != null) shield.gameObject.SetActive(false);

            // 取景锚点：Head 骨骼（优先激活蒙皮骨骼表，防共享骨架重复骨骼）
            Transform head = FindBone(instance, "Head");
            if (head == null) { Debug.LogError($"[Icon] ① {t.tag} 找不到 Head 骨骼，跳过"); return; }
            Vector3 headPos = head.position;
            Debug.Log($"[Icon] ① {t.tag} 锚点 Head @ {headPos}");

            // 相机（朝向探针验证：模型正面朝 +X → 相机置于 +X 侧看向面部）
            camGO = new GameObject("__IconCam");
            cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BgColor;
            cam.fieldOfView = CamFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            camGO.transform.position = headPos + new Vector3(CamDistance, CamUpOffset, 0f);
            camGO.transform.LookAt(headPos + new Vector3(0f, LookDownOffset, 0f));

            // 临时平行光（与截图工具默认一致：白 1.0 @ (50,-30,0)）
            lightGO = new GameObject("__IconLight");
            Light li = lightGO.AddComponent<Light>();
            li.type = LightType.Directional;
            li.color = Color.white;
            li.intensity = 1.0f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 渲染 256(8xAA) → 64 双线性降采样
            rt = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            rt.Create();
            rtSmall = new RenderTexture(FinalSize, FinalSize, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            rtSmall.Create();
            cam.targetTexture = rt;
            cam.Render();
            Graphics.Blit(rt, rtSmall);

            RenderTexture.active = rtSmall;
            tex = new Texture2D(FinalSize, FinalSize, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, FinalSize, FinalSize), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Debug.Log($"[Icon] ① {t.tag} 渲染完成 {FinalSize}×{FinalSize} → {outPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Icon] ① {t.tag} 渲染异常：{e.Message}");
        }
        finally
        {
            RenderTexture.active = null;
            if (cam != null) cam.targetTexture = null;
            if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
            if (rtSmall != null) UnityEngine.Object.DestroyImmediate(rtSmall);
            if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            if (camGO != null) UnityEngine.Object.DestroyImmediate(camGO);
            if (lightGO != null) UnityEngine.Object.DestroyImmediate(lightGO);
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    // ============================================================
    // ② Sprite 导入设置（对齐 Knight.png）
    // ============================================================
    private static void Step2_ConfigureImporter(Target t)
    {
        string path = $"{IconDir}/{t.iconName}.png";
        if (!File.Exists(path)) { Debug.LogError($"[Icon] ② 找不到 {path}"); return; }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError($"[Icon] ② {path} 导入器获取失败"); return; }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 100f;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.alphaIsTransparency = true;

        // spriteMeshType 在 TextureImporterSettings 上（TextureImporter 无此直接属性）
        TextureImporterSettings settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.Tight;
        ti.SetTextureSettings(settings);

        ti.SaveAndReimport();
        Debug.Log($"[Icon] ② {t.iconName}.png 导入设置完成（Sprite / Single / PPU100 / Clamp）");
    }

    // ============================================================
    // ③ 回填 FD.portraitIcon
    // ============================================================
    private static void Step3_Assign(Target t)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconDir}/{t.iconName}.png");
        FighterData fd = LoadFd(t.fdName);
        if (sprite == null || fd == null)
        {
            Debug.LogError($"[Icon] ③ {t.tag} 回填失败（sprite={(sprite != null)} fd={(fd != null)}）");
            return;
        }
        fd.portraitIcon = sprite;
        EditorUtility.SetDirty(fd);
        Debug.Log($"[Icon] ③ {t.fdName}.portraitIcon ← {t.iconName}.png");
    }

    // ============================================================
    // ④ 校验
    // ============================================================
    private static void Step4_Verify()
    {
        Debug.Log("[Icon] ===== 校验 =====");
        foreach (Target t in Targets)
        {
            string path = $"{IconDir}/{t.iconName}.png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            FighterData fd = LoadFd(t.fdName);
            Check(File.Exists(path), $"{t.iconName}.png 文件存在");
            Check(sprite != null, $"{t.iconName}.png 导入为 Sprite");
            if (sprite != null)
                Check(sprite.rect.width == FinalSize && sprite.rect.height == FinalSize,
                      $"{t.iconName} 尺寸 {sprite.rect.width}×{sprite.rect.height}（期望 {FinalSize}×{FinalSize}）");
            Check(fd != null && fd.portraitIcon != null, $"{t.fdName}.portraitIcon 已回填");
        }
    }

    // ---- 工具 ----
    private static FighterData LoadFd(string fdName)
        => AssetDatabase.LoadAssetAtPath<FighterData>(FdDir + fdName + ".asset");

    private static void Check(bool ok, string label)
    {
        if (ok) Debug.Log($"[Icon] OK - {label}");
        else Debug.LogError($"[Icon] FAIL - {label}");
    }

    // 头部骨骼：优先在激活的蒙皮渲染器骨骼表里找（共享骨架防重复），退回全树深度搜索
    private static Transform FindBone(GameObject root, string boneName)
    {
        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || !smr.gameObject.activeInHierarchy || smr.bones == null) continue;
            foreach (Transform b in smr.bones)
                if (b != null && b.name == boneName) return b;
        }
        return FindDeep(root.transform, boneName);
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform r = FindDeep(parent.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
