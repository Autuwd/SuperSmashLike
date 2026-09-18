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
//
// 工作原理（重要变更记录 2026-08-11）：
//   原方案依赖 PlayerInput 组件的 "Send Messages" 模式。
//   实测发现：PlayerInput 通过 InputUser 关联 actions 资产时，
//   会对整个资产施加 scheme 过滤(asset.bindingMask) + 设备需求过滤，
//   在 pairedDevices=0（scheme 设备需求未满足）时，绑定解析全部失败
//   （解析控件数=0，任何按键无信号）。
//   修复：InputManager 运行时克隆一份独立的 actions 资产（JSON 克隆），
//   完全绕开 InputUser 的污染，手动订阅全部 Action 回调。
//   克隆资产解析控件数正常(=8)，输入验证通过。
// ============================================================
namespace SuperSmashLike.InputSystem
{
    public class InputManager : MonoBehaviour
    {
        [Header("References")]
        public FighterController fighterController;  // 要控制的斗士

        private PlayerInput playerInput;  // 仅用于获取 actions 资产引用（组件已被禁用）
        private InputActionAsset runtimeActions;  // 运行时克隆的独立资产（InputUser 无法污染）
        private InputActionMap gameplayMap;       // Gameplay 地图（启用中）

        // ==================== 输入状态（暂存） ====================
        // 这些由 Action 回调写入，在 Update 中消费
        public Vector2 MoveInput { get; private set; }      // 移动方向（左摇杆/WASD）
        public bool JumpPressed { get; private set; }        // 跳跃（按下的瞬间）
        public bool AttackPressed { get; private set; }      // 攻击（按下的瞬间）
        public bool SpecialPressed { get; private set; }     // 必杀技（按下的瞬间）
        public bool ShieldHeld { get; private set; }         // 防御（按住/松开）
        public bool GrabPressed { get; private set; }        // 抓取（按下的瞬间）
        public bool TauntPressed { get; private set; }       // 嘲讽（按下的瞬间）
        public bool PausePressed { get; private set; }       // 暂停


        // ===== 长按判定（09-15 新增）=====
        public bool AttackHeld { get; private set; }     // 攻击键是否按住（长按判定用）
        public float AttackPressTime { get; private set; } // 按下瞬间的时刻
        private Vector2 pendingAttackDir;                  // 按下瞬间的方向快照
        private bool chargePending;                        // 是否在等"短按/长按"判定窗
        public float longPressThreshold = 0.15f;           // 长按阈值（秒）

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            if (fighterController == null)
                fighterController = GetComponent<FighterController>();

            if (playerInput != null)
            {
                // 禁用 PlayerInput：其 InputUser 关联会污染整个资产导致绑定解析失败（根因）
                playerInput.enabled = false;

                // JSON 克隆独立资产：与 InputUser 完全隔离
                if (playerInput.actions != null)
                {
                    runtimeActions = ScriptableObject.CreateInstance<InputActionAsset>();
                    runtimeActions.LoadFromJson(playerInput.actions.ToJson());

                    // ⭐ 双人输入隔离（2026-08-17）：
                    // 克隆资产默认包含全部绑定（P1键盘区+P2键盘区+手柄），
                    // 不加 bindingMask 时两套 InputManager 都响应所有按键 → 双人同输入 Bug。
                    // 按 playerID 只保留本玩家的 control scheme 组：
                    //   P1=KeyboardP1(WASD+JKL) / P2=KeyboardP2(方向键+小键盘) / 其他=Gamepad
                    int pid = fighterController != null ? fighterController.playerID : 0;
                    string group = pid == 0 ? "KeyboardP1" : pid == 1 ? "KeyboardP2" : "Gamepad";
                    runtimeActions.bindingMask = InputBinding.MaskByGroup(group);
                    Debug.Log($"[InputManager] 输入分组隔离: playerID={pid} → {group}", this);
                }
            }

            Debug.Log($"[InputManager] 初始化 | PlayerInput: {(playerInput != null ? "已找到(已禁用)" : "缺失")} | " +
                      $"克隆资产: {(runtimeActions != null ? "OK" : "失败!")} | " +
                      $"FighterController: {(fighterController != null ? "已找到" : "缺失!")}", this);
        }

        private void OnEnable()
        {
            if (runtimeActions == null) return;

            gameplayMap = runtimeActions.FindActionMap("Gameplay");
            if (gameplayMap == null)
            {
                Debug.LogError("[InputManager] 找不到 Gameplay ActionMap！", this);
                return;
            }

            // 手动订阅全部 Action 回调（Button 类型用 started 捕获按下瞬间）
            gameplayMap.FindAction("Move").performed += OnMoveCtx;
            gameplayMap.FindAction("Move").canceled += OnMoveCtx;
            gameplayMap.FindAction("Jump").started += OnJumpCtx;
            gameplayMap.FindAction("Attack").started += OnAttackCtx;
            gameplayMap.FindAction("Attack").canceled += OnAttackCancelCtx;
            gameplayMap.FindAction("Special").started += OnSpecialCtx;
            gameplayMap.FindAction("Shield").started += OnShieldStartCtx;
            gameplayMap.FindAction("Shield").canceled += OnShieldCancelCtx;
            gameplayMap.FindAction("Grab").started += OnGrabCtx;
            gameplayMap.FindAction("Taunt").started += OnTauntCtx;
            gameplayMap.FindAction("Pause").started += OnPauseCtx;

            gameplayMap.Enable();

            var move = gameplayMap.FindAction("Move");
            Debug.Log($"[InputManager] 输入就绪 | 移动控件数: {move.controls.Count}", this);
        }

        private void OnDisable()
        {
            if (gameplayMap == null) return;

            gameplayMap.FindAction("Move").performed -= OnMoveCtx;
            gameplayMap.FindAction("Move").canceled -= OnMoveCtx;
            gameplayMap.FindAction("Jump").started -= OnJumpCtx;
            gameplayMap.FindAction("Attack").started -= OnAttackCtx;
            gameplayMap.FindAction("Attack").canceled -= OnAttackCancelCtx;
            gameplayMap.FindAction("Special").started -= OnSpecialCtx;
            gameplayMap.FindAction("Shield").started -= OnShieldStartCtx;
            gameplayMap.FindAction("Shield").canceled -= OnShieldCancelCtx;
            gameplayMap.FindAction("Grab").started -= OnGrabCtx;
            gameplayMap.FindAction("Taunt").started -= OnTauntCtx;
            gameplayMap.FindAction("Pause").started -= OnPauseCtx;

            gameplayMap.Disable();
        }

        // ==================== Action 回调 ====================
        private void OnMoveCtx(InputAction.CallbackContext ctx)
        {
            MoveInput = ctx.ReadValue<Vector2>();
        }

        private void OnJumpCtx(InputAction.CallbackContext ctx)
        {
            JumpPressed = true;
        }

        private void OnAttackCtx(InputAction.CallbackContext ctx)
        {
            AttackPressed = true;
            AttackHeld = true;
            AttackPressTime = Time.time;
        }

        private void OnAttackCancelCtx(InputAction.CallbackContext ctx)
        {
            AttackHeld = false;   // 松手瞬间 → 记录"没按着了"
        }

        private void OnSpecialCtx(InputAction.CallbackContext ctx)
        {
            SpecialPressed = true;
        }

        private void OnShieldStartCtx(InputAction.CallbackContext ctx)
        {
            ShieldHeld = true;
        }

        private void OnShieldCancelCtx(InputAction.CallbackContext ctx)
        {
            ShieldHeld = false;
        }

        private void OnGrabCtx(InputAction.CallbackContext ctx)
        {
            GrabPressed = true;
        }

        private void OnTauntCtx(InputAction.CallbackContext ctx)
        {
            TauntPressed = true;
        }

        // 暂停：切换暂停/恢复
        private void OnPauseCtx(InputAction.CallbackContext ctx)
        {
            if (GameManager.Instance.CurrentGameState == GameState.Battle)
                GameManager.Instance.PauseGame();
            else if (GameManager.Instance.CurrentGameState == GameState.Paused)
                GameManager.Instance.ResumeGame();
        }

        // 每帧将暂存的输入状态应用到 FighterController
        private void Update()
        {
            if (fighterController == null) return;

            // 将移动方向传给 FighterController
            fighterController.MoveInput = MoveInput;

            // 跳跃（按下的瞬间处理一次）
            if (JumpPressed && MoveInput.y < -0.5f && fighterController.IsGrounded)
            {
                // 落穿：↓ + 跳跃键 → 从平台下落，不起跳
                fighterController.DropThroughPlatform();
                JumpPressed = false;  // ← 关键：消费掉，避免下面又 TryJump
            }
            else if (JumpPressed)
            {
                fighterController.TryJump();
                JumpPressed = false;
            }

            // 攻击（根据输入方向+是否空中选择攻击类型）
            if (AttackPressed)
            {
                FighterState st = fighterController.StateMachine.CurrentState;

                // ① 被抓中：攻击键 = 挣扎（原逻辑保留）
                if (st == FighterState.Grabbed)
                {
                    fighterController.OnMashGrabEscape();
                    chargePending = false;      // 顺手取消可能残留的判定窗
                }
                // ② 抓取中：攻击键 = 投掷
                //    ★ 必须走这里，绝不能掉进下面的判定窗——否则长按会触发 StartCharge，
                //      把 Grab 状态顶成 Attack，导致投掷失效 + 双方死锁
                //    ★ 传什么 AttackData 无所谓：TryAttack 开头会拦截 grabTarget 并执行 PerformThrow
                else if (st == FighterState.Grab)
                {
                    AttackPressed = false;
                }
                // ③ 空中 / 地面无方向 → 立即出招，零延迟
                else if (fighterController.StateMachine.IsInAir()
                         || (Mathf.Abs(MoveInput.x) <= 0.5f && Mathf.Abs(MoveInput.y) <= 0.5f))
                {
                    AttackData attack = GetImmediateAttack();   // 空中 / 无方向 → 立即攻击
                    if (attack != null)
                        fighterController.TryAttack(attack);
                }
                // ④ 地面 + 有方向 → 进入判定窗（等 0.15s 区分短按/长按）
                else
                {
                    pendingAttackDir = MoveInput;   // ★ 方向快照！以按下瞬间为准
                    chargePending = true;
                }
                AttackPressed = false;   // 消费
            }

            // 判定窗倒计时（chargePending 期间每帧查）
            if (chargePending)
            {
                float held = Time.time - AttackPressTime;
                if (held >= longPressThreshold)
                {
                    chargePending = false;
                    fighterController.StartCharge(GetSmashAttack(pendingAttackDir));  // 长按 → 蓄力
                }
                else if (!AttackHeld)
                {
                    chargePending = false;
                    fighterController.TryAttack(GetTiltAttack(pendingAttackDir));     // 短按 → 强攻击
                }
                // 都不到 → 继续等
            }

            // 蓄力中松手 → 释放
            if (fighterController.isCharging && !AttackHeld)
            {
                fighterController.ReleaseCharge();
            }

            // 特殊攻击（先做 Neutral Special，方向特技后补）
            if (SpecialPressed)
            {
                fighterController.TrySpecial(GetContextualSpecial());
                SpecialPressed = false;
            }

            // 抓取（按下的瞬间处理一次）
            if (GrabPressed) 
            {
                FighterState st = fighterController.StateMachine.CurrentState;
                if (st == FighterState.Grab && fighterController.grabTarget != null)
                {
                    // 已抓住人 → 按抓取键 = 投掷（指定方向由当前摇杆方向决定）
                    fighterController.TryAttack(fighterController.fighterData.jab1);  // 传什么无所谓，TryAttack 会拦截 grabTarget
                }
                else
                {
                    // 未抓人 → 尝试抓取
                    fighterController.TryGrab();
                }
                GrabPressed = false;
            }

            // 防御（按住/松开）
            fighterController.SetShielding(ShieldHeld);

            fighterController.TechInputHeld = ShieldHeld;
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
        //private AttackData GetContextualAttack()
        //{
        //    if (fighterController.fighterData == null) return null;

        //    bool isInAir = fighterController.StateMachine.IsInAir();

        //    // 空中攻击
        //    if (isInAir)
        //    {
        //        if (MoveInput.y > 0.5f) return fighterController.fighterData.aerialUp;
        //        if (MoveInput.y < -0.5f) return fighterController.fighterData.aerialDown;
        //        if (Mathf.Abs(MoveInput.x) > 0.5f) return MoveInput.x > 0
        //            ? fighterController.fighterData.aerialForward
        //            : fighterController.fighterData.aerialBack;
        //        return fighterController.fighterData.aerialNeutral;
        //    }

        //    // 地面攻击
        //    if (Mathf.Abs(MoveInput.y) > 0.5f)
        //        return MoveInput.y > 0
        //            ? fighterController.fighterData.tiltUp
        //            : fighterController.fighterData.tiltDown;

        //    if (Mathf.Abs(MoveInput.x) > 0.5f)
        //        return fighterController.fighterData.tiltSide;

        //    return fighterController.fighterData.jab1;
        //}

        // 立即攻击：空中（四向+空N）/ 地面无方向（jab）—— 零延迟路径
        // 空中方向判定必须用"当前 MoveInput"（空中没有判定窗，按下即出）
        private AttackData GetImmediateAttack()
        {
            if (fighterController.fighterData == null) return null;

            // 空中攻击（方向决定招式）
            if (fighterController.StateMachine.IsInAir())
            {
                if (MoveInput.y > 0.5f) return fighterController.fighterData.aerialUp;
                if (MoveInput.y < -0.5f) return fighterController.fighterData.aerialDown;
                if (Mathf.Abs(MoveInput.x) > 0.5f)
                    return MoveInput.x > 0
                        ? fighterController.fighterData.aerialForward
                        : fighterController.fighterData.aerialBack;
                return fighterController.fighterData.aerialNeutral;
            }

            // 地面无方向 → 轻击
            return fighterController.fighterData.jab1;
        }

        // 短按 → tilt（强攻击）
        private AttackData GetTiltAttack(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > 0.5f)
                return dir.y > 0 ? fighterController.fighterData.tiltUp
                                 : fighterController.fighterData.tiltDown;
            return fighterController.fighterData.tiltSide;   // 横向（前/后同招）
        }

        // 长按 → smash（蓄力基值）
        private AttackData GetSmashAttack(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > 0.5f)
                return dir.y > 0 ? fighterController.fighterData.smashUp
                                 : fighterController.fighterData.smashDown;
            return fighterController.fighterData.smashSide;
        }

        private AttackData GetContextualSpecial()
        {
            if (Mathf.Abs(MoveInput.y) > 0.5f)
                return MoveInput.y > 0 ? fighterController.fighterData.specialUp
                                       : fighterController.fighterData.specialDown;
            if (Mathf.Abs(MoveInput.x) > 0.5f)
                return fighterController.fighterData.specialSide;   // 前/后同一个
            return fighterController.fighterData.specialNeutral;
        }
    }
}