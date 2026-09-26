// ============================================================
// MSYHDynamicFontFix.cs
// 用途：在「保持动态字体模式」的前提下，把项目 UI 缺少的中文字
//       预生成进 MSYH_Regular SDF 的字符表/图集，并保存资产。
//       解决 "抓投" 等字在动态字体下仍显示方块的问题。
// 用法：Unity 菜单栏 -> Tools -> Fix MSYH Font Missing Chars（执行一次即可）
// 原理：TMP_FontAsset.TryAddCharacters() 是动态字体的官方补字 API，
//       字形会真实渲染进图集纹理，之后构建/运行都不再依赖运行时补字。
// ============================================================

using UnityEditor;
using UnityEngine;
using TMPro;

public static class MSYHDynamicFontFix
{
    private const string FontAssetPath = "Assets/_Game/Fonts/MSYH_Regular SDF.asset";

    [MenuItem("Tools/Fix MSYH Font Missing Chars")]
    public static void FixMissingChars()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null)
        {
            Debug.LogError("[FontFix] 找不到字体资产: " + FontAssetPath);
            return;
        }

        // 项目 UI 用到的全部中文字：6 个 Tab + 出招表默认文案 + 常用招式词
        var needed =
            "抓投轻击强击蓄力空中必杀伤害起手持续收招击中攻击连段上下左右一二三四五六七八九十前后方";

        bool allOk = font.TryAddCharacters(needed, out string missing);

        if (allOk)
        {
            Debug.Log("[FontFix] 全部字符已成功添加，正在保存资产...");
        }
        else
        {
            Debug.LogWarning("[FontFix] 以下字符添加失败（源字体无此字形?）: " + missing);
        }

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[FontFix] 完成。当前图集字符数: " + font.characterTable.Count +
                  " | 图集尺寸: " + font.atlasWidth + "x" + font.atlasHeight);
    }
}