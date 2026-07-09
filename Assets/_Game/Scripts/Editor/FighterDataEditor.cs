using UnityEngine;
using UnityEditor;
using SuperSmashLike.Core;

// ============================================================
// FighterDataEditor — 角色数据自定义 Inspector 编辑器
// 职责：
//   在 Unity Inspector 中为 FighterData 添加可视化辅助功能：
//   - 统计总攻击数量
//   - 一键重置为默认值
// 架构位置：Editor 层（仅在 Editor 中编译，不会打包进游戏）
// ============================================================
namespace SuperSmashLike.EditorTools
{
    // 告诉 Unity 这个编辑器要自定义哪个类型的 Inspector
    [CustomEditor(typeof(FighterData))]
    public class FighterDataEditor : Editor
    {
        // 重写 Inspector GUI 绘制
        public override void OnInspectorGUI()
        {
            FighterData data = (FighterData)target;

            // 先绘制默认的 Inspector 布局（所有公开字段）
            DrawDefaultInspector();

            // 在默认 Inspector 下方添加自定义统计面板
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Statistics Overview", EditorStyles.boldLabel);

            // 显示统计信息（只读）
            EditorGUILayout.LabelField("Total Attack Count", CountAttacks(data).ToString());
            EditorGUILayout.LabelField("Weight Class", data.weightClass.ToString());
            EditorGUILayout.LabelField("Jump Count", data.jumpCount.ToString());

            // 重置按钮
            if (GUILayout.Button("Reset to Default Values"))
            {
                // Undo.RecordObject 让重置操作可以被撤销（Ctrl+Z）
                Undo.RecordObject(data, "Reset Fighter Data");
                ResetToDefaults(data);
            }
        }

        // 统计该角色已配置了多少种攻击（用于快速检查是否配置完整）
        private int CountAttacks(FighterData data)
        {
            int count = 0;
            if (data.jab1 != null) count++;
            if (data.jab2 != null) count++;
            if (data.jab3 != null) count++;
            if (data.tiltSide != null) count++;
            if (data.tiltUp != null) count++;
            if (data.tiltDown != null) count++;
            if (data.smashSide != null) count++;
            if (data.smashUp != null) count++;
            if (data.smashDown != null) count++;
            if (data.aerialNeutral != null) count++;
            if (data.aerialForward != null) count++;
            if (data.aerialBack != null) count++;
            if (data.aerialUp != null) count++;
            if (data.aerialDown != null) count++;
            return count;
        }

        // 重置为默认值（避免手动改错后到处找原始值）
        private void ResetToDefaults(FighterData data)
        {
            data.weight = 100f;
            data.walkSpeed = 5f;
            data.runSpeed = 8f;
            data.airSpeed = 6f;
            data.jumpForce = 12f;
            data.jumpCount = 2;
        }
    }
}
