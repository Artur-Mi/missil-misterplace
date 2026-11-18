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
| `AMissile` | missilSLBM-misterplace | **Aquatic** (SLBM) ammo, see below |
| `LauncherXMissile` | LauncherX | **Aquatic** (SLBM) ammo, see below. Its own stats (damage, speed, fire rate) stay in LauncherX |
| `Flare` | external | Decoy |
| `Missile`, `PMissile`, `MissileT2`–`T5` | vanilla / other mods | Guided missiles |
| `CEC_ArtilleryRocket_BP` | other mod | Interceptable by the Iron Dome turret |

### Aquatic (SLBM) behaviour

Subtypes in `GuidedMissileSystem.AquaticAmmoSubtypes` (test with `GuidedMissileSystem.IsAquaticAmmo`) get:

- A guider even without a target (other missiles only get one when a target is found).
- While underwater (Water Mod `IsUnderwater`): a straight path at `max(10 - 0.5·t, 0)` m/s, no steering.
- On leaving the water (only if it was underwater first): a smoke effect and a splash, the clock resets, then normal
  acceleration from `MissileInitialSpeed` to the ammo's `DesiredSpeed` and steering toward the target. Exception: ammo in
  `HoldInitialSpeedAboveWaterSubtypes` (`AMissile`, the original SLBM behaviour) stays at its initial speed above water.
- A missile launched in the air, or in a world with no water, never splashes and just flies as a guided missile with
  its own stats. (The Water Mod throws when asked for a splash where there is no water; the splash is also wrapped in a
  try/catch so it can never crash the session.)
- Iron Dome blocks ignore the missile while it is still underwater, and can intercept it once it surfaces
  (both subtypes are in `MissileTurret.missilesSubtypeIdWhitelist`).

Only the behaviour lives here; the ammo's stats stay in its own mod. To give another missile this behaviour, add its
subtype to `AquaticAmmoSubtypes`, and to the Iron Dome whitelist if the Iron Dome should be able to intercept it.
Being able to fire underwater only is not part of this: it is the block's `WaterConfig.xml` in the block mod
(both SLBM and LauncherX set it).

When a block mod adds a new missile, the brain must know its subtype. Phase 4 of the modpack plan
(`../../docs/modpack-plan.md`) is about replacing these hard-coded names with registration.

## Blocks defined here

- `LargeMissileTurret/LargeMissileTurretIronDome`: weapon `LargeMissileTurretID`
- `SmallMissileLauncher/LargeMissileLauncherIronDome`: weapon `LargeMissileLauncherID`
- Magazine `IronDomeMissile`, ammo `IDMissile`, blueprint `IronDomeInterceptMissile`

## Known issues

- `MissileTurret.missilesSubtypeIdWhitelist` contains `"PMissike"`, a typo for `"PMissile"`. That means
  the turret never intercepts `PMissile`.
