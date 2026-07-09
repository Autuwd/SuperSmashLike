# SuperSmashLike

Unity 2022.3 LTS project — Super Smash Bros clone using Synty assets.

## Engine
- Unity 2022.3.62f3 (LTS)
- Built-In Render Pipeline (BIRP)
- Unity Input System 1.14.2

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
  Editor/        FighterDataEditor
Assets/_Game/Scenes/    SampleScene.unity
```

## Commands
- No formal test framework yet
- Docs/ contains full design documentation (11 docs + README)

## Conventions
- Namespace: SuperSmashLike.{Core|Combat|InputSystem|Managers|Stage|EditorTools}
- All custom code under Assets/_Game/
- Use ScriptableObjects for data-driven config
- Comments in Chinese for architecture documentation
