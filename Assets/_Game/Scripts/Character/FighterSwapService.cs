using SuperSmashLike.InputSystem;
using SuperSmashLike.Training;
using UnityEngine;

// ============================================================
// FighterSwapService — 选人确认时"整实例换将"服务（M6-D1）
// 职责：
//   把某玩家当前的斗士实例，整体替换为所选 FighterData 对应的 Fighter 预制体实例。
// 设计：
//   - 静态无状态服务；由 CharacterSelectPanel.ConfirmPlayer 在写入选择后调用。
//   - 幂等：目标角色 == 当前角色 → 跳过；未配置 fighterPrefab → 告警并保持现状。
//   - 换将后同步：GameManager 注册表 + TrainingModeController.player1/player2。
// 【注意】场景中直接引用斗士的只有 TMC.player1/player2；其余系统（CameraManager /
//   MatchManager / UI）均动态读取 GameManager.ActivePlayers，换将后自动跟随新实例。
// ============================================================
namespace SuperSmashLike.Core
{
    public static class FighterSwapService
    {
        // 【做什么】按玩家 ID 执行换将（选人确认时调用）
        public static void ApplySelection(int playerID)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.gameSettings == null)
            {
                Debug.LogWarning($"[FighterSwap] GameManager/GameSettings 缺失，跳过 P{playerID + 1}");
                return;
            }

            GameSettings gs = gm.gameSettings;
            FighterData fd = gs.GetSelection(playerID);
            if (fd == null)
            {
                Debug.LogWarning($"[FighterSwap] P{playerID + 1} 尚未选择角色，跳过");
                return;
            }
            if (fd.fighterPrefab == null)
            {
                Debug.LogWarning($"[FighterSwap] {fd.fighterName} 未配置 fighterPrefab，保持现有斗士");
                return;
            }

            FighterController old = FindFighter(gm, playerID);
            if (old == null)
            {
                Debug.LogWarning($"[FighterSwap] 找不到 P{playerID + 1} 的现有斗士实例，跳过");
                return;
            }
            if (old.fighterData == fd)
                return;   // 已是目标角色 → 幂等跳过（重复确认不重复换将）

            // ---- 1) 记录旧实例的可继承状态 ----
            Transform oldT = old.transform;
            Vector3 pos = oldT.position;
            Quaternion rot = oldT.rotation;
            Transform parent = oldT.parent;
            bool wasActive = old.gameObject.activeSelf;
            bool compEnabled = old.enabled;
            bool facingRight = old.isFacingRight;
            Vector3 visualScale = old.visual != null ? old.visual.localScale : Vector3.one;
            Quaternion visualRot = old.visual != null ? old.visual.localRotation : Quaternion.identity;

            InputManager oldIm = old.GetComponent<InputManager>();
            bool inputEnabled = oldIm != null && oldIm.enabled;

            CPUPlayer oldCpu = old.GetComponent<CPUPlayer>();
            bool hasCpu = oldCpu != null;
            bool cpuEnabled = hasCpu && oldCpu.enabled;

            // ---- 2) 实例化新斗士（同位置/朝向/父节点）----
            GameObject go = Object.Instantiate(fd.fighterPrefab, pos, rot, parent);
            FighterController next = go.GetComponent<FighterController>();
            if (next == null)
            {
                Debug.LogError($"[FighterSwap] {fd.fighterPrefab.name} 上缺少 FighterController，撤销换将");
                Object.Destroy(go);
                return;
            }

            // ---- 3) 关键时序：在 Start 运行前写 playerID / 角色数据 ----
            next.playerID = playerID;
            next.fighterData = fd;
            next.isFacingRight = facingRight;
            next.enabled = compEnabled;

            // ---- 4) 镜像激活态 / 朝向视觉 / 输入启用态 ----
            if (next.visual != null)
            {
                next.visual.localScale = visualScale;
                next.visual.localRotation = visualRot;
            }
            go.SetActive(wasActive);
            InputManager nextIm = next.GetComponent<InputManager>();
            if (nextIm != null) nextIm.enabled = inputEnabled;

            // ---- 5) 镜像 CPU 状态（缺失时补组件）----
            if (hasCpu)
            {
                CPUPlayer nextCpu = next.GetComponent<CPUPlayer>();
                if (nextCpu == null) nextCpu = go.AddComponent<CPUPlayer>();
                nextCpu.enabled = cpuEnabled;
                nextCpu.SetAiFrozen(!cpuEnabled);
                nextCpu.SetAttackEnabled(cpuEnabled);
                if (!cpuEnabled) nextCpu.ForceIdle();
            }

            // ---- 6) GameManager 注册表交棒 ----
            gm.UnregisterFighter(old);
            gm.RegisterFighter(next);

            // ---- 7) 重绑训练控制器（场景中唯一直接引用斗士的地方）----
            foreach (TrainingModeController tmc in Object.FindObjectsOfType<TrainingModeController>(true))
            {
                if (tmc.player1 == old) tmc.player1 = next;
                if (tmc.player2 == old) tmc.player2 = next;
            }

            // ---- 8) 销毁旧实例 ----
            string oldName = old.fighterData != null ? old.fighterData.fighterName : old.name;
            Object.Destroy(old.gameObject);

            Debug.Log($"[FighterSwap] P{playerID + 1}: {oldName} → {fd.fighterName}（换将完成）");
        }

        // 【做什么】查找指定玩家当前的斗士实例
        // ActivePlayers（首选）→ TMC.player1/2（后备）→ 全场景 FighterController（兜底）
        private static FighterController FindFighter(GameManager gm, int playerID)
        {
            foreach (FighterController f in gm.ActivePlayers)
                if (f != null && f.playerID == playerID) return f;

            foreach (TrainingModeController tmc in Object.FindObjectsOfType<TrainingModeController>(true))
            {
                if (playerID == 0 && tmc.player1 != null && tmc.player1.playerID == playerID) return tmc.player1;
                if (playerID == 1 && tmc.player2 != null && tmc.player2.playerID == playerID) return tmc.player2;
            }

            foreach (FighterController f in Object.FindObjectsOfType<FighterController>(true))
                if (f != null && f.playerID == playerID) return f;

            return null;
        }
    }
}
