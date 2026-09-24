using UnityEngine;

// ============================================================
// FighterStateMachine — 角色状态机（纯 C# 类，非 MonoBehaviour）
// 职责：
//   1. 管理斗士的所有行为状态（待机/移动/跳跃/攻击/受击/击飞/防御/抓投…）
//   2. 定义状态转换规则（哪些状态可以切换到哪些）
//   3. 提供辅助判断方法（IsInAir / CanAct / IsVulnerable）
// 架构位置：Character 层，被 FighterController 持有和使用
// 关系：FighterController 每帧根据输入和物理决定 → 调用 TransitionTo
//
// 【重点】CurrentState 是全项目状态的"唯一真相源"，
//   FighterController 靠它写 Animator 的 State 参数、判断能否行动、能否被攻击。
// ============================================================
namespace SuperSmashLike.Core
{
    // 斗士所有可能的状态枚举
    // 【注意】数字顺序不重要，重要的是 CanTransitionTo 里的转换规则；
    //   但 (int) 值会被写进 Animator 的 State 参数，改动顺序会让动画错位
    public enum FighterState
    {
        Idle,        // 地面待机——默认状态，可做任何操作
        Run,         // 地面移动——左右移动时
        Jump,        // 跳跃上升——起跳后到最高点前
        Fall,        // 自由下落——最高点后到落地前
        Attack,      // 攻击中——播放攻击动画期间，行为受限
        Hit,         // 受击硬直——被攻击命中的小硬直
        Stun,        // 眩晕——大硬直，不能操作
        Knockback,   // 击飞飞行——被强力攻击击飞，空中翻滚
        Shield,      // 防御——举盾状态，减伤但消耗护盾耐久
        ShieldStun,  // 防御硬直——盾被攻击后的小硬直
        Grab,        // 抓取——抓取对手成功后的状态
        Grabbed,     // 被抓取——被对手抓住，可挣扎
        Dead,        // 死亡——出界或被淘汰
    }

    // [System.Serializable] 使它在 Inspector 中可展开查看
    [System.Serializable]
    public class FighterStateMachine
    {
        #region 1. 状态数据

        // 当前状态（只读外部）
        public FighterState CurrentState { get; private set; } = FighterState.Idle;

        // 上一个状态（用于状态退出/进入时的判断）
        public FighterState PreviousState { get; private set; } = FighterState.Idle;

        // 状态变更通知事件（FighterController 监听它做动画切换、特效触发）
        public System.Action<FighterState, FighterState> OnStateChanged;

        #endregion

        #region 2. 公开 API

        // 【做什么】初始化状态机（重生时必须调用）
        // 【注意】Dead 状态无法 TransitionTo(Idle)，所以重生走 Initialize 而不是 TransitionTo
        public void Initialize(FighterState startState)
        {
            CurrentState = startState;
            PreviousState = startState;
        }

        // 【做什么】判断能否从当前状态切换到目标状态
        // 【返回】true = 允许切换
        // 【注意】这是大乱斗风格的状态转换约束表，改动前先想清楚会不会破坏：
        //   死亡后只能重生 / 攻击中可连段 / 击飞中只能落地或死亡 / 防御中可盾反或抓取
        public bool CanTransitionTo(FighterState target)
        {
            // 死亡后不能做任何事，除非重生回到 Idle
            if (CurrentState == FighterState.Dead && target != FighterState.Idle)
                return false;

            // 攻击中允许连击取消（攻击→攻击 = 连段）
            if (CurrentState == FighterState.Attack && target == FighterState.Attack)
                return true;

            // 击飞中只能：自由下落 / 死亡 / 落地待机 / 继续受击
            if (CurrentState == FighterState.Knockback)
                return target == FighterState.Fall || target == FighterState.Dead
                    || target == FighterState.Idle || target == FighterState.Hit;

            // 受击小硬直中：可被连击刷新(→Hit) / 升级(→Stun) / 击飞(→Knockback) / 落地 / 死亡
            if (CurrentState == FighterState.Hit)
                return target == FighterState.Idle || target == FighterState.Fall
                    || target == FighterState.Knockback || target == FighterState.Dead
                    || target == FighterState.Hit || target == FighterState.Stun;

            // 大硬直中：只能刷新(→Stun) / 被击飞 / 落地 / 死亡 —— 不降级
            if (CurrentState == FighterState.Stun)
                return target == FighterState.Idle || target == FighterState.Fall
                    || target == FighterState.Knockback || target == FighterState.Dead
                    || target == FighterState.Stun;

            // 防御中只能：放下盾 / 被打出盾硬直 / 出抓取
            if (CurrentState == FighterState.Shield)
                return target == FighterState.Idle || target == FighterState.ShieldStun
                    || target == FighterState.Grab || target == FighterState.Stun
                    || target == FighterState.Grabbed;

            // 其他情况默认允许转换
            return true;
        }

        // 【做什么】执行状态转换
        // 【流程】CanTransitionTo 校验 → 记录上一个状态 → 设置新状态 → 派发事件
        // 【注意】被拒绝时不报错只记日志 —— 很多"看起来没生效"的问题根源就在这里，
        //   排障时先打开 State 通道看有没有"切换被拒"
        public void TransitionTo(FighterState newState)
        {
            if (!CanTransitionTo(newState))
            {
                if (SmashDebug.IsOn(DebugChannel.State))
                    SmashDebug.Log(DebugChannel.State, $"切换被拒: {CurrentState} → {newState}");
                return;
            }

            PreviousState = CurrentState;
            CurrentState = newState;
            OnStateChanged?.Invoke(PreviousState, newState);
        }

        #endregion

        #region 3. 状态查询

        // 【做什么】角色是否在空中？（用于判断能否用空中攻击、空中闪避等）
        public bool IsInAir()
        {
            return CurrentState == FighterState.Jump
                || CurrentState == FighterState.Fall
                || CurrentState == FighterState.Knockback;
        }

        // 【做什么】角色能否主动做出操作？（用于判断能否攻击/跳跃/防御）
        public bool CanAct()
        {
            return CurrentState == FighterState.Idle
                || CurrentState == FighterState.Run
                || CurrentState == FighterState.Jump
                || CurrentState == FighterState.Fall;
        }

        // 【做什么】角色是否可被攻击？（举盾/死亡/抓取状态下不可被攻击）
        public bool IsVulnerable()
        {
            return CurrentState != FighterState.Shield
                && CurrentState != FighterState.Dead
                && CurrentState != FighterState.Grab
                && CurrentState != FighterState.Grabbed;
        }

        #endregion
    }
}
