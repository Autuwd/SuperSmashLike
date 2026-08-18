using SuperSmashLike.Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// HitboxSceneSync — 判定框场景可视化编辑（挂在 AttackHitbox 上）
// 用法：
//   1. AttackHitbox 挂 BoxCollider2D + Hitbox + 本脚本
//   2. Inspector 配 fighter（所属角色）+ attackField（编辑哪个攻击）
//   3. Scene 视图选中 AttackHitbox：
//      - 移动工具(W) 拖对象 = 改 hitboxOffset（位置）
//      - 直接拖 collider 绿框边缘 = 改 hitboxSize（大小）
//   4. 修改自动写回 FighterData 资产，运行时定位读同一份数据
// ============================================================

[ExecuteAlways]
public class HitboxSceneSync : MonoBehaviour
{
    [Header("绑定")]
    public FighterController fighter;    // 所属角色（Inspector 拖）
    public string attackField = "jab1";  // 编辑的攻击字段名（jab1/jab2/tiltSide...）

    // 反射获取对应的 AttackData
    public AttackData GetAttackData()
    {
        if (fighter == null || fighter.fighterData == null) return null;
        var field = fighter.fighterData.GetType().GetField(attackField);
        return field?.GetValue(fighter.fighterData) as AttackData;
    }

    // 资产 → 对象（选中时/切攻击时调用）
    public void LoadFromAsset()
    {
        var ad = GetAttackData();
        if (ad == null) return;
        transform.localPosition = new Vector3(ad.hitboxOffset.x, ad.hitboxOffset.y, 0);
        var box = GetComponent<BoxCollider2D>();
        if (box != null) box.size = ad.hitboxSize;
    }

    // 对象 → 资产（检测到拖动时调用）
    public void SaveToAsset()
    {
        var ad = GetAttackData();
        if (ad == null) return;
        var box = GetComponent<BoxCollider2D>();
        if (box != null) ad.hitboxSize = box.size;
        ad.hitboxOffset = new Vector2(transform.localPosition.x, transform.localPosition.y);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(fighter.fighterData);   // 标记资产已改，Ctrl+S 保存
#endif
    }
}
