# Input System 配置指南

> 本项目已安装 `com.unity.inputsystem@1.14.2`，可直接使用

---

## 一、启用新输入系统

在 Unity Editor 中：

```
Edit → Project Settings → Player → Other Settings
    → Active Input Handling → 选择 "Input System Package (New)" 或 "Both"
```

设置完成后重启 Unity Editor。

---

## 二、创建 Input Action Asset

1. 在 `Assets/_Game/Input/` 目录右键 → Create → Input Actions
2. 命名为 `SmashControls.inputactions`
3. 双击打开 Input Actions 编辑器

### Action Map 设计

```
┌─ Action Map: Gameplay ───────────────────────────────┐
│                                                       │
│  Action: Move          → Vector2 (左摇杆/WASD)        │
│    ├─ Left Stick       → Gamepad/leftStick           │
│    └─ WASD             → Keyboard/WASD               │
│                                                       │
│  Action: Jump          → Button (跳跃按键)            │
│    ├─ A Button         → Gamepad/buttonSouth          │
│    ├─ Space            → Keyboard/space              │
│    └─ Up Arrow         → Keyboard/upArrow             │
│                                                       │
│  Action: Attack        → Button (普通攻击)            │
│    ├─ X Button         → Gamepad/buttonWest           │
│    ├─ B Button         → Keyboard/J                   │
│    └─ Mouse Left       → Mouse/leftButton              │
│                                                       │
│  Action: Special       → Button (必杀技)              │
│    ├─ B Button         → Gamepad/buttonNorth          │
│    └─ Y Button         → Keyboard/K                   │
│                                                       │
│  Action: Shield        → Button (防御)                │
│    ├─ Left Trigger     → Gamepad/leftTrigger          │
│    ├─ Right Trigger    → Gamepad/rightTrigger         │
│    └─ Left Shift       → Keyboard/L                   │
│                                                       │
│  Action: Grab          → Button (抓取)                │
│    ├─ Right Shoulder   → Gamepad/rightShoulder        │
│    └─ U                → Keyboard/U                   │
│                                                       │
│  Action: Pause         → Button (暂停)                │
│    ├─ Start            → Gamepad/start                │
│    └─ Escape           → Keyboard/escape              │
│                                                       │
│  Action: Taunt         → Button (嘲讽)                │
│    ├─ D-Pad Up         → Gamepad/dpad/up              │
│    └─ T                → Keyboard/T                   │
│                                                       │
└──────────────────────────────────────────────────────┘

┌─ Action Map: Menu ────────────────────────────────────┐
│                                                       │
│  Action: Navigate     → Vector2 (选单导航)             │
│  Action: Confirm      → Button (确认)                  │
│  Action: Cancel       → Button (取消)                  │
│  Action: Start        → Button (开始游戏)              │
│                                                       │
└──────────────────────────────────────────────────────┘
```

### 控制方案 (Control Schemes)

建议定义多种控制方案以支持不同设备：

```
Keyboard&Mouse: 键盘鼠标绑定
Gamepad:        通用手柄绑定 (自动匹配 Xbox/PS/Switch)
```

---

## 三、代码接入方式

### 方式A：PlayerInput 组件（推荐）

```csharp
// 在角色Prefab上挂载 PlayerInput 组件
// Actions → 选择 SmashControls.inputactions
// Default Map → Gameplay

// 通过 Send Messages 自动接收
public void OnMove(InputValue value)
{
    moveInput = value.Get<Vector2>();
}

public void OnJump(InputValue value)
{
    if (value.isPressed) TryJump();
}

public void OnAttack(InputValue value)
{
    if (value.isPressed) TryAttack();
}
```

### 方式B：直接代码引用

```csharp
public class InputManager : MonoBehaviour
{
    private SmashControls controls;

    void Awake()
    {
        controls = new SmashControls();
    }

    void OnEnable()
    {
        controls.Gameplay.Enable();
        controls.Gameplay.Jump.performed += OnJumpPerformed;
        controls.Gameplay.Attack.performed += OnAttackPerformed;
    }

    void OnDisable()
    {
        controls.Gameplay.Jump.performed -= OnJumpPerformed;
        controls.Gameplay.Attack.performed -= OnAttackPerformed;
        controls.Gameplay.Disable();
    }

    void Update()
    {
        Vector2 move = controls.Gameplay.Move.ReadValue<Vector2>();
        // 处理移动...
    }
}
```

### 方式C：多人设备分配

```csharp
// 使用 PlayerInputManager 组件自动为每个连接的手柄创建角色
// PlayerInputManager → Join Behavior → Join Players When Join Action Is Triggered
// Join Action → 任意按键加入

// 或在代码中手动分配：
var player1 = PlayerInput.Instantiate(prefab, controlScheme: "Gamepad", pairWithDevice: Gamepad.current);
var player2 = PlayerInput.Instantiate(prefab, controlScheme: "Keyboard&Mouse", pairWithDevice: Keyboard.current);
```

---

## 四、手柄检测与调试

### Unity Input Debugger

```
Window → Analysis → Input Debugger
```
在此面板可以看到：
- 所有已连接的设备列表
- 实时输入数值
- 事件记录

### 代码检测设备连接

```csharp
// 检测手柄连接数
int connectedGamepads = Gamepad.all.Count;

// 检测设备连接/断开事件
InputSystem.onDeviceChange += (device, change) =>
{
    if (change == InputDeviceChange.Added)
        Debug.Log($"{device} 已连接");
    else if (change == InputDeviceChange.Removed)
        Debug.Log($"{device} 已断开");
};

// 按键提示图标切换（检测当前使用设备）
var currentDevice = playerInput.GetDevice<Gamepad>();
```

---

## 五、按键提示图标切换

### 使用 Synty 已有图标资源

不同设备类型显示对应的按键图标：

```csharp
public enum DeviceType { Keyboard, Xbox, PlayStation, Switch, SteamDeck }

public Sprite GetActionIcon(string actionName, DeviceType device)
{
    // 从 Synty InterfaceFantasyWarriorHUD/Sprites/Icons_Input/ 加载对应图标
    // 路径格式: [设备类型]/[动作名称].png
    
    string path = $"Icons_Input/{device}/{actionName}";
    return Resources.Load<Sprite>(path);
}
```

---

*下一章：[开发环境与工作流](08_开发环境与工作流.md)*
