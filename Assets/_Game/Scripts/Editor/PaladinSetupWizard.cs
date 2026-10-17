using System.Reflection;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// PaladinSetupWizard — M6-D2：一键搭建"圣骑士（Paladin）"角色全套资产
// 使用：菜单 SuperSmashLike → 42. Setup Paladin (M6-D2)
//      或 -batchmode -executeMethod PaladinSetupWizard.Run
//
// 五个步骤（可重复执行，幂等：每次从干净的 FD_Knight / Fighter_Knight 重建）：
//   ① FD_Paladin.asset     ：105 / 4.8 / 7.5 / 5.5 / 跳 11 —— 均衡型"剑盾"斗士；
//                            盾耐倍率 1.35（盾耐略高）；特攻走"盾击"：对盾伤害 ×1.6
//   ② P4.asset             ：复制 P1.asset 生成圣骑士骨架 Avatar
//   ③ 资源副本             ：森林守护者模型 + 骑士盾（取自 PolygonDungeon）+ 细刃剑 → Assets/_Game/Art/
//   ④ Fighter_Paladin.prefab：复制 Fighter_Knight 结构 → 换模型 / 左手盾 / 右手剑 / 头像 / 数据 / 判定框
//   ⑤ GS_Default 注册 + 回填各 FD 的 fighterPrefab 映射 + 自校验
//
// 【设计依据】Docs/12_逐日实施清单.md §11.2 角色差异化参数模板：
//   Paladin = weight 105 / walk 4.8 / run 7.5 / air 5.5 / jump 11 / jumpCount 2 / 特色"防御+盾耐"
// 【选型依据】Docs/03_美术资源手册.md §1.1：ForestGuardian = 防守型（中量）→ 圣骑士
// 【注意】Synty PolygonFantasyRivals 无盾、无弓 3D 模型；盾改用 PolygonDungeon（11 面盾），
//   与工程既有做法一致（SM_Wep_Banner_04 亦取自该包）。
// ============================================================
public static class PaladinSetupWizard
{
    // ---- 路径常量 ----
    private const string FdKnightPath   = "Assets/_Game/ScriptableObjects/Characters/FD_Knight.asset";
    private const string FdPaladinPath  = "Assets/_Game/ScriptableObjects/Characters/FD_Paladin.asset";
    private const string FdBarbarianPath= "Assets/_Game/ScriptableObjects/Characters/FD_Barbarian.asset";
    private const string FdRoguePath    = "Assets/_Game/ScriptableObjects/Characters/FD_Rogue.asset";
    private const string P1AvatarPath   = "Assets/_Game/Art/Character/P1.asset";
    private const string P4AvatarPath   = "Assets/_Game/Art/Character/P4.asset";

    private const string ModelSrcPath   = "Assets/Synty/PolygonFantasyRivals/Prefabs/Characters/SM_Chr_ForestGuardian_01.prefab";
    private const string ModelDstPath   = "Assets/_Game/Art/Character/SM_Chr_ForestGuardian_01.prefab";
    private const string SwordSrcPath   = "Assets/Synty/PolygonFantasyRivals/Prefabs/Weapons/SM_Wep_Medusa_01.prefab";
    private const string SwordDstPath   = "Assets/_Game/Art/Weapon/SM_Wep_Medusa_01.prefab";
    private const string ShieldSrcPath  = "Assets/Synty/PolygonDungeon/Prefabs/Weapons/SM_Wep_Shield_Heater_01.prefab";
    private const string ShieldDstPath  = "Assets/_Game/Art/Weapon/SM_Wep_Shield_Heater_01.prefab";

    private const string KnightPrefabPath     = "Assets/_Game/Prefabs/Fighters/Fighter_Knight.prefab";
    private const string BarbarianPrefabPath  = "Assets/_Game/Prefabs/Fighters/Fighter_Barbarian.prefab";
    private const string RoguePrefabPath  = "Assets/_Game/Prefabs/Fighters/Fighter_Rogue.prefab";
    private const string PaladinPrefabPath= "Assets/_Game/Prefabs/Fighters/Fighter_Paladin.prefab";
    private const string GsPath           = "Assets/_Game/ScriptableObjects/GameModes/GS_Default.asset";

    // ---- 圣骑士数值（相对剑士的缩放系数）----
    private const float StartupScale   = 0.90f;   // 前摇/后摇（剑盾比巨剑快一档）
    private const float DamageScale    = 0.90f;   // 伤害
    private const float KnockbackScale = 0.90f;   // 基础击飞 + 成长率
    private const float ShieldDamageMult = 1.60f; // 对盾伤害（圣骑士特色：盾击）
    private const float ShieldHPMult     = 1.35f; // 自身盾耐倍率（盾耐略高）

    [MenuItem("SuperSmashLike/42. Setup Paladin (M6-D2)", false, 42)]
    public static void Run()
    {
        Debug.Log("[Paladin] ===== 开始搭建圣骑士（Paladin）资产 =====");

        FighterData paladin      = Step1_BuildFighterData();
        Avatar      avatar       = Step2_BuildAvatar();
        GameObject  modelPrefab  = Step3_CopyAsset(ModelSrcPath,  ModelDstPath);
        GameObject  swordPrefab  = Step3_CopyAsset(SwordSrcPath,  SwordDstPath);
        GameObject  shieldPrefab = Step3_CopyAsset(ShieldSrcPath, ShieldDstPath);
        GameObject  paladinPrefab= Step4_BuildFighterPrefab(paladin, avatar, modelPrefab, swordPrefab, shieldPrefab);
        Step5_RegisterAndVerify(paladin, paladinPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Paladin] ===== 搭建结束 =====");
    }

    // ============================================================
    // ① 角色数据：FD_Paladin.asset
    // ============================================================
    private static FighterData Step1_BuildFighterData()
    {
        // 先删后建：数值缩放是乘法，必须每次从干净的 FD_Knight 克隆
        if (AssetDatabase.LoadAssetAtPath<FighterData>(FdPaladinPath) != null)
            AssetDatabase.DeleteAsset(FdPaladinPath);

        if (!AssetDatabase.CopyAsset(FdKnightPath, FdPaladinPath))
        {
            Debug.LogError($"[Paladin] ① 复制 {FdKnightPath} → {FdPaladinPath} 失败");
            return null;
        }

        FighterData fd = AssetDatabase.LoadAssetAtPath<FighterData>(FdPaladinPath);
        if (fd == null) { Debug.LogError("[Paladin] ① FD_Paladin 加载失败"); return null; }

        // 身份（金色主题：与骑士红 / 法师黄错开）
        fd.fighterName  = "圣骑士";
        fd.portraitIcon = null;                                 // 头像留到 D3 补
        fd.uiColor      = new Color(1f, 0.60f, 0.15f, 1f);

        // 移动：剑盾均衡型（Docs/12 §11.2）
        fd.weightClass = WeightClass.Medium;
        fd.weight      = 105f;
        fd.walkSpeed   = 4.8f;
        fd.runSpeed    = 7.5f;
        fd.airSpeed    = 5.5f;
        fd.jumpForce   = 11f;
        fd.jumpCount   = 2;

        // 防御特色：盾耐略高（FighterController.ResolveMaxShieldHP 消费）
        fd.shieldMaxHPMultiplier = ShieldHPMult;

        // 攻击：23 槽整体微调 + 盾击特性
        int tuned = 0;
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
            a.shieldDamage    *= ShieldDamageMult;   // 【圣骑士特色】打盾特别疼
            tuned++;
        }

        EditorUtility.SetDirty(fd);
        Debug.Log($"[Paladin] ① FD_Paladin 已生成（weight={fd.weight} walk={fd.walkSpeed} run={fd.runSpeed} " +
                  $"air={fd.airSpeed} jump={fd.jumpForce} 盾耐×{fd.shieldMaxHPMultiplier} 攻击槽={tuned}）");
        return fd;
    }

    // ============================================================
    // ② 骨架 Avatar：P4.asset
    // ============================================================
    private static Avatar Step2_BuildAvatar()
    {
        Avatar p4 = AssetDatabase.LoadAssetAtPath<Avatar>(P4AvatarPath);
        if (p4 != null) { Debug.Log("[Paladin] ② P4.asset 已存在，直接复用"); return p4; }

        if (AssetDatabase.CopyAsset(P1AvatarPath, P4AvatarPath))
            p4 = AssetDatabase.LoadAssetAtPath<Avatar>(P4AvatarPath);

        if (p4 == null)
        {
            Avatar p1 = AssetDatabase.LoadAssetAtPath<Avatar>(P1AvatarPath);
            if (p1 != null)
            {
                Avatar dup = Object.Instantiate(p1);
                AssetDatabase.CreateAsset(dup, P4AvatarPath);
                p4 = AssetDatabase.LoadAssetAtPath<Avatar>(P4AvatarPath);
            }
        }

        if (p4 == null)
        {
            Debug.LogWarning("[Paladin] ② P4.asset 生成失败，回退为直接引用 P1.asset");
            p4 = AssetDatabase.LoadAssetAtPath<Avatar>(P1AvatarPath);
        }
        else Debug.Log($"[Paladin] ② P4.asset 就绪（{p4.name}）");
        return p4;
    }

    // ============================================================
    // ③ 资源副本：Synty 原件 → Assets/_Game/Art/
    // ============================================================
    private static GameObject Step3_CopyAsset(string src, string dst)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
        if (existing != null) { Debug.Log($"[Paladin] ③ 复用已有副本 {dst}"); return existing; }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null)
        {
            Debug.LogError($"[Paladin] ③ 源资源不存在：{src}");
            return null;
        }
        if (!AssetDatabase.CopyAsset(src, dst))
        {
            Debug.LogError($"[Paladin] ③ 复制失败：{src} → {dst}");
            return null;
        }
        GameObject copy = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
        Debug.Log($"[Paladin] ③ 已复制 {src} → {dst}");
        return copy;
    }

    // ============================================================
    // ④ 斗士预制体：Fighter_Paladin.prefab（以 Fighter_Knight 为模板）
    // ============================================================
    private static GameObject Step4_BuildFighterPrefab(FighterData paladin, Avatar avatar,
                                                       GameObject modelPrefab, GameObject swordPrefab,
                                                       GameObject shieldPrefab)
    {
        if (paladin == null || avatar == null || modelPrefab == null || swordPrefab == null || shieldPrefab == null)
        {
            Debug.LogError("[Paladin] ④ 前置资产缺失，跳过斗士预制体搭建");
            return null;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefabPath) != null)
            AssetDatabase.DeleteAsset(PaladinPrefabPath);

        GameObject root = PrefabUtility.LoadPrefabContents(KnightPrefabPath);
        if (root == null) { Debug.LogError($"[Paladin] ④ 无法加载模板 {KnightPrefabPath}"); return null; }

        try
        {
            FighterController fc = root.GetComponent<FighterController>();
            Transform visual = (fc != null && fc.visual != null) ? fc.visual : root.transform.Find("Visual");
            if (visual == null) { Debug.LogError("[Paladin] ④ 模板缺少 Visual 节点"); return null; }

            // 记录旧模型 TRS
            Transform oldModel = visual.childCount > 0 ? visual.GetChild(0) : null;
            if (oldModel == null) { Debug.LogError("[Paladin] ④ Visual 下没有模型实例"); return null; }
            Vector3    mPos   = oldModel.localPosition;
            Quaternion mRot   = oldModel.localRotation;
            Vector3    mScale = oldModel.localScale;

            // 记录旧武器（SM_Wep* 前缀）→ 父骨骼名 + 局部 TRS
            Transform oldWeapon = FindByNamePrefix(oldModel, "SM_Wep");
            string    boneName = "Hand_R";
            Vector3   wPos = Vector3.zero, wScale = Vector3.one;
            Quaternion wRot = Quaternion.identity;
            if (oldWeapon != null)
            {
                if (oldWeapon.parent != null) boneName = oldWeapon.parent.name;
                wPos = oldWeapon.localPosition; wRot = oldWeapon.localRotation; wScale = oldWeapon.localScale;
                Debug.Log($"[Paladin] ④ 旧武器：{oldWeapon.name} @ {boneName}");
            }
            else Debug.LogWarning("[Paladin] ④ 旧模型未找到 SM_Wep* 武器，使用 Hand_R 默认挂点");

            // 换模型：销毁旧实例 → 实例化森林守护者副本
            Object.DestroyImmediate(oldModel.gameObject);
            GameObject newModel = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, visual);
            newModel.name = modelPrefab.name;
            newModel.transform.SetSiblingIndex(0);
            newModel.transform.localPosition = mPos;
            newModel.transform.localRotation = mRot;
            newModel.transform.localScale    = mScale;

            // 移除模型自带武器（避免与下面挂的剑/盾重复）
            foreach (Transform t in newModel.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.name.StartsWith("SM_Wep"))
                {
                    Debug.Log($"[Paladin] ④ 移除模型自带武器 {t.name}");
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            // 移除模型自带 Animator（否则与 Visual 上的斗士 Animator 抢驱动）
            foreach (Animator a in newModel.GetComponentsInChildren<Animator>(true))
                if (a != null) Object.DestroyImmediate(a);

            // 右手剑（沿用模板武器的局部 TRS，即"握住"的姿势）
            Transform rightHand = FindHandBone(newModel, boneName);
            if (rightHand == null) rightHand = FindHandBone(newModel, "Hand_R");
            AttachProp(swordPrefab, rightHand, Vector3.zero, wRot, Vector3.one, "剑");

            // 左手盾（同骨架镜像，沿用同一套 TRS 即为合理持盾姿势）
            Transform leftHand = FindHandBone(newModel, "Hand_L");
            AttachProp(shieldPrefab, leftHand, Vector3.zero, wRot, Vector3.one, "盾");

            // 头像 / 角色数据 / 判定框
            Animator visualAnim = visual.GetComponent<Animator>();
            if (visualAnim != null) visualAnim.avatar = avatar;
            else Debug.LogWarning("[Paladin] ④ Visual 上没有 Animator，头像未设置");

            if (fc != null) fc.fighterData = paladin;
            foreach (HitboxPreview hp in root.GetComponentsInChildren<HitboxPreview>(true))
                hp.data = paladin;

            root.name = "Fighter_Paladin";

            PrefabUtility.SaveAsPrefabAsset(root, PaladinPrefabPath);
            Debug.Log("[Paladin] ④ Fighter_Paladin.prefab 已生成");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefabPath);
    }

    // 把道具挂到骨骼上（bone 为空只告警，不抛异常 —— 保证向导其余步骤照常走完）
    private static void AttachProp(GameObject prop, Transform bone, Vector3 pos, Quaternion rot, Vector3 scale, string label)
    {
        if (bone == null) { Debug.LogWarning($"[Paladin] ④ 找不到挂点，{label} 未挂载"); return; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prop, bone);
        go.name = prop.name;
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale    = scale;
        Debug.Log($"[Paladin] ④ {label} {prop.name} 已挂到骨骼 {bone.name}");
    }

    // ============================================================
    // ⑤ 注册 + 映射 + 校验
    // ============================================================
    private static void Step5_RegisterAndVerify(FighterData paladin, GameObject paladinPrefab)
    {
        GameSettings gs = AssetDatabase.LoadAssetAtPath<GameSettings>(GsPath);
        if (gs == null) { Debug.LogError($"[Paladin] ⑤ 找不到 {GsPath}"); return; }

        // 注册可选角色
        if (gs.selectableFighters == null) gs.selectableFighters = new System.Collections.Generic.List<FighterData>();
        gs.selectableFighters.RemoveAll(x => x == null);
        if (paladin != null && !gs.selectableFighters.Contains(paladin))
            gs.selectableFighters.Add(paladin);
        EditorUtility.SetDirty(gs);

        // 回填 fighterPrefab 映射（换将服务依赖它）
        SetFighterPrefab(FdKnightPath,    KnightPrefabPath);
        SetFighterPrefab(FdBarbarianPath, BarbarianPrefabPath);
        SetFighterPrefab(FdRoguePath,     RoguePrefabPath);
        SetFighterPrefab(FdPaladinPath,   PaladinPrefabPath);

        // ---- 校验 ----
        Debug.Log("[Paladin] ===== 校验 =====");
        Check(AssetDatabase.LoadAssetAtPath<FighterData>(FdPaladinPath) != null, "FD_Paladin.asset 存在");
        Check(AssetDatabase.LoadAssetAtPath<Avatar>(P4AvatarPath) != null, "P4.asset 存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(ModelDstPath) != null, "森林守护者模型副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(SwordDstPath) != null, "剑副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(ShieldDstPath) != null, "盾副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefabPath) != null, "Fighter_Paladin.prefab 存在");
        Check(gs.selectableFighters.Count == 6, $"GS 可选角色数 = {gs.selectableFighters.Count}（期望 6）");

        FighterData fdCheck = AssetDatabase.LoadAssetAtPath<FighterData>(FdPaladinPath);
        if (fdCheck != null)
        {
            Check(fdCheck.weight == 105f && fdCheck.walkSpeed == 4.8f && fdCheck.runSpeed == 7.5f
                  && fdCheck.airSpeed == 5.5f && fdCheck.jumpForce == 11f, "圣骑士移动数值符合 Docs/12 §11.2 模板");
            Check(fdCheck.shieldMaxHPMultiplier == ShieldHPMult, $"盾耐倍率 = {fdCheck.shieldMaxHPMultiplier}（期望 {ShieldHPMult}）");
            Check(fdCheck.fighterPrefab != null, "FD_Paladin.fighterPrefab 已回填");
            Check(!string.IsNullOrEmpty(fdCheck.jab1.attackName), "攻击槽名称已克隆（根运动白名单按名匹配）");
            Check(fdCheck.jab1.shieldDamage > 0f, $"盾击特性生效（jab1 对盾伤害={fdCheck.jab1.shieldDamage:F2}）");
        }

        GameObject prefabCheck = AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefabPath);
        if (prefabCheck != null)
        {
            FighterController fc = prefabCheck.GetComponent<FighterController>();
            Check(fc != null && fc.fighterData == paladin, "Fighter_Paladin.fighterData = FD_Paladin");
            Transform visual = (fc != null && fc.visual != null) ? fc.visual : prefabCheck.transform.Find("Visual");
            Check(visual != null && visual.childCount > 0 && visual.GetChild(0).name.Contains("ForestGuardian"),
                  "Visual 下已换成森林守护者模型");
            Animator av = visual != null ? visual.GetComponent<Animator>() : null;
            Check(av != null && av.avatar != null, "Visual Animator 头像有效");
            Transform rh = visual != null ? FindDeep(visual, "Hand_R") : null;
            Transform lh = visual != null ? FindDeep(visual, "Hand_L") : null;
            Check(rh != null && FindByNamePrefix(rh, "SM_Wep") != null, "剑已挂在 Hand_R");
            Check(lh != null && FindByNamePrefix(lh, "SM_Wep") != null, "盾已挂在 Hand_L");
            Check(prefabCheck.transform.Find("ShieldVisual") != null, "ShieldVisual 节点保留（举盾表现层）");
        }
    }

    private static void SetFighterPrefab(string fdPath, string prefabPath)
    {
        FighterData fd = AssetDatabase.LoadAssetAtPath<FighterData>(fdPath);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (fd == null)     { Debug.LogWarning($"[Paladin] ⑤ 找不到 {fdPath}"); return; }
        if (prefab == null) { Debug.LogWarning($"[Paladin] ⑤ 找不到 {prefabPath}"); return; }
        fd.fighterPrefab = prefab;
        EditorUtility.SetDirty(fd);
    }

    private static void Check(bool ok, string label)
    {
        if (ok) Debug.Log($"[Paladin] OK - {label}");
        else    Debug.LogError($"[Paladin] FAIL - {label}");
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

    // 【重点】共享骨架 prefab 里同一个 prefab 含多套角色网格，骨骼名可能重复；
    //   直接 FindDeep 可能命中"未激活那一套"的骨骼 → 武器挂在看不见的骨头上。
    //   所以优先在【激活的蒙皮渲染器】的骨骼表里找，找不到再退回全树搜索。
    private static Transform FindHandBone(GameObject model, string boneName)
    {
        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || !smr.gameObject.activeInHierarchy || smr.bones == null) continue;
            foreach (Transform b in smr.bones)
                if (b != null && b.name == boneName) return b;
        }
        return FindDeep(model.transform, boneName);
    }
}
