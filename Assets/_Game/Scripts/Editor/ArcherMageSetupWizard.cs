using System.Reflection;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// ArcherMageSetupWizard — M6-D3b：一键搭建"弓箭手（Archer）/ 法师（Mage）"角色资产
// 使用：菜单 SuperSmashLike → 44. Setup Archer & Mage (M6-D3)
//      或 -batchmode -executeMethod ArcherMageSetupWizard.Run
//
// 步骤（幂等：Avatar / 资源副本复用已有，斗士预制体每次重建）：
//   ① P5.asset / P6.asset ：复制 P1.asset 生成两个角色的骨架 Avatar
//   ② 资源副本：Mystic 模型+法杖（新复制）；AncientQueen 模型+武器（已有副本复用）
//   ③ Fighter_Archer.prefab：模板 Fighter_P1 → 换 AncientQueen 模型 + 女王武器挂 Hand_R
//   ④ Fighter_Mage.prefab  ：模板 Fighter_P1 → 换 Mystic 模型 + 法杖挂 Hand_R
//   ⑤ 回填 FD_Archer / FD_Mage.fighterPrefab + 校验（GS 可选 6 人、模型/头像/挂点）
//
// 【选型依据】Docs/12 §11.2：Archer=弓箭·快·远程(特殊) / Mage=法杖·轻击·魔法必杀；
//   Docs/03 §1.1：AncientQueen / Mystic 均为 Rivals 包轻量级角色。
// 【注意】Rivals 包无 3D 弓（仅 UI 图标）→ Archer 武器暂用女王长武器；
//   远程必杀弹（箭矢）已由 AttackTableWizard 配好（Arrow_Projectile）→ 弓模型缺失记入待办清单。
// 【注意】武器挂载约定：zero 局部位置 + 继承模板武器旋转（X-90°）+ 缩放 1（与 Paladin 一致）。
// ============================================================
public static class ArcherMageSetupWizard
{
    // ---- 路径常量 ----
    private const string P1AvatarPath = "Assets/_Game/Art/Character/P1.asset";
    private const string P5AvatarPath = "Assets/_Game/Art/Character/P5.asset";
    private const string P6AvatarPath = "Assets/_Game/Art/Character/P6.asset";

    private const string QueenModelSrc   = "Assets/Synty/PolygonFantasyRivals/Prefabs/Characters/SM_Chrr_AncientQueen_01.prefab";
    private const string QueenModelDst   = "Assets/_Game/Art/Character/SM_Chrr_AncientQueen_01.prefab";
    private const string QueenWeaponSrc  = "Assets/Synty/PolygonFantasyRivals/Prefabs/Weapons/SM_Wep_AncientQueen_01.prefab";
    private const string QueenWeaponDst  = "Assets/_Game/Art/Weapon/SM_Wep_AncientQueen_01.prefab";
    private const string MysticModelSrc  = "Assets/Synty/PolygonFantasyRivals/Prefabs/Characters/SM_Chr_Mystic_01.prefab";
    private const string MysticModelDst  = "Assets/_Game/Art/Character/SM_Chr_Mystic_01.prefab";
    private const string MysticWeaponSrc = "Assets/Synty/PolygonFantasyRivals/Prefabs/Weapons/SM_Wep_Mystic_01.prefab";
    private const string MysticWeaponDst = "Assets/_Game/Art/Weapon/SM_Wep_Mystic_01.prefab";

    private const string P1PrefabPath      = "Assets/_Game/Prefabs/Fighters/Fighter_P1.prefab";
    private const string P2PrefabPath      = "Assets/_Game/Prefabs/Fighters/Fighter_P2.prefab";
    private const string RoguePrefabPath   = "Assets/_Game/Prefabs/Fighters/Fighter_Rogue.prefab";
    private const string PaladinPrefabPath = "Assets/_Game/Prefabs/Fighters/Fighter_Paladin.prefab";
    private const string ArcherPrefabPath  = "Assets/_Game/Prefabs/Fighters/Fighter_Archer.prefab";
    private const string MagePrefabPath    = "Assets/_Game/Prefabs/Fighters/Fighter_Mage.prefab";

    private const string FdDir = "Assets/_Game/ScriptableObjects/Characters/";
    private const string GsPath = "Assets/_Game/ScriptableObjects/GameModes/GS_Default.asset";

    [MenuItem("SuperSmashLike/44. Setup Archer & Mage (M6-D3)", false, 44)]
    public static void Run()
    {
        Debug.Log("[AM] ===== 开始搭建 Archer & Mage 资产 =====");

        FighterData archer = LoadFd("FD_Archer");
        FighterData mage   = LoadFd("FD_Mage");
        if (archer == null || mage == null)
        {
            Debug.LogError("[AM] FD_Archer / FD_Mage 缺失，中止");
            return;
        }

        Avatar p5 = Step1_BuildAvatar(P5AvatarPath, "P5");
        Avatar p6 = Step1_BuildAvatar(P6AvatarPath, "P6");

        GameObject queenModel   = Step2_CopyAsset(QueenModelSrc,   QueenModelDst);
        GameObject queenWeapon  = Step2_CopyAsset(QueenWeaponSrc,  QueenWeaponDst);
        GameObject mysticModel  = Step2_CopyAsset(MysticModelSrc,  MysticModelDst);
        GameObject mysticWeapon = Step2_CopyAsset(MysticWeaponSrc, MysticWeaponDst);

        GameObject archerPrefab = Step3_BuildFighterPrefab("Fighter_Archer", ArcherPrefabPath,
            archer, p5, queenModel, queenWeapon, "AncientQueen");
        GameObject magePrefab = Step3_BuildFighterPrefab("Fighter_Mage", MagePrefabPath,
            mage, p6, mysticModel, mysticWeapon, "Mystic");

        Step4_BackfillAndVerify(archer, archerPrefab, mage, magePrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AM] ===== 搭建结束 =====");
    }

    // ============================================================
    // ① 骨架 Avatar：P5.asset / P6.asset
    // ============================================================
    private static Avatar Step1_BuildAvatar(string dstPath, string tag)
    {
        Avatar av = AssetDatabase.LoadAssetAtPath<Avatar>(dstPath);
        if (av != null) { Debug.Log($"[AM] ① {tag}.asset 已存在，直接复用"); return av; }

        if (AssetDatabase.CopyAsset(P1AvatarPath, dstPath))
            av = AssetDatabase.LoadAssetAtPath<Avatar>(dstPath);

        if (av == null)
        {
            Avatar p1 = AssetDatabase.LoadAssetAtPath<Avatar>(P1AvatarPath);
            if (p1 != null)
            {
                Avatar dup = Object.Instantiate(p1);
                AssetDatabase.CreateAsset(dup, dstPath);
                av = AssetDatabase.LoadAssetAtPath<Avatar>(dstPath);
            }
        }

        if (av == null)
        {
            Debug.LogWarning($"[AM] ① {tag}.asset 生成失败，回退为直接引用 P1.asset");
            av = AssetDatabase.LoadAssetAtPath<Avatar>(P1AvatarPath);
        }
        else Debug.Log($"[AM] ① {tag}.asset 就绪（{av.name}）");
        return av;
    }

    // ============================================================
    // ② 资源副本：Synty 原件 → Assets/_Game/Art/
    // ============================================================
    private static GameObject Step2_CopyAsset(string src, string dst)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
        if (existing != null) { Debug.Log($"[AM] ② 复用已有副本 {dst}"); return existing; }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null)
        {
            Debug.LogError($"[AM] ② 源资源不存在：{src}");
            return null;
        }
        if (!AssetDatabase.CopyAsset(src, dst))
        {
            Debug.LogError($"[AM] ② 复制失败：{src} → {dst}");
            return null;
        }
        GameObject copy = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
        Debug.Log($"[AM] ② 已复制 {src} → {dst}");
        return copy;
    }

    // ============================================================
    // ③④ 斗士预制体：以 Fighter_P1 为模板（单武器挂 Hand_R）
    // ============================================================
    private static GameObject Step3_BuildFighterPrefab(string rootName, string prefabPath,
        FighterData fd, Avatar avatar, GameObject modelPrefab, GameObject weaponPrefab, string modelNameHint)
    {
        if (fd == null || avatar == null || modelPrefab == null || weaponPrefab == null)
        {
            Debug.LogError($"[AM] {rootName} 前置资产缺失，跳过搭建");
            return null;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            AssetDatabase.DeleteAsset(prefabPath);

        GameObject root = PrefabUtility.LoadPrefabContents(P1PrefabPath);
        if (root == null) { Debug.LogError($"[AM] {rootName} 无法加载模板 {P1PrefabPath}"); return null; }

        try
        {
            FighterController fc = root.GetComponent<FighterController>();
            Transform visual = (fc != null && fc.visual != null) ? fc.visual : root.transform.Find("Visual");
            if (visual == null) { Debug.LogError($"[AM] {rootName} 模板缺少 Visual 节点"); return null; }

            // 记录旧模型 TRS
            Transform oldModel = visual.childCount > 0 ? visual.GetChild(0) : null;
            if (oldModel == null) { Debug.LogError($"[AM] {rootName} Visual 下没有模型实例"); return null; }
            Vector3 mPos = oldModel.localPosition;
            Quaternion mRot = oldModel.localRotation;
            Vector3 mScale = oldModel.localScale;

            // 记录旧武器（SM_Wep* 前缀）→ 父骨骼名 + 局部旋转
            Transform oldWeapon = FindByNamePrefix(oldModel, "SM_Wep");
            string boneName = "Hand_R";
            Quaternion wRot = Quaternion.identity;
            if (oldWeapon != null)
            {
                if (oldWeapon.parent != null) boneName = oldWeapon.parent.name;
                wRot = oldWeapon.localRotation;
                Debug.Log($"[AM] {rootName} 旧武器：{oldWeapon.name} @ {boneName}（旋转 {wRot.eulerAngles}）");
            }
            else Debug.LogWarning($"[AM] {rootName} 旧模型未找到 SM_Wep* 武器，使用 Hand_R 默认挂点");

            // 换模型：销毁旧实例 → 实例化新副本
            Object.DestroyImmediate(oldModel.gameObject);
            GameObject newModel = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, visual);
            newModel.name = modelPrefab.name;
            newModel.transform.SetSiblingIndex(0);
            newModel.transform.localPosition = mPos;
            newModel.transform.localRotation = mRot;
            newModel.transform.localScale = mScale;

            // 移除模型自带武器（避免与新挂武器重复）
            foreach (Transform t in newModel.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.name.StartsWith("SM_Wep"))
                {
                    Debug.Log($"[AM] {rootName} 移除模型自带武器 {t.name}");
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            // 移除模型自带 Animator
            foreach (Animator a in newModel.GetComponentsInChildren<Animator>(true))
                if (a != null) Object.DestroyImmediate(a);

            // 武器挂右手（约定：zero 位置 + 继承模板旋转 + 缩放 1）
            Transform rightHand = FindHandBone(newModel, boneName);
            if (rightHand == null) rightHand = FindHandBone(newModel, "Hand_R");
            AttachProp(weaponPrefab, rightHand, Vector3.zero, wRot, Vector3.one, $"{rootName} 武器");

            // 头像 / 角色数据 / 判定框
            Animator visualAnim = visual.GetComponent<Animator>();
            if (visualAnim != null) visualAnim.avatar = avatar;
            else Debug.LogWarning($"[AM] {rootName} Visual 上没有 Animator，头像未设置");

            if (fc != null) fc.fighterData = fd;
            foreach (HitboxPreview hp in root.GetComponentsInChildren<HitboxPreview>(true))
                hp.data = fd;

            root.name = rootName;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[AM] {rootName}.prefab 已生成（模型={modelNameHint}）");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static void AttachProp(GameObject prop, Transform bone, Vector3 pos, Quaternion rot, Vector3 scale, string label)
    {
        if (bone == null) { Debug.LogWarning($"[AM] 找不到挂点，{label} 未挂载"); return; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prop, bone);
        go.name = prop.name;
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        Debug.Log($"[AM] {label} {prop.name} 已挂到骨骼 {bone.name}");
    }

    // ============================================================
    // ⑤ 回填 + 校验
    // ============================================================
    private static void Step4_BackfillAndVerify(FighterData archer, GameObject archerPrefab,
                                                FighterData mage, GameObject magePrefab)
    {
        // 回填 fighterPrefab（六角色全量对齐，幂等）
        SetFighterPrefab("FD_Knight", P1PrefabPath);
        SetFighterPrefab("FD_Barbarian", P2PrefabPath);
        SetFighterPrefab("FD_Rogue", RoguePrefabPath);
        SetFighterPrefab("FD_Paladin", PaladinPrefabPath);
        SetFighterPrefab("FD_Archer", ArcherPrefabPath);
        SetFighterPrefab("FD_Mage", MagePrefabPath);

        GameSettings gs = AssetDatabase.LoadAssetAtPath<GameSettings>(GsPath);
        if (gs != null)
        {
            if (gs.selectableFighters == null) gs.selectableFighters = new System.Collections.Generic.List<FighterData>();
            gs.selectableFighters.RemoveAll(x => x == null);
            if (archer != null && !gs.selectableFighters.Contains(archer)) gs.selectableFighters.Add(archer);
            if (mage != null && !gs.selectableFighters.Contains(mage)) gs.selectableFighters.Add(mage);
            EditorUtility.SetDirty(gs);
        }

        // ---- 校验 ----
        Debug.Log("[AM] ===== 校验 =====");
        Check(AssetDatabase.LoadAssetAtPath<Avatar>(P5AvatarPath) != null, "P5.asset 存在");
        Check(AssetDatabase.LoadAssetAtPath<Avatar>(P6AvatarPath) != null, "P6.asset 存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(QueenModelDst) != null, "AncientQueen 模型副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(QueenWeaponDst) != null, "AncientQueen 武器副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(MysticModelDst) != null, "Mystic 模型副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(MysticWeaponDst) != null, "Mystic 武器副本存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(ArcherPrefabPath) != null, "Fighter_Archer.prefab 存在");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(MagePrefabPath) != null, "Fighter_Mage.prefab 存在");
        Check(gs != null && gs.selectableFighters != null && gs.selectableFighters.Count == 6,
              $"GS 可选角色数 = {(gs != null && gs.selectableFighters != null ? gs.selectableFighters.Count : -1)}（期望 6）");

        VerifyPrefab("Archer", ArcherPrefabPath, archer, archerPrefab, "AncientQueen");
        VerifyPrefab("Mage", MagePrefabPath, mage, magePrefab, "Mystic");

        FighterData fdA = LoadFd("FD_Archer");
        FighterData fdM = LoadFd("FD_Mage");
        Check(fdA != null && fdA.fighterPrefab != null, "FD_Archer.fighterPrefab 已回填");
        Check(fdM != null && fdM.fighterPrefab != null, "FD_Mage.fighterPrefab 已回填");
    }

    private static void VerifyPrefab(string tag, string prefabPath, FighterData fd, GameObject builtPrefab, string modelHint)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) { Check(false, $"{tag} prefab 加载失败"); return; }

        FighterController fc = prefab.GetComponent<FighterController>();
        Check(fc != null && fc.fighterData == fd, $"Fighter_{tag}.fighterData = FD_{tag}");
        Transform visual = (fc != null && fc.visual != null) ? fc.visual : prefab.transform.Find("Visual");
        Check(visual != null && visual.childCount > 0 && visual.GetChild(0).name.Contains(modelHint),
              $"Visual 下已换成 {modelHint} 模型");
        Animator av = visual != null ? visual.GetComponent<Animator>() : null;
        Check(av != null && av.avatar != null, $"{tag} Visual Animator 头像有效");
        Transform rh = visual != null ? FindDeep(visual, "Hand_R") : null;
        Check(rh != null && FindByNamePrefix(rh, "SM_Wep") != null, $"{tag} 武器已挂在 Hand_R");
        Check(prefab.transform.Find("ShieldVisual") != null, $"{tag} ShieldVisual 节点保留（举盾表现层）");
    }

    private static void SetFighterPrefab(string fdName, string prefabPath)
    {
        FighterData fd = LoadFd(fdName);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (fd == null) { Debug.LogWarning($"[AM] ⑤ 找不到 FD_{fdName}"); return; }
        if (prefab == null) { Debug.LogWarning($"[AM] ⑤ 找不到 {prefabPath}"); return; }
        fd.fighterPrefab = prefab;
        EditorUtility.SetDirty(fd);
    }

    private static FighterData LoadFd(string fdName)
        => AssetDatabase.LoadAssetAtPath<FighterData>(FdDir + fdName + ".asset");

    private static void Check(bool ok, string label)
    {
        if (ok) Debug.Log($"[AM] OK - {label}");
        else Debug.LogError($"[AM] FAIL - {label}");
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

    // 【重点】共享骨架 prefab 含多套角色网格，骨骼名可能重复；优先在"激活的蒙皮渲染器"
    //   骨骼表里找，防止武器挂到未激活那套的骨头上。
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
