using System.Collections.Generic;
using UnityEngine;

// ============================================================
// GameSettings — 全局游戏规则配置（ScriptableObject 资产）
// 职责：
//   1. 集中管理所有游戏参数，无需修改代码即可调整规则
//   2. 支持创建多个配置资产（快速换不同规则集测试）
//   3. 缓存"选人结果"实现跨场景保持
// 架构位置：Data 层
// 使用方式：
//   右键 → Create → SuperSmashLike → Game Settings
//   创建后拖到 GameManager.gameSettings 字段
//
// 【注意】本资产里有若干字段"配置了但运行时无人读取"（见各字段注释），
//   属于未实现功能的占位配置，不要误以为改了就会生效。
// ============================================================
namespace SuperSmashLike.Core
{
    [CreateAssetMenu(menuName = "SuperSmashLike/Game Settings", fileName = "GS_NewSettings")]
    public class GameSettings : ScriptableObject
    {
        #region 1. 比赛规则

        [Header("Match Rules")]
        public MatchMode matchMode = MatchMode.Stock;  // 比赛模式（命数制 / 时间制）
        public int stockCount = 3;                      // 命数制：每人几条命（已生效）
        public float matchTimeSeconds = 300f;           // 时间制：比赛时长（秒）（已生效）
        public float damageRatio = 1f;                  // 伤害比例（全局难度缩放，已生效）
        public bool enableTeamMode;                     // 团队模式（未实现，无人读取）

        #endregion

        #region 2. 出生与边界

        [Header("Spawn")]
        public Vector2 blastZoneWidth = new(30f, 30f);   // 出界边界宽（未实现，无人读取）
        public float blastZoneHeight = 20f;              // 出界边界高（未实现，无人读取）
        public float respawnTime = 3f;                   // 重生等待时间（已生效）
        public int respawnInvincibilityFrames = 120;     // 重生无敌帧数（未实现，实际用 respawnTime）

        #endregion

        #region 3. 战斗

        [Header("Combat")]
        public float hitstopScale = 1f;          // Hitstop 缩放（0=关，1=正常）（已生效）
        public float shieldSize = 1.5f;          // 护盾大小（未实现，无人读取）
        public float shieldMaxHP = 100f;         // 护盾最大耐久（已生效）
        public float shieldRegenPerSecond = 10f; // 护盾每秒变化量（举盾时扣、松盾时回，已生效）
        public bool enableFriendlyFire;          // 友军伤害（未实现，无人读取）

        #endregion

        #region 4. 道具（未实现）

        [Header("Items")]
        public bool enableItems = true;          // 是否开启道具（未实现，无人读取）
        public float itemSpawnInterval = 15f;    // 道具刷新间隔（未实现）
        public int maxItemsOnField = 5;          // 场上最大道具数（未实现）

        #endregion

        #region 5. 选人

        [Header("Character Select")]
        public List<FighterData> selectableFighters = new();   // 可选手列表（Inspector 配置）

        // 每名玩家选中的角色（不序列化，靠 ScriptableObject 资产实现跨场景保持）
        private FighterData[] _selections = new FighterData[4];

        // 【做什么】返回可选手列表（供选人面板用）
        public List<FighterData> GetSelectableFighters() => selectableFighters;

        // 【做什么】记录某玩家的选择（跨场景保持）
        public void SetSelection(int playerIndex, FighterData fd)
        {
            if (playerIndex >= 0 && playerIndex < _selections.Length)
                _selections[playerIndex] = fd;
        }

        // 【做什么】读取某玩家的选择（战斗场景 Spawn 用）
        // 【返回】下标越界或未选择时返回 null
        public FighterData GetSelection(int playerIndex)
            => playerIndex >= 0 && playerIndex < _selections.Length ? _selections[playerIndex] : null;

        #endregion

        #region 6. 枚举

        public enum MatchMode
        {
            Stock,  // 命数制：每人几条命，死了扣命→重生，命用完→淘汰
            Time,   // 时间制：限时内击杀数决定胜负
        }

        #endregion
    }
}
