using UnityEngine;
using SuperSmashLike.Core;

namespace SuperSmashLike.Combat
{
    // 投射物：箭/火球通用 — 直线飞行 + 命中伤害 + 自动回收
    public class Projectile : MonoBehaviour
    {
        public float speed = 15f;
        public float lifeTime = 2f;

        private AttackData attackData;
        private FighterController owner;
        private int dir = 1;
        private float timer;

        public static Projectile Spawn(GameObject prefab, Vector3 pos, int dir,
            AttackData data, FighterController attacker)
        {
            var obj = ObjectPooler.Instance.Spawn(prefab, pos, Quaternion.identity);
            var p = obj.GetComponent<Projectile>();
            p.Init(data, attacker, dir);
            return p;
        }

        void Init(AttackData data, FighterController attacker, int direction)
        {
            attackData = data; owner = attacker; dir = direction; timer = 0f;
        }

        void Update()
        {
            transform.Translate(Vector2.right * dir * speed * Time.deltaTime);
            timer += Time.deltaTime;
            if (timer >= lifeTime) ObjectPooler.Instance.Despawn(gameObject);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var target = other.GetComponentInParent<FighterController>();
            if (target == null || owner == null || target == owner) return;   // 不打自己
            if (target.StateMachine.CurrentState == FighterState.Dead) return;

            target.ApplyDamage(attackData, owner);  // 让命中判定走统一伤害流程
            // 特效（可选）：ObjectPooler.Instance.Spawn(impactFx, transform.position, Quaternion.identity)
            ObjectPooler.Instance.Despawn(gameObject);
        }
    }
}