using UnityEngine;
using System.Collections.Generic;

// ============================================================
// ObjectPooler — 通用对象池（单例）
// 职责：
//   1. 预创建常用对象的实例，避免运行时频繁 Instantiate/Destroy（性能优化）
//   2. 管理特效（命中特效、斩击）、弹丸等短暂存在对象的复用
// 架构位置：Core 层，被 Combat / Stage / UI 等所有模块使用
// 使用示例：
//   ObjectPooler.Instance.Spawn(explosionPrefab, pos, rot);
//   ObjectPooler.Instance.Despawn(obj, 0.5f);   // 0.5 秒后回收
//
// 【注意】池以【预制体名】为 key —— 不同预制体不能重名，否则会串池
// 【注意】Spawn 后对象仍在队列里（用 SetActive 控制可见性），
//   所以 Despawn 只是失活，不要 Destroy 池里的对象
// ============================================================
namespace SuperSmashLike.Core
{
    public class ObjectPooler : MonoBehaviour
    {
        #region 1. 数据结构

        // Inspector 中预设的池配置
        [System.Serializable]
        public class Pool
        {
            public GameObject prefab;       // 要池化的预制体
            public int initialSize = 10;    // 预创建数量
        }

        #endregion

        #region 2. 运行时状态

        public static ObjectPooler Instance { get; private set; }

        [SerializeField] private List<Pool> pools = new();                     // Inspector 配置
        private Dictionary<string, Queue<GameObject>> poolDictionary = new();  // 运行时池（key = 预制体名）

        #endregion

        #region 3. Unity 生命周期

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 启动时预创建所有配置的池
            foreach (var pool in pools)
                CreatePool(pool.prefab, pool.initialSize);
        }

        #endregion

        #region 4. 公开 API

        // 【做什么】创建一个对象池并预创建 initialSize 个实例
        // 【注意】已存在的池会直接跳过（幂等），不会重复创建
        public void CreatePool(GameObject prefab, int initialSize)
        {
            if (prefab == null) return;
            if (poolDictionary.ContainsKey(prefab.name)) return;

            Queue<GameObject> objectPool = new();
            for (int i = 0; i < initialSize; i++)
            {
                GameObject obj = Instantiate(prefab, transform);   // 作为 ObjectPooler 的子物体
                obj.SetActive(false);                              // 预创建的对象初始隐藏
                objectPool.Enqueue(obj);
            }

            poolDictionary.Add(prefab.name, objectPool);
        }

        // 【做什么】从池中取一个对象并摆到指定位置
        // 【参数】prefab = 要生成的对象模板；position/rotation = 目标位姿
        // 【返回】被激活的对象实例
        // 【注意】池为空时会自动扩容（Instantiate 一个新的）；池不存在时会自动建池
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            string objectTag = prefab.name;

            // 自动创建池（如果还没创建过）
            if (!poolDictionary.ContainsKey(objectTag))
                CreatePool(prefab, 5);

            Queue<GameObject> pool = poolDictionary[objectTag];

            // 池为空时自动扩容
            if (pool.Count == 0)
            {
                GameObject newObj = Instantiate(prefab, transform);
                pool.Enqueue(newObj);
            }

            // 从队列取出 → 摆位姿 → 激活 → 重新放回队尾
            // 放回队尾意味着 Spawn 后池中仍有该对象引用，靠 SetActive 控制可见性，所以没问题
            GameObject objToSpawn = pool.Dequeue();
            objToSpawn.transform.position = position;
            objToSpawn.transform.rotation = rotation;
            objToSpawn.SetActive(true);
            pool.Enqueue(objToSpawn);

            return objToSpawn;
        }

        // 【做什么】把对象归还池中（只是隐藏，不销毁）
        // 【参数】delay > 0 时延迟回收（让特效播放完毕再隐藏）
        public void Despawn(GameObject obj, float delay = 0f)
        {
            if (obj == null) return;

            if (delay > 0f)
                StartCoroutine(DespawnAfterDelay(obj, delay));
            else
                obj.SetActive(false);   // 隐藏，下次 Spawn 时复用
        }

        #endregion

        #region 5. 私有逻辑

        // 【做什么】延迟回收协程
        private System.Collections.IEnumerator DespawnAfterDelay(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (obj != null) obj.SetActive(false);
        }

        #endregion
    }
}
