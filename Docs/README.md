# SuperSmashLike 项目文档

> 基于 Unity 2022.3 LTS 的类《任天堂明星大乱斗》平台格斗游戏

## 文档目录

| 文件 | 内容 |
|------|------|
| [00_项目概述.md](00_项目概述.md) | 项目背景、目标、技术栈、毕业设计意义 |
| [01_系统架构设计.md](01_系统架构设计.md) | 整体架构分层、模块职责、数据流 |
| [02_核心系统详细设计.md](02_核心系统详细设计.md) | 状态机设计、击飞公式、连段系统、防御系统 |
| [03_美术资源手册.md](03_美术资源手册.md) | 所有美术资源的定位、用途、对照表 |
| [04_开发路线图.md](04_开发路线图.md) | 14周里程碑、每个阶段的具体任务 |
| [05_数据配置规范.md](05_数据配置规范.md) | ScriptableObject 配置结构和规范 |
| [06_API参考手册.md](06_API参考手册.md) | 核心类公开接口和用法 |
| [07_InputSystem配置指南.md](07_InputSystem配置指南.md) | 输入系统设置、Action Map设计、手柄支持 |
| [08_开发环境与工作流.md](08_开发环境与工作流.md) | Git工作流、场景管理、调试技巧 |
| [09_论文大纲.md](09_论文大纲.md) | 毕业设计论文完整大纲 |
| [10_操作手册.md](10_操作手册.md) | 玩家操作说明 |

## 代码架构

```
Assets/_Game/
├── Scripts/
│   ├── Core/           GameManager, ObjectPooler
│   ├── Character/      FighterController, FighterStateMachine
│   ├── Combat/         DamageSystem, Hitbox, Hurtbox
│   ├── Stage/          BlastZone, Platform
│   ├── Managers/       MatchManager, CameraManager
│   ├── UI/             HUDManager (待实现)
│   ├── Input/          InputManager
│   └── Data/           FighterData, GameSettings, StageData (ScriptableObject)
├── Prefabs/             (预制体存放)
├── Scenes/              (场景存放)
├── Animations/          (Animator Controller存放)
└── ScriptableObjects/
    ├── Characters/      游戏角色数据资产
    ├── Stages/          舞台数据资产
    └── GameModes/       游戏规则配置资产
```

## 快速开始

1. 在 Unity 中打开本项目
2. 进入 `Project Settings → Player → Other Settings → Active Input Handling` 选择 `Both`
3. 创建第一个角色数据资产：右键 `Create → SuperSmashLike → Fighter Data`
4. 打开 `Assets/_Game/Scenes/BattleTest.unity`（需要先创建）
5. 按 Play 开始测试

## 开发状态

- [x] 项目框架搭建
- [x] 核心系统代码（GameManager、FighterController、Combat）
- [x] 文档体系完整
- [ ] Input Action Asset 配置
- [ ] 角色 Prefab 制作
- [ ] 舞台场景搭建
- [ ] UI 系统
- [ ] 多角色适配
- [ ] 完整游戏循环

---

*最后更新: 2026年7月*
