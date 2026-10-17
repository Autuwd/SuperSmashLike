using System.Collections.Generic;
using System.Reflection;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// RogueSetupWizard — M6-D1：一键搭建"游侠（Rogue）"角色全套资产
// 使用：菜单 SuperSmashLike → 41. Setup Rogue (M6-D1)
//      或 -batchmode -executeMethod RogueSetupWizard.Run
// 五个步骤（可重复执行，幂等）：
//   ① FD_Rogue.asset      ：复制 FD_Knight → 改名/换色/调数值；23 个攻击槽整体克隆并缩放
//   ② P3.asset            ：复制 P1.asset 生成游侠骨架 Avatar
//   ③ 资源副本            ：Synty 暗精灵模型/匕首 → Assets/_Game/Art/（保持项目内引用）
//   ④ Fighter_Rogue.prefab：复制 Fighter_Knight 结构，换模型/武器/头像/数据/判定框
//   ⑤ GS_Default 注册 + 回填各 FD 的 fighterPrefab 映射 + 校验
// ============================================================
public static class RogueSetupWizard
{
    // ---- 路径常量 ----
    private const string FdKnightPath    = "Assets/_Game/ScriptableObjects/Characters/FD_Knight.asset";
    private const string FdRoguePath     = "Assets/_Game/ScriptableObjects/Characters/FD_Rogue.asset";
    private const string FdBarbarianPath = "Assets/_Game/ScriptableObjects/Characters/FD_Barbarian.asset";
    private const string P1AvatarPath    = "Assets/_Game/Art/Character/P1.asset";
    private const string P3AvatarPath    = "Assets/_Game/Art/Character/P3.asset";
    private const string ModelSrcPath    = "Assets/Synty/PolygonFantasyRivals/Prefabs/Characters/SM_Chr_DarkElf_01.prefab";
    private const string ModelDstPath    = "Assets/_Game/Art/Character/SM_Chr_DarkElf_01.prefab";
    private const string DaggerSrcPath   = "Assets/Synty/PolygonFantasyRivals/Prefabs/Weapons/SM_Wep_DarkElf_01.prefab";
    private const string DaggerDstPath   = "Assets/_Game/Art/Weapon/SM_Wep_DarkElf_01.prefab";
    private const string KnightPrefabPath    = "Assets/_Game/Prefabs/Fighters/Fighter_Knight.prefab";
    private const string BarbarianPrefabPath = "Assets/_Game/Prefabs/Fighters/Fighter_Barbarian.prefab";
    private const string RoguePrefabPath = "Assets/_Game/Prefabs/Fighters/Fighter_Rogue.prefab";
    private const string GsPath          = "Assets/_Game/ScriptableObjects/GameModes/GS_Default.asset";

    // ---- 游侠数值（相对剑士的缩放系数）----
    private const float StartupScale   = 0.8f;    // 前摇/后摇
    private const float DamageScale    = 0.85f;   // 伤害
    private const float KnockbackScale = 0.85f;   // 基础击飞 + 成长率

    [MenuItem("SuperSmashLike/41. Setup Rogue (M6-D1)", false, 41)]
    public static void Run()
    {
        Debug.Log("[Rogue] ===== 开始搭建游侠（Rogue）资产 =====");

        FighterData rogue       = Step1_BuildFighterData();
        Avatar      avatar      = Step2_BuildAvatar();
        GameObject  modelPrefab = Step3_CopyAsset(ModelSrcPath, ModelDstPath);
        GameObject  daggerPrefab= Step3_CopyAsset(DaggerSrcPath, DaggerDstPath);
        GameObject  roguePrefab = Step4_BuildFighterPrefab(rogue, avatar, modelPrefab, daggerPrefab);
        Step5_RegisterAndVerify(rogue, roguePrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Rogue] ===== 搭建结束 =====");
    }

    // ============================================================
    // ① 角色数据：FD_Rogue.asset
    // ============================================================
    private static FighterData Step1_BuildFighterData()
    {
        // 先删后建：数值缩放是乘法，必须每次从干净的 FD_Knight 克隆
        if (AssetDatabase.LoadAssetAtPath<FighterData>(FdRoguePath) != null)
            AssetDatabase.DeleteAsset(FdRoguePath);

        if (!AssetDatabase.CopyAsset(FdKnightPath, FdRoguePath))
        {
            Debug.LogError($"[Rogue] ① 复制 {FdKnightPath} → {FdRoguePath} 失败");
            return null;
        }

        FighterData fd = AssetDatabase.LoadAssetAtPath<FighterData>(FdRoguePath);
        if (fd == null) { Debug.LogError("[Rogue] ① FD_Rogue 加载失败"); return null; }

        // 身份
        fd.fighterName  = "游侠";
        fd.portraitIcon = null;                                 // 头像留到 D3 补
        fd.uiColor      = new Color(0.62f, 0.36f, 0.9f, 1f);    // 紫色主题

        // 移动：快而轻（剑士为 110 / 2.8 / 5.5）
        fd.weightClass = WeightClass.Light;
        fd.weight      = 80f;
        fd.walkSpeed   = 7f;
        fd.runSpeed    = 10f;

        // 攻击：全部 23 槽 —— 前摇/后摇缩短、伤害与击飞下调
        foreach (FieldInfo f in typeof(FighterData).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType != typeof(AttackData)) continue;
            AttackData a = f.GetValue(fd) as AttackData;
            if (a == null) continue;
            a.startupTime     *= StartupScale;
            a.recoveryTime    *= StartupScale;
            a.damage          *= DamageScale;
            a.knockbackBase   *= KnockbackScale;
            a.knockbackGrowth *= KnockbackScale;
        }

        EditorUtility.SetDirty(fd);
        Debug.Log($"[Rogue] ① FD_Rogue 已生成（weight={fd.weight} walk={fd.walkSpeed} run={fd.runSpeed}）");
        return fd;
    }

    // ============================================================
    // ② 骨架 Avatar：P3.asset
    // ============================================================
    private static Avatar Step2_BuildAvatar()
    {
        Avatar p3 = AssetDatabase.LoadAssetAtPath<Avatar>(P3AvatarPath);
        if (p3 != null) { Debug.Log("[Rogue] ② P3.asset 已存在，直接复用"); return p3; }

        if (AssetDatabase.CopyAsset(P1AvatarPath, P3AvatarPath))
            p3 = AssetDatabase.LoadAssetAtPath<Avatar>(P3AvatarPath);

        if (p3 == null)
        {
            // 后备：手动复制对象再落盘
            Avatar p1 = AssetDatabase.LoadAssetAtPath<Avatar>(P1AvatarPath);
            if (p1 != null)
            {
                Avatar dup = Object.Instantiate(p1);
                AssetDatabase.CreateAsset(dup, P3AvatarPath);
                p3 = AssetDatabase.LoadAssetAtPath<Avatar>(P3AvatarPath);
            }
        }

        if (p3 == null)
        {
            Debug.LogWarning("[Rogue] ② P3.asset 生成失败，回退为直接引用 P1.asset");
            p3 = AssetDatabase.LoadAssetAtPath<Avatar>(P1AvatarPath);
        }
        else Debug.Log($"[Rogue] ② P3.asset 就绪（{p3.name}）");
        return p3;
    }

    // ============================================================
    // ③ 资源副本：Synty 原件 → Assets/_Game/Art/
    // ============================================================
    private static GameObject Step3_CopyAsset(string src, string dst)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
        if (existing != null) { Debug.Log($"[Rogue] ③ 复用已有副本 {dst}"); return existing; }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null)
        {
            Debug.LogError($"[Rogue] ③ 源资源不存在：{src}");
            return null;
        }
        if (!AssetDatabase.CopyAsset(src, dst))
        {
            Debug.LogError($"[Rogue] ③ 复制失败：{src} → {dst}");
            return null;
        }
        GameObject copy = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
        Debug.Log($"[Rogue] ③ 已复制 {src} → {dst}");
        return copy;
    }

    // ============================================================
    // ④ 斗士预制体：Fighter_Rogue.prefab（以 Fighter_Knight 为模板）
    // ============================================================
    private static GameObject Step4_BuildFighterPrefab(FighterData rogue, Avatar avatar,
                                                       GameObject modelPrefab, GameObject daggerPrefab)
    {
        if (rogue == null || avatar == null || modelPrefab == null || daggerPrefab == null)
        {
            Debug.LogError("[Rogue] ④ 前置资产缺失，跳过斗士预制体搭建");
            return null;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(RoguePrefabPath) != null)
            AssetDatabase.DeleteAsset(RoguePrefabPath);

        GameObject root = PrefabUtility.LoadPrefabContents(KnightPrefabPath);
        if (root == null) { Debug.LogError($"[Rogue] ④ 无法加载模板 {KnightPrefabPath}"); return null; }

        try
        {
            FighterController fc = root.GetComponent<FighterController>();
            Transform visual = (fc != null && fc.visual != null) ? fc.visual : root.transform.Find("Visual");
            if (visual == null) { Debug.LogError("[Rogue] ④ 模板缺少 Visual 节点"); return null; }

            // 记录旧模型 TRS
            Transform oldModel = visual.childCount > 0 ? visual.GetChild(0) : null;
            if (oldModel == null) { Debug.LogError("[Rogue] ④ Visual 下没有模型实例"); return null; }
            Vector3    mPos   = oldModel.localPosition;
            Quaternion mRot   = oldModel.localRotation;
            Vector3    mScale = oldModel.localScale;

            // 记录旧武器（SM_Wep* 名前缀）→ 父骨骼名 + 局部 TRS
            Transform oldWeapon = FindByNamePrefix(oldModel, "SM_Wep");
            string    boneName = "Hand_R";
            Vector3   wPos = Vector3.zero, wScale = Vector3.one;
            Quaternion wRot = Quaternion.identity;
            if (oldWeapon != null)
            {
                if (oldWeapon.parent != null) boneName = oldWeapon.parent.name;
                wPos = oldWeapon.localPosition; wRot = oldWeapon.localRotation; wScale = oldWeapon.localScale;
                Debug.Log($"[Rogue] ④ 旧武器：{oldWeapon.name} @ {boneName}");
            }
            else Debug.LogWarning("[Rogue] ④ 旧模型未找到 SM_Wep* 武器，使用 Hand_R 默认挂点");

            // 换模型：销毁旧实例 → 实例化暗精灵副本
            Object.DestroyImmediate(oldModel.gameObject);
            GameObject newModel = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, visual);
            newModel.name = modelPrefab.name;
            newModel.transform.SetSiblingIndex(0);
            newModel.transform.localPosition = mPos;
            newModel.transform.localRotation = mRot;
            newModel.transform.localScale    = mScale;

            // 移除模型自带武器（避免与下面挂的匕首重复）
            Transform[] modelChildren = newModel.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in modelChildren)
            {
                if (t == null) continue;
                if (t.name.StartsWith("SM_Wep"))
                {
                    Debug.Log($"[Rogue] ④ 移除模型自带武器 {t.name}");
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            // 移除模型自带 Animator（否则与 Visual 上的斗士 Animator 抢驱动）
            foreach (Animator a in newModel.GetComponentsInChildren<Animator>(true))
                if (a != null) Object.DestroyImmediate(a);

            // 换武器：挂到新模型对应骨骼
            Transform bone = FindDeep(newModel.transform, boneName);
            if (bone == null) bone = FindDeep(newModel.transform, "Hand_R");
            if (bone != null)
            {
                GameObject weapon = (GameObject)PrefabUtility.InstantiatePrefab(daggerPrefab, bone);
                weapon.name = daggerPrefab.name;
                weapon.transform.localPosition = wPos;
                weapon.transform.localRotation = wRot;
                weapon.transform.localScale    = wScale;
                Debug.Log($"[Rogue] ④ 匕首已挂到骨骼 {bone.name}");
            }
            else Debug.LogWarning($"[Rogue] ④ 新模型找不到骨骼 {boneName}/Hand_R，未挂武器");

            // 头像 / 角色数据 / 判定框
            Animator visualAnim = visual.GetComponent<Animator>();
            if (visualAnim != null) visualAnim.avatar = avatar;
            else Debug.LogWarning("[Rogue] ④ Visual 上没有 Animator，头像未设置");

            if (fc != null) fc.fighterData = rogue;
            foreach (HitboxPreview hp in root.GetComponentsInChildren<HitboxPreview>(true))
                hp.data = rogue;

            root.name = "Fighter_Rogue";

            PrefabUtility.SaveAsPrefabAsset(root, RoguePrefabPath);
            Debug.Log("[Rogue] ④ Fighter_Rogue.prefab 已生成");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(RoguePrefabPath);
    }

    // ============================================================
    // ⑤ 注册 + 映射 + 校验
    // ============================================================
    private static void Step5_RegisterAndVerify(FighterData rogue, GameObject roguePrefab)
    {
        GameSettings gs = AssetDatabase.LoadAssetAtPath<GameSettings>(GsPath);
        if (gs == null) { Debug.LogError($"[Rogue] ⑤ 找不到 {GsPath}"); return; }

        // 注册可选角色
        if (gs.selectableFighters == null) gs.selectableFighters = new List<FighterData>();
        gs.selectableFighters.RemoveAll(x => x == null);
        if (rogue != null && !gs.selectableFighters.Contains(rogue))
            gs.selectableFighters.Add(rogue);
        EditorUtility.SetDirty(gs);

        // 回填 fighterPrefab 映射
        SetFighterPrefab(FdKnightPath, KnightPrefabPath);
        SetFighterPrefab(FdBarbarianPath, BarbarianPrefabPath);
        SetFighterPrefab(FdRoguePath, RoguePrefabPath);

        // ---- 校验 ----
        Debug.Log("[Rogue] ===== 校验 =====");
        Check(AssetDatabase.LoadAssetAtPath<FighterData>(FdRoguePath) != null, "FD_Rogue.asset 存在");
        Check(AssetDatabase.LoadAssetAtPath<Avatar>(P3AvatarPath) != null, "P3.asset 存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(ModelDstPath) != null, "暗精灵模型副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(DaggerDstPath) != null, "匕首副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(RoguePrefabPath) != null, "Fighter_Rogue.prefab 存在");
        Check(gs.selectableFighters.Count == 5, $"GS 可选角色数 = {gs.selectableFighters.Count}（期望 5）");

        FighterData fdCheck = AssetDatabase.LoadAssetAtPath<FighterData>(FdRoguePath);
        if (fdCheck != null)
        {
            Check(fdCheck.weight == 80f && fdCheck.walkSpeed == 7f && fdCheck.runSpeed == 10f, "游侠移动数值正确");
            Check(fdCheck.fighterPrefab != null, "FD_Rogue.fighterPrefab 已回填");
        }

        GameObject prefabCheck = AssetDatabase.LoadAssetAtPath<GameObject>(RoguePrefabPath);
        if (prefabCheck != null)
        {
            FighterController fc = prefabCheck.GetComponent<FighterController>();
            Check(fc != null && fc.fighterData == rogue, "Fighter_Rogue.fighterData = FD_Rogue");
            Transform visual = (fc != null && fc.visual != null) ? fc.visual : prefabCheck.transform.Find("Visual");
            Check(visual != null && visual.childCount > 0 && visual.GetChild(0).name.Contains("DarkElf"), "Visual 下已换成暗精灵模型");
            Animator av = visual != null ? visual.GetComponent<Animator>() : null;
            Check(av != null && av.avatar != null, "Visual Animator 头像有效");
            Transform hand = visual != null ? FindDeep(visual, "Hand_R") : null;
            Check(hand != null && FindByNamePrefix(hand, "SM_Wep") != null, "匕首已挂在 Hand_R");
        }
    }

    private static void SetFighterPrefab(string fdPath, string prefabPath)
    {
        FighterData fd = AssetDatabase.LoadAssetAtPath<FighterData>(fdPath);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (fd == null)     { Debug.LogWarning($"[Rogue] ⑤ 找不到 {fdPath}"); return; }
        if (prefab == null) { Debug.LogWarning($"[Rogue] ⑤ 找不到 {prefabPath}"); return; }
        fd.fighterPrefab = prefab;
        EditorUtility.SetDirty(fd);
    }

    private static void Check(bool ok, string label)
    {
        if (ok) Debug.Log($"[Rogue] OK - {label}");
        else    Debug.LogError($"[Rogue] FAIL - {label}");
    }

    // ---- 递归查找工具 ----
    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform r = FindDeep(parent.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    private static Transform FindByNamePrefix(Transform parent, string prefix)
    {
        if (parent.name.StartsWith(prefix)) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform r = FindByNamePrefix(parent.GetChild(i), prefix);
            if (r != null) return r;
        }
        return null;
    }
}
