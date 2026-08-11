using UnityEditor;
using UnityEngine;

namespace SuperSmashLike.EditorTools
{
    /// <summary>
    /// 编辑器中一键清除 PlayerPrefs 缓存（用于修复输入系统旧绑定缓存导致的输入失效）。
    /// 用法：菜单 Tools → 清除 PlayerPrefs 缓存
    /// </summary>
    public static class PlayerPrefsCleaner
    {
        [MenuItem("Tools/清除 PlayerPrefs 缓存")]
        public static void DeleteAllPlayerPrefs()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("[PlayerPrefsCleaner] 已清除全部 PlayerPrefs 缓存，重启 Play 后输入绑定将重新初始化。");
        }
    }
}