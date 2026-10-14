using System;
using System.Text;
using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using SuperSmashLike.Training;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ============================================================
// TrainingSmokeTest — M5 训练模式无头 Play 冒烟（编辑器一次性工具）
// 用法（批处理，注意【不要】加 -quit，本测试完成后自行 Exit(code)）：
//   Unity.exe -batchmode -nographics -projectPath <proj> \
//     -executeMethod TrainingSmokeTest.Run -logFile <log>
// 设计：
//   - SessionState 跨 domain reload 保活，进入 Play 后逐帧轮询
//   - 待场景 Awake/Start 完成后跑校验；失败项逐条打印，最后 exit 1；全通过 exit 0
// 【注意】校验真实读取 GameObject.activeInHierarchy（防止只读 enabled 的假阳性）
// ============================================================
public static class TrainingSmokeTest
{
    private const string ScenePath = "Assets/_Game/Scenes/BattleTest.unity";
    private const string RunningKey = "TrainingSmoke.Running";
    private const string FrameKey = "TrainingSmoke.Frame";

    private static readonly StringBuilder Report = new StringBuilder();
    private static int _failCount;

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(FrameKey, 0);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
        Debug.Log("[TRAINING-SMOKE] 已请求进入 Play 模式，等待场景初始化...");
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

        // 早期阶段：若场景还没斗士，主动开一场以触发斗士生成
        if (f == 5)
        {
            var gm0 = GameManager.Instance;
            if (gm0 != null && (gm0.ActivePlayers == null || gm0.ActivePlayers.Count == 0))
            {
                if (MatchManager.Instance != null) MatchManager.Instance.StartMatch();
            }
        }

        if (f < 60) return;   // 等场景 Awake/Start/生成完成

        SessionState.SetBool(RunningKey, false);
        EditorApplication.update -= Tick;

        try
        {
            Check();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TRAINING-SMOKE] EXCEPTION: {e}");
            _failCount++;
        }

        Debug.Log($"[TRAINING-SMOKE] ===== RESULT fails={_failCount} =====\n{Report}");
        EditorApplication.Exit(_failCount == 0 ? 0 : 1);
    }

    private static void Assert(bool cond, string label)
    {
        if (cond) Report.AppendLine($"  [PASS] {label}");
        else { Report.AppendLine($"  [FAIL] {label}"); _failCount++; }
    }

    private static void Warn(string label)
    {
        Report.AppendLine($"  [WARN] {label}");
    }

    private static void Check()
    {
        // 1. GameManager
        var gm = GameManager.Instance;
        Assert(gm != null, "GameManager.Instance != null");
        if (gm == null) return;
        Assert(gm.gameObject.activeInHierarchy, "GameManager.gameObject.activeInHierarchy");

        // 2. Training 状态
        gm.SwitchState(GameState.Training);
        Assert(gm.CurrentGameState == GameState.Training, "CurrentGameState == Training");
        Assert(gm.IsCombatState(true), "IsCombatState(includeTraining:true) == true");
        Assert(!gm.IsCombatState(false), "IsCombatState(includeTraining:false) == false");

        // 3. 时间缩放（BaseTimeScale / ApplyTimeScale）
        gm.BaseTimeScale = 1f; gm.ApplyTimeScale();
        Assert(Mathf.Approximately(Time.timeScale, 1f), "ApplyTimeScale -> Time.timeScale == 1.0");
        gm.BaseTimeScale = 0.5f; gm.ApplyTimeScale();
        Assert(Mathf.Approximately(Time.timeScale, 0.5f), "ApplyTimeScale -> Time.timeScale == 0.5 (慢动作)");
        gm.BaseTimeScale = 1f; gm.ApplyTimeScale();

        // 4. Training 组件（运行时挂载，复核 activeInHierarchy）
        var controllerGO = new GameObject("TrainingModeController_SmokeTest");
        var tmc = controllerGO.AddComponent<TrainingModeController>();
        Assert(tmc != null, "TrainingModeController 已挂载");
        Assert(controllerGO.activeInHierarchy, "TrainingModeController.gameObject.activeInHierarchy");

        var hudGO = new GameObject("TrainingHud_SmokeTest");
        var hud = hudGO.AddComponent<TrainingHud>();
        Assert(hud != null, "TrainingHud 已挂载");
        Assert(hudGO.activeInHierarchy, "TrainingHud.gameObject.activeInHierarchy");

        // 5. 斗士相关（若场景/比赛已生成斗士）
        var players = gm.ActivePlayers;
        int n = players == null ? -1 : players.Count;
        Report.AppendLine($"  [INFO] ActivePlayers.Count = {n}");
        if (players == null || players.Count == 0)
        {
            Warn("场景无斗士：跳过 ResetToSpawn / debugInvincible 行为校验（训练需在战斗中进入）");
        }
        else
        {
            var f = players[0];
            Assert(f != null && f.gameObject.activeInHierarchy, "P1.gameObject.activeInHierarchy");

            if (f != null)
            {
                // debugInvincible 守卫：启用时 ApplyDamage 在解引用攻方前返回（故 attacker=null 安全）
                f.debugInvincible = true;
                float before = f.CurrentDamage;
                var atk = new AttackData { damage = 10f };
                f.ApplyDamage(atk, null);
                Assert(Mathf.Approximately(f.CurrentDamage, before), "debugInvincible 拦截 ApplyDamage（伤害不变）");

                // 对照：关闭无敌、给出有效攻方，应确实累加伤害
                if (players.Count >= 2 && players[1] != null)
                {
                    f.debugInvincible = false;
                    float before2 = f.CurrentDamage;
                    f.ApplyDamage(atk, players[1]);
                    Assert(f.CurrentDamage > before2, "关闭无敌后 ApplyDamage 累加伤害（对照）");
                }

                // ResetToSpawn：归位并清零伤害
                Vector3 spawn = MatchManager.Instance != null
                    ? MatchManager.Instance.GetSpawnPositionFor(f)
                    : f.transform.position;
                f.ResetToSpawn(spawn, true);
                Assert(Mathf.Approximately(f.CurrentDamage, 0f), "ResetToSpawn 清零伤害");
                f.debugInvincible = false;
            }
        }

        // 6. 清理运行时挂载对象
        UnityEngine.Object.Destroy(controllerGO);
        UnityEngine.Object.Destroy(hudGO);
    }
}
