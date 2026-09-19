using SuperSmashLike.Core;
using UnityEngine;

// ============================================================
// HitboxSceneSync — 判定框场景可视化编辑（挂在 AttackHitbox 上）
// 职责：
//   1. 在 Scene 视图直接拖动判定框 → 自动写回 FighterData 资产
//   2. 切换攻击字段时从资产读回位置与大小
// 架构位置：Combat 层（编辑器工具，[ExecuteAlways] 在编辑模式也运行）
// 依赖：FighterController（拿 fighterData）、HitboxSceneSyncEditor（驱动调用）
//
// 用法：
//   1. AttackHitbox 挂 BoxCollider2D + Hitbox + 本脚本
//   2. Inspector 配 fighter（所属角色）+ attackField（编辑哪个攻击）
//   3. Scene 视图选中 AttackHitbox：
//      - 移动工具(W) 拖对象 = 改 hitboxOffset（位置）
//      - 直接拖 collider 绿框边缘 = 改 hitboxSize（大小）
//   4. 修改自动写回 FighterData 资产，运行时定位读同一份数据
//
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致 ——
//   若要统一请连同 HitboxSceneSyncEditor 一起改，本次整理未动。
// ============================================================
[ExecuteAlways]
public class HitboxSceneSync : MonoBehaviour
{
    #region 1. Inspector 配置

    [Header("绑定")]
    public FighterController fighter;    // 所属角色（Inspector 拖）
    public string attackField = "jab1";  // 编辑的攻击字段名（jab1/jab2/tiltSide...）

    #endregion

    #region 2. 公开 API

    // 【做什么】用反射取出当前 attackField 对应的 AttackData
    // 【返回】fighter 或 fighterData 未配置、字段名不存在时返回 null
    public AttackData GetAttackData()
    {
        if (fighter == null || fighter.fighterData == null) return null;
        var field = fighter.fighterData.GetType().GetField(attackField);
        return field?.GetValue(fighter.fighterData) as AttackData;
    }

    // 【做什么】资产 → 场景对象（选中时 / 切换攻击字段时调用）
    // 【副作用】会覆盖 transform.localPosition 与 BoxCollider2D.size
    public void LoadFromAsset()
    {
        var ad = GetAttackData();
        if (ad == null) return;

        transform.localPosition = new Vector3(ad.hitboxOffset.x, ad.hitboxOffset.y, 0);
        var box = GetComponent<BoxCollider2D>();
        if (box != null) box.size = ad.hitboxSize;
    }

    // 【做什么】场景对象 → 资产（检测到拖动时调用）
    // 【副作用】会修改 FighterData 资产并标记为脏
    // 【注意】只在编辑器下标记脏，运行时不要写资产
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

    #endregion
}
