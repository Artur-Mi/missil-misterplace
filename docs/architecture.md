# Architecture

The brain of the MisterWeapon modpack: guided-missile logic based on Cython's Guided Missiles,
namespace `Cython.GuidedMissiles`. All code is under `Mod/Data/Scripts/GuidedMissileSystem/`.
It runs inside the game's Mod API sandbox: C# 7.3, and only whitelisted APIs.

## Components

| File | Type | What it does |
|---|---|---|
| `GuidedMissileSystem.cs` | Session component (`BeforeSimulation`) | Tracks every missile in flight and steers it toward its target (`MissileGuider`). Also handles flares, hand-held rocket-launcher lock-on (`RocketLauncherTracker`, with HUD notifications in Portuguese), underwater behaviour (WaterMod) and NPC targets (MES). |
| `MissileLauncher.cs` | Game logic on `SmallMissileLauncher` / `LargeMissileLauncherIronDome` | Iron Dome launcher: finds enemy missiles and fires interceptors at them. |
| `MissileTurret.cs` | Game logic on `LargeMissileTurret` / `LargeMissileTurretIronDome` | Iron Dome turret. Holds `missilesSubtypeIdWhitelist`, the list of missile ammo it can intercept. |
| `Network/` | Packet layer | Based on THDigi's network example (`NetworkLib/Usage.txt`). Syncs guidance between the server and clients. |
| `APIs/MESApi.cs` | Third-party API | Modular Encounters Systems: lets missiles target NPC grids. |
| `APIs/WaterModAPI.cs` | Third-party API | Jakaria's Water Mod: underwater detection and splash effects. |

## Ammo subtypes the code depends on

The guidance logic looks up behaviour by **ammo subtype name**, which is hard-coded in the code.

| Subtype | Defined in | Behaviour |
|---|---|---|
| `IDMissile` | this mod (`Ammos.sbc`) | Iron Dome interceptor; targets missiles only |
| `AMissile` | missilSLBM-misterplace | Special handling in the guider and the Iron Dome blocks |
| `Flare` | external | Decoy |
| `Missile`, `PMissile`, `MissileT2`–`T5` | vanilla / other mods | Guided missiles |
| `CEC_ArtilleryRocket_BP` | other mod | Interceptable by the Iron Dome turret |

When a block mod adds a new missile, the brain must know its subtype. Phase 4 of the modpack plan
(`../../docs/modpack-plan.md`) is about replacing these hard-coded names with registration.

## Blocks defined here

- `LargeMissileTurret/LargeMissileTurretIronDome`: weapon `LargeMissileTurretID`
- `SmallMissileLauncher/LargeMissileLauncherIronDome`: weapon `LargeMissileLauncherID`
- Magazine `IronDomeMissile`, ammo `IDMissile`, blueprint `IronDomeInterceptMissile`

## Known issues

- `MissileTurret.missilesSubtypeIdWhitelist` contains `"PMissike"`, a typo for `"PMissile"`. That means
  the turret never intercepts `PMissile`.
