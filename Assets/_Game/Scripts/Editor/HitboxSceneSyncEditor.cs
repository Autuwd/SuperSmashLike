using UnityEditor;
using UnityEngine;

// ============================================================
// HitboxSceneSyncEditor — 检测 AttackHitbox 被拖动 → 自动写回资产
// 原理：EditorApplication.update 每帧比较对象位置/大小，
//       变了就调用 SaveToAsset()（这就是"可视化编辑"的魔法）
// ============================================================
[CustomEditor(typeof(HitboxSceneSync))]
public class HitboxSceneSyncEditor : Editor
{
    private Vector3 lastPos;     // 上次记录的对象位置
    private Vector2 lastSize;    // 上次记录的 collider 大小

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
        EditorApplication.update -= Tick;   // 取消注册
    }

    private void Tick()
    {
        var sync = (HitboxSceneSync)target;
        if (sync == null) return;
        var box = sync.GetComponent<BoxCollider2D>();
        if (box == null) return;

        Vector3 pos = sync.transform.localPosition;
        Vector2 size = box.size;

        // 位置或大小变了 → 用户拖了 → 写回资产
        if (pos != lastPos || size != lastSize)
        {
            sync.SaveToAsset();
            lastPos = pos;
            lastSize = size;
        }
    }
}