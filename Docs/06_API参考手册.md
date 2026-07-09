# API 参考手册

本文档定义所有核心类的公开接口和用法。

---

## Core 命名空间

### GameManager

```csharp
// 单例访问
GameManager.Instance

// 属性
GameManager.Instance.CurrentGameState    // GameState枚举
GameManager.Instance.PlayerCount          // 当前玩家数量
GameManager.Instance.ActivePlayers        // List<FighterController>

// 方法
GameManager.Instance.StartMatch()         // 开始比赛
GameManager.Instance.EndMatch()           // 结束比赛
GameManager.Instance.PauseGame()          // 暂停
GameManager.Instance.ResumeGame()         // 恢复

// 事件
GameManager.OnGameStateChanged           // 状态变化事件
```

### ObjectPooler

```csharp
// 初始化（在场景加载时调用）
ObjectPooler.Instance.CreatePool(GameObject prefab, int initialSize)

// 从池中取出
GameObject obj = ObjectPooler.Instance.Spawn(prefab, position, rotation)

// 归还到池中
ObjectPooler.Instance.Despawn(GameObject obj, float delay = 0f)
```

---

## Character 命名空间

### FighterController

```csharp
FighterController player = GetComponent<FighterController>();

// 配置
player.fighterData = myFighterDataAsset;     // 角色数据
player.playerID = 0;                         // 玩家ID (0~3)

// 运行时状态
player.CurrentState                           // FighterState枚举
player.CurrentDamage                          // 当前伤害百分比
player.CurrentKnockbackSpeed                  // 当前击飞速度
player.IsGrounded                             // 是否在地面
player.IsInHitstun                            // 是否在受击硬直中
player.IsShielding                            // 是否在防御

// 方法
player.ApplyDamage(AttackData attack, FighterController attacker)
// 应用伤害+击飞

player.ApplyKnockback(Vector3 direction, float speed)
// 直接应用击飞

player.SwitchState(FighterState newState)
// 强制切换状态

player.Respawn(Vector3 spawnPoint)
// 重生

// 事件
player.OnStateChanged                         // 状态变化
player.OnDamaged                              // 受伤
player.OnKilled                               // 被击杀
```

### FighterStateMachine

```csharp
stateMachine.Initialize(FighterState idleState)     // 初始化
stateMachine.TransitionTo(FighterState newState)     // 转换
stateMachine.CanTransitionTo(FighterState target)   // 是否可转换
stateMachine.GetCurrentState()                       // 获取当前状态
```

---

## Combat 命名空间

### Hitbox

```csharp
Hitbox hitbox = GetComponent<Hitbox>();

hitbox.attackData = myAttackData;                    // 攻击数据
hitbox.owner = thisFighter;                          // 所属角色
hitbox.Activate()                                    // 激活碰撞
hitbox.Deactivate()                                  // 停用碰撞
hitbox.OnHit += OnHitCallback;                       // 命中回调
```

### DamageSystem

```csharp
// 计算击飞
float speed = DamageSystem.CalculateKnockbackVelocity(
    attackData, 
    targetDamage, 
    targetWeight
);

// 计算伤害
float finalDamage = DamageSystem.CalculateFinalDamage(
    attackData, 
    damageMultiplier
);

// 计算硬直
float hitstun = DamageSystem.CalculateHitstun(
    finalDamage, 
    knockbackSpeed
);
```

### KnockbackSystem

```csharp
// 每帧更新击飞物理
KnockbackSystem.UpdateKnockback(FighterController fighter, float deltaTime)

// 碰撞反弹
KnockbackSystem.HandleWallBounce(FighterController fighter, ContactPoint2D contact)

// 受身检测
bool teched = KnockbackSystem.CheckTech(FighterController fighter)
```

---

## Stage 命名空间

### BlastZone

```csharp
// 检测角色是否出界
BlastZone.OnPlayerOutOfBounds += OnPlayerOut;

// 自定义边界
blastZone.bounds = new Bounds(center, size);    // 设置边界范围
```

### Platform

```csharp
platform.isPassThrough = true;     // 是否可穿越（从下方通过）
platform.CanPassThrough(fighter)   // 判断某个角色能否穿越
platform.GetDropThroughTime()      // 获取穿过平台的时间
```

---

## UI 命名空间

### HUDManager

```csharp
HUDManager.Instance.UpdateDamageDisplay(int playerID, float damage)
HUDManager.Instance.UpdateTimerDisplay(float timeRemaining)
HUDManager.Instance.UpdateStockDisplay(int playerID, int stockCount)
HUDManager.Instance.ShowKillNotification(string killer, string victim)
HUDManager.Instance.ShowGameOver(int winnerID)
```

---

*下一章：[Input System 配置指南](07_InputSystem配置指南.md)*
