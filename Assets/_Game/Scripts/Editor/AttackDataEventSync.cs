#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// ============================================================
// AttackDataEventSync — 攻击时序自动同步（编辑器工具）
// 职责：
//   把动画剪辑上的 Animation Event（Activate/Deactivate/AttackFinished）
//   换算成 AttackData 的三段时长，自动写回 FighterData 资产。
//
//   公式（唯一权威关系）：
//     startupTime  = Activate 事件时间
//     activeTime   = Deactivate - Activate
//     recoveryTime = AttackFinished - Deactivate
//     三者之和 == 剪辑长度
//
//   为什么必须以事件为准：
//     判定框的实际开/关由 HitboxEventRelay 的动画事件驱动（零依赖 AttackData），
//     所以 AttackData 里的三段时长只是"帧数表显示的副本"，必须以事件为源。
//
// 架构位置：Editor 层（必须放在任意名为 Editor 的文件夹内）
// 使用方式：
//   1) Project 里选中 FD_Knight → 菜单 Tools → SuperSmashLike → 帧数表同步
//   2) 或在 FighterDataEditor 里加一个按钮调用 AttackDataEventSync.Sync(data)
// ============================================================
namespace SuperSmashLike.EditorTools
{
    public static class AttackDataEventSync
    {
        #region 1. 配置

        // ---- 配置区 ----
        private const string ControllerPath = "Assets/_Game/Animations/Animator/FighterAnimator.controller";
        private const string AttackTypeParam = "AttackType";

        private const string EvActivate = "Activate";          // 判定框开启
        private const string EvDeactivate = "Deactivate";      // 判定框关闭
        private const string EvFinished = "AttackFinished";    // 攻击结束

        // 不参与同步的字段。
        // grab 的 animIndex 也是 0，和 jab1 撞号；且抓取是代码驱动（OverlapCircleAll），
        // 不走 AttackType，所以必须排除，否则会被 Jab1 的时序覆盖。
        private static readonly HashSet<string> SkipFields = new HashSet<string> { "grab" };

        #endregion

        #region 2. 菜单入口

        // ==================== 菜单入口 ====================

        [MenuItem("Tools/SuperSmashLike/帧数表同步/检查选中角色（只预览，不写入）", false, 1)]
        private static void CheckSelected()
        {
            var list = PickSelected();
            if (list == null) return;
            foreach (var d in list) Sync(d, true);
        }

        [MenuItem("Tools/SuperSmashLike/帧数表同步/同步选中角色（写入）", false, 2)]
        private static void SyncSelected()
        {
            var list = PickSelected();
            if (list == null) return;
            foreach (var d in list) Sync(d, false);
        }

        [MenuItem("Tools/SuperSmashLike/帧数表同步/同步全部角色（写入）", false, 21)]
        private static void SyncAll()
        {
            var guids = AssetDatabase.FindAssets("t:FighterData");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[AttackDataEventSync] 项目里没有 FighterData 资产。");
                return;
            }

            int total = 0;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var d = AssetDatabase.LoadAssetAtPath<FighterData>(path);
                if (d != null) total += Sync(d, false);
            }
            Debug.Log($"[AttackDataEventSync] 全部角色同步完成，共更新 {total} 条。");
        }

        #endregion

        #region 3. 对外 API（供自定义 Inspector 调用）

        // ==================== 对外 API（供自定义 Inspector 调用）====================

        /// <summary>同步一个角色的所有 AttackData。返回实际更新的字段数。</summary>
        public static int Sync(FighterData data) => Sync(data, false);

        /// <summary>
        /// 同步一个角色。dryRun=true 时只打印差异、不写入资产。
        /// </summary>
        public static int Sync(FighterData data, bool dryRun)
        {
            if (data == null)
            {
                Debug.LogWarning("[AttackDataEventSync] data 为 null，跳过。");
                return 0;
            }

            // ---- 1. 读控制器，构建 AttackType → 剪辑 映射 ----
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null)
            {
                Debug.LogError($"[AttackDataEventSync] 找不到控制器：{ControllerPath}\n" +
                               "请确认路径没变（若改过文件夹，改本脚本顶部的 ControllerPath 常量）。");
                return 0;
            }

            var map = new Dictionary<int, AnimationClip>();
            foreach (var layer in ctrl.layers)
                WalkStateMachine(layer.stateMachine, map);

            // ---- 2. 反射取所有 AttackData 字段 ----
            var fields = typeof(FighterData)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(AttackData))
                .ToArray();

            // ---- 3. 撞号检测（animIndex 重复 = 隐患）----
            var dup = fields
                .Select(f => new { f.Name, Atk = (AttackData)f.GetValue(data) })
                .Where(x => x.Atk != null)
                .GroupBy(x => x.Atk.animIndex)
                .Where(g => g.Count() > 1);
            foreach (var g in dup)
                Debug.LogWarning($"[AttackDataEventSync] {data.name}：animIndex={g.Key} 被多个字段共用 → " +
                                 string.Join("、", g.Select(x => x.Name)) +
                                 "（若其中之一是 grab/投技，属预期；否则建议改成不重复的值）");

            // ---- 4. 逐个字段换算并写回 ----
            var log = new StringBuilder();
            int updated = 0, unchanged = 0, skipped = 0;

            if (!dryRun)
                Undo.RecordObject(data, "Sync AttackData From Anim Events");

            foreach (var f in fields
                         .OrderBy(f => ((AttackData)f.GetValue(data))?.animIndex ?? int.MaxValue)
                         .ThenBy(f => f.Name))
            {
                var atk = (AttackData)f.GetValue(data);
                if (atk == null) { skipped++; continue; }

                if (SkipFields.Contains(f.Name))
                {
                    log.AppendLine($"  跳过  {f.Name,-16} —— 在跳过名单中（代码驱动，不参与同步）");
                    skipped++;
                    continue;
                }

                if (!map.TryGetValue(atk.animIndex, out var clip) || clip == null)
                {
                    log.AppendLine($"  跳过  {f.Name,-16} (AT={atk.animIndex,3}) —— 控制器里没有对应剪辑（如必杀技未配动画）");
                    skipped++;
                    continue;
                }

                if (!TryReadTiming(clip, out float s, out float a, out float r, out string why))
                {
                    log.AppendLine($"  跳过  {f.Name,-16} (AT={atk.animIndex,3}) —— {clip.name} {why}");
                    skipped++;
                    continue;
                }

                // 已一致 → 不写、不报错
                bool same = Mathf.Abs(atk.startupTime - s) < 1e-4f
                         && Mathf.Abs(atk.activeTime - a) < 1e-4f
                         && Mathf.Abs(atk.recoveryTime - r) < 1e-4f;
                if (same) { unchanged++; continue; }

                log.AppendLine($"  同步  {f.Name,-16} (AT={atk.animIndex,3}) {clip.name,-20} " +
                               $"{atk.startupTime:F4}/{atk.activeTime:F4}/{atk.recoveryTime:F4}" +
                               $"  →  {s:F4}/{a:F4}/{r:F4}");

                if (!dryRun)
                {
                    atk.startupTime = s;
                    atk.activeTime = a;
                    atk.recoveryTime = r;
                }
                updated++;
            }

            // ---- 5. 落盘 ----
            if (!dryRun && updated > 0)
            {
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
            }

            string head = dryRun ? "【预览·未写入】" : "【已写入】";
            Debug.Log($"[AttackDataEventSync] {head}{data.name}\n" +
                      $"  控制器映射 {map.Count} 条 AttackType→剪辑\n" +
                      $"  更新 {updated} 条 / 已一致 {unchanged} 条 / 跳过 {skipped} 条\n" +
                      log);

            return updated;
        }

        // ==================== 内部实现 ====================

        #endregion

        #region 4. 私有工具

        private static List<FighterData> PickSelected()
        {
            var list = Selection.objects.OfType<FighterData>().ToList();
            if (list.Count == 0)
            {
                Debug.LogWarning("[AttackDataEventSync] 请先在 Project 窗口里选中一个 FighterData 资产（如 FD_Knight），再执行本菜单。");
                return null;
            }
            return list;
        }

        /// <summary>递归遍历状态机：AnyState 转移 + 各 State 自身的转移，子状态机也递归。</summary>
        private static void WalkStateMachine(AnimatorStateMachine sm, Dictionary<int, AnimationClip> map)
        {
            if (sm == null) return;

            foreach (var t in sm.anyStateTransitions)          // ★ 本项目 25 条全在这里
                TryAddFromTransition(t, map);

            foreach (var child in sm.states)
                foreach (var t in child.state.transitions)     // 兜底：普通状态转移
                    TryAddFromTransition(t, map);

            foreach (var sub in sm.stateMachines)              // 子状态机
                WalkStateMachine(sub.stateMachine, map);
        }

        /// <summary>从一条转移里挖出 (AttackType == N) → 目标 State 的剪辑。</summary>
        private static void TryAddFromTransition(AnimatorStateTransition t, Dictionary<int, AnimationClip> map)
        {
            if (t == null || t.destinationState == null) return;

            int? attackType = null;
            foreach (var c in t.conditions)
            {
                if (c.parameter == AttackTypeParam && c.mode == AnimatorConditionMode.Equals)
                {
                    attackType = Mathf.RoundToInt(c.threshold);
                    break;
                }
            }
            if (attackType == null) return;

            // Motion 为空（如 Attack_SpecialN）或不是 AnimationClip（如 BlendTree）→ 不入表
            var clip = t.destinationState.motion as AnimationClip;
            if (clip == null) return;

            if (map.TryGetValue(attackType.Value, out var old))
            {
                if (old != clip)
                    Debug.LogWarning($"[AttackDataEventSync] AttackType={attackType} 同时映射到 " +
                                     $"{old.name} 和 {clip.name}（保留前者，请检查状态机）");
            }
            else
            {
                map[attackType.Value] = clip;
            }
        }

        /// <summary>从剪辑事件换算三段时长。缺任一事件都返回 false（保留原值）。</summary>
        private static bool TryReadTiming(AnimationClip clip, out float s, out float a, out float r, out string why)
        {
            s = a = r = 0f;
            why = null;

            float tAct = -1f, tDeact = -1f, tFin = -1f;
            foreach (var e in AnimationUtility.GetAnimationEvents(clip))
            {
                if (e.functionName == EvActivate) tAct = e.time;
                else if (e.functionName == EvDeactivate) tDeact = e.time;
                else if (e.functionName == EvFinished) tFin = e.time;
            }

            if (tAct < 0f && tDeact < 0f && tFin < 0f) { why = "没有任何攻击事件（如投技动画）"; return false; }
            if (tAct < 0f) { why = "缺 Activate 事件"; return false; }
            if (tDeact < 0f) { why = "缺 Deactivate 事件"; return false; }
            if (tFin < 0f) { why = "缺 AttackFinished 事件"; return false; }
            if (tDeact < tAct) { why = "Deactivate 早于 Activate（事件顺序可疑）"; return false; }

            s = tAct;
            a = tDeact - tAct;
            r = Mathf.Max(0f, tFin - tDeact);
            return true;
        }

        #endregion
    }
}
#endif