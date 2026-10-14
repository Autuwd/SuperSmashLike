using System;
using System.Text;
using SuperSmashLike.Core;
using SuperSmashLike.InputSystem;
using SuperSmashLike.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ============================================================
// TrainingFlowProbe — 无头诊断：真实走 GameManager.StartTraining() 入口，
// 检查训练模式是否真的进入可操作状态（IsMatchActive / respawnLock / InputManager）
// 用法：-executeMethod TrainingFlowProbe.Run（不加 -quit，脚本自 Exit）
// ============================================================
public static class TrainingFlowProbe
{
    private const string ScenePath = "Assets/_Game/Scenes/BattleTest.unity";
    private const string RunningKey = "TrainingProbe.Running";
    private const string FrameKey = "TrainingProbe.Frame";

    private static readonly StringBuilder Report = new StringBuilder();

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(FrameKey, 0);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
        Debug.Log("[TF-PROBE] 进入 Play");
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (!EditorApplication.isPlaying) return;

        int f = SessionState.GetInt(FrameKey, 0) + 1;
        SessionState.SetInt(FrameKey, f);

        if (f == 10)
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                try
                {
                    gm.StartTraining();
                    Debug.Log("[TF-PROBE] StartTraining() 已调用（未抛异常）");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TF-PROBE] StartTraining() 抛异常: {e}");
                }
            }
        }

        if (f < 80) return;

        SessionState.SetBool(RunningKey, false);
        EditorApplication.update -= Tick;
        try { Check(); }
        catch (Exception e) { Debug.LogError($"[TF-PROBE] Check 异常: {e}"); }
        Debug.Log($"[TF-PROBE] ===== RESULT =====\n{Report}");
        EditorApplication.Exit(0);
    }

    private static void Check()
    {
        var gm = GameManager.Instance;
        Line(gm != null, "GameManager.Instance != null");
        if (gm == null) return;

        Report.AppendLine($"  state={gm.CurrentGameState}  IsCombatState(true)={gm.IsCombatState(true)}");

        var mm = MatchManager.Instance;
        Line(mm != null, "MatchManager.Instance != null");
        if (mm != null) Report.AppendLine($"  MatchManager.IsMatchActive={mm.IsMatchActive}");

        var players = gm.ActivePlayers;
        Report.AppendLine($"  ActivePlayers.Count={(players == null ? -1 : players.Count)}");

        if (players != null)
        {
            foreach (var p in players)
            {
                if (p == null) { Report.AppendLine("  (null fighter)"); continue; }
                var im = p.GetComponent<InputManager>();
                Report.AppendLine(
                    $"  {p.name}: playerID={p.playerID} activeInHierarchy={p.gameObject.activeInHierarchy} " +
                    $"respawnLocked={p.IsRespawnLocked} isAttacking={p.isAttacking} " +
                    $"InputManager={(im == null ? "MISSING" : (im.enabled ? "enabled" : "disabled"))}");
            }
        }

        // ---- 训练菜单（R2/R3）----
        var tmc = UnityEngine.Object.FindObjectOfType<SuperSmashLike.Training.TrainingModeController>();
        Line(tmc != null, "TrainingModeController(场景) != null");
        if (tmc != null)
        {
            Line(tmc.menuPanel != null, "menuPanel 已接线");
            tmc.SetMenu(true);
            Line(tmc.MenuOpen, "SetMenu(true) -> MenuOpen=true");
            Line(tmc.menuPanel != null && tmc.menuPanel.activeInHierarchy, "打开后 menuPanel activeInHierarchy");
            Line(Mathf.Approximately(Time.timeScale, 0f), "菜单打开时 Time.timeScale == 0");
            tmc.SetMenu(false);
            Line(!tmc.MenuOpen && (tmc.menuPanel == null || !tmc.menuPanel.activeInHierarchy), "关闭后 menuPanel 隐藏");
        }
    }

    private static void Line(bool cond, string label)
    {
        Report.AppendLine($"  [{(cond ? "PASS" : "FAIL")}] {label}");
    }
}
