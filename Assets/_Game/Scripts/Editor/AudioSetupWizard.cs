using System;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

// ============================================================
// AudioSetupWizard — M8-D1 音频系统一键配置（编辑器工具）
// 职责（按步骤）：
//   1. 生成 16 个占位 SFX WAV（复用已有 PlaceholderSfxGenerator，不重写）
//   2. 创建/更新 AudioCatalog.asset 并按 SfxId 顺序填 16 个 SFX
//   3. 按 MusicId 顺序填 4 首 BGM（Battle Music Pack DEMO 1..4）
//   4. BattleTest 场景根节点创建/复用 AudioManager 物体并挂组件 + 赋 catalog
//   5. 可选：反射创建 MainMixer.mixer（失败只 Log 跳过，绝不中断）
// 使用：菜单 SuperSmashLike → 40；或 -executeMethod AudioSetupWizard.Run
// 【幂等】重跑不会重复创建 AudioManager 物体，也不会重复堆叠 catalog 条目
//   （数组按枚举下标覆写，永远恰好 16 + 4 项）。
// ============================================================
public static class AudioSetupWizard
{
    private const string SfxDir = "Assets/_Game/Art/Audio/SFX";
    private const string CatalogDir = "Assets/_Game/ScriptableObjects/Audio";
    private const string CatalogPath = CatalogDir + "/AudioCatalog.asset";
    private const string ScenePath = "Assets/_Game/Scenes/BattleTest.unity";
    private const string MixerDir = "Assets/_Game/Audio";
    private const string MixerPath = MixerDir + "/MainMixer.mixer";

    // SFX 文件名与 SfxId 枚举值一一对应（下标即枚举值，顺序不可改）
    private static readonly string[] SfxNames =
    {
        "hit_light", "hit_medium", "hit_heavy", "swing",
        "shield_hit", "shield_break", "parry", "ko",
        "jump", "land", "dash",
        "ui_move", "ui_confirm", "ui_cancel", "ui_start", "countdown",
    };

    // BGM 与 MusicId 对应：Title=1 / Select=2 / Battle=3 / Result=4
    private static readonly string[] MusicPaths =
    {
        "Assets/Battle Music Pack DEMO/Battle Theme 1_demo.wav",
        "Assets/Battle Music Pack DEMO/Battle Theme 2_demo.wav",
        "Assets/Battle Music Pack DEMO/Battle Theme 3_demo.wav",
        "Assets/Battle Music Pack DEMO/Battle Theme 4_demo.wav",
    };

    [MenuItem("SuperSmashLike/40. Setup Audio (M8-D1)", false, 40)]
    public static void Run()
    {
        // ---- Step 1：占位 SFX（幂等：已存在的 WAV 默认跳过）----
        PlaceholderSfxGenerator.Generate(false);

        // ---- Step 2 + 3：创建/更新 AudioCatalog 资产 ----
        var catalog = FillCatalog(out int sfxOk, out int musicOk);

        // ---- Step 4：BattleTest 场景挂 AudioManager ----
        SetupScene(catalog);

        // ---- Step 5：可选 mixer（失败不影响整体结果）----
        TryCreateMixer();

        // ---- 汇总日志 ----
        int sfxMissing = SfxNames.Length - sfxOk;
        int musicMissing = MusicPaths.Length - musicOk;
        if (sfxMissing > 0)
            Debug.LogWarning($"[AudioWizard] SFX 缺失 {sfxMissing} 个（检查 {SfxDir} 下 WAV 是否生成/导入成功）");
        if (musicMissing > 0)
            Debug.LogWarning($"[AudioWizard] BGM 缺失 {musicMissing} 首（检查 Assets/Battle Music Pack DEMO）");

        Debug.Log($"[AudioWizard] 完成：SFX {sfxOk}/{SfxNames.Length}，BGM {musicOk}/{MusicPaths.Length}，" +
                  $"catalog={CatalogPath}，场景={ScenePath}");
    }

    // ============================================================
    // Step 2 + 3：AudioCatalog 资产 —— 不存在则创建，存在则覆写数组
    // ============================================================
    private static AudioCatalog FillCatalog(out int sfxOk, out int musicOk)
    {
        EnsureFolder(CatalogDir);

        var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<AudioCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            Debug.Log("[AudioWizard] 新建 catalog：" + CatalogPath);
        }

        // 数组长度强制对齐枚举（幂等：不追加、不重复，只按下标覆写）
        if (catalog.sfx == null || catalog.sfx.Length != (int)SfxId.Count)
            catalog.sfx = new AudioClip[(int)SfxId.Count];
        if (catalog.music == null || catalog.music.Length != (int)MusicId.Count)
            catalog.music = new AudioClip[(int)MusicId.Count];

        sfxOk = 0;
        for (int i = 0; i < SfxNames.Length; i++)
        {
            var path = $"{SfxDir}/{SfxNames[i]}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning($"[AudioWizard] 找不到 SFX：{path}");
            else
                sfxOk++;
            catalog.sfx[i] = clip;   // 找不到写 null，GetSfx 下发时静默 no-op
        }

        musicOk = 0;
        for (int i = 0; i < MusicPaths.Length; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPaths[i]);
            if (clip == null)
                Debug.LogWarning($"[AudioWizard] 找不到 BGM：{MusicPaths[i]}");
            else
                musicOk++;
            catalog.music[i] = clip;
        }

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    // ============================================================
    // Step 4：BattleTest 场景 —— 找/建根物体 "AudioManager"，挂组件赋 catalog
    // ============================================================
    private static void SetupScene(AudioCatalog catalog)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            Debug.LogError("[AudioWizard] 找不到场景：" + ScenePath);
            return;
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 幂等：根节点已有 "AudioManager" 则复用，绝不重复创建
        GameObject root = null;
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name == "AudioManager") { root = go; break; }
        }
        bool created = false;
        if (root == null)
        {
            root = new GameObject("AudioManager");
            created = true;
        }

        var am = root.GetComponent<AudioManager>();
        if (am == null) am = root.AddComponent<AudioManager>();

        // 幂等：catalog 已指同一资产时赋值无副作用
        am.catalog = catalog;
        am.autoBindGameStateMusic = true;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[AudioWizard] 场景就绪：AudioManager {(created ? "已创建" : "已复用")}（{ScenePath}）");
    }

    // ============================================================
    // Step 5：可选 —— 反射创建 AudioMixerController 资产
    // 【为什么用反射】：UnityEditor.Audio.AudioMixerController 是 internal 类型，
    //   无公开创建 API；任何失败（类型找不到/资产已存在/路径非法）都只 Log 跳过，
    //   绝不影响前面已完成的 catalog 与场景配置。
    // ============================================================
    private static void TryCreateMixer()
    {
        try
        {
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null)
            {
                Debug.Log("[AudioWizard] MainMixer 已存在，跳过创建");
                return;
            }

            EnsureFolder(MixerDir);

            var controllerType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
            if (controllerType == null)
            {
                Debug.Log("[AudioWizard] AudioMixer 创建跳过（可选）");
                return;
            }

            var mixer = ScriptableObject.CreateInstance(controllerType);
            if (mixer == null)
            {
                Debug.Log("[AudioWizard] AudioMixer 创建跳过（可选）");
                return;
            }

            AssetDatabase.CreateAsset(mixer, MixerPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioWizard] 已创建 mixer（可选）：" + MixerPath +
                      "；如需启用请手动把输出组接到 AudioManager.mixer 并给各 AudioSource 赋 outputAudioMixerGroup");
        }
        catch (Exception e)
        {
            Debug.Log("[AudioWizard] AudioMixer 创建跳过（可选）: " + e.Message);
        }
    }

    // 递归确保目录存在（AssetDatabase 只认逐级创建）
    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        var parts = folderPath.Split('/');
        string current = parts[0];   // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
