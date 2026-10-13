using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// StageManager — 舞台加载 / 切换管理器（M4-D2）
// 职责：
//   1. 按 GameSettings 选中的 StageData 实例化对应舞台预制体
//   2. 切换舞台时回填 MatchManager.spawnPoints 与 CameraManager 边界
// 架构位置：Stage 层，场景中一个实例（挂在 StageRoot 上）
// 依赖：GameManager（读选中舞台）、MatchManager、CameraManager
// ============================================================
namespace SuperSmashLike.Stage
{
    public class StageManager : MonoBehaviour
    {
        public static StageManager Instance { get; private set; }

        #region 1. Inspector 配置

        [Header("References")]
        public Transform stageRoot;              // 舞台实例的父节点
        public MatchManager matchManager;        // 用于回填出生点
        public CameraManager cameraManager;      // 用于更新相机边界
        public GameObject fallbackStagePrefab;   // 未选中舞台时的兜底舞台

        #endregion

        #region 2. 运行时状态

        private GameObject _current;             // 当前舞台实例

        #endregion

        #region 3. 生命周期

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // 若场景里已摆放一个舞台实例（编辑器摆好），认领它作为当前舞台
            if (_current == null && stageRoot != null && stageRoot.childCount > 0)
                _current = stageRoot.GetChild(0).gameObject;
        }

        #endregion

        #region 4. 公开 API

        // 【做什么】加载当前 GameSettings 选中的舞台
        public void LoadSelectedStage()
        {
            var gs = GameManager.Instance != null ? GameManager.Instance.gameSettings : null;
            LoadStage(gs != null ? gs.GetSelectedStage() : null);
        }

        // 【做什么】切换舞台：销毁旧实例 → 实例化新舞台 → 回填出生点 / 相机边界
        // 【注意】未选中舞台或预制体为空时直接返回，保留场景中现有的舞台，不做破坏
        public void LoadStage(StageData sd)
        {
            GameObject prefab = (sd != null && sd.stagePrefab != null) ? sd.stagePrefab : fallbackStagePrefab;
            if (prefab == null) return;

            if (_current != null) Destroy(_current);

            Transform parent = stageRoot != null ? stageRoot : transform;
            _current = Instantiate(prefab, parent);
            // 【关键】归一化根节点：BattleArena.prefab 内部根位置有偏移，而场景里原实例是 (0,0,0)。
            // 不归一化 → 舞台整体偏移 → 与相机边界/出界边界对不上。
            _current.transform.localPosition = Vector3.zero;
            _current.transform.localRotation = Quaternion.identity;
            _current.transform.localScale = Vector3.one;
            _current.name = (sd != null && !string.IsNullOrEmpty(sd.stageName)) ? sd.stageName : prefab.name;

            // 回填出生点：舞台下所有名字以 SpawnPoint 开头的 Transform
            if (matchManager != null)
            {
                var list = new List<Transform>();
                foreach (var t in _current.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("SpawnPoint")) list.Add(t);
                if (list.Count > 0) matchManager.spawnPoints = list.ToArray();
            }

            // 更新相机移动边界
            if (cameraManager != null && sd != null)
            {
                cameraManager.cameraBoundsMin = sd.cameraBoundsMin;
                cameraManager.cameraBoundsMax = sd.cameraBoundsMax;
            }
        }

        #endregion
    }
}
