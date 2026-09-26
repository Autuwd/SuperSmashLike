using System.Collections.Generic;
using UnityEngine;

// ============================================================
// FighterData — 角色数据配置（ScriptableObject 资产）
// 职责：
//   1. 集中存储一个角色的所有属性（移动、攻击、视觉）
//   2. 通过 Unity Editor 创建资产文件，可配置不同角色
// 架构位置：Data 层（ScriptableObject 资产）
// 使用方式：
//   右键 → Create → SuperSmashLike → Fighter Data
//   创建后拖到 FighterController.fighterData 字段
//
// 【重点】AttackData 是 class（引用类型），不是 struct！
//   本资产里的 23 个 AttackData 槽位是"共享实例"，
//   任何要改数值的地方（如蓄力加成）必须先 CloneAttackData 深拷贝，
//   否则会永久污染本资产。
// ============================================================
namespace SuperSmashLike.Core
{
    [CreateAssetMenu(menuName = "SuperSmashLike/Fighter Data", fileName = "FD_NewFighter")]
    public class FighterData : ScriptableObject
    {
        #region 1. 身份

        [Header("Identity")]
        public string fighterName = "New Fighter";   // 角色显示名称
        public Sprite portraitIcon;                  // 角色头像（选人界面用）

        #endregion

        #region 2. 移动参数

        [Header("Movement")]
        public float weight = 100f;          // 体重（越大越难被击飞，参与击飞公式）
        public float walkSpeed = 5f;         // 地面行走速度
        public float runSpeed = 8f;          // 地面冲刺速度（双击方向键触发）
        public float airSpeed = 6f;          // 空中水平速度
        public float jumpForce = 12f;        // 一段跳初速度
        public float doubleJumpForce = 9f;   // 二段跳初速度（当前未被使用，代码里是 jumpForce*0.85）
        public int jumpCount = 2;            // 可跳跃次数（大乱斗通常 2）
        public int airDodgeCount = 1;        // 空中闪避次数（空中闪避功能未实现）
        public float fallSpeed = 8f;         // 下落速度上限
        public float fastFallSpeed = 15f;    // 速降速度（Fall 中按住 ↓ 时使用）

        #endregion

        #region 3. 视觉

        [Header("Visual")]
        public WeightClass weightClass = WeightClass.Medium;  // 体重分类（当前仅 Editor 显示用）
        public Color uiColor = Color.white;                   // UI 主题色

        #endregion

        #region 4. 攻击配置

        // 大乱斗攻击体系：
        //   地面: Jab(轻击3段) / Tilt(强攻击3方向) / Smash(蓄力攻击3方向)
        //   空中: Neutral / Forward / Back / Up / Down Air
        //   必杀: Neutral / Side / Up / Down B
        //   抓投: Grab / Throw 4 Direction
        [Header("Attack Data")]
        public AttackData jab1;              // 地面轻击第1段
        public AttackData jab2;              // 地面轻击第2段（连段）
        public AttackData jab3;              // 地面轻击第3段（终结）
        public AttackData tiltSide;          // 横强攻击
        public AttackData tiltUp;            // 上强攻击
        public AttackData tiltDown;          // 下强攻击（地面时附带小跳）
        public AttackData smashSide;         // 横蓄力攻击
        public AttackData smashUp;           // 上蓄力攻击
        public AttackData smashDown;         // 下蓄力攻击
        public AttackData aerialNeutral;     // 空中普通攻击（空N）
        public AttackData aerialForward;     // 空中前向攻击（空前）
        public AttackData aerialBack;        // 空中后向攻击（空后）
        public AttackData aerialUp;          // 空中上向攻击（空上）
        public AttackData aerialDown;        // 空中下向攻击（空下）
        public AttackData specialNeutral;    // 必杀技（不推方向）
        public AttackData specialSide;       // 横必杀技
        public AttackData specialUp;         // 上必杀技
        public AttackData specialDown;       // 下必杀技
        public AttackData grab;              // 抓取
        public AttackData throwForward;      // 前投
        public AttackData throwBack;         // 后投
        public AttackData throwUp;           // 上投
        public AttackData throwDown;         // 下投

        #endregion

        [System.Serializable]
        public class MoveListEntry
        {
            public string name; 
            public string input; 
            public float damage;
            public int startupF, activeF, recoveryF;
            public string description;
            public Sprite icon;
            public MoveCategory category;   // 所属 Tab
        }

        public List<MoveListEntry> GetMoveEntries()
        {
            var list = new List<MoveListEntry>();
            AddEntry(list, jab1, MoveCategory.Jab); AddEntry(list, jab2, MoveCategory.Jab); AddEntry(list, jab3, MoveCategory.Jab);
            AddEntry(list, tiltSide, MoveCategory.Tilt); AddEntry(list, tiltUp, MoveCategory.Tilt); AddEntry(list, tiltDown, MoveCategory.Tilt);
            AddEntry(list, smashSide, MoveCategory.Smash); AddEntry(list, smashUp, MoveCategory.Smash); AddEntry(list, smashDown, MoveCategory.Smash);
            AddEntry(list, aerialNeutral, MoveCategory.Aerial); AddEntry(list, aerialForward, MoveCategory.Aerial);
            AddEntry(list, aerialBack, MoveCategory.Aerial); AddEntry(list, aerialUp, MoveCategory.Aerial); AddEntry(list, aerialDown, MoveCategory.Aerial);
            AddEntry(list, specialNeutral, MoveCategory.Special); AddEntry(list, specialSide, MoveCategory.Special);
            AddEntry(list, specialUp, MoveCategory.Special); AddEntry(list, specialDown, MoveCategory.Special);
            AddEntry(list, grab, MoveCategory.Grab);
            AddEntry(list, throwForward, MoveCategory.Grab); AddEntry(list, throwBack, MoveCategory.Grab);
            AddEntry(list, throwUp, MoveCategory.Grab); AddEntry(list, throwDown, MoveCategory.Grab);
            return list;
        }

        private void AddEntry(List<MoveListEntry> list, AttackData ad, MoveCategory cat)
        {
            if (ad == null) return;                    // 未配置槽位直接跳过
            list.Add(new MoveListEntry
            {
                name = string.IsNullOrEmpty(ad.attackName) ? "(未命名)" : ad.attackName,
                input = ad.inputCommand,
                damage = ad.damage,
                startupF = Mathf.RoundToInt(ad.startupTime * 60f),   // 秒→帧（1/60s）
                activeF = Mathf.RoundToInt(ad.activeTime * 60f),
                recoveryF = Mathf.RoundToInt(ad.recoveryTime * 60f),
                description = ad.description,
                category = cat                          // 新增
            });
        }
    }

    // 出招表分类（决定条目显示在哪个 Tab）
    public enum MoveCategory
    {
        Jab,        // 轻击
        Tilt,       // 强击
        Smash,      // 蓄力
        Aerial,     // 空中
        Special,    // 必杀
        Grab,       // 抓投（含 4 方向投）
        Throw,      // 投（如需与抓分开；前 6 个已够用可不加）
    }

    // 体重分类（当前只用于 Editor 显示，未参与逻辑）
    public enum WeightClass
    {
        Light,       // 轻量级
        Medium,      // 中量级
        Heavy,       // 重量级
        SuperHeavy,  // 超重量级
    }

    // ============================================================
    // AttackData — 单个攻击动作的数据（内嵌可序列化类）
    // 职责：存储一次攻击的全部参数（伤害/击飞/时间/特效/判定框）
    // 【重点】是 class（引用类型），改数值前必须深拷贝（见 FighterController.CloneAttackData）
    // 【注意】新增字段时，FighterController.CloneAttackData 必须同步补充，否则克隆会丢字段
    // ============================================================
    [System.Serializable]
    public class AttackData
    {
        #region 动画

        [Header("Animation")]
        public int animIndex;                 // 动画索引 → 写进 Animator 的 AttackType 参数

        #endregion

        #region 伤害

        [Header("Damage")]
        public string attackName = "Attack";  // 攻击名称（根运动白名单按它匹配）
        public float damage = 5f;             // 伤害值（百分比）
        public float shieldDamage = 3f;       // 对护盾的伤害

        #endregion

        #region 击飞

        [Header("Knockback")]
        public float knockbackAngle = 45f;     // 击飞角度（0=水平, 90=垂直）
        public float knockbackBase = 30f;      // 基础击飞值
        public float knockbackGrowth = 50f;    // 击飞成长率（伤害越高影响越大）
        public float hitstunOverride = -1f;    // 手动指定硬直秒数（<0 = 按公式算）

        #endregion

        #region 时间（秒）

        [Header("Timing (seconds)")]
        public float startupTime = 0.1f;       // 前摇（按键到判定出现）
        public float activeTime = 0.1f;        // 判定持续时间
        public float recoveryTime = 0.2f;      // 后摇（判定结束到可行动）

        #endregion

        #region 打击感

        [Header("Hitstop")]
        public float hitstopDuration = 0.05f;  // 命中时的时间暂停时长（× GameSettings.hitstopScale）

        #endregion

        #region 取消窗口（当前未实现）

        [Header("Cancel")]
        // 【注意】以下三个字段当前【没有任何逻辑读取】——
        //   只在 FighterController.CloneAttackData 里被复制。
        //   取消窗口系统（连段取消/跳跃取消/必杀取消）尚未实现。
        public string[] cancelIntoAttacks;     // 可取消进入的下一个攻击
        public bool canJumpCancel;             // 是否可跳跃取消
        public bool canSpecialCancel;          // 是否可必杀技取消

        #endregion

        #region 表现

        [Header("Visual")]
        public GameObject hitEffectPrefab;     // 命中特效预制体
        public AudioClip hitSound;             // 命中音效（尚未接入）

        #endregion

        #region 投射物（特殊攻击用）

        [Header("Projectile")]
        public GameObject projectilePrefab;      // 弹种预制体（null = 不是投射物招）
        public Vector2 projectileSpawnOffset;    // 出生点偏移（x 按朝向翻转；y 相对角色脚底）
        public float projectileSpeed = 12f;      // 弹速（覆盖预制体默认值）

        #endregion

        #region 判定框

        [Header("Hitbox")]
        public Vector2 hitboxOffset;   // 判定框偏移（x 会按朝向翻转）
        public Vector2 hitboxSize;     // 判定框大小（为 0 时攻击无判定，工具会告警）

        #endregion

        [Header("Move List")]
        public string inputCommand = "";   // 输入指令："A" / "←/→ + A" / "↑ + A（蓄力）"...
        public string description = "";    // 招式一句话说明（选填）
        public Sprite moveIcon;      // 招式图标（选填，null 则条目不显示图）

        // 总持续时间（前摇 + 判定 + 后摇）
        public float TotalDuration => startupTime + activeTime + recoveryTime;
    }
}
