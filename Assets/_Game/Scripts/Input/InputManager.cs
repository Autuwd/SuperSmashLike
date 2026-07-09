using UnityEngine;
using UnityEngine.InputSystem;
using SuperSmashLike.Core;

// ============================================================
// InputManager — 输入管理器
// 职责：
//   1. 通过 Unity Input System 接收玩家输入
//   2. 将输入转换为游戏操作，调用 FighterController 对应方法
//   3. 根据上下文（地面/空中、是否推摇杆）决定使用哪个攻击
// 架构位置：Input 层
// 工作原理：
//   使用 Unity Input System 的 "Send Messages" 模式
//   → PlayerInput 组件收到输入后自动调用 OnMove/OnJump/OnAttack 等方法
//   → 在 Update 中将这些输入转换为游戏行为
// 多人支持：
//   每个玩家角色身上挂一个 PlayerInput + InputManager
//   PlayerInputManager 自动为新接入的设备分配角色
// ============================================================
namespace SuperSmashLike.InputSystem
{
    public class InputManager : MonoBehaviour
    {
        [Header("References")]
        public FighterController fighterController;  // 要控制的斗士

        private PlayerInput playerInput;  // Unity Input System 组件

        // ==================== 输入状态（暂存） ====================
        // 这些由 OnXxx 方法写入，在 Update 中消费
        public Vector2 MoveInput { get; private set; }      // 移动方向（左摇杆/WASD）
        public bool JumpPressed { get; private set; }        // 跳跃（按下的瞬间）
        public bool AttackPressed { get; private set; }      // 攻击（按下的瞬间）
        public bool SpecialPressed { get; private set; }     // 必杀技（按下的瞬间）
        public bool ShieldHeld { get; private set; }         // 防御（按住/松开）
        public bool GrabPressed { get; private set; }        // 抓取（按下的瞬间）
        public bool TauntPressed { get; private set; }       // 嘲讽（按下的瞬间）
        public bool PausePressed { get; private set; }       // 暂停

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            if (fighterController == null)
                fighterController = GetComponent<FighterController>();
        }

        // 每帧将暂存的输入状态应用到 FighterController
        private void Update()
        {
            if (fighterController == null) return;

            // 将移动方向传给 FighterController
            fighterController.MoveInput = MoveInput;

            // 跳跃（按下的瞬间处理一次）
            if (JumpPressed)
            {
                fighterController.TryJump();
                JumpPressed = false;  // 消费掉，避免重复触发
            }

            // 攻击（根据输入方向+是否空中选择攻击类型）
            if (AttackPressed)
            {
                AttackData attack = GetContextualAttack();
                if (attack != null)
                    fighterController.TryAttack(attack);
                AttackPressed = false;
            }

            // 防御（按住/松开）
            fighterController.SetShielding(ShieldHeld);
        }

        // ================================================================
        // 根据上下文选择攻击类型（大乱斗风格的方向+攻击）
        //
        // 判定逻辑（优先级从上到下）：
        //   1. 空中状态 → 空中攻击（空N/空前/空后/空上/空下）
        //   2. 推上/下摇杆 → 上/下Tilt攻击
        //   3. 推左右摇杆 → 横Tilt攻击
        //   4. 不推方向 → 近距离轻击（Jab 1）
        //
        // 这里简化了 Smash 攻击的判定（需要同时按攻击+方向,
        // 实际大乱斗中推摇杆的力度决定是 Tilt 还是 Smash）
        // ================================================================
        private AttackData GetContextualAttack()
        {
            if (fighterController.fighterData == null) return null;

            bool isInAir = fighterController.StateMachine.IsInAir();

            // 空中攻击
            if (isInAir)
            {
                if (MoveInput.y > 0.5f) return fighterController.fighterData.aerialUp;
                if (MoveInput.y < -0.5f) return fighterController.fighterData.aerialDown;
                if (Mathf.Abs(MoveInput.x) > 0.5f) return MoveInput.x > 0
                    ? fighterController.fighterData.aerialForward
                    : fighterController.fighterData.aerialBack;
                return fighterController.fighterData.aerialNeutral;
            }

            // 地面攻击
            if (Mathf.Abs(MoveInput.y) > 0.5f)
                return MoveInput.y > 0
                    ? fighterController.fighterData.tiltUp
                    : fighterController.fighterData.tiltDown;

            if (Mathf.Abs(MoveInput.x) > 0.5f)
                return fighterController.fighterData.tiltSide;

            return fighterController.fighterData.jab1;
        }

        // ==================== Unity Input System 回调 ====================
        // 这些方法由 PlayerInput 组件通过 "Send Messages" 模式自动调用
        // 方法名必须与 Input Action Asset 中定义的 Action 名称一致

        public void OnMove(InputValue value)
        {
            MoveInput = value.Get<Vector2>();
        }

        public void OnJump(InputValue value)
        {
            if (value.isPressed)
                JumpPressed = true;
        }

        public void OnAttack(InputValue value)
        {
            if (value.isPressed)
                AttackPressed = true;
        }

        public void OnSpecial(InputValue value)
        {
            if (value.isPressed)
                SpecialPressed = true;
        }

        public void OnShield(InputValue value)
        {
            ShieldHeld = value.isPressed;
        }

        public void OnGrab(InputValue value)
        {
            if (value.isPressed)
                GrabPressed = true;
        }

        public void OnTaunt(InputValue value)
        {
            if (value.isPressed)
                TauntPressed = true;
        }

        // 暂停：切换暂停/恢复
        public void OnPause(InputValue value)
        {
            if (value.isPressed)
            {
                if (GameManager.Instance.CurrentGameState == GameState.Battle)
                    GameManager.Instance.PauseGame();
                else if (GameManager.Instance.CurrentGameState == GameState.Paused)
                    GameManager.Instance.ResumeGame();
            }
        }
    }
}
