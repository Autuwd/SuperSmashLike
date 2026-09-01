using System.Collections.Generic;
using UnityEngine;

// ============================================================
// GameSettings — 全局游戏规则配置（ScriptableObject 资产）
// 职责：
//   1. 集中管理所有游戏参数，无需修改代码即可调整规则
//   2. 支持创建多个配置资产（快速换不同规则集测试）
// 架构位置：Data 层
// 使用方式：
//   右键 → Create → SuperSmashLike → Game Settings
//   创建后拖到 GameManager.gameSettings 字段
// ============================================================
namespace SuperSmashLike.Core
{
    [CreateAssetMenu(menuName = "SuperSmashLike/Game Settings", fileName = "GS_NewSettings")]
    public class GameSettings : ScriptableObject
    {
        [Header("Match Rules")]
        public MatchMode matchMode = MatchMode.Stock;  // 比赛模式
        public int stockCount = 3;                      // 命数制：每人几条命
        public float matchTimeSeconds = 300f;           // 时间制：比赛时长（秒）
        public float damageRatio = 1f;                  // 伤害比例（整体难度缩放）
        public bool enableTeamMode;                     // 是否启用团队模式

        [Header("Spawn")]
        public Vector2 blastZoneWidth = new(30f, 30f);  // 左右边界距离
        public float blastZoneHeight = 20f;              // 上下边界距离
        public float respawnTime = 3f;                   // 重生等待时间
        public int respawnInvincibilityFrames = 120;     // 重生后无敌帧数

        [Header("Combat")]
        public float hitstopScale = 1f;         // Hitstop缩放（0=关, 1=正常）
        public float shieldSize = 1.5f;         // 护盾大小
        public float shieldMaxHP = 100f;        // 护盾最大耐久
        public float shieldRegenPerSecond = 10f;// 护盾恢复速度
        public bool enableFriendlyFire;         // 是否开启友军伤害

        [Header("Items")]
        public bool enableItems = true;          // 是否开启道具
        public float itemSpawnInterval = 15f;    // 道具刷新间隔
        public int maxItemsOnField = 5;          // 场上最大道具数

        [Header("Character Select")]
        public List<FighterData> selectableFighters = new();   // 可选手列表（Inspector 配置）
        private FighterData[] _selections = new FighterData[4]; // 每名玩家选中的角色

        /// <summary>返回可选手列表（供选人面板用）</summary>
        public List<FighterData> GetSelectableFighters() => selectableFighters;

        /// <summary>记录某玩家的选择（跨场景保持）</summary>
        public void SetSelection(int playerIndex, FighterData fd)
        {
            if (playerIndex >= 0 && playerIndex < _selections.Length)
                _selections[playerIndex] = fd;
        }

        /// <summary>读取某玩家的选择（战斗场景 Spawn 用）</summary>
        public FighterData GetSelection(int playerIndex)
            => playerIndex >= 0 && playerIndex < _selections.Length ? _selections[playerIndex] : null;

        public enum MatchMode
        {
            Stock,  // 命数制：每人几条命，死了扣命→重生，命用完→淘汰
            Time,   // 时间制：限时内击杀数决定胜负
        }
    }
}
