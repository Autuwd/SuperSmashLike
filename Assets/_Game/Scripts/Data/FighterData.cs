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
// AttackData — 攻击动作数据（内嵌类）
// 职责：
//   存储单个攻击动作的所有参数（伤害、击飞、时间、特效）
//   在 FighterData 中为每个攻击类型配置
// ============================================================
namespace SuperSmashLike.Core
{
    [CreateAssetMenu(menuName = "SuperSmashLike/Fighter Data", fileName = "FD_NewFighter")]
    public class FighterData : ScriptableObject
    {
        [Header("Identity")]
        public string fighterName = "New Fighter";   // 角色显示名称
        public Sprite portraitIcon;                  // 角色头像（选人界面用）

        [Header("Movement")]
        public float weight = 100f;          // 体重（越大越难被击飞）
        public float walkSpeed = 5f;         // 地面行走速度
        public float runSpeed = 8f;          // 地面冲刺速度
        public float airSpeed = 6f;          // 空中水平速度
        public float jumpForce = 12f;        // 跳跃力（初速度）
        public float doubleJumpForce = 9f;   // 二段跳跃力
        public int jumpCount = 2;            // 可跳跃次数（大乱斗通常2）
        public int airDodgeCount = 1;        // 空中闪避次数
        public float fallSpeed = 8f;         // 下落速度
        public float fastFallSpeed = 15f;    // 速降速度

        [Header("Visual")]
        public WeightClass weightClass = WeightClass.Medium;  // 体重分类（影响受击动画风格）
        public Color uiColor = Color.white;                   // UI 主题色

        // ==================== 攻击配置 ====================
        //  大乱斗攻击体系：
        //  地面: Jab(轻击3段) / Tilt(强攻击3方向) / Smash(蓄力攻击3方向)
        //  空中: Neutral Air / Forward Air / Back Air / Up Air / Down Air
        //  必杀技: Neutral B / Side B / Up B / Down B
        //  抓投: Grab / Throw 4 Direction
        [Header("Attack Data")]
        public AttackData jab1;              // 地面轻击第1段
        public AttackData jab2;              // 地面轻击第2段（连段）
        public AttackData jab3;              // 地面轻击第3段（终结）
        public AttackData tiltSide;          // 横强攻击
        public AttackData tiltUp;            // 上强攻击
        public AttackData tiltDown;          // 下强攻击
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
        public AttackData specialUp;         // 上必杀技（通常带上升）
        public AttackData specialDown;       // 下必杀技
        public AttackData grab;              // 抓取
        public AttackData throwForward;      // 前投
        public AttackData throwBack;         // 后投
        public AttackData throwUp;           // 上投
        public AttackData throwDown;         // 下投
    }

    // 体重分类（影响受击动画和特效）
    public enum WeightClass
    {
        Light,       // 轻量级（被击飞更远）
        Medium,      // 中量级
        Heavy,       // 重量级
        SuperHeavy,  // 超重量级（几乎打不动）
    }

    // [System.Serializable] 使 AttackData 可以在 Inspector 中展开编辑
    [System.Serializable]
    public class AttackData
    {
        [Header("Damage")]
        public string attackName = "Attack";  // 攻击名称
        public float damage = 5f;             // 伤害值（百分比）
        public float shieldDamage = 3f;       // 对护盾的伤害

        [Header("Knockback")]
        public float knockbackAngle = 45f;     // 击飞角度（0=水平, 90=垂直）
        public float knockbackBase = 30f;      // 基础击飞值
        public float knockbackGrowth = 50f;    // 击飞成长率（伤害越高影响越大）

        [Header("Timing (seconds)")]
        public float startupTime = 0.1f;       // 前摇时间（按键到判定出现）
        public float activeTime = 0.1f;        // 判定持续时间
        public float recoveryTime = 0.2f;      // 后摇时间（判定结束到可行动）

        [Header("Hitstop")]
        public float hitstopDuration = 0.05f;  // 命中时的时间暂停时长

        [Header("Cancel")]
        public string[] cancelIntoAttacks;     // 可取消进入的下一个攻击（连段用）
        public bool canJumpCancel;             // 是否可跳跃取消
        public bool canSpecialCancel;          // 是否可必杀技取消

        [Header("Visual")]
        public GameObject hitEffectPrefab;     // 命中特效预制体
        public AudioClip hitSound;             // 命中音效

        [Header("Hitbox")]
        public Vector2 hitboxOffset;   // 判定框偏移（x 按朝向翻转）
        public Vector2 hitboxSize;     // 判定框大小

        // 总持续时间（前摇+判定+后摇）
        public float TotalDuration => startupTime + activeTime + recoveryTime;
    }
}
