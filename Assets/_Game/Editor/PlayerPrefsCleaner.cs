using UnityEditor;
using UnityEngine;

// ============================================================
// PlayerPrefsCleaner — 一键清除 PlayerPrefs 缓存
// 职责：修复"输入系统旧绑定缓存导致输入失效"的问题
// 架构位置：Editor 层（不进包体）
// 使用方式：菜单 Tools → 清除 PlayerPrefs 缓存
//
// 【注意】会清掉所有 PlayerPrefs（含画质/音量等设置），不只是输入绑定
// ============================================================
namespace SuperSmashLike.EditorTools
{
    public static class PlayerPrefsCleaner
    {
        #region 菜单入口

        [MenuItem("Tools/清除 PlayerPrefs 缓存")]
        public static void DeleteAllPlayerPrefs()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("[PlayerPrefsCleaner] 已清除全部 PlayerPrefs 缓存，重启 Play 后输入绑定将重新初始化。");
        }

        #endregion
    }
}
