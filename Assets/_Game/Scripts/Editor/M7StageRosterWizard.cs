using System.Collections.Generic;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// M7StageRosterWizard — M7-D3 选图接入（编辑器工具）
// 职责：把 GameSettings.selectableStages 接线为 M7 三舞台：
//   天空竞技场(SD_Arena) / 迷雾森林(SD_Forest) / 幽暗地牢(SD_Dungeon)
// 使用：菜单 SuperSmashLike → 33；或 -executeMethod M7StageRosterWizard.Run
// 【注意】幂等；若重跑 30 号向导（会重置 selectableStages），重跑本向导即可恢复。
// ============================================================
public static class M7StageRosterWizard
{
    private const string GsPath = "Assets/_Game/ScriptableObjects/GameModes/GS_Default.asset";
    private const string SoDir = "Assets/_Game/ScriptableObjects/Stages";

    [MenuItem("SuperSmashLike/33. Wire M7 Stage Roster (M7-D3)", false, 33)]
    public static void Setup()
    {
        Run();
    }

    public static void Run()
    {
        var gs = AssetDatabase.LoadAssetAtPath<GameSettings>(GsPath);
        if (gs == null) { Debug.LogError("[M7Roster] 找不到 " + GsPath); return; }

        var names = new[] { "SD_Arena", "SD_Forest", "SD_Dungeon" };
        var roster = new List<StageData>();
        var ok = true;
        foreach (var n in names)
        {
            var sd = AssetDatabase.LoadAssetAtPath<StageData>(SoDir + "/" + n + ".asset");
            if (sd == null) { Debug.LogError("[M7Roster] 缺 StageData：" + n); ok = false; continue; }
            if (sd.stagePrefab == null) { Debug.LogError("[M7Roster] " + n + " 缺 stagePrefab"); ok = false; }
            if (sd.stageThumbnail == null) { Debug.LogError("[M7Roster] " + n + " 缺 stageThumbnail"); ok = false; }
            var sp = 0;
            if (sd.stagePrefab != null)
                foreach (var t in sd.stagePrefab.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("SpawnPoint")) sp++;
            if (sp == 0) { Debug.LogError("[M7Roster] " + n + " 无 SpawnPoint"); ok = false; }
            Debug.Log("[M7Roster] " + n + " [" + sd.stageName + "] prefab=" +
                (sd.stagePrefab != null ? sd.stagePrefab.name : "null") + " sp=" + sp +
                " cam=" + sd.cameraBoundsMin + "~" + sd.cameraBoundsMax);
            roster.Add(sd);
        }
        if (!ok) { Debug.LogError("[M7Roster] 校验未过，未写入 selectableStages。"); return; }

        gs.selectableStages = roster;
        EditorUtility.SetDirty(gs);
        AssetDatabase.SaveAssets();
        Debug.Log("[M7Roster] 完成：selectableStages = 天空竞技场 / 迷雾森林 / 幽暗地牢");
    }
}
