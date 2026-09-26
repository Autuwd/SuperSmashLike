// ============================================================
// MSYHDynamicFontRebuild.cs  (一次性修复工具 v2 - TMP 3.0.9 API)
// 问题：旧 MSYH_Regular SDF.asset 的 TryAddCharacters 对表外新字全部失败，
//       "抓投强蓄力空中必杀..." 等 UI 字无法动态补入。
// 诊断（2026-09-26 查证）：
//   * 源字体 MSYH_Regular.ttf 为真 TTF（magic 0x00010000），18.67MB 完整雅黑，
//     includeFontData=1，资产 m_SourceFontFile 引用完整 -> 字体本身无问题。
//   * TMP 3.0.9 无 CreateDynamicFontAsset / includeFontData 参数（3.1+ 才有），
//     正确 API 为 TMP_FontAsset.CreateFontAsset(Font, ...)。
//   * GlyphRenderMode 位于 UnityEngine.TextCore.LowLevel 命名空间（需 using）。
// 方案：用 3.0.9 官方 CreateFontAsset 重建 Dynamic 字体资产，
//       先合并旧资产全部字符表 + 全部缺字，TryAddCharacters 预写验证，
//       成功后自动替换全项目旧 guid 引用。
// 用法：Unity 菜单 -> Tools -> Rebuild MSYH Dynamic Font
// 回滚：旧资产已备份到 Temp\opencode\
// ============================================================

using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TMPro;
using UnityEngine.TextCore.LowLevel; // GlyphRenderMode (TMP 3.0.9)

public static class MSYHDynamicFontRebuild
{
    private const string OldGuid = "b44396079db355247870be3addd9e4db";
    private const string OldAssetPath = "Assets/_Game/Fonts/MSYH_Regular SDF.asset";
    private const string NewAssetPath = "Assets/_Game/Fonts/MSYH_Dynamic SDF.asset";

    // 本次新增的缺字（Tab 六分类 + 出招表文案 + 常用招式词）
    private const string NeededChars =
        "抓投轻击强击蓄力空中必杀伤害起手持续收招击中攻击连段上下左右一二三四五六七八九十前后方";

    // 源字体候选：优先 ttf，失败换 TTC（雅黑原生是 TTC 集合）
    private static readonly string[] FontCandidates =
    {
        "Assets/_Game/Fonts/MSYH_Regular.ttf",
        "Assets/_Game/Fonts/MSYH.TTC",
    };

    // 建议图集尺寸：90pt SDF 字形较大，1024 可能不够 -> 同时开启 Multi-Atlas 兜底
    private const int AtlasSize = 1024;
    private const int SamplingPointSize = 90;
    private const int AtlasPadding = 9;

    [MenuItem("Tools/Rebuild MSYH Dynamic Font")]
    public static void Rebuild()
    {
        // ---- 0. 清理上次崩溃可能残留的新资产文件（避免 GUID 冲突） ----
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(NewAssetPath) != null
            || File.Exists(NewAssetPath))
        {
            AssetDatabase.DeleteAsset(NewAssetPath);
            AssetDatabase.Refresh();
            Debug.Log("[FontFix] 已清理残留的新资产: " + NewAssetPath);
        }

        // ---- 0.5 备份旧资产（回滚保险） ----
        string backupDir = Path.Combine(Path.GetTempPath(), "opencode");
        Directory.CreateDirectory(backupDir);
        string backupPath = Path.Combine(backupDir, "MSYH_Regular SDF.backup.asset");
        try
        {
            string srcFull = Path.GetFullPath(OldAssetPath);
            File.Copy(srcFull, backupPath, true);
            Debug.Log("[FontFix] 旧资产已备份: " + backupPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FontFix] 备份失败（继续，不影响主流程): " + e.Message);
        }

        // ---- 1. 收集旧资产的全部字符（避免旧字丢失） ----
        TMP_FontAsset oldFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OldAssetPath);
        StringBuilder sb = new StringBuilder(NeededChars);
        if (oldFont != null && oldFont.characterTable != null)
        {
            foreach (TMP_Character c in oldFont.characterTable)
            {
                if (c == null) continue;
                try { sb.Append(char.ConvertFromUtf32((int)c.unicode)); }
                catch { /* 跳过非法 unicode */ }
            }
        }
        string allChars = sb.ToString();
        Debug.Log("[FontFix] 将写入字符数(去重前): " + allChars.Length
            + " (旧表 " + (oldFont != null ? oldFont.characterTable.Count : 0)
            + " + 新增 " + NeededChars.Length + ")");

        // ---- 2. 用 TMP 3.0.9 官方 API 创建 Dynamic 字体 ----
        TMP_FontAsset newFont = null;
        string usedSource = null;
        foreach (string path in FontCandidates)
        {
            Font src = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (src == null)
            {
                Debug.LogWarning("[FontFix] 无法加载源字体: " + path);
                continue;
            }
            try
            {
                newFont = TMP_FontAsset.CreateFontAsset(
                    src, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                    AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic,
                    enableMultiAtlasSupport: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FontFix] 用 " + path + " 创建失败: " + e.Message);
                continue;
            }
            if (newFont != null)
            {
                usedSource = path;
                break;
            }
        }

        if (newFont == null)
        {
            EditorUtility.DisplayDialog("FontFix 失败",
                "两个源字体都无法创建动态字体资产。\n检查 Font Import 设置中 Include Font Data 是否勾选。", "OK");
            return;
        }
        Debug.Log("[FontFix] 动态字体创建成功，源字体: " + usedSource);

        // ---- 3. 预写入全部字符（旧字 + 缺字）。
        // 【关键时序】此时资产【尚未】持久化 (IsPersistent=false)，
        // 图集满触发 Multi-Atlas 时 SetupNewAtlasTexture 会跳过编辑器分配子资产分支，
        // 避免访问未反序列化的 m_AtlasTexture -> 不 NRE。TMP 官方 Creator 也是先加字后落盘。 ----
        bool ok = newFont.TryAddCharacters(allChars, out string missing);
        if (!ok)
        {
            EditorUtility.DisplayDialog(
                "FontFix 失败",
                "仍有字符无法添加(缺失: " + missing + ")，已中止，未改动任何引用。",
                "OK");
            UnityEngine.Object.DestroyImmediate(newFont);
            return;
        }
        Debug.Log("[FontFix] 字符全部写入成功，字符表大小: " + newFont.characterTable.Count);

        // ---- 4. 落盘（此刻才持久化；把所有图集纹理 + 材质一起挂为子资产） ----
        AssetDatabase.CreateAsset(newFont, NewAssetPath);
        if (newFont.atlasTextures != null)
        {
            foreach (Texture2D tex in newFont.atlasTextures)
            {
                if (tex != null)
                    AssetDatabase.AddObjectToAsset(tex, newFont);
            }
        }
        if (newFont.material != null)
            AssetDatabase.AddObjectToAsset(newFont.material, newFont);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string newGuid = AssetDatabase.AssetPathToGUID(NewAssetPath);
        if (string.IsNullOrEmpty(newGuid))
        {
            EditorUtility.DisplayDialog("FontFix 失败", "新资产 GUID 获取失败。", "OK");
            return;
        }
        Debug.Log("[FontFix] 新资产已保存: " + NewAssetPath + "  guid: " + newGuid);

        // ---- 5. 替换全项目旧 guid 引用（.unity / .prefab） ----
        int replacedFiles = 0, replacedRefs = 0;
        string[] allFiles = Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories);
        foreach (string file in allFiles)
        {
            if (!file.EndsWith(".unity") && !file.EndsWith(".prefab")) continue;

            string text = File.ReadAllText(file);
            if (!text.Contains(OldGuid)) continue;

            string newText = text.Replace(OldGuid, newGuid);
            byte[] raw = File.ReadAllBytes(file);
            bool hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            File.WriteAllText(file, newText, hasBom ? new UTF8Encoding(true) : new UTF8Encoding(false));

            int diff = CountOccurrences(newText, newGuid) - CountOccurrences(text, newGuid);
            replacedRefs += diff;
            replacedFiles++;
            Debug.Log("[FontFix] 已替换: " + file + "  (引用数 +" + diff + ")");
        }

        AssetDatabase.Refresh();

        Debug.Log(string.Format(
            "[FontFix] 全部完成：新动态字体 {0} | 修改文件 {1} 个 | 替换引用 {2} 处 | 字符 {3} 个",
            NewAssetPath, replacedFiles, replacedRefs, newFont.characterTable.Count));

        EditorUtility.DisplayDialog("FontFix 完成",
            "动态字体已重建并替换全项目引用。\n\n"
            + "修改文件: " + replacedFiles + "\n替换引用: " + replacedRefs
            + "\n字符数: " + newFont.characterTable.Count
            + "\n\n现在回到场景验证「抓」字。", "OK");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}