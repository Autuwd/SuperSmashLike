using System.Collections.Generic;
using System.Reflection;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// AttackTableWizard — M6-D3：补齐 Barbarian / Archer / Mage 的出招表与必杀配置
// 使用：菜单 SuperSmashLike → 43. Fill Attack Tables (M6-D3)
//      或 -batchmode -executeMethod AttackTableWizard.Run
//
// 四个步骤（幂等，可重复执行）：
//   ① 规格表：以 FD_Knight 为基准摘 23 槽 (名称/输入/描述)
//   ② 文本补齐：三缺表角色按字段名搬运 —— attackName 为默认 "Attack" 的 9 槽改名、
//      inputCommand / description 为空则填 Knight 文本
//   ③ 必杀配置：三角色 SN/SS/SD 补 projectilePrefab+速度+偏移（原数据为空 → 按钮发不出招），
//      SU 补 hitEffectPrefab（瞬移闪光，取自 Knight）；仅当为空时填
//   ④ 必杀文案：N/S/D 换成本角色实际机制文案（箭矢/魔法/蛮力），U 沿用剑士瞬移文案
//
// 【依据】FighterController.TrySpecial 注释（L1014）"投射物型：Archer 箭 / Mage 火球" +
//   Docs/12 §11.2 角色差异化（Archer=弓箭远程 / Mage=魔法必杀 / Barbarian=斧·最重）。
//   M6-D3 验收要求"描述与实际机制相符"→ 必杀弹种必须配置，文案必须按实际弹种写。
// 【注意】改名安全性：全工程仅 VisualRootMotion.keepVerticalFor 按 attackName 匹配，
//   该组件未挂到任何 prefab/场景 → 改名无副作用。
// ============================================================
public static class AttackTableWizard
{
    private const string FdDir = "Assets/_Game/ScriptableObjects/Characters/";
    private static readonly string[] TargetNames = { "FD_Barbarian", "FD_Archer", "FD_Mage" };
    private static readonly string[] VerifyNames = { "FD_Knight", "FD_Barbarian", "FD_Archer", "FD_Mage", "FD_Rogue", "FD_Paladin" };

    private struct ProjSpec
    {
        public string prefab;
        public float speed;
        public Vector2 offset;
    }

    // ③ 必杀弹种配置（仅当 projectilePrefab 为空时应用）
    private static readonly Dictionary<string, ProjSpec> SpecialSpecs = new Dictionary<string, ProjSpec>()
    {
        { "FD_Barbarian|specialNeutral", new ProjSpec { prefab = "Shockwave",        speed = 14f, offset = new Vector2(0f, 1.1f) } },
        { "FD_Barbarian|specialSide",    new ProjSpec { prefab = "Shockwave",        speed = 18f, offset = new Vector2(0f, 1.1f) } },
        { "FD_Barbarian|specialDown",    new ProjSpec { prefab = "Stone",            speed = 8f,  offset = new Vector2(1f, -0.2f) } },
        { "FD_Archer|specialNeutral",    new ProjSpec { prefab = "Arrow_Projectile", speed = 14f, offset = new Vector2(0f, 1.1f) } },
        { "FD_Archer|specialSide",       new ProjSpec { prefab = "Arrow_Projectile", speed = 20f, offset = new Vector2(0f, 1.1f) } },
        { "FD_Archer|specialDown",       new ProjSpec { prefab = "Arrow_Projectile", speed = 10f, offset = new Vector2(0f, 1.1f) } },
        { "FD_Mage|specialNeutral",      new ProjSpec { prefab = "Fireball",         speed = 12f, offset = new Vector2(0f, 1.1f) } },
        { "FD_Mage|specialSide",         new ProjSpec { prefab = "Shockwave",        speed = 16f, offset = new Vector2(0f, 1.1f) } },
        { "FD_Mage|specialDown",         new ProjSpec { prefab = "Fireball",         speed = 9f,  offset = new Vector2(0f, 1.1f) } },
    };

    // ④ 必杀文案（按实际弹种写；每次执行都会校准，幂等）
    private static readonly Dictionary<string, string> SpecialDescs = new Dictionary<string, string>()
    {
        { "FD_Barbarian|specialNeutral", "中性必杀：砸出一道冲击波攻击前方敌人。" },
        { "FD_Barbarian|specialSide",    "横向必杀：横扫打出一道高速冲击波。" },
        { "FD_Barbarian|specialDown",    "下必杀：从地面抓起石头掷出，轻按和长按 K 对应不同距离。" },
        { "FD_Archer|specialNeutral",    "中性必杀：射出一支直线飞行的箭矢。" },
        { "FD_Archer|specialSide",       "横向必杀：射出一支高速强力箭。" },
        { "FD_Archer|specialDown",       "下必杀：射出一支慢速箭。" },
        { "FD_Mage|specialNeutral",      "中性必杀：发射一枚魔法火球。" },
        { "FD_Mage|specialSide",         "横向必杀：发出一道高速魔法冲击波。" },
        { "FD_Mage|specialDown",         "下必杀：发射一枚慢速重火球。" },
    };

    [MenuItem("SuperSmashLike/43. Fill Attack Tables (M6-D3)", false, 43)]
    public static void Run()
    {
        Debug.Log("[Atk] ===== 开始补齐出招表 / 必杀配置（Barbarian | Archer | Mage）=====");

        FighterData knight = Load("FD_Knight");
        if (knight == null) { Debug.LogError("[Atk] FD_Knight 缺失，中止"); return; }

        // ① 规格表
        var spec = new Dictionary<string, (string name, string cmd, string desc)>();
        foreach (var f in AttackFields())
        {
            var a = f.GetValue(knight) as AttackData;
            if (a == null) continue;
            spec[f.Name] = (a.attackName, a.inputCommand, a.description);
        }
        Debug.Log($"[Atk] 规格表：Knight {spec.Count}/23 槽");

        var knightSu = GetSlot(knight, "specialUp");

        // ② + ③ + ④ 逐角色处理
        foreach (string fdName in TargetNames)
        {
            FighterData fd = Load(fdName);
            if (fd == null) { Debug.LogError($"[Atk] {fdName} 缺失"); continue; }

            // ② 文本搬运
            int renamed = 0, filledCmd = 0, filledDesc = 0;
            foreach (var f in AttackFields())
            {
                var a = f.GetValue(fd) as AttackData;
                if (a == null || !spec.TryGetValue(f.Name, out var s)) continue;

                if (string.IsNullOrEmpty(a.attackName) || a.attackName == "Attack")
                { a.attackName = s.name; renamed++; }
                if (string.IsNullOrEmpty(a.inputCommand)) { a.inputCommand = s.cmd; filledCmd++; }
                if (string.IsNullOrEmpty(a.description)) { a.description = s.desc; filledDesc++; }
            }

            // ③ 必杀弹种（仅填空）
            int projFilled = 0;
            foreach (var kv in SpecialSpecs)
            {
                if (!kv.Key.StartsWith(fdName + "|")) continue;
                string slot = kv.Key.Substring(fdName.Length + 1);
                var a = GetSlot(fd, slot);
                if (a == null || a.projectilePrefab != null) continue;

                var go = FindProjectile(kv.Value.prefab);
                if (go == null) { Debug.LogError($"[Atk] {kv.Key} 找不到弹种 {kv.Value.prefab}"); continue; }
                a.projectilePrefab = go;
                a.projectileSpeed = kv.Value.speed;
                a.projectileSpawnOffset = kv.Value.offset;
                projFilled++;
            }
            // ③b SU 闪光（仅填空）
            int fxFilled = 0;
            var su = GetSlot(fd, "specialUp");
            if (su != null && su.hitEffectPrefab == null && knightSu != null && knightSu.hitEffectPrefab != null)
            { su.hitEffectPrefab = knightSu.hitEffectPrefab; fxFilled++; }

            // ④ 必杀文案校准（N/S/D 按本角色弹种；U 沿用剑士瞬移文案）
            int descFixed = 0;
            foreach (var kv in SpecialDescs)
            {
                if (!kv.Key.StartsWith(fdName + "|")) continue;
                string slot = kv.Key.Substring(fdName.Length + 1);
                var a = GetSlot(fd, slot);
                if (a == null || a.description == kv.Value) continue;
                a.description = kv.Value;
                descFixed++;
            }
            if (su != null && knightSu != null && su.description != knightSu.description)
            { su.description = knightSu.description; descFixed++; }

            EditorUtility.SetDirty(fd);
            Debug.Log($"[Atk] {fdName}：改名 {renamed} / 补输入 {filledCmd} / 补描述 {filledDesc} / 补弹种 {projFilled} / 补闪光 {fxFilled} / 文案校准 {descFixed}");
        }

        AssetDatabase.SaveAssets();

        // ⑤ 六角色总校验
        int bad = 0;
        foreach (string fdName in VerifyNames)
        {
            FighterData fd = Load(fdName);
            if (fd == null) { Debug.LogError($"[Atk] 校验缺 {fdName}"); bad++; continue; }

            int total = 0, nullSlots = 0, emptyCmd = 0, emptyDesc = 0, dupAttack = 0;
            foreach (var f in AttackFields())
            {
                var a = f.GetValue(fd) as AttackData;
                if (a == null) { nullSlots++; continue; }
                total++;
                if (string.IsNullOrEmpty(a.inputCommand)) emptyCmd++;
                if (string.IsNullOrEmpty(a.description)) emptyDesc++;
                if (a.attackName == "Attack") dupAttack++;
            }

            var sn = GetSlot(fd, "specialNeutral");
            var ss = GetSlot(fd, "specialSide");
            var sd = GetSlot(fd, "specialDown");
            var vSu = GetSlot(fd, "specialUp");
            string Spec(AttackData a) => a == null || a.projectilePrefab == null ? "!" : a.projectilePrefab.name;
            string specInfo = $"N:{Spec(sn)},S:{Spec(ss)},D:{Spec(sd)},U:{(vSu != null && vSu.hitEffectPrefab != null ? "FX" : "!")}";
            bool specOK = sn != null && sn.projectilePrefab != null
                       && ss != null && ss.projectilePrefab != null
                       && sd != null && sd.projectilePrefab != null
                       && vSu != null && vSu.hitEffectPrefab != null;

            bool ok = nullSlots == 0 && emptyCmd == 0 && emptyDesc == 0 && dupAttack == 0 && total == 23 && specOK;
            if (!ok) bad++;
            Debug.Log($"[Atk] {(ok ? "OK " : "BAD")} {fdName} total={total} null={nullSlots} emptyCmd={emptyCmd} emptyDesc={emptyDesc} nameAttack={dupAttack} spec={specInfo}");
        }
        Debug.Log($"[Atk] ===== 结束：{(bad == 0 ? "6/6 全部通过" : bad + " 个不合格")} =====");
    }

    private static AttackData GetSlot(FighterData fd, string fieldName)
        => typeof(FighterData).GetField(fieldName)?.GetValue(fd) as AttackData;

    private static GameObject FindProjectile(string fileName)
    {
        foreach (var g in AssetDatabase.FindAssets(fileName + " t:Prefab", new[] { "Assets/_Game/Prefabs/Projectiles" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == fileName)
                return AssetDatabase.LoadAssetAtPath<GameObject>(p);
        }
        return null;
    }

    private static IEnumerable<FieldInfo> AttackFields()
    {
        foreach (var f in typeof(FighterData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (f.FieldType == typeof(AttackData)) yield return f;
    }

    private static FighterData Load(string fdName)
        => AssetDatabase.LoadAssetAtPath<FighterData>(FdDir + fdName + ".asset");
}
