using UnityEngine;
using UnityEngine.InputSystem;
using SuperSmashLike.Core;
using SuperSmashLike.Managers;

// ============================================================
// InputManager — 输入管理器
// 职责：
//   1. 通过 Unity Input System 接收玩家输入（手动订阅，不用 PlayerInput 的 Send Messages）
//   2. 把输入转换成游戏操作，调用 FighterController 的对应方法
//   3. 根据上下文（地面/空中、是否推摇杆、短按/长按）决定使用哪个招式
// 架构位置：Input 层
// 依赖：FighterController（被控对象）、GameManager（暂停）
//
// 工作原理（重要变更记录 2026-08-11）：
//   原方案依赖 PlayerInput 组件的 "Send Messages" 模式。
//   实测发现：PlayerInput 通过 InputUser 关联 actions 资产时，
//   会对整个资产施加 scheme 过滤(asset.bindingMask) + 设备需求过滤，
//   在 pairedDevices=0（scheme 设备需求未满足）时，绑定解析全部失败
//   （解析控件数=0，任何按键无信号）。
//   修复：InputManager 运行时克隆一份独立的 actions 资产（JSON 克隆），
//   完全绕开 InputUser 的污染，手动订阅全部 Action 回调。
//
// 【重点】攻击按键的 4 分支判定顺序敏感，不能随意调换（见 Update 内注释）
// ============================================================
namespace SuperSmashLike.InputSystem
{
    public class InputManager : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("References")]
        public FighterController fighterController;  // 要控制的斗士（留空则自动取同物体上的）

        [Header("长按判定")]
        public float longPressThreshold = 0.15f;     // 短按/长按的判定阈值（秒）

        #endregion

        #region 2. 运行时状态

        private PlayerInput playerInput;          // 仅用于获取 actions 资产引用（组件已被禁用）
        private InputActionAsset runtimeActions;  // 运行时克隆的独立资产（InputUser 无法污染）
        private InputActionMap gameplayMap;       // Gameplay 地图（启用中）

        // ---- Action 回调写入的输入状态（在 Update 中消费）----
        public Vector2 MoveInput { get; private set; }    // 移动方向（左摇杆/WASD）
        public bool JumpPressed { get; private set; }     // 跳跃（按下的瞬间）
        public bool AttackPressed { get; private set; }   // 攻击（按下的瞬间）
        public bool SpecialPressed { get; private set; }  // 必杀技（按下的瞬间）
        public bool ShieldHeld { get; private set; }      // 防御（按住/松开）
        public bool GrabPressed { get; private set; }     // 抓取（按下的瞬间）
        public bool TauntPressed { get; private set; }    // 嘲讽（按下的瞬间，功能未实现）
        public bool PausePressed { get; private set; }    // 暂停
        public bool AttackHeld { get; private set; }      // 攻击键是否按住（长按判定用）
        public float AttackPressTime { get; private set; }// 攻击按下的时刻

        private Vector2 pendingAttackDir;   // 按下瞬间的方向快照（判定窗期间使用）
        private bool chargePending;         // 是否在等"短按/长按"判定窗

        //特殊攻击蓄力
        private bool specialChargePending;
        private float specialPressTime;
        private Vector2 pendingSpecialDir;

        private float lastDownTapTime = -1f;          // 上次按"下"的时间
        private const float DownDoubleTapWindow = 0.25f;  // 双击判定窗口（可调）
        private bool previousMoveInputY;

        #endregion

        #region 3. Unity 生命周期

        // 【做什么】克隆一份独立的 actions 资产，按 playerID 做输入分组隔离
        // 【注意】必须禁用 PlayerInput 组件 —— 它的 InputUser 关联会污染整个资产
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

                    // 双人输入隔离（2026-08-17）：
                    // 克隆资产默认包含全部绑定（P1键盘区 + P2键盘区 + 手柄），
                    // 不加 bindingMask 时两套 InputManager 都响应所有按键 → 双人同输入 Bug。
                    // 按 playerID 只保留本玩家的 control scheme 组。
                    int pid = fighterController != null ? fighterController.playerID : 0;
                    string group = pid == 0 ? "KeyboardP1" : pid == 1 ? "KeyboardP2" : "Gamepad";
                    runtimeActions.bindingMask = InputBinding.MaskByGroup(group);

                    if (SmashDebug.IsOn(DebugChannel.Input))
                        SmashDebug.Log(DebugChannel.Input, $"输入分组隔离: playerID={pid} → {group}");
                }
            }

            if (SmashDebug.IsOn(DebugChannel.Input))
                SmashDebug.Log(DebugChannel.Input,
                    $"初始化 | PlayerInput: {(playerInput != null ? "已找到(已禁用)" : "缺失")} | " +
                    $"克隆资产: {(runtimeActions != null ? "OK" : "失败!")} | " +
                    $"FighterController: {(fighterController != null ? "已找到" : "缺失!")}");
        }

        // 【做什么】订阅全部 Action 回调并启用 Gameplay 地图
        // 【注意】Button 类型用 started 捕获"按下瞬间"，canceled 捕获"松开"
        private void OnEnable()
        {
            if (runtimeActions == null) return;

            gameplayMap = runtimeActions.FindActionMap("Gameplay");
            if (gameplayMap == null)
            {
                Debug.LogError("[InputManager] 找不到 Gameplay ActionMap！", this);
                return;
            }

            gameplayMap.FindAction("Move").performed += OnMoveCtx;
            gameplayMap.FindAction("Move").canceled += OnMoveCtx;
            gameplayMap.FindAction("Jump").started += OnJumpCtx;
            gameplayMap.FindAction("Attack").started += OnAttackCtx;
            gameplayMap.FindAction("Attack").canceled += OnAttackCancelCtx;
            gameplayMap.FindAction("Special").started += OnSpecialCtx;
            gameplayMap.FindAction("Special").canceled += OnSpecialCtx;
            gameplayMap.FindAction("Shield").started += OnShieldStartCtx;
            gameplayMap.FindAction("Shield").canceled += OnShieldCancelCtx;
            gameplayMap.FindAction("Grab").started += OnGrabCtx;
            gameplayMap.FindAction("Taunt").started += OnTauntCtx;
            // 两个玩家的 InputManager 都会触发 → 只让 P1 订阅，避免"暂停→立即恢复"的双触发
            if (fighterController != null && fighterController.playerID == 0)
                gameplayMap.FindAction("Pause").started += OnPauseCtx;

            gameplayMap.Enable();

            if (SmashDebug.IsOn(DebugChannel.Input))
            {
                var move = gameplayMap.FindAction("Move");
                SmashDebug.Log(DebugChannel.Input, $"输入就绪 | 移动控件数: {move.controls.Count}");
            }
        }

        // 【做什么】退订全部回调并禁用地图（订阅/退订必须严格对称）
        private void OnDisable()
        {
            if (gameplayMap == null) return;

            gameplayMap.FindAction("Move").performed -= OnMoveCtx;
            gameplayMap.FindAction("Move").canceled -= OnMoveCtx;
            gameplayMap.FindAction("Jump").started -= OnJumpCtx;
            gameplayMap.FindAction("Attack").started -= OnAttackCtx;
            gameplayMap.FindAction("Attack").canceled -= OnAttackCancelCtx;
            gameplayMap.FindAction("Special").started -= OnSpecialCtx;
            gameplayMap.FindAction("Special").canceled -= OnSpecialCtx;
            gameplayMap.FindAction("Shield").started -= OnShieldStartCtx;
            gameplayMap.FindAction("Shield").canceled -= OnShieldCancelCtx;
            gameplayMap.FindAction("Grab").started -= OnGrabCtx;
            gameplayMap.FindAction("Taunt").started -= OnTauntCtx;
            if (fighterController != null && fighterController.playerID == 0)
                gameplayMap.FindAction("Pause").started -= OnPauseCtx;

            gameplayMap.Disable();
        }

        #endregion

        #region 4. Action 回调（只写字段，不做决策）

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
            AttackHeld = false;   // 松手瞬间
        }

        private void OnSpecialCtx(InputAction.CallbackContext ctx)
        {
            if (ctx.started)
            {
                specialPressTime = Time.time;
                pendingSpecialDir = MoveInput;  // 快照方向
                specialChargePending = true;
            }
            else if (ctx.canceled && specialChargePending)
            {
                float held = Time.time - specialPressTime;
                bool isLongPress = held >= longPressThreshold;
                // 注入到 FighterController（或直接写招式数据的 spawnOffset 缩放因子）
                fighterController.TrySpecial(GetContextualSpecial(pendingSpecialDir), isLongPress);
                specialChargePending = false;
            }
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
            TauntPressed = true;   // 当前无消费方（嘲讽功能未实现）
        }

        // 【做什么】暂停/恢复切换
        private void OnPauseCtx(InputAction.CallbackContext ctx)
        {
            SmashDebug.Log(DebugChannel.Input, $"[Pause] 动作触发! state={GameManager.Instance.CurrentGameState}");
            if (GameManager.Instance.CurrentGameState == GameState.Battle)
                GameManager.Instance.PauseGame();
            else if (GameManager.Instance.CurrentGameState == GameState.Paused)
                GameManager.Instance.ResumeGame();
        }

        #endregion

        #region 5. 每帧消费输入 → 调用 FighterController

        // 【做什么】把本帧暂存的输入意图转成具体动作
        // 【注意】攻击分支的顺序敏感，见下方注释
        private void Update()
        {
            if (fighterController == null) return;

            // 【修复】只在 Battle 且非"重生锁定"时才消费游戏输入；
            // 暂停/标题/选人/结算一律清 latch 并早退（Update 不受 timeScale 影响）
            bool playable = GameManager.Instance != null
                         && GameManager.Instance.CurrentGameState == GameState.Battle
                         && MatchManager.Instance != null
                         && MatchManager.Instance.IsMatchActive;   // 倒计时期间 false → 不能动

            if (!playable || fighterController.IsRespawnLocked)   // ← IsRespawnLocked 见问题2
            {
                ClearLatchedInput();
                return;
            }

            // ===== 1. 移动方向（无条件写入，Grab/Grabbed 状态下也要有值）=====
            fighterController.MoveInput = MoveInput;

            // ===== 落穿：双击下（两次按下的 rising edge 间隔 < 窗口）=====
            bool downNow = MoveInput.y < -0.5f;
            bool downJustPressed = downNow && !previousMoveInputY;
            previousMoveInputY = downNow;

            if (downJustPressed)
            {
                if (Time.time - lastDownTapTime <= DownDoubleTapWindow)
                {
                    if (fighterController.IsGrounded)
                        fighterController.DropThroughPlatform();
                    lastDownTapTime = -1f;   // 消费掉，防三连下误触发
                }
                else
                {
                    lastDownTapTime = Time.time;
                }
            }

            // ===== 2. 跳跃（按下的瞬间处理一次）=====
            if (JumpPressed)
            {
                fighterController.TryJump();
                JumpPressed = false;
            }

            // ===== 3. 攻击（4 分支，顺序敏感）=====
            if (AttackPressed)
            {
                FighterState st = fighterController.StateMachine.CurrentState;

                // ① 被抓中：攻击键 = 挣扎
                if (st == FighterState.Grabbed)
                {
                    fighterController.OnMashGrabEscape();
                    chargePending = false;      // 顺手取消可能残留的判定窗
                }
                // ② 抓取中：攻击键 = 投掷
                //    必须走这里，绝不能掉进下面的判定窗 —— 否则长按会触发 StartCharge，
                //    把 Grab 状态顶成 Attack，导致投掷失效 + 双方死锁。
                //    传什么 AttackData 无所谓：TryAttack 开头会拦截 grabTarget 并执行 PerformThrow。
                else if (st == FighterState.Grab)
                {
                    AttackPressed = false;
                }
                // ③ 空中 / 地面无方向 → 立即出招，零延迟
                else if (fighterController.StateMachine.IsInAir()
                         || (Mathf.Abs(MoveInput.x) <= 0.5f && Mathf.Abs(MoveInput.y) <= 0.5f))
                {
                    AttackData attack = GetImmediateAttack();
                    if (attack != null) fighterController.TryAttack(attack);
                }
                // ④ 地面 + 有方向 → 进入判定窗（等 0.15s 区分短按/长按）
                else
                {
                    pendingAttackDir = MoveInput;   // 方向快照！以按下瞬间为准
                    chargePending = true;
                }

                AttackPressed = false;   // 消费
            }

            // ===== 4. 短按/长按判定窗 =====
            if (chargePending)
            {
                float held = Time.time - AttackPressTime;
                if (held >= longPressThreshold)
                {
                    chargePending = false;
                    fighterController.StartCharge(GetSmashAttack(pendingAttackDir));   // 长按 → 蓄力
                }
                else if (!AttackHeld)
                {
                    chargePending = false;
                    fighterController.TryAttack(GetTiltAttack(pendingAttackDir));      // 短按 → 强攻击
                }
                // 都不到 → 继续等
            }

            // ===== 5. 蓄力中松手 → 释放 =====
            if (fighterController.isCharging && !AttackHeld)
                fighterController.ReleaseCharge();

            //// ===== 6. 特殊攻击 =====
            //if (SpecialPressed)
            //{
            //    fighterController.TrySpecial(GetContextualSpecial(pendingSpecialDir));
            //    SpecialPressed = false;
            //}

            // ===== 7. 抓取（按下的瞬间处理一次）=====
            if (GrabPressed)
            {
                FighterState st = fighterController.StateMachine.CurrentState;
                if (st == FighterState.Grab && fighterController.grabTarget != null)
                {
                    // 已抓住人 → 按抓取键 = 投掷（方向由当前摇杆决定）
                    fighterController.TryAttack(fighterController.fighterData.jab1);
                }
                else
                {
                    fighterController.TryGrab();
                }
                GrabPressed = false;
            }

            // ===== 8. 防御与受身（按住/松开，每帧同步）=====
            fighterController.SetShielding(ShieldHeld);
            fighterController.TechInputHeld = ShieldHeld;   // 受身输入复用护盾键
        }

        #endregion

        #region 6. 招式选择工具

        // 【做什么】立即攻击：空中（四向 + 空 N）/ 地面无方向（jab）—— 零延迟路径
        // 【注意】空中方向判定必须用"当前 MoveInput"（空中没有判定窗，按下即出）
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

        // 【做什么】短按 → tilt（强攻击）
        // 【参数】dir = 按下瞬间的方向快照
        private AttackData GetTiltAttack(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > 0.5f)
                return dir.y > 0 ? fighterController.fighterData.tiltUp
                                 : fighterController.fighterData.tiltDown;
            return fighterController.fighterData.tiltSide;   // 横向（前/后同招）
        }

        // 【做什么】长按 → smash（蓄力基值）
        // 【参数】dir = 按下瞬间的方向快照
        private AttackData GetSmashAttack(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > 0.5f)
                return dir.y > 0 ? fighterController.fighterData.smashUp
                                 : fighterController.fighterData.smashDown;
            return fighterController.fighterData.smashSide;
        }

        // 【做什么】必杀技按方向选招（前/后共用同一招）
        private AttackData GetContextualSpecial(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > 0.5f)
                return dir.y > 0 ? fighterController.fighterData.specialUp
                                 : fighterController.fighterData.specialDown;
            if (Mathf.Abs(dir.x) > 0.5f)
                return fighterController.fighterData.specialSide;
            return fighterController.fighterData.specialNeutral;
        }

        #endregion

        // 【做什么】清掉所有一次性 latch + 方向，防"非战斗期按的键"在进战斗/解锁瞬间爆发
        private void ClearLatchedInput()
        {
            JumpPressed = false;
            AttackPressed = false;
            SpecialPressed = false;
            GrabPressed = false;
            TauntPressed = false;
            chargePending = false;
            specialChargePending = false;
            pendingAttackDir = Vector2.zero;
            pendingSpecialDir = Vector2.zero;
            previousMoveInputY = false;
            fighterController.MoveInput = Vector2.zero;   // 不给方向 → 不转身
        }
    }
}
