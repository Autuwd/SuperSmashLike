using UnityEngine;
using System.Collections.Generic;

// ============================================================
// ObjectPooler — 通用对象池（单例）
// 职责：
//   1. 预创建常用对象的实例，避免运行时频繁 Instantiate/Destroy（性能优化）
//   2. 管理特效（Hit Impact、Slash）、弹丸等短暂存在对象的复用
// 使用示例：
//   ObjectPooler.Instance.Spawn(explosionPrefab, pos, rot);
//   ObjectPooler.Instance.Despawn(obj, 0.5f);  // 0.5秒后回收
// 架构位置：Core 层，被 Combat/Stage/UI 等所有模块使用
// ============================================================
namespace SuperSmashLike.Core
{
    public class ObjectPooler : MonoBehaviour
    {
        public static ObjectPooler Instance { get; private set; }

        // 在 Unity Inspector 中预设要池化的对象列表
        [System.Serializable]
        public class Pool
        {
            public GameObject prefab;       // 要池化的预制体
            public int initialSize = 10;    // 预创建数量
        }

        [SerializeField] private List<Pool> pools = new();           // Inspector 配置
        private Dictionary<string, Queue<GameObject>> poolDictionary = new();  // 运行时池

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

        // 创建一个对象池（传入预制体和初始数量）
        // 内部以预制体名称为 key 存储
        public void CreatePool(GameObject prefab, int initialSize)
        {
            if (poolDictionary.ContainsKey(prefab.name)) return;

            Queue<GameObject> objectPool = new();
            for (int i = 0; i < initialSize; i++)
            {
                GameObject obj = Instantiate(prefab, transform);  // 作为 ObjectPooler 的子物体
                obj.SetActive(false);   // 预创建的对象初始为隐藏
                objectPool.Enqueue(obj);
            }
            poolDictionary.Add(prefab.name, objectPool);
        }

        // 从池中取一个对象
        // 如果池为空则自动扩展（Instantiate 新的）
        // 使用后记得用 Despawn 归还
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
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

            // 从队列取出 → 设置位置/旋转 → 激活 → 重新放回队尾
            // 注意：放回队尾意味着 Spawn 后池中仍有该对象引用
            // 但因为我们用 SetActive(true/false) 来控制可见性，所以没问题
            GameObject objToSpawn = pool.Dequeue();
            objToSpawn.transform.position = position;
            objToSpawn.transform.rotation = rotation;
            objToSpawn.SetActive(true);
            pool.Enqueue(objToSpawn);

            return objToSpawn;
        }

        // 将对象归还池中（隐藏它）
        // delay 参数可指定延迟回收（让特效播放完毕再隐藏）
        public void Despawn(GameObject obj, float delay = 0f)
        {
            if (delay > 0f)
                StartCoroutine(DespawnAfterDelay(obj, delay));
            else
                obj.SetActive(false);  // 设为隐藏，下次 Spawn 时复用
        }

        private System.Collections.IEnumerator DespawnAfterDelay(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            obj.SetActive(false);
        }
    }
}
