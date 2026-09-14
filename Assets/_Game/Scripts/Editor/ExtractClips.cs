using System.IO;
using UnityEditor;
using UnityEngine;

public static class ExtractClips
{
    [MenuItem("Tools/Extract AttackForThree Clips")]
    public static void Extract()
    {
        string srcPath = "Assets/_Game/Animations/FBX/AttackForThree.fbx";
        string dstDir = "Assets/_Game/Animations";
        Directory.CreateDirectory(dstDir);

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(srcPath))
        {
            // 只取动画 clip，跳过 __preview 这类隐藏预览资产
            if (asset is AnimationClip ac && !ac.name.StartsWith("__preview"))
            {
                string dst = $"{dstDir}/{ac.name}.anim";

                // 已存在同名则先删，避免冲突
                if (File.Exists(dst))
                    AssetDatabase.DeleteAsset(dst);

                var copy = new AnimationClip();
                EditorUtility.CopySerialized(ac, copy);
                AssetDatabase.CreateAsset(copy, dst);
                Debug.Log($"提取: {dst}");
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("? 提取完成");
    }
}