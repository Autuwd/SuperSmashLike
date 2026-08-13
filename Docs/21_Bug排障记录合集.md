# 21. Bug 与 Debug 排障记录合集（Doc19 + 后续）

> 本文件汇总 SuperSmashLike 项目开发过程中遇到的全部 Bug 与 Debug 流程，一次查看。
> 格式统一为 Doc19 六段式：**现象 → 根因（一句话结论）→ 调试过程 → 解决方案 → 可复用知识点 → 遗留事项**。
>
> - **Part A** = Doc19 输入系统排障（2026-08-11，角色无法操作）
> - **Part B** = S1 实操排障系列 10 项（2026-08-12，`FighterController.cs` / `InputManager.cs` / Animator 配置）
>
> 之后新 bug 持续追加到 Part C。

---

# Part A · 输入系统排障：角色无法操作（08-11）

> 日期：2026-08-11
> 状态：✅ 已解决（输入链路验证通过）
> 涉及文件：`Assets/_Game/Scripts/Input/InputManager.cs`、`Fighter_Base.prefab`、`Controls.inputactions`、`BattleArena.prefab`、`TagManager.asset`

## 1. 现象

- 角色无法通过键盘操作：移动 / 跳跃 / 攻击均无反应
- Console 无任何报错，仅静默失效
- 已确认：输入系统已启用（`Active Input Handling` = Input System Package (New)）、`Controls.inputactions` 配置完整（WASD/方向键/手柄绑定齐全）、PlayerInput 组件配置正确

## 2. 根因（一句话结论）

**PlayerInput 组件通过 InputUser 关联 actions 资产时，会对整个资产施加「scheme 组过滤」+「设备需求过滤」。当 InputUser 未配对任何设备（`pairedDevices.Count = 0`，scheme 声明需要 Keyboard 但配对从未发生）时，整个资产的绑定解析全部失败（解析控件数 = 0），任何按键都不产生信号。**

排除过程中曾尝试清除 `asset.bindingMask`，**无效**——InputUser 层存在更深层的设备过滤。

## 3. 调试过程（分层诊断，逐层收敛）

> 排查思路：自顶向下确认「配置 → 硬件 → 资产 → 配对 → Action 信号 → 绑定解析」每一层，用日志证据锁定断点。

### 3.1 第一轮：配置层检查（未发现问题）

| 检查项 | 结果 |
|---|---|
| `Controls.inputactions` 三个 scheme 的 devices 补全（KeyboardP1/P2→Keyboard、Gamepad→Gamepad） | ✅ JSON 校验通过 |
| `TagManager.asset` Layer 6 = Ground | ✅ 已添加 |
| `BattleArena.prefab` 4 个平台 `m_Layer` → 6 | ✅ |
| `Fighter_Base.prefab` `groundLayer m_Bits` → 64、GroundCheck y → -0.1 | ✅ |
| `PlayerInput`：`m_NotificationBehavior` → 3 (Send Messages)、`m_DefaultControlScheme` → KeyboardP1、`m_DefaultActionMap` → Gameplay 的 GUID | ✅ |
| `.inputactions` meta GUID 与 prefab `m_Actions` 引用一致 | ✅ |

> ⚠️ 本轮有个干扰项：PlayerInput 与 Unity 同名单 `UnityEngine.InputSystem.InputSystem` 与自定义命名空间 `SuperSmashLike.InputSystem` 冲突，静态调用必须全限定（先写 `InputSystem.devices` 会报 CS0234）。

### 3.2 第二轮：硬件与资产层（正常）

```log
[InputManager] 输入系统设备(3): Keyboard:Keyboard, Mouse:Mouse, Pen:Pen   ← 键盘设备存在
[InputManager] Actions资产: 有效(Controls) | User配对设备(0):              ← 资产有效，但配对=0
[InputManager] Start确认 | User配对设备数: 0 | 激活的ActionMap: Gameplay   ← Start 后仍为 0
```

设备在、资产有效、Gameplay 激活 → 但 **Action 层无任何信号**（SendMessage 回调、`onActionTriggered` 均无输出）。

### 3.3 第三轮：绑定解析层（确认死点）

```log
[Direct] Move: enabled=True | 已解析控件数=0 | 绑定数=10   ← ★ 绑定存在但一个控件都没解析到
[Direct] binding: path='<Gamepad>/leftStick' | groups=';Gamepad'
[Direct] binding: path='<Keyboard>/w'        | groups=''      ← WASD 是 NoScheme 组
[Direct] binding: path='<Keyboard>/upArrow'  | groups=';KeyboardP2'
```

- 绑定路径完全正确，但 `controls.Count = 0` → **绑定解析失败**
- 硬件层无问题：`[Poll] W按下=True`（键盘事件正常进入输入系统）

### 3.4 第四轮：bindingMask 实验（发现 scheme 过滤）

```log
[Mask] asset.bindingMask=KeyboardP1 | map.bindingMask=无 | move.bindingMask=无
```

- **asset 级 bindingMask = "KeyboardP1"** —— InputUser 关联时设置的 scheme 组过滤
- Move 的 WASD 绑定组为空（NoScheme）、摇杆为 `;Gamepad`、方向键为 `;KeyboardP2`——**没有一条属于 KeyboardP1 组** → 全部被过滤

**尝试清除后依然 0**：

```log
[ClearMask] 已清除 asset.bindingMask | Move enabled=True | 解析控件数=0
```

→ 除了 bindingMask，InputUser 层还有设备需求过滤（scheme 需要 Keyboard 但未配对）。

### 3.5 第五轮：终极隔离实验（实锤根因）

| 实验 | 解析控件数 | 输入信号 |
|---|---|---|
| 原资产（被 PlayerInput 接管） | **0** | ❌ 无 |
| **克隆资产**（`ToJson`+`LoadFromJson`，绕过 PlayerInput/InputUser） | **8** | ✅ `Move performed value=(0.00, 1.00)`（按住 W） |

```log
[Clone] 克隆资产 Move enabled=True | 解析控件数=8 | map.enabled=True
[FindControl] Keyboard/w => W
[Clone] Move performed value=(0.00, 1.00)      ← W 键 = 上方向，输入完全正常！
```

**结论：绑定、设备、资产全部正常；故障 100% 在 PlayerInput/InputUser 关联层。**

## 4. 解决方案

**InputManager 弃用 PlayerInput 的 SendMessage 机制，运行时 JSON 克隆一份独立资产自行管理**（就是第五轮验证通过的路径）。

### 4.1 核心改动（InputManager.cs）

```csharp
private void Awake()
{
    playerInput = GetComponent<PlayerInput>();
    // 禁用 PlayerInput：其 InputUser 关联会污染整个资产导致绑定解析失败（根因）
    playerInput.enabled = false;
    // JSON 克隆独立资产：与 InputUser 完全隔离
    runtimeActions = ScriptableObject.CreateInstance<InputActionAsset>();
    runtimeActions.LoadFromJson(playerInput.actions.ToJson());
}

private void OnEnable()
{
    // 手动订阅全部 Action 回调
    gameplayMap = runtimeActions.FindActionMap("Gameplay");
    gameplayMap.FindAction("Move").performed += OnMoveCtx;        // Value 类型用 performed
    gameplayMap.FindAction("Jump").started += OnJumpCtx;          // Button 类型用 started（按下瞬间）
    gameplayMap.FindAction("Shield").started += OnShieldStartCtx; // 按住态：started=按下
    gameplayMap.FindAction("Shield").canceled += OnShieldCancelCtx; // canceled=松开
    gameplayMap.Enable();
}

private void OnDisable()
{
    // 必须成对反订阅 + Disable，避免多次 OnEnable 重复订阅
    gameplayMap.FindAction("Move").performed -= OnMoveCtx;
    // ...（其余同理）
    gameplayMap.Disable();
}
```

### 4.2 要点

- **必须克隆**：直接用 `playerInput.actions`（被 InputUser 关联的原始资产）依然解析 0 控件。JSON 克隆彻底隔离。
- **PlayerInput 组件保留但禁用**：留在 prefab 上无副作用（不执行 OnEnable → 不再关联 InputUser）。
- **回调签名**：从 `OnMove(InputValue)`（SendMessage）改为 `OnMoveCtx(InputAction.CallbackContext)`（事件回调）。
- 验证通过：移动 ✅ 跳跃 ✅ 攻击（地面/空中攻击区分正确）✅

## 5. 可复用知识点 / 快速排查清单

以后再遇到「输入没反应」按此顺序排查（自底向上验证，每层有日志证据再往下）：

| 步骤 | 检查 | 观测点 | 正常信号 |
|---|---|---|---|
| 0 | Active Input Handling | ProjectSettings `activeInputHandler` = 2 | 新输入系统已启用（需重启编辑器生效） |
| 1 | 硬件层 | `InputSystem.devices` | 列表含 Keyboard/Gamepad |
| 2 | 资产层 | `playerInput.actions` 非空 | 资产名正确 |
| 3 | 配对层 | `playerInput.user.pairedDevices.Count` | ⚠️ 0 是 PlayerInput 隐患信号 |
| 4 | Action 信号 | `playerInput.onActionTriggered` | 按键时有回调 |
| 5 | **绑定解析** | `action.controls.Count` | **> 0（黄金指标）** |
| 6 | 过滤因素 | `asset.bindingMask?.groups` | 正常应为空 |
| 7 | **隔离实验** | JSON 克隆资产 + Enable | controls > 0 → 锁定 PlayerInput 层 |

**核心教训：**
1. `action.controls.Count`（已解析控件数）是判断输入断点的**黄金指标**——绑定存在但解析不到设备，一切输入都静默失效。
2. PlayerInput 的 scheme 激活依赖设备配对；**未配对（Unpaired Devices 模式下 requirements 未满足）会导致整个资产绑定解析失败**，且清 `asset.bindingMask` 无效（InputUser 层过滤更深）。
3. 隔离实验（克隆资产）是区分「PlayerInput 层问题」和「输入管线底层问题」的决定性手段。
4. 命名空间陷阱：自定义命名空间不要用 `InputSystem` 等与 Unity 同名，或静态调用全限定。

## 6. 遗留事项（本次未处理）

| 事项 | 阶段 | 说明 |
|---|---|---|
| 角色动画缺失 | Phase 1 | `FighterAnimator.controller` 8 个状态 `m_Motion` 全空；状态转换未使用代码驱动的 `State` 整数参数（Attack/Hit/Shield/Knockback 不可达）；`Damage` 参数缺失（代码 SetFloat 静默失败）。做动画时需重建转换架构或 Editor 工具生成占位动画 |
| P2 双人输入 | Phase 2+ | 克隆资产无 scheme 隔离，P2 也会响应同一键盘。将来需 PlayerInputManager SplitKeyboard 或手柄分配方案 |

---

# Part B · S1 实操排障系列（08-12）

> 日期：2026-08-12
> 状态：10 项全部定位根因；9 项已修复，排障 10（动画自切）修复方案已定待执行
> 涉及文件：`Assets/_Game/Scripts/Character/FighterController.cs`、`Assets/_Game/Scripts/Input/InputManager.cs`、`FighterAnimator.controller`、`Fighter_Base.prefab`

> 共性调试方法：**在 `TransitionTo` 打日志 → 看调用栈**，快速锁定「谁在错误地触发状态切换」。

## 排障 1 · MoveInput 不归零 → 滑行/落地直接 Run

### 现象
- 松开方向键后角色仍朝最后方向滑行
- 落地瞬间直接进入 Run 状态而非 Idle

### 根因（一句话结论）
**Move 是 Value 类型 Action，松开全部按键触发的是 `canceled`（不是 performed）；`InputManager` 只订阅了 `performed` → `MoveInput` 卡在最后按下的值永远不归零。**

### 解决方案
补 `Move.canceled += OnMoveCtx`（canceled 时 `ReadValue<Vector2>()` 返回 zero）。

### 可复用知识点
- **Input System 三事件**：`started`（按下瞬间）/ `performed`（值变化）/ `canceled`（释放）
- **Value 类 Action 必须双订阅 performed + canceled**；或改用轮询 `ReadValue`

## 排障 2 · SetShielding 每帧强切 Idle → 状态机全污染

### 现象
- 跳跃 / 攻击 / 受击状态全部不稳，起跳的 Jump 同帧被打回 Idle

### 根因（一句话结论）
**`InputManager.Update` 每帧调用 `SetShielding(false)` → 其内部无条件 `TransitionTo(Idle)` → 起跳的 Jump 同帧被打回 Idle，状态永远不稳。**

### 调试过程
在 `TransitionTo` 打日志 → 看调用栈 → 抓到 `InputManager→SetShielding`。

### 解决方案
SetShielding 加**状态变化检测**（仅盾状态真实变化才 `TransitionTo`）。

### 可复用知识点
**每帧调用方法里的无条件 `TransitionTo` = 状态机污染源**；状态切换必须带变化检测。

## 排障 3 · 快速下落失效（两个经典坑）

### 现象
- 空中按下下落键，角色快速下落不生效

### 根因（一句话结论）
两个独立坑叠加：
1. **`Mathf.Max` 方向反**：下落更快 = 数值**更负**，应改用 `Mathf.Min`（口诀：向上用 Max，向下用 Min）
2. **悬垂 else**：fallSpeed 钳制插入 if-else 链中间 → 落地处理被 else 劫持（C# else 绑定**最近的未配对 if**）

### 解决方案
- 钳制改用 `Mathf.Min`
- 重排 if-else 结构，避免悬垂 else

### 可复用知识点
向下加速类逻辑统一用 Min；插入 else 链前先确认配对关系。

## 排障 4 · fallSpeed 语义（上限 vs 实际值）

### 现象
- 调大 `fallSpeed` 但下落速度没变化

### 根因（一句话结论）
**`fallSpeed` 是上限（钳制值），角色实际落速 -7.5 够不着 -20 → 改大无效（参数没触到阈值 = 改了像没改）。**

### 解决方案（两选一）
1. 下落直接 `= -fallSpeed`（直觉：改大更快）
2. 保留重力 + 钳制（物理：调 `gravityScale` 管下落快慢）

### 可复用知识点
调参前先确认参数是「目标值」还是「上限钳制」——触不到阈值时调参无效。

## 排障 5 · 跳跃调参公式

### 现象
- 想调「跳低 + 落地快」却没好用的参数组合

### 知识点（公式）
- 最大高度 `h = v² / 2g`；空中总时间 `t = 2v / g`
- 想「跳低 + 落地快」= **降 `jumpForce` + 升 `gravityScale`**

## 排障 6 · 双击方向键 → 跑步（键盘）

### 现象
- 键盘上双击方向键想触发跑步，识别不准

### 根因（一句话结论）
**键盘输入离散 ±1（无摇杆中间值）**，需要区分「按住」与「再次按下」。

### 解决方案
**边沿检测**（`currentDir != 0 && lastTapDir == 0` 识别"刚按下"）+ **时间窗口**（0.25s）+ **方向记忆**。

### 可复用知识点
边沿检测 = `GetButtonDown` 的底层原理。

## 排障 7 · 碰撞卡边缘

### 现象
- Fall + `IsGrounded=false` + 角色不动 = 卡在平台边缘

### 根因（一句话结论）
**Box vs Box 边缘互卡**（直角边互相咬住）。

### 解决方案
角色主碰撞体换 **CapsuleCollider2D + Edge Radius=0.1**（圆角平滑）；碰撞体比模型略小。

## 排障 8 · Animator 挂载与 Avatar

### 现象
- 动画不播放 / 模型自带 Animator 冲突

### 根因与解决方案
1. `GetComponentInChildren<Animator>()` → Animator 要挂 **Visual 子物体**（动画事件要求组件在同一物体）
2. **Synty 模型自带 Animator 必须删**（防冲突 / 防 `GetComponentInChildren` 拿错）
3. **Humanoid 动画必须填 Avatar**（模型 Rig 设 Humanoid → Apply → Avatar 拖到 Animator.Avatar）；Generic 不需要

### 可复用知识点
换皮后 Animator 挂载点 + Avatar 是最常见的动画失效源。

## 排障 9 · 2D 格斗侧面朝向

### 现象
- 3D 模型导入后正面朝镜头，看不到格斗侧面剪影

### 根因（一句话结论）
**模型默认面朝 +Z（正对镜头）**。

### 解决方案
模型子物体 **Y 旋转 90°**（面朝 X 轴，镜头在 Z 轴）→ 侧面剪影；根物体 Y 0/180 做左右翻转。

## 排障 10 · 动画快/闪烁/自切（今日最大战场）

### 现象三连
- **动画定住**：循环动画播一遍停在结尾
- **动画快**：跑动动画像抽搐
- **疯狂自切**：动画每帧重播闪烁

### 根因（一句话结论）
三个独立问题：
1. **定住** = 循环动画 **Loop Time 未勾**（播一遍停在结尾）
2. **快** = clip 短（Sprint 0.6s）；节奏公式 **一圈时长 = clip.Length ÷ 状态 Speed**（0.6s÷0.5=1.2s 正常跑）；Sprint 换 `A_Run` 更干净
3. **自切 = AnyState 重入死循环**：AnyState 转换 + 条件恒满足（`State==n`）+ 目标==当前状态 → **无限重入**（动画每帧重播）。单条 AnyState→Idle 二分测试确认

### 解决方案
**每条 AnyState 转换取消勾选 `Can Transition To Self`**（禁止重入当前状态）——标准解法。

备选①：普通状态→状态转换（有重入保护，需按转换矩阵配 50-70 条）
备选②：`animator.CrossFade(状态名, 0.05f)` 代码强制切换（Animator 退化为动画仓库）

### 可复用知识点
- **AnyState 只适合"一次性/低频"条件**（Trigger / 死亡）；持续恒值条件（`State==n`）用普通转换或取消 `Can Transition To Self`
- **Animator 参数类型必须匹配**：`SetInteger` 对 Float 参数**静默失效**

## Part B 遗留事项

| 事项 | 状态 |
|---|---|
| 动画自切修复落地 | ⏳ 待执行：勾掉 10 条 AnyState 转换的 `Can Transition To Self`（或改普通转换 / CrossFade）→ 动画正常 → 跑 S1 验证清单 → 收 S1 |

---

*记录人：Sora（开发导师）与主人的联合调试*