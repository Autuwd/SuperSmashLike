using UnityEditor;
using UnityEngine;

// ============================================================
// HitboxSceneSyncEditor — 检测 AttackHitbox 被拖动 → 自动写回资产
// 职责：让"在 Scene 里拖判定框"变成"直接改 FighterData 资产"
// 架构位置：Editor 层（不进包体），HitboxSceneSync 的自定义 Inspector
// 依赖：HitboxSceneSync（被编辑的目标）
//
// 原理：EditorApplication.update 每帧比较对象位置/大小，
//       变了就调用 SaveToAsset() —— 这就是"可视化编辑"的实现方式。
//
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// 【注意】必须在 OnDisable 里退订 EditorApplication.update，否则会泄漏回调
// ============================================================
[CustomEditor(typeof(HitboxSceneSync))]
public class HitboxSceneSyncEditor : Editor
{
    #region 1. 运行时状态

    private Vector3 lastPos;     // 上次记录的对象位置
    private Vector2 lastSize;    // 上次记录的 collider 大小

    #endregion

    #region 2. Unity 生命周期（Editor）

    private void OnEnable()
    {
        var sync = (HitboxSceneSync)target;
        sync.LoadFromAsset();    // 选中时自动从资产加载数据到对象

        lastPos = sync.transform.localPosition;
        var box = sync.GetComponent<BoxCollider2D>();
        lastSize = box != null ? box.size : Vector2.zero;

        EditorApplication.update += Tick;   // 注册每帧回调
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;   // 取消注册（必须，否则回调泄漏）
    }

    #endregion

    #region 3. 每帧检测

    // 【做什么】比较位置/大小是否变化，变了就写回资产
    private void Tick()
    {
        var sync = (HitboxSceneSync)target;
        if (sync == null) return;

        var box = sync.GetComponent<BoxCollider2D>();
        if (box == null) return;

        Vector3 pos = sync.transform.localPosition;
        Vector2 size = box.size;

        // 位置或大小变了 → 用户拖了 → 写回资产
        if (pos == lastPos && size == lastSize) return;

        sync.SaveToAsset();
        lastPos = pos;
        lastSize = size;
    }

    #endregion
}
