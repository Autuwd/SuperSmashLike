using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using UnityEngine;

// ============================================================
// Hitbox — 攻击判定框
// 职责：
//   1. 挂在角色手脚/武器上的碰撞体，检测是否命中对手
//   2. 由 Animation Event 控制激活/停用时机（匹配攻击动画的判定帧）
//   3. 命中后调用 FighterController.ApplyDamage 处理伤害和击飞
//   4. 实现 Hitstop（打击暂停）—— 命中时短暂冻结时间，增强打击感
// 架构位置：Combat 层
// 依赖：HitboxEventRelay（动画事件转发）、CameraManager（震屏）、GameManager（读 hitstopScale）
// 使用方式：
//   在角色子物体上加 BoxCollider2D + Hitbox 脚本
//   在攻击动画中用 Animation Event 调用 Activate() / Deactivate() / AttackFinished()
//
// 【注意】Activate() 里必须做 collider off→on 强制翻转 ——
//   Unity 的 OnTriggerEnter2D 是"边沿触发"，不翻转则同一判定框重复攻击不触发。
// ============================================================
namespace SuperSmashLike.Combat
{
    public class Hitbox : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Settings")]
        public AttackData attackData;                      // 这个攻击框对应的攻击数据
        [HideInInspector] public FighterController owner;  // 所属角色（由 FighterController 在 Awake 中赋值）

        [Header("Debug")]
        public bool showGizmos = true;
        public Color activeColor = Color.red;       // 激活时红色
        public Color inactiveColor = Color.green;   // 未激活时绿色

        #endregion

        #region 2. 运行时状态

        public bool IsActive { get; private set; }      // 判定框当前是否打开
        public System.Action<FighterController> OnHit;  // 命中回调（预留：音效/连击计数等）

        private Collider2D hitCollider;

        // ===== M8-D2 音效参数 =====
        private const float HitSfxVolume = 0.8f;      // 命中音量
        private const float SwingSfxVolume = 0.35f;   // 挥击音量

        #endregion

        #region 3. Unity 生命周期

        private void Awake()
        {
            hitCollider = GetComponent<Collider2D>();
            if (hitCollider != null)
                hitCollider.enabled = false;  // 默认禁用，由动画事件激活
        }

        #endregion

        #region 4. 动画事件入口（由 HitboxEventRelay 转发）

        // 【做什么】开启判定框
        // 【副作用】置 owner.hitboxActivatedThisAttack = true（连段守卫依赖此标记）
        // 【注意】collider 先 disable 再 enable 是必须的 —— 强制产生一次 on→off→on 边沿，
        //   否则同一次攻击的第二下不会触发 OnTriggerEnter2D
        public void Activate()
        {
            IsActive = true;
            if (owner != null) owner.hitboxActivatedThisAttack = true;

            if (hitCollider != null)
            {
                hitCollider.enabled = false;
                hitCollider.enabled = true;
            }

            // 【M8-D2】挥击音：Activate 每次攻击只被动画事件调用一次（放 Update 里会逐帧连响）；
            //   pitch 随机 0.95~1.05，避免连续攻击音高完全一致
            AudioManager.Sfx(SfxId.Swing, SwingSfxVolume, Random.Range(0.95f, 1.05f));
        }

        // 【做什么】关闭判定框
        public void Deactivate()
        {
            IsActive = false;
            if (hitCollider != null)
                hitCollider.enabled = false;
        }

        // 【做什么】攻击动画播完 → 解除攻击态，恢复操作能力
        // 【注意】条件放宽到"只要还在攻击中"就解除，因为动画可能被落地逻辑提前切走状态；
        //   只有状态机还停在 Attack 时才切回 Idle，避免重复切换
        public void AttackFinished()
        {
            if (owner == null || !owner.isAttacking) return;

            owner.comboStep = 0;
            owner.isAttacking = false;
            if (owner.StateMachine.CurrentState == FighterState.Attack)
                owner.StateMachine.TransitionTo(FighterState.Idle);
        }

        #endregion

        #region 5. 命中检测

        // 【做什么】判定框与受击框接触 → 应用伤害 + 打击感三件套
        // 【副作用】改目标伤害/击飞、触发震屏、生成特效、冻结时间（Hitstop）
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsActive) return;

            Hurtbox hurtbox = other.GetComponent<Hurtbox>();
            // 确认碰撞到的是受击框，且不是自己（避免自伤）
            if (hurtbox == null || hurtbox.owner == owner) return;

            // 【M8-D2】先快照命中确认态，再调 ApplyDamage —— ApplyDamage 会让被击者进受击/击飞态，
            //   无敌/举盾（盾反/破盾走护盾音效分支）等情况不算"真正打中人"，不播分级命中音，
            //   避免与 ShieldHit/Parry/ShieldBreak 叠加
            FighterController hitTarget = hurtbox.owner;
            bool hitConfirmed = !hitTarget.isInvincible
                             && !hitTarget.debugInvincible
                             && hitTarget.StateMachine.CurrentState != FighterState.Dead
                             && !hitTarget.isShielding;

            // 1. 对目标斗士应用伤害（统一伤害流程入口）
            hurtbox.owner.ApplyDamage(attackData, owner);

            // 1.5 【M8-D2】分级命中音：>=16 重击 / >=9 中击 / 其余轻击；attackData 缺失时不播
            if (hitConfirmed && attackData != null)
                AudioManager.Sfx(SelectHitSfx(attackData.damage), HitSfxVolume);

            // 2. 打击感：震屏（重击震得厉害 —— 强度/时长后续可挂到 AttackData）
            Camera cam = Camera.main;
            if (cam != null && cam.TryGetComponent<CameraManager>(out var camMgr))
                camMgr.Shake(0.3f, 0.15f);

            // 3. 打击感：命中特效（在 AttackData 资产里配）
            if (attackData != null && attackData.hitEffectPrefab != null)
            {
                GameObject fx = Instantiate(attackData.hitEffectPrefab, other.transform.position, Quaternion.identity);
                Destroy(fx, 0.1f);   // 短暂显示后清理（特效自身有生命周期时也应保留这层兜底）
            }

            OnHit?.Invoke(hurtbox.owner);

            // 4. 打击感：Hitstop（格斗游戏的 hit freeze）
            if (attackData != null && GameManager.Instance.gameSettings.hitstopScale > 0f)
                StartCoroutine(HitstopRoutine());
        }

        // 【做什么】冻结时间一小段再恢复
        // 【注意】必须用 WaitForSecondsRealtime —— Time.timeScale = 0 时普通 WaitForSeconds 不会计时
        private System.Collections.IEnumerator HitstopRoutine()
        {
            float duration = attackData.hitstopDuration * GameManager.Instance.gameSettings.hitstopScale;
            if (duration <= 0f) yield break;

            float restoreTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(duration);

            if (GameManager.Instance == null)
            {
                Time.timeScale = restoreTimeScale;
                yield break;
            }

            // 统一走 ApplyTimeScale：尊重 Paused / 外部冻结（训练菜单），不再直写 BaseTimeScale
            GameManager.Instance.ApplyTimeScale();
        }

        // 【做什么】按伤害值选分级命中音（【M8-D2】）：>=16 重击 / >=9 中击 / 其余轻击
        private static SfxId SelectHitSfx(float damage)
        {
            if (damage >= 16f) return SfxId.HitHeavy;
            if (damage >= 9f) return SfxId.HitMedium;
            return SfxId.HitLight;
        }

        #endregion

        #region 6. 调试可视化

        // 【做什么】在 Scene 视图画出判定框范围（红=激活 / 绿=未激活）
        private void OnDrawGizmos()
        {
            if (!showGizmos) return;
            Gizmos.color = IsActive ? activeColor : inactiveColor;

            if (hitCollider is BoxCollider2D box)
                Gizmos.DrawWireCube(transform.position + (Vector3)box.offset, box.size);
            else if (hitCollider is CircleCollider2D circle)
                Gizmos.DrawWireSphere(transform.position + (Vector3)circle.offset, circle.radius);
        }

        #endregion
    }
}
