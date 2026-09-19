using System.IO;
using UnityEditor;
using UnityEngine;

// ============================================================
// ExtractClips — 把 FBX 里的只读动画 clip 导出为独立 .anim
// 职责：解决"FBX 子资产 clip 只读、无法挂 Animation Event"的问题
// 架构位置：Editor 层（不进包体）
// 使用方式：菜单 Tools → Extract AttackForThree Clips
//
// 【为什么需要】Mixamo 导出的 FBX 里，动画 clip 是只读子资产，
//   不能直接加动画事件（Activate/Deactivate/AttackFinished）。
//   导出成独立 .anim 后就能自由编辑。
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// 【注意】源路径是硬编码的，换 FBX 需要改 srcPath
// ============================================================
public static class ExtractClips
{
    [MenuItem("Tools/Extract AttackForThree Clips")]
    public static void Extract()
    {
        const string srcPath = "Assets/_Game/Animations/FBX/AttackForThree.fbx";
        const string dstDir = "Assets/_Game/Animations";
        Directory.CreateDirectory(dstDir);

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(srcPath))
        {
            // 只取动画 clip，跳过 __preview 这类隐藏预览资产
            if (asset is not AnimationClip ac || ac.name.StartsWith("__preview")) continue;

            string dst = $"{dstDir}/{ac.name}.anim";

            // 已存在同名则先删，避免冲突
            if (File.Exists(dst))
                AssetDatabase.DeleteAsset(dst);

            // CopySerialized 深拷贝曲线数据（直接赋值只会拿到只读引用）
            var copy = new AnimationClip();
            EditorUtility.CopySerialized(ac, copy);
            AssetDatabase.CreateAsset(copy, dst);

            Debug.Log($"[ExtractClips] 已提取: {dst}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ExtractClips] 提取完成");
    }
}
