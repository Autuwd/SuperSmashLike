# SuperSmashLike

Unity 2022.3 LTS project — Super Smash Bros clone using Synty assets.

## Engine
- Unity 2022.3.62f3 (LTS)
- Built-In Render Pipeline (BIRP)
- Unity Input System 1.14.2

## Project Status
- 16 C# scripts: ✅ **fully implemented**
- Prefabs/Scenes/SO assets: ⏳ **not yet created**
- Demo Setup Wizard: ✅ `Editor/DemoSetupWizard.cs`

## Quick Start (1-click)
1. Open project in Unity
2. `Project Settings → Player → Other Settings → Active Input Handling` → **Both**
3. Unity top menu → **SuperSmashLike → 1. Setup All Demo Assets**
4. Open `Assets/_Game/Scenes/BattleTest.unity`
5. Press **Play** → P1(WASD+Space+J) vs P2(Arrow+NumPad)

## Project Structure
```
Assets/_Game/Scripts/
  Core/          GameManager, ObjectPooler
  Character/     FighterController, FighterStateMachine
  Combat/        DamageSystem, Hitbox, Hurtbox
  Input/         InputManager
  Managers/      CameraManager, MatchManager
  Stage/         BlastZone, Platform
  Data/          FighterData, GameSettings, StageData
  Editor/        FighterDataEditor, DemoSetupWizard
Assets/_Game/Scenes/    BattleTest.unity (auto-created)
```

## Learning Docs
| File | For |
|------|-----|
| `Docs/13_零基础学习指南.md` | Complete beginner, starts from Unity basics |
| `Docs/12_逐日实施清单.md` | Day-by-day build plan, exact methods to write |
| `Docs/11_项目实施计划.md` | Overall architecture and implementation strategy |
| `Docs/01_系统架构设计.md` | System architecture and data flow |
| `Docs/02_核心系统详细设计.md` | State machine, knockback formula, combat design |
| `Docs/06_API参考手册.md` | Public API references for all core classes |

## Demo Controls
| Action | P1 (Keyboard) | P2 (Keyboard) |
|--------|--------------|--------------|
| Move | WASD | Arrow Keys |
| Jump | Space | NumPad 0 |
| Attack | J | NumPad 1 |
| Special | K | NumPad 2 |
| Shield | L | NumPad 3 |
| Grab | U | NumPad 4 |
| Taunt | T | NumPad 5 |
| Pause | Escape | Escape |

## Conventions
- Namespace: SuperSmashLike.{Core|Combat|InputSystem|Managers|Stage|EditorTools}
- All custom code under Assets/_Game/
- Use ScriptableObjects for data-driven config
- Comments in Chinese for architecture documentation
