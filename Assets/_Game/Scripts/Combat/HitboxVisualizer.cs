using SuperSmashLike.Core;
using UnityEngine;

// ============================================================
// HitboxVisualizer — 判定框可视化预览（挂在 Fighter 根物体）
// 用途：编辑模式下在 Scene 视图预览所有攻击判定框的位置/大小，
//       每个框显示攻击名称，方便调参。
// 配合 Editor/HitboxVisualizerEditor.cs 可在 Scene 中拖拽修改。
// 使用：把本脚本挂到 Fighter 根 → 选中物体即可看到彩色判定框
// ============================================================
[ExecuteAlways]
public class HitboxVisualizer : MonoBehaviour
{
    [Header("显示设置")]
    public bool showAll = true;   // 显示所有攻击的判定框
    public Color boxColor = new Color(1f, 0.2f, 0.2f, 0.35f);  // 填充色（半透明红）
    public Color outlineColor = Color.red;                      // 边框色

    // 攻击名称映射（字段名 → 中文显示名）
    public static readonly System.Collections.Generic.Dictionary<string, string> DisplayNames = new()
    {
        { "jab1", "Jab1 轻击①" }, { "jab2", "Jab2 轻击②" }, { "jab3", "Jab3 轻击③" },
        { "tiltSide", "横强攻击" }, { "tiltUp", "上强攻击" }, { "tiltDown", "下强攻击" },
        { "smashSide", "横蓄力" }, { "smashUp", "上蓄力" }, { "smashDown", "下蓄力" },
        { "aerialNeutral", "空中N" }, { "aerialForward", "空前" }, { "aerialBack", "空后" },
        { "aerialUp", "空上" }, { "aerialDown", "空下" },
        { "specialNeutral", "必杀N" }, { "specialSide", "横必杀" },
        { "specialUp", "上必杀" }, { "specialDown", "下必杀" },
        { "grab", "抓取" }, { "throwForward", "前投" }, { "throwBack", "后投" },
        { "throwUp", "上投" }, { "throwDown", "下投" },
    };

    // 反射收集 FighterData 里所有已配置的 AttackData（字段名 → 数据）
    public System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, AttackData>> GetAllAttacks()
    {
        var result = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, AttackData>>();
        var fc = GetComponent<FighterController>();
        if (fc == null || fc.fighterData == null) return result;

        foreach (var field in fc.fighterData.GetType().GetFields())
        {
            if (field.FieldType != typeof(AttackData)) continue;
            var ad = field.GetValue(fc.fighterData) as AttackData;
            if (ad == null) continue;   // 未配置的攻击跳过
            result.Add(new System.Collections.Generic.KeyValuePair<string, AttackData>(field.Name, ad));
        }
        return result;
    }

    private void OnDrawGizmos()
    {
        if (!showAll) return;
        var fc = GetComponent<FighterController>();
        if (fc == null || fc.fighterData == null) return;

        foreach (var pair in GetAllAttacks())
        {
            var ad = pair.Value;
            // 判定框中心（相对角色脚底，x 朝右；z 抬 0.5 避免被模型遮挡）
            Vector3 center = transform.position + new Vector3(ad.hitboxOffset.x, ad.hitboxOffset.y, 0.5f);
            Vector3 size = new Vector3(ad.hitboxSize.x, ad.hitboxSize.y, 0.05f);

            bool isCurrent = fc.attackData == ad;   // 当前使用的攻击高亮
            Gizmos.color = isCurrent ? Color.yellow : boxColor;
            Gizmos.DrawCube(center, size);
            Gizmos.color = isCurrent ? Color.yellow : outlineColor;
            Gizmos.DrawWireCube(center, size);
        }
    }
}