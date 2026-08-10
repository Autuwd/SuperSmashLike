# 📖 Phase 3 手把手实现手册：S0 SO 资产 + S1 角色控制

> 覆盖范围：Day13 → Day15 计划中的项目实现板块（S0 + S1 起步）
> 使用方式：**全程 Unity 编辑器手工操作，不写一行代码**（代码骨架已就绪，本手册只做「装配」）
> 环境：Unity 2022（2D 项目 · Input System 包已安装）
> 上次实操教训已内嵌到各步骤的 ⚠️ 提示中

---

## 0️⃣ 本手册对应的计划行

| 计划 | 内容 |
|------|------|
| Day13 项目实现 | Phase 3 启动 · **S0 SO 资产**：`GameSettingsSO`+`FighterDataSO`+`StageDataSO` |
| Day14 项目实现 | S0 收尾 + **S1 角色控制起步**：4 个 FighterDataSO asset + Fighter Prefab 骨架 + 移动跳跃 |
| Day15 项目实现 | S0 遗留（3 SO 资产 + 4 角色数据）→ S1（Animator 4 状态 + Input System 绑定 + 状态机框架） |

**完成本手册后你将拥有**：一个可 Play 的 2 人格斗 Demo 场景（BattleTest），包含 4 个角色数据资产、2 个可操作角色（WASD/方向键控制移动、跳跃、攻击、防御）。

---

## 1️⃣ 开始前检查清单（5 分钟）

### 1.1 确认脚本代码已就绪（无需修改）

确认 `Assets/_Game/Scripts/` 下存在以下 16 个脚本（本手册只装配、不改码）：

```
Character/  FighterController.cs  FighterStateMachine.cs
Combat/     DamageSystem.cs  Hitbox.cs  Hurtbox.cs
Core/       GameManager.cs  ObjectPooler.cs
Data/       FighterData.cs  GameSettings.cs  StageData.cs
Editor/     DemoSetupWizard.cs  FighterDataEditor.cs
Input/      InputManager.cs
Managers/   CameraManager.cs  MatchManager.cs
Stage/      BlastZone.cs  Platform.cs
```

### 1.2 确认无编译错误

- 打开 Unity，等编译完成，看 **Console** 窗口：应无红色 Error（有黄色警告不阻塞，先记下）

### 1.3 ⚠️ 关键检查：Input System 是否启用（上次教训 2 的根源）

上次角色「能跑但不能控制」的第一嫌疑就是这里，现在**先确认再动手**：

1. 菜单 `Edit → Project Settings`
2. 左侧选 **Player**，右侧滚到 **Other Settings** 区域
3. 找到 **Active Input Handling**：
   - 显示 **Both** 或 **Input System Package (New)** → ✅ 已启用，继续（无需改动）
   - 显示 **Input Manager (Old)** → ⚠️ 改成 **Both**（选完会提示重启编辑器，重启后再继续）

> 💡 底层原理：这个开关写进 `ProjectSettings.asset` 的 `activeInputHandler: 2`（2=Both）。上次已确认本机是 2，正常。

---

## 2️⃣ 阶段 A：创建文件夹结构（5 分钟）

在 **Project 窗口**（左下）右键 `Assets/` → `Create → Folder`，逐个创建（注意路径含 `/` 表示层级，逐级建）：

```
Assets/_Game/
├── ScriptableObjects/
│   ├── Characters/      ← 角色数据资产（4 个 FD_）
│   ├── Stages/          ← 舞台数据资产（1 个 SD_）
│   └── GameModes/       ← 全局规则资产（1 个 GS_）
├── Prefabs/
│   ├── Fighters/        ← 角色预制体
│   ├── Stages/          ← 舞台预制体
│   └── Effects/         ← 特效（本次留空）
├── Animations/          ← Animator Controller
├── Scenes/              ← BattleTest 场景
└── Input/               ← Controls.inputactions
```

操作提示：可以在 Project 窗口顶部搜索框输入 `_Game` 定位，右键 `_Game` 文件夹依次新建子目录。

---

## 3️⃣ 阶段 B（S0）：创建 6 个 ScriptableObject 资产

> 概念关联（面试点）：SO = 数据驱动设计、享元模式——一份数据资产被多个对象共享，改一处全生效。

### B1. GameSettings 资产（GS_Default）

1. 在 Project 窗口选中 `Assets/_Game/ScriptableObjects/GameModes/` 文件夹
2. 右键 → `Create → SuperSmashLike → Game Settings`
3. 资产自动命名为 `GS_NewSettings`，**按 F2 重命名为 `GS_Default`**（改资产文件名，不是改内部名字）
4. 选中它，Inspector 按以下表格填写（对照源码 `GameSettings.cs` 字段）：

| 字段（分组） | 值 | 说明 |
|---|---|---|
| **Match Rules** | | |
| Match Mode | `Stock` | 命数制 |
| Stock Count | `3` | 每人 3 条命 |
| Match Time Seconds | `300` | 时间制备用值 |
| Damage Ratio | `1` | 伤害比例 |
| Enable Team Mode | 不勾 | |
| **Spawn** | | |
| Blast Zone Width | `(30, 30)` | 左右边界 |
| Blast Zone Height | `20` | 上下边界 |
| Respawn Time | `3` | 重生等待秒数 |
| Respawn Invincibility Frames | `180` | 无敌帧（=3秒@60fps） |
| **Combat** | | |
| Hitstop Scale | `1` | 打击暂停 1 倍 |
| Shield Size | `1.5`（默认） | 本次用不到 |
| Shield Max HP | `100` | 护盾耐久 |
| Shield Regen Per Second | `10` | 护盾恢复 |
| Enable Friendly Fire | 不勾 | |
| **Items** | | |
| Enable Items | **不勾** | 本次关道具 |

5. 填完检查一遍，然后**保存项目**（`Ctrl+S` 或菜单 File → Save）。

> ⚠️ 如果右键菜单里找不到 `SuperSmashLike` 子菜单：说明脚本没有编译成功（回到 1.2 查 Console），或者没有选中正确的文件夹 —— 右键菜单的 Create 选项只出现在**选中文件夹**时。

### B2. FighterData 资产 ×4（FD_Knight / FD_Archer / FD_Mage / FD_Barbarian）

1. 选中 `ScriptableObjects/Characters/` 文件夹
2. 右键 → `Create → SuperSmashLike → Fighter Data`，生成后改名为 `FD_Knight`
3. **重复 3 次**，创建 `FD_Archer`、`FD_Mage`、`FD_Barbarian`（共 4 个）

#### B2.1 每个资产的身份 + 移动字段（对照 `FighterData.cs` 源码）

| 字段 | FD_Knight | FD_Archer | FD_Mage | FD_Barbarian | 代码含义 |
|---|---|---|---|---|---|
| Fighter Name | `Knight` | `Archer` | `Mage` | `Barbarian` | 显示名 |
| Weight | `110` | `85` | `75` | `120` | 体重：越大越难被击飞 |
| Walk Speed | `4.5` | `6.5` | `5.5` | `4.0` | 地面行走速度 |
| Run Speed | `7` | `9` | `8` | `6` | 冲刺速度（本版代码暂未用） |
| Air Speed | `5` | `7` | `7.5` | `4.5` | 空中水平速度 |
| Jump Force | `10` | `13` | `14` | `8` | 一段跳初速度 |
| Double Jump Force | `9`（默认） | `9`（默认） | `9`（默认） | `9`（默认） | 二段跳（本版代码用 jumpForce×0.85） |
| Jump Count | `2` | `2` | `3` | `2` | 可跳次数 |
| Air Dodge Count | `1`（默认） | `1` | `1` | `1` | 空中闪避 |
| Fall Speed | `8`（默认） | `8` | `8` | `8` | 下落速度（本版代码暂未用） |
| Fast Fall Speed | `15`（默认） | `15` | `15` | `15` | 速降（暂未用） |
| **Visual** | | | | | |
| Weight Class | `Medium` | `Light` | `Light` | `SuperHeavy` | 体重分类 |
| UI Color | 随意（建议各角色一个颜色） | | | | UI 主题色 |

> 数值出处：计划 Day15 行「Knight 110/4.5、Archer 85/6.5、Mage 75/5.5、Barbarian 120/4.0」+ DemoSetupWizard 源码。

#### B2.2 攻击数据（Attack Data 分组）—— 4 个角色完全相同，逐个展开填写

这是最繁琐的一步。每个攻击项是一个折叠组（`AttackData` 内嵌类），**点开小三角展开填写**。表格中每个攻击的 8 个数值照抄：

| 攻击项 | Attack Name | Damage | Knockback Angle | Knockback Base | Knockback Growth | Startup | Active | Recovery |
|---|---|---|---|---|---|---|---|---|
| Jab 1 | `Jab1` | 3 | 80 | 10 | 20 | 0.05 | 0.05 | 0.1 |
| Jab 2 | `Jab2` | 3 | 80 | 15 | 25 | 0.05 | 0.05 | 0.1 |
| Jab 3 | `Jab3` | 5 | 70 | 30 | 40 | 0.08 | 0.05 | 0.15 |
| Tilt Side | `SideTilt` | 8 | 40 | 30 | 50 | 0.1 | 0.07 | 0.2 |
| Tilt Up | `UpTilt` | 7 | 90 | 35 | 45 | 0.1 | 0.08 | 0.2 |
| Tilt Down | `DownTilt` | 6 | 0 | 30 | 40 | 0.08 | 0.07 | 0.17 |
| Smash Side | `SmashSide` | 15 | 45 | 50 | 80 | 0.3 | 0.07 | 0.3 |
| Smash Up | `SmashUp` | 14 | 90 | 55 | 75 | 0.28 | 0.08 | 0.28 |
| Smash Down | `SmashDown` | 13 | 0 | 50 | 70 | 0.25 | 0.07 | 0.3 |
| Aerial Neutral | `AirN` | 8 | 70 | 35 | 40 | 0.08 | 0.15 | 0.15 |
| Aerial Forward | `AirF` | 10 | 50 | 40 | 55 | 0.1 | 0.1 | 0.18 |
| Aerial Back | `AirB` | 10 | 60 | 40 | 55 | 0.1 | 0.1 | 0.18 |
| Aerial Up | `AirU` | 8 | 90 | 35 | 45 | 0.08 | 0.15 | 0.2 |
| Aerial Down | `AirD` | 9 | 0 | 40 | 50 | 0.1 | 0.15 | 0.12 |

每个攻击项的 **Hitstop Duration**：攻击项 Damage > 10 的填 `0.08`，其余填 `0.04`。
（Shield Damage / Cancel / Visual 等其余字段保持默认即可。）

> 💡 快速核对法：填完后 Inspector 底部会出现自定义面板 **Statistics Overview —— Total Attack Count: 14**（这是 `FighterDataEditor.cs` 做的，14 个攻击都有值即成功）。

### B3. StageData 资产（SD_Arena）

1. 选中 `ScriptableObjects/Stages/` 文件夹
2. 右键 → `Create → SuperSmashLike → Stage Data`，改名 `SD_Arena`
3. 填写：

| 字段 | 值 | 说明 |
|---|---|---|
| Stage Name | `Battle Arena` | |
| Stage Prefab | 留空（阶段 F 做好舞台预制体后回来拖） | ⚠️ 稍后填 |
| Camera Bounds Min | `(-20, -10)` | 摄像机左下界 |
| Camera Bounds Max | `(20, 15)` | 摄像机右上界 |
| Blast Zone Width | `30` | |
| Blast Zone Height | `20` | |
| Spawn Points | 留空（阶段 G 场景里拖） | ⚠️ 稍后填 |

### ✅ B 阶段验证

- Project 窗口 `GameModes/GS_Default.asset`、`Characters/FD_*.asset ×4`、`Stages/SD_Arena.asset` 全部存在
- 每个资产双击能在 Inspector 看到字段，FD 资产底部显示 `Total Attack Count: 14`

---

## 4️⃣ 阶段 C（S1）：创建 Input Actions 资产（Controls.inputactions）

> 职责：定义「按哪个键 = 哪个操作」。InputManager.cs 用 **Send Messages 模式**：PlayerInput 组件收到操作后自动调用同名方法（`OnMove`/`OnJump`/`OnAttack`…），**方法名必须与下面创建的 Action 名完全一致**。

### C1. 创建资产

1. 选中 `Assets/_Game/Input/` 文件夹
2. 右键 → `Create → Input Actions`，命名为 `Controls`
3. 双击打开 **Input Actions 编辑器** 窗口

### C2. 创建 Action Map

1. 点 **`+` → New Action Map**，重命名为 **`Gameplay`**
2. 选中 Gameplay，看右侧 Inspector：

| 字段 | 值 |
|---|---|
| Name | (空，保持默认即 map 名) |
| **Auto-Save** | ✅ 勾上（编辑自动保存） |

### C3. 创建 8 个 Action

选中 Gameplay，点 **`+ → New Action`** 8 次，逐个重命名并设类型：

| # | Action 名 | Action Type | Control Type |
|---|---|---|---|
| 1 | `Move` | Value | Vector 2 |
| 2 | `Jump` | Button | (无/Any) |
| 3 | `Attack` | Button | |
| 4 | `Special` | Button | |
| 5 | `Shield` | Button | |
| 6 | `Grab` | Button | |
| 7 | `Taunt` | Button | |
| 8 | `Pause` | Button | |

（选中 Action 后，右侧 Inspector 的 Properties 区设置 Action Type / Control Type。）

> ⚠️ Action 名必须一字不差：InputManager 靠 `On<Action名>` 反射调用，拼错 = 输入永远不生效（上次疑点之一）。

### C4. 绑定按键（Bindings）

选中某个 Action → 下方 Binding 区域点 **`+` → Add Binding**，按表格逐个添加（`Groups` 列 = 在 Binding 的 Inspector 里勾选 Control Scheme）：

**Move（5 个绑定）**：

| Path | Groups |
|---|---|
| `Gamepad/leftStick` | Gamepad |
| `Keyboard/w` | KeyboardP1 |
| `Keyboard/a` | KeyboardP1 |
| `Keyboard/s` | KeyboardP1 |
| `Keyboard/d` | KeyboardP1 |
| `Keyboard/upArrow` | KeyboardP2 |
| `Keyboard/leftArrow` | KeyboardP2 |
| `Keyboard/downArrow` | KeyboardP2 |
| `Keyboard/rightArrow` | KeyboardP2 |

**Jump（3 个）**：`Gamepad/buttonSouth`(Gamepad)、`Keyboard/space`(KeyboardP1)、`Keyboard/numpad0`(KeyboardP2)

**Attack（3 个）**：`Gamepad/buttonWest`(Gamepad)、`Keyboard/j`(KeyboardP1)、`Keyboard/numpad1`(KeyboardP2)

**Special（3 个）**：`Gamepad/buttonNorth`(Gamepad)、`Keyboard/k`(KeyboardP1)、`Keyboard/numpad2`(KeyboardP2)

**Shield（3 个）**：`Gamepad/leftTrigger`(Gamepad)、`Keyboard/l`(KeyboardP1)、`Keyboard/numpad3`(KeyboardP2)

**Grab（3 个）**：`Gamepad/rightShoulder`(Gamepad)、`Keyboard/u`(KeyboardP1)、`Keyboard/numpad4`(KeyboardP2)

**Taunt（3 个）**：`Gamepad/dpad/up`(Gamepad)、`Keyboard/t`(KeyboardP1)、`Keyboard/numpad5`(KeyboardP2)

**Pause（3 个）**：`Gamepad/start`(Gamepad)、`Keyboard/escape`(**KeyboardP1 和 KeyboardP2 两个组都勾**)

> 操作细节：添加 Binding 时 Path 可以直接点右侧 **⚙ 图标 → 监听（Listen）**，然后在键盘/手柄上按一下目标键自动填入 —— 手打容易打错。

### C5. 创建 3 个 Control Scheme

回到窗口顶部，点 `+` **（Control Schemes 区）** 新建 3 个：

| Scheme 名 | Device Requirements |
|---|---|
| `KeyboardP1` | Keyboard、Mouse |
| `KeyboardP2` | Keyboard、Mouse |
| `Gamepad` | Gamepad |

（选中 scheme，右侧 Assign Existing Devices 或 Manually 添加设备类型。）

### ✅ C 阶段验证

- Input Actions 编辑器内：Gameplay 下有 8 个 ✓ Action、每个都有绑定 ✓
- 关闭窗口时选择 **Save**（若 Auto-Save 已勾选则自动保存）
- 确认项目 `Assets/_Game/Input/Controls.inputactions` 文件存在

> 💡 底层知识：该文件本质是 JSON（DemoSetupWizard 里直接写 JSON 字符串生成），编辑器可视化创建的也是同一个东西。手动创建后可以打开文本看内容结构。

---

## 5️⃣ 阶段 D（S1）：创建 Animator Controller（FighterAnimator）

> 职责：把「状态 → 动画」映射。`FighterController.UpdateAnimation()` 每帧写入 4 个参数驱动它。本次只搭**空状态骨架**（没有动画片段影片，只有状态名），动画 Clip 后续阶段再补——保证结构正确。

### D1. 创建资产

1. 选中 `Assets/_Game/Animations/` 文件夹
2. 右键 → `Create → Animator Controller`，命名 **`FighterAnimator`**
3. 双击打开 **Animator 窗口**

### D2. 添加 4 个参数（左侧 Parameters 面板 `+` 按钮）

| 参数名 | 类型 |
|---|---|
| `State` | Int |
| `Speed` | Float |
| `IsGrounded` | Bool |
| `VerticalSpeed` | Float |

> 对应 `FighterController.UpdateAnimation()`：`SetFloat("Speed",…)`、`SetBool("IsGrounded",…)`、`SetFloat("VerticalSpeed",…)`、`SetInteger("State",…)` —— 名字必须一致。

### D3. 创建 8 个状态（右键状态机空白处 → Create State → Empty）

| 状态名 | 是否设为默认 |
|---|---|
| `Idle` | ✅（右键 → Set as Layer Default State） |
| `Run` | |
| `Jump` | |
| `Fall` | |
| `Attack` | |
| `Hit` | |
| `Knockback` | |
| `Shield` | |

### D4. 添加 5 条转换（右键状态 → Make Transition → 点目标状态）

按表格设置每条转换的 **Conditions**（选中转换线，右侧 Inspector → Conditions 区 `+`）：

| # | 从 → 到 | 条件（Condition） |
|---|---|---|
| 1 | Idle → Run | `Speed` **Greater** `0.1` |
| 2 | Run → Idle | `Speed` **Less** `0.1` |
| 3 | Idle → Jump | `VerticalSpeed` **Greater** `0.1` **AND** `IsGrounded` **Equal** `False` |
| 4 | Jump → Fall | `VerticalSpeed` **Less** `0` |
| 5 | Fall → Idle | `IsGrounded` **Equal** `True` |

（第 3 条要加 2 个条件：点 `+` 两次，分别选参数和比较方式，右侧 Mode 选 If / If Not。）

### ✅ D 阶段验证

- Animator 窗口：Idle 为橙色默认状态，5 条箭头转换线，线右侧条件与上表一致
- Parameters 面板 4 个参数齐全

---

## 6️⃣ 阶段 E（S1）：搭建 Fighter_Base 预制体（⭐核心步骤）

> 上次实操卡住的装配环节全在这里，每一步都对照表格，别漏。

### E1. 创建根物体

1. 菜单 `GameObject → Create Empty`，命名 **`Fighter_Base`**（Hierarchy 中选中它）
2. 在 Inspector 把 Position 设为 `(0, 0, 0)`

### E2. 根物体挂组件（选中 Fighter_Base → Add Component）

**① Rigidbody2D**（Add Component → Physics 2D → Rigidbody 2D）：

| 字段 | 值 |
|---|---|
| Body Type | `Dynamic` |
| Gravity Scale | `3` |
| Constraints | ✅（勾选）Freeze Rotation Z |

**② BoxCollider2D**（Add Component → Physics 2D → Box Collider 2D）：

| 字段 | 值 |
|---|---|
| Size | `X=1, Y=1.8` |
| Offset | `X=0, Y=0.9` |
| (Is Trigger 不勾) | 实体碰撞，用于落地/防止互穿 |

**③ FighterController**（Add Component → 输入 `FighterController`）：

先别管字段，马上 E3-E5 建子物体后回来拖引用。

**④ InputManager**（Add Component → 输入 `InputManager`）。

**⑤ PlayerInput**（Add Component → 输入 `Player Input`）—— ⚠️ 关键组件：

| 字段 | 值 |
|---|---|
| Actions | 拖入 `Assets/_Game/Input/Controls.inputactions` |
| Default Map | `Gameplay` |
| Default Scheme | 先留空（场景里 P1/P2 分别设） |
| Behavior | `Send Messages`（默认即是，确认） |

> ⚠️ **上次教训 2 复盘**：没有 PlayerInput 组件，InputManager 的 `OnMove` 等回调永远不会被调用。这个组件必须挂在**每个角色**身上，Actions 必须拖上资产。之前预制的伙伴引用检查时只有 PlayerInput.Actions 有值才正常——这就是输入链路的最后一环。

### E3. 创建 Hurtbox 子物体

1. 右键 Hierarchy 的 `Fighter_Base` → `Create Empty`，命名 **`Hurtbox`**
2. 设 local Position = `(0, 0, 0)`（Parent 已经是它，所以用右上角 Reset 也行）
3. Add Component → **BoxCollider2D**：Size `(0.8, 1.6)`，**勾选 Is Trigger** ✅
4. Add Component → **Hurtbox**（脚本）

> 职责：标记「哪里能被攻击命中」。`FighterController.Awake()` 自动认领它作为 owner。

### E4. 创建 GroundCheck 子物体

1. 右键 `Fighter_Base` → `Create Empty`，命名 **`GroundCheck`**
2. local Position = `(0, -0.9, 0)`（脚底）
3. 不加任何组件（就是个空点）

> 职责：脚底检测点。`FighterController` 每帧以它为圆心画小圆检测地面。选中 Fighter_Base 时 Scene 视图会出现绿/红圆圈（Gizmos 可视化）——绿色=接地，红色=悬空。

### E5. 创建 Visual 子物体（临时方块替代模型）

1. 右键 `Fighter_Base` → `Create Empty`，命名 **`Visual`**
2. local Position = `(0, 0, 0)`
3. Add Component → **Sprite Renderer**
4. 给 Sprite 一个占位图：点击 Sprite 字段右侧小圆点 → 输入 "Square" → 选择 Unity 内置的 `UISprite`/`Knob` 等任意方形（或 `Create → Sprites → Square` 先建一个再拖）
   - 也可以从菜单 `Assets → Create → Sprites → Square` 建立一个 `Square` 精灵资产拖进去
5. Sorting Order = `1`（确保显示在舞台之上）

> DemoSetupWizard 的原始版是用代码生成蓝身肤头的 32×32 纹理。手动版用内置 Square 快速占位即可，后续换真实模型/骨骼动画时替换 Sprite 就行。

### E6. 回填 FighterController 引用（选中 Fighter_Base，看 Inspector）

对照表格从 Hierarchy 拖拽填空（⭐ 不要有 None）：

| 字段 | 拖什么 | 备注 |
|---|---|---|
| **Configuration** | | |
| Fighter Data | `FD_Knight.asset`（第一步先统一用 Knight，之后可换） | ⚠️ 必填，否则攻击函数返回 null |
| Player ID | `0`（场景里 P2 会改成 1） | |
| **References** | | |
| Animator | 留空（Awake 自动找子物体；本次 Visual 没挂 Animator，留空=动画不驱动但不报错） | 若想驱动动画：给 Visual 挂 Animator 组件并拖入 FighterAnimator.controller，再拖过来 |
| Rigidbody | 留空（Awake 自动 GetComponent） | |
| Main Collider | 留空（自动） | |
| Hurtbox | 拖入 `Hurtbox`（子物体） | 自动认领，但拖上更保险 |
| **Ground Detection** | | |
| Ground Check | 拖入 `GroundCheck`（子物体） | ⚠️ 必填，否则接地检测报错 |
| Ground Check Radius | `0.1` | 检测半径（Wizard 值） |
| Ground Layer | 勾选 `Default` 层 | ⚠️ 必填 |

> 主动学习点：哪些字段代码里「拖不拖都行」（Awake 有自动查找兜底：rb/animator/hurtbox），哪些「必须拖」（fighterData、groundCheck、groundLayer）——源码 `FighterController.Awake()` 里写了答案。

### E7. 保存为预制体

1. 确认 Hierarchy 结构长这样：

```
Fighter_Base            [Rigidbody2D, BoxCollider2D, FighterController, InputManager, PlayerInput]
├── Hurtbox             [BoxCollider2D(Trigger), Hurtbox]
├── GroundCheck         [Transform only]
└── Visual              [SpriteRenderer]
```

2. 把 Hierarchy 里的 `Fighter_Base` **拖到** Project 窗口的 `Assets/_Game/Prefabs/Fighters/` 文件夹里
3. 自动生成 `Fighter_Base.prefab`，原物体变蓝色（预制体实例）→ 右键它 → `Delete` 删掉场景里的原件（预制体已保存）

> ⚠️ 修改预制体的正确姿势：以后改字段，双击 Project 里的 prefab 进入预制体编辑模式再改，或改完点 Inspector 右上角 Overrides → Apply All。

### ✅ E 阶段验证

- Project 里 `Prefabs/Fighters/Fighter_Base.prefab` 存在
- 双击打开它：Inspector 无红色 ❌ 警告，Fighter Data / Ground Check / Ground Layer 都有值
- Scene 视图选中能看到 GroundCheck 圆圈 Gizmos + Hurtbox 橙色框 Gizmos

---

## 7️⃣ 阶段 F：搭建 BattleArena 舞台预制体

### F1. 创建根物体 + 4 个平台

1. `GameObject → Create Empty`，命名 **`BattleArena`**
2. 平台创建方式（重复 4 次）：`GameObject → 3D Object → Cube`，作为 BattleArena 子物体（拖到它下面）

每个 Cube 做完 3 步：① 删掉自带 **BoxCollider（3D）**（Inspector 组件右键 Remove）② Add Component → **BoxCollider2D** ③ Add Component → **Platform**（脚本）

| 平台名 | Scale（right 3D 转 2D 用） | local Position | BoxCollider2D Size | 材质颜色（Material → 新建或选默认改色） |
|---|---|---|---|---|
| MainPlatform | `(20, 0.5, 1)` | `(0, -2, 0)` | `(20, 0.5)` | 深灰 (0.3,0.3,0.3) |
| LeftPlatform | `(4, 0.5, 1)` | `(-6, 2, 0)` | `(4, 0.5)` | 中灰 (0.4,0.4,0.4) |
| RightPlatform | `(4, 0.5, 1)` | `(6, 2, 0)` | `(4, 0.5)` | 中灰 (0.4,0.4,0.4) |
| TopPlatform | `(6, 0.5, 1)` | `(0, 5, 0)` | `(6, 0.5)` | 浅灰 (0.5,0.5,0.5) |

> 为什么 Cube：2D 项目里 3D Cube 自带材质和网格，配 BoxCollider2D 是最快的占位平台。⚠️ 材质颜色修改：选中 Cube → 展开 Material 属性 → 点材质球右侧小圆点新建材质（New Material）→ 设 Albedo 颜色。**不要改内置 Default-Material**（上次教训 1：在编辑器脚本里直接改 `renderer.material` 会实例化泄漏；手动 UI 操作新建材质球则没这个问题，但养成习惯：永远用自己新建的材质）。

### F2. 创建 4 个 BlastZone（出界淘汰区）

`GameObject → Create Empty` ×4，作为 BattleArena 子物体，每个：Add Component → **BoxCollider2D（勾 Is Trigger）** + Add Component → **BlastZone**（脚本）

| 名称 | local Position | 碰撞 Size | BlastZone 勾选 |
|---|---|---|---|
| BlastZone_Top | `(0, 15, 0)` | `(50, 2)` | Is Top ✅ |
| BlastZone_Bottom | `(0, -15, 0)` | `(50, 2)` | Is Bottom ✅ |
| BlastZone_Left | `(-25, 0, 0)` | `(2, 40)` | Is Left ✅ |
| BlastZone_Right | `(25, 0, 0)` | `(2, 40)` | Is Right ✅ |

> 职责：角色碰到 → `Kill()`。Scene 视图显示半透明红框。

### F3. 创建 4 个 SpawnPoint（出生/重生点）

`GameObject → Create Empty` ×4，子物体，**无组件**，位置：

`SpawnPoint_1 (‑4, 3, 0)` · `SpawnPoint_2 (4, 3, 0)` · `SpawnPoint_3 (‑2, 8, 0)` · `SpawnPoint_4 (2, 8, 0)`

### F4. 保存为预制体

Hierarchy 的 `BattleArena` 拖到 `Assets/_Game/Prefabs/Stages/` → 生成 `BattleArena.prefab` → 删除场景原件。

### F5. 回填 SD_Arena 引用

1. 选中 `SD_Arena.asset`
2. **Stage Prefab** 字段 ← 拖入 `BattleArena.prefab`（Project 里）
3. **Spawn Points** 字段：Size 设 4 → 依次把预制体内 4 个 SpawnPoint 拖进来
   - 方法：双击打开 BattleArena.prefab 进入编辑模式，选中各 SpawnPoint 依次拖到 SD_Arena 的 Spawn Points 列表

### ✅ F 阶段验证

- 双击 BattleArena.prefab：4 平台 + 4 红框 + 4 空点，结构完整
- SD_Arena 的 Stage Prefab / Spawn Points 已填

---

## 8️⃣ 阶段 G：装配 BattleTest 场景（最终决战）

### G1. 新建并保存场景

1. `File → New Scene → Basic (Built-in)` → Create
2. 立刻 `File → Save As` → 存为 `Assets/_Game/Scenes/BattleTest.unity`

### G2. 建 GameManager

1. `GameObject → Create Empty`，命名 `GameManager`
2. Add Component → **GameManager**：`Game Settings` ← 拖 `GS_Default.asset`；**勾选 `Skip To Battle`** ✅（跳过菜单直接开打）
3. Add Component → **ObjectPooler**（单例，无配置）

### G3. 建 MainCamera

1. 若场景自带 Main Camera 直接改名；否则 `GameObject → Camera`，命名 `MainCamera`
2. Camera 组件设置：

| 字段 | 值 |
|---|---|
| Projection | **Orthographic**（正交，2D 格斗） |
| Size | `12` |
| Background | 深蓝紫 (0.2, 0.2, 0.25) |
| Position | `(0, 0, -10)` |

3. Add Component → **CameraManager**：

| 字段 | 值 |
|---|---|
| Target Camera | 拖 `MainCamera` 的 Camera 组件（或留空自动用 main camera） |
| Camera Bounds Min | `(-20, -10)` |
| Camera Bounds Max | `(20, 15)` |

> 职责：跟踪所有存活玩家中心 + 自动缩放 + 平滑跟随。Scene 选中它时显示青色边界框。

### G4. 建 MatchManager

1. `GameObject → Create Empty`，命名 `MatchManager`
2. Add Component → **MatchManager**
3. 引用填写：
   - **Camera Manager** ← Hierarchy 拖 `MainCamera`（组件）
   - **Spawn Points**：Size=4 → 把 BattleArena 实例里的 4 个 SpawnPoint 拖进来（见 G5 顺序）

### G5. 实例化舞台

1. 把 Project 的 `Prefabs/Stages/BattleArena.prefab` **拖进 Hierarchy 场景**，命名 `BattleArena`
2. Position `(0,0,0)`
3. 完成 MatchManager.Spawn Points 的拖拽（上一步）

### G6. 实例化两个角色 ⭐

1. Project 拖 `Prefabs/Fighters/Fighter_Base.prefab` 进场景，命名 **`Fighter_P1`**，Position `(-4, 3, 0)`
2. 再拖一次，命名 **`Fighter_P2`**，Position `(4, 3, 0)`
3. 分别选中两个角色，检查修改：

| 字段 | Fighter_P1 | Fighter_P2 |
|---|---|---|
| FighterController.Player ID | `0` | `1` |
| PlayerInput.Default Scheme | `KeyboardP1` | `KeyboardP2` |
| PlayerInput.Default Map | `Gameplay`（确认） | `Gameplay`（确认） |
| PlayerInput.Actions | Controls（确认已拖） | Controls（确认已拖） |

> ⚠️ 上次输入问题的完整排查链（自查 5 项）：
> 1. ProjectSettings Active Input Handling = Both ✅（1.3 已查）
> 2. 角色身上**有 PlayerInput** ✅
> 3. PlayerInput.Actions = Controls.inputactions ✅
> 4. Default Map = Gameplay（与 inputactions 里 map 名一致）✅
> 5. Default Scheme = KeyboardP1/P2（与控制方案名一致）✅
> 六项全对，输入链路才通。

### G7. 建 EventSystem（UI 必需）

`GameObject → UI → Event System`，自动带 EventSystem + StandaloneInputModule 两个组件，别删。

### G8. 加入 Build Settings（可选但推荐）

1. `File → Build Settings…` → 左侧 **Add Open Scenes**（把当前 BattleTest 加进列表）
2. 关闭窗口

### G9. 保存场景（`Ctrl+S`）

### ✅ G 阶段验证（最终 Hierarchy 检查）

```
GameManager         [GameManager + ObjectPooler]
MainCamera          [Camera + CameraManager]
MatchManager        [MatchManager]
BattleArena         [4平台 + 4BlastZone + 4SpawnPoint]
Fighter_P1          [所有组件 + KeyboardP1]
Fighter_P2          [所有组件 + KeyboardP2]
EventSystem         [EventSystem + StandaloneInputModule]
```

无红色报错、无 Missing Script。

---

## 9️⃣ 阶段 H：Play 测试手册

点 **Play**。Console 应依次输出：`Countdown: 3 → 2 → 1` → `Match Started!`

### 键位表

| 操作 | P1 | P2 | 手柄 |
|---|---|---|---|
| 移动 | WASD | 方向键 ↑↓←→ | 左摇杆 |
| 跳跃 | Space | 小键盘 0 | A（南键） |
| 攻击 | J | 小键盘 1 | X（西键） |
| 必杀 | K | 小键盘 2 | Y（北键） |
| 防御（按住） | L | 小键盘 3 | LT |
| 抓取 | U | 小键盘 4 | RB |
| 嘲讽 | T | 小键盘 5 | 十字键上 |
| 暂停 | Esc | Esc | Start |

### 测试清单（逐项打勾）

| # | 操作 | 预期表现 | 对应代码 |
|---|---|---|---|
| 1 | 不动 | 角色待机（状态 Idle） | StateMachine Idle |
| 2 | 按 A/D | 角色左右移动，Scene 里 Gizmos 圆圈变绿 | UpdateMovement → Run |
| 3 | 按 Space | 跳起，圆圈变红（离地） | TryJump → Jump |
| 4 | 空中再按 Space | 二段跳（更高一点） | remainingJumps ×0.85 |
| 5 | 不按方向自由下落 | 落地自动回 Idle，圆圈变绿 | UpdateGravity → Idle |
| 6 | 按 J | 攻击动作（状态 Attack，动画暂无但逻辑走了） | GetContextualAttack → jab1 |
| 7 | 空中按 J | 使用空中攻击（AirN） | GetContextualAttack |
| 8 | 按住 L | 防御状态（有绿盾 Gizmos 可看状态） | SetShielding(true) |
| 9 | 按 Esc | 游戏暂停（Time.timeScale=0，画面冻结），再按恢复 | OnPause ↔ PauseGame |
| 10 | 把 P2 推出边界（或用 P1 快速把 P2 顶出） | P2 从 BlastZone 出界 → Kill → Console 输出状态变化 | BlastZone → Kill |

> 若测试 1-5 全通过 = **S1 角色控制核心达成**（骨架版）。攻击/防御有逻辑没动画、P2 的 KD 啥的暂不影响验收目标。

---

## 🔟 常见问题排查表

| 症状 | 原因 & 修复 |
|---|---|
| **角色完全不动，按键无反应** | ① 1.3 的 Active Input Handling 不是 Both/New → 改；② 角色没 PlayerInput → 加；③ Actions 没拖 Controls → 拖；④ Default Map/Scheme 名字不对 → 对照 C3/C5 名字；⑤ 角色是旧预制体实例没刷新 → 删了重新拖 |
| **角色一直往下掉** | ① 舞台没碰撞体（平台 BoxCollider2D 没加）；② Platform 的 Is Pass Through 勾选导致 Trigger（正常，主平台不该设 Trigger——检查 MainPlatform 的 Is Trigger 未勾）；③ Ground Layer 设的不是 Default 层 |
| **可以跳但一直算"落地"** | GroundCheck 位置不在脚底（应 -(0,0.9)）或 GroundCheckRadius 太小 |
| **攻击键没反应** | Fighter Data 没拖（GetContextualAttack 返回 null）→ 拖 FD 资产 |
| **Jab 打不出连击** | 正常——本次只有单段攻击逻辑，连段（cancelIntoAttacks）后续 S2 再做 |
| **Console 报 material 类警告** | 只有运行编辑器脚本才会触发（上次教训 1）；手动操作一般不会。若出现：检查是否动过内置 Default-Material，改用自建材质球 |
| **Animator 没动画效果** | 正常——本次只搭了控制器骨架（空状态），Animation Clip 是后续阶段的事；只要无报错即可 |
| **预制体改完场景里不变** | 在场景实例上改的 → 需要点 Overrides → Apply All；正确姿势是双击 prefab 进入编辑模式修改 |

---

## 📎 附录

### A. 攻击数据速查（已汇总于 B2.2 表格，此处备注 hitstop 规则）
- Hitstop：Damage > 10 → 0.08s；其余 → 0.04s

### B. 快捷键速查
- 按住 **V**：顶点吸附拖拽（对齐平台边缘）
- 按住 **Ctrl+D**：复制对象
- 选对象按 **F**：Scene 视图聚焦

### C. 面试联动（做完这轮，你要能口头回答）
1. **ScriptableObject 是什么？** → 数据资产，可被多对象共享（享元模式），改一处全生效；对比 Resources/AB/Addressables 加载方式（Day15 已学）
2. **状态机为什么适合格斗游戏？** → 行为清晰、转换受控（CanTransitionTo）、可穷举测试；对比行为树
3. **新输入系统 Send Messages 模式？** → PlayerInput 按 Action 名反射调用 OnXxx 方法；对比直接读取模式（`action.ReadValue`）与事件回调模式
4. **为什么刚体用 FixedUpdate 更新速度？** → 物理稳定、固定步长（默认 50Hz），避免帧率波动影响物理
5. **对象池解决什么问题？** → 避免频繁 Instantiate/Destroy 的内存分配与 GC（Day15 Mono GC 碎片化联动）

### D. 本手册与代码的对应关系
| 手册步骤 | 对应源码 |
|---|---|
| B1-B3 SO 资产 | `Data/GameSettings.cs` `Data/FighterData.cs` `Data/StageData.cs` |
| C Input Actions | `Input/InputManager.cs`（OnMove/OnJump/… 方法名） |
| D Animator | `Character/FighterController.cs` 的 UpdateAnimation() |
| E Fighter Prefab | `Character/FighterController.cs` Awake/Update/FixedUpdate |
| F BattleArena | `Stage/Platform.cs` `Stage/BlastZone.cs` |
| G 场景 | `Core/GameManager.cs` `Managers/MatchManager.cs` `Managers/CameraManager.cs` |
| H 测试 | 全链路 |

> 📌 想偷懒？`Editor/DemoSetupWizard.cs` 菜单 `SuperSmashLike → 1. Setup All Demo Assets` 一键生成上述全部内容——本手册就是把它做的事手动拆解 + 补上它没做好的（PlayerInput 场景装配校验）。建议先手动做一遍理解装配，之后删资产重跑 Wizard 对比差异，学习效果最佳。