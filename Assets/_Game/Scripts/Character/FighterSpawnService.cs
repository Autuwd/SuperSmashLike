using SuperSmashLike.InputSystem;
using SuperSmashLike.Managers;
using SuperSmashLike.Training;
using UnityEngine;

// ============================================================
// FighterSpawnService — 战斗/训练开局按选人结果重建斗士（M6-D1）
// 职责：
//   1. 进入 Battle / Training 时清场：销毁场景中的旧斗士实例（并显式反注册）
//   2. 按 GameManager.playerCount / aiCount 与 GameSettings 选人结果逐槽生成（训练模式固定 1 人类 + 1 CPU）
//   3. 人类槽 → 开 InputManager；CPU 槽 → 挂 / 开 CPUPlayer（AI 定位由 aiArchetype 决定）
//   4. 生成后回绑 TrainingModeController.player1 / player2（场景中唯一保留斗士引用的系统）
// 设计：
//   - 静态无状态服务；调用方：MatchManager.OnGameStateChangedHandler（Battle 分支）
//     与 GameManager.StartTraining（训练流程不走 Battle 状态分支）
//   - 【时序】必须在 ResetMatchData 之前调用 —— 后者依赖 ActivePlayers 已就位
//   - 选人缺失（skipToBattle / 直接进训练）→ 从 selectableFighters 按序兜底轮询
//   - 每次进战斗都"销毁重建"：换人 / 换人数 / 换舞台都无需手工清理，重赛自然走同一路径
// 【注意】FighterController.Start 里会再次 RegisterFighter（幂等）；本服务先注册，
//   是为了让同帧的 ResetMatchData / 事件重绑立刻看到新实例。
// ============================================================
namespace SuperSmashLike.Core
{
    public static class FighterSpawnService
    {
        // 【做什么】按当前玩家配置与选人结果，重建全部斗士实例
        public static void SpawnForCurrentMatch()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FighterSpawn] 非运行状态，跳过斗士生成");
                return;
            }

            GameManager gm = GameManager.Instance;
            if (gm == null || gm.gameSettings == null)
            {
                Debug.LogError("[FighterSpawn] GameManager / GameSettings 缺失，无法生成斗士");
                return;
            }

            // ---- 1) 清场：销毁旧斗士 ----
            // 【注意】必须显式 UnregisterFighter —— FighterController.OnDestroy 不做反注册
            int oldCount = 0;
            foreach (FighterController old in Object.FindObjectsOfType<FighterController>(true))
            {
                if (old == null) continue;
                gm.UnregisterFighter(old);
                Object.Destroy(old.gameObject);
                oldCount++;
            }

            // ---- 2) 人数：训练固定 1v1；普通战斗人类 ≥1，CPU 补足且总数 ≤4 ----
            GameSettings gs = gm.gameSettings;
            int human = Mathf.Clamp(gm.playerCount, 1, 4);
            int ai = Mathf.Clamp(gm.aiCount, 0, 4 - human);

            // 【为什么】训练不沿用普通模式人数配置：否则按上一次战斗的 playerCount/aiCount 生成 P3/P4
            // 额外玩家；固定 1v1 同时保证木桩回绑目标（pid1）就是 CPU 槽
            if (gm.CurrentGameState == GameState.Training)
            {
                human = 1;
                ai = 1;
            }

            // ---- 3) 逐槽生成（先人类后 CPU）----
            for (int i = 0; i < human + ai; i++)
                SpawnSlot(gm, gs, i, i >= human);

            // ---- 4) 回绑训练控制器 ----
            RebindTrainingControllers(gm);

            Debug.Log($"[FighterSpawn] 重建完成：清理旧实例 {oldCount} 个 → 人类 {human} + CPU {ai}（共 {human + ai} 名）");
        }

        // 【做什么】生成单个槽位的斗士，并按槽位类型分配输入 / AI
        private static void SpawnSlot(GameManager gm, GameSettings gs, int playerID, bool isCpu)
        {
            FighterData fd = ResolveFighterData(gs, playerID);
            if (fd == null) return;

            if (fd.fighterPrefab == null)
            {
                Debug.LogError($"[FighterSpawn] {fd.fighterName} 未配置 fighterPrefab，P{playerID + 1} 槽位跳过");
                return;
            }

            Vector3 pos = ResolveSpawnPosition(playerID);
            GameObject go = Object.Instantiate(fd.fighterPrefab, pos, Quaternion.identity);
            go.name = $"Fighter_P{playerID + 1}_{fd.fighterName}";

            FighterController fc = go.GetComponent<FighterController>();
            if (fc == null)
            {
                Debug.LogError($"[FighterSpawn] {fd.fighterPrefab.name} 上缺少 FighterController，撤销 P{playerID + 1} 生成");
                Object.Destroy(go);
                return;
            }

            // 【关键时序】在 Start 运行前写入 playerID / 角色数据
            // （Start 会读 fighterData.jumpCount 并自注册；InputManager 的 Start 亦按新 pid 自愈）
            fc.playerID = playerID;
            fc.fighterData = fd;

            // 朝向：左半场朝右、右半场朝左（与 SetFacing 同规则：只翻 Visual 的 x 符号）
            bool faceRight = pos.x <= 0f;
            fc.isFacingRight = faceRight;
            if (fc.visual != null)
            {
                Vector3 s = fc.visual.localScale;
                fc.visual.localScale = new Vector3((faceRight ? 1f : -1f) * Mathf.Abs(s.x), s.y, s.z);
            }

            // 输入 / AI 分配
            InputManager im = go.GetComponent<InputManager>();
            CPUPlayer cpu = go.GetComponent<CPUPlayer>();

            if (isCpu)
            {
                if (im != null) im.enabled = false;                    // CPU 槽不接人类输入
                if (cpu == null)
                    cpu = go.AddComponent<CPUPlayer>();                // AddComponent 立即执行 Awake（此刻 fighterData 已写好）
                cpu.enabled = true;
                cpu.SetAiFrozen(false);
                cpu.SetAttackEnabled(true);
            }
            else
            {
                if (im != null) im.enabled = true;
                if (cpu != null) cpu.enabled = false;                  // 人类槽：预制体自带 CPU 组件 → 停用
            }

            gm.RegisterFighter(fc);   // 立即注册（Start 会再注册一次，RegisterFighter 幂等）

            Debug.Log($"[FighterSpawn] P{playerID + 1} → {fd.fighterName} @ {pos}（{(isCpu ? "CPU" : "人类")}）");
        }

        // 【做什么】解析某槽位的角色数据：选人结果优先；缺失时从可选手列表兜底轮询
        private static FighterData ResolveFighterData(GameSettings gs, int playerID)
        {
            FighterData fd = gs.GetSelection(playerID);
            if (fd != null) return fd;

            var pool = gs.GetSelectableFighters();
            if (pool != null && pool.Count > 0)
            {
                fd = pool[playerID % pool.Count];
                if (fd != null)
                {
                    Debug.LogWarning($"[FighterSpawn] P{playerID + 1} 无选人记录（跳过选人流程？），兜底使用 {fd.fighterName}");
                    return fd;
                }
            }

            Debug.LogError($"[FighterSpawn] P{playerID + 1} 无选人记录且 selectableFighters 为空，无法生成");
            return null;
        }

        // 【做什么】解析某槽位的出生点（与 MatchManager.GetSpawnPointFor 同规则：playerID 取模）
        private static Vector3 ResolveSpawnPosition(int playerID)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) return Vector3.zero;

            if (mm.spawnPoints != null && mm.spawnPoints.Length > 0)
            {
                Transform t = mm.spawnPoints[Mathf.Abs(playerID) % mm.spawnPoints.Length];
                if (t != null) return t.position;
            }
            return mm.transform.position;
        }

        // 【做什么】把场景中训练控制器的 player1/player2 指向新实例（旧引用清场后已悬空）
        private static void RebindTrainingControllers(GameManager gm)
        {
            TrainingModeController[] controllers = Object.FindObjectsOfType<TrainingModeController>(true);
            if (controllers == null || controllers.Length == 0) return;

            FighterController p1 = FindByPlayerID(gm, 0);
            FighterController p2 = FindByPlayerID(gm, 1);
            foreach (TrainingModeController tmc in controllers)
            {
                if (tmc == null) continue;
                tmc.player1 = p1;
                tmc.player2 = p2;
            }
        }

        // 【做什么】从 ActivePlayers 找指定 playerID 的实例（没有则 null）
        private static FighterController FindByPlayerID(GameManager gm, int playerID)
        {
            foreach (FighterController f in gm.ActivePlayers)
                if (f != null && f.playerID == playerID) return f;
            return null;
        }
    }
}
