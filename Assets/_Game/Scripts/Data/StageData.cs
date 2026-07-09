using UnityEngine;

// ============================================================
// StageData — 舞台数据配置（ScriptableObject 资产）
// 职责：
//   1. 存储一个舞台的所有配置参数
//   2. 方便后续添加多个主题舞台（竞技场/森林/地牢/农场）
// 架构位置：Data 层
// 使用方式：
//   右键 → Create → SuperSmashLike → Stage Data
//   创建后分配 stagePrefab → 在场景加载时实例化
// ============================================================
namespace SuperSmashLike.Core
{
    [CreateAssetMenu(menuName = "SuperSmashLike/Stage Data", fileName = "SD_NewStage")]
    public class StageData : ScriptableObject
    {
        [Header("Identity")]
        public string stageName = "New Stage";   // 舞台显示名称
        public Sprite stageThumbnail;             // 选舞台时的缩略图

        [Header("Prefab")]
        public GameObject stagePrefab;            // 舞台预制体（包含所有平台/装饰）

        [Header("Camera Bounds")]
        public Vector2 cameraBoundsMin = new(-15f, -8f);  // 摄像机移动范围左下
        public Vector2 cameraBoundsMax = new(15f, 12f);   // 摄像机移动范围右上

        [Header("Blast Zone")]
        public float blastZoneWidth = 30f;   // 边界宽度（左右各15单位）
        public float blastZoneHeight = 20f;  // 边界高度（上下各10单位）

        [Header("Spawn Points")]
        public Transform[] spawnPoints;      // 玩家出生点（用于重生）

        [Header("Visual")]
        public Color ambientLight = Color.white;   // 环境光颜色
        public AudioClip backgroundMusic;           // 背景音乐
    }
}
