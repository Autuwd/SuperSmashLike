using UnityEngine;

// ============================================================
// FighterStateMachine — 角色状态机
// 职责：
//   1. 管理斗士的所有行为状态（待机/移动/跳跃/攻击/受击/击飞/防御…）
//   2. 定义状态转换规则（哪些状态可以切换到哪些）
//   3. 提供辅助判断方法（IsInAir/CanAct/IsVulnerable）
// 架构位置：Character 层，被 FighterController 持有和使用
// 关系：FighterController 每帧根据输入和物理决定 → 调用 TransitionTo
// ============================================================
namespace SuperSmashLike.Core
{
    // 斗士所有可能的状态枚举
    // 数字顺序不重要，重要的是转换规则
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
        FreeMove,    // 自由移动——击飞后速度降低到一定程度，可空中受控但还不能攻击
    }

    // [System.Serializable] 使它在 Inspector 中可展开查看
    [System.Serializable]
    public class FighterStateMachine
    {
        // 当前状态（只读外部）
        public FighterState CurrentState { get; private set; } = FighterState.Idle;

        // 上一个状态（用于状态退出/进入时的判断）
        public FighterState PreviousState { get; private set; } = FighterState.Idle;

        // 状态变更通知事件
        // FighterController 监听此事件来做动画切换、特效触发等
        public System.Action<FighterState, FighterState> OnStateChanged;

        // 初始化状态机，设置初始状态为 Idle
        public void Initialize(FighterState startState)
        {
            CurrentState = startState;
            PreviousState = startState;
        }

        // 核心逻辑：判断能否从当前状态切换到目标状态
        // 这里定义了大乱斗风格的状态转换约束：
        // - 死亡后只能重生（切换到 Idle）
        // - 攻击中可连段（Attack → Attack 允许连击取消）
        // - 击飞中只能落地/死亡/自由移动
        // - 防御中可盾反/抓取
        public bool CanTransitionTo(FighterState target)
        {
            // 死亡后不能做任何事，除非重生回到 Idle
            if (CurrentState == FighterState.Dead && target != FighterState.Idle)
                return false;

            // 攻击中允许连击取消（攻击→攻击=连段）
            if (CurrentState == FighterState.Attack && target == FighterState.Attack)
                return true;

            // 击飞中只能：自由下落/死亡/落地待机/继续受击
            if (CurrentState == FighterState.Knockback)
                return target == FighterState.Fall || target == FighterState.Dead
                    || target == FighterState.Idle || target == FighterState.Hit;

            // 受击/眩晕中只能：落地/继续被击飞/死亡
            if (CurrentState == FighterState.Hit || CurrentState == FighterState.Stun)
                return target == FighterState.Idle || target == FighterState.Fall
                    || target == FighterState.Knockback || target == FighterState.Dead;

            // 防御中只能：放下盾/被打出盾硬直/出抓取
            if (CurrentState == FighterState.Shield)
                return target == FighterState.Idle || target == FighterState.ShieldStun
                    || target == FighterState.Grab;

            // 其他情况默认允许转换
            return true;
        }

        // 执行状态转换
        // 调用 CanTransitionTo 校验 → 记录上一个状态 → 设置新状态 → 派发事件
        public void TransitionTo(FighterState newState)
        {
            if (!CanTransitionTo(newState))
            {
                //Debug.Log($"🚫 切换被拒: {CurrentState} → {newState}");
                return;
            }
            //Debug.Log($"✅ 切换: {CurrentState} → {newState}");
            PreviousState = CurrentState;
            CurrentState = newState;
            OnStateChanged?.Invoke(PreviousState, newState);
        }

        // 辅助方法：角色是否在空中？
        // 用于判断是否使用空中攻击、空中闪避等
        public bool IsInAir()
        {
            return CurrentState == FighterState.Jump
                || CurrentState == FighterState.Fall
                || CurrentState == FighterState.Knockback
                || CurrentState == FighterState.FreeMove;
        }

        // 辅助方法：角色能否主动做出操作？
        // 用于判断是否可攻击/跳跃/防御等
        public bool CanAct()
        {
            return CurrentState == FighterState.Idle
                || CurrentState == FighterState.Run
                || CurrentState == FighterState.Jump
                || CurrentState == FighterState.Fall
                || CurrentState == FighterState.FreeMove;
        }

        // 辅助方法：角色是否可被攻击？
        // 举盾/死亡/抓取状态下不可被攻击
        public bool IsVulnerable()
        {
            return CurrentState != FighterState.Shield
                && CurrentState != FighterState.Dead
                && CurrentState != FighterState.Grab
                && CurrentState != FighterState.Grabbed;
        }
    }
}
