# Architecture

The brain of the MisterWeapon modpack: guided-missile logic based on Cython's Guided Missiles,
namespace `Cython.GuidedMissiles`. All code is under `Mod/Data/Scripts/GuidedMissileSystem/`.
It runs inside the game's Mod API sandbox: C# 7.3, and only whitelisted APIs.

## Components

| File | Type | What it does |
|---|---|---|
| `GuidedMissileSystem.cs` | Session component (`BeforeSimulation`) | Tracks every missile in flight and steers it toward its target (`MissileGuider`). Also handles flares, hand-held rocket-launcher lock-on (`RocketLauncherTracker`, with HUD notifications in Portuguese), underwater behaviour (WaterMod) and NPC targets (MES). |
| `MissileLauncher.cs` | Game logic on `SmallMissileLauncher` / `LargeMissileLauncherIronDome` (IronDomeClassic) and `IronDomeX` (IronDomeX) | Iron Dome launcher: finds enemy missiles and fires interceptors at them. |
| `MissileTurret.cs` | Game logic on `LargeMissileTurret` / `LargeMissileTurretIronDome` (block defined in IronDomeClassic) | Iron Dome turret. Holds `missilesSubtypeIdWhitelist`, the list of missile ammo it can intercept. |
| `AquaticLauncher.cs` | Game logic on the SLBM and LauncherX blocks (`LargeMissileLauncherSLBM`, `LauncherX`) | Underwater-only enforcement: switches the block off while its muzzle is not under water (see below). |
| `Network/` | Packet layer | Based on THDigi's network example (`NetworkLib/Usage.txt`). Syncs guidance between the server and clients. |
| `APIs/MESApi.cs` | Third-party API | Modular Encounters Systems: lets missiles target NPC grids. |
| `APIs/WaterModAPI.cs` | Third-party API | Jakaria's Water Mod: underwater detection and splash effects. |

## Ammo subtypes the code depends on

The guidance logic looks up behaviour by **ammo subtype name**, which is hard-coded in the code.

| Subtype | Defined in | Behaviour |
|---|---|---|
| `IDMissile` | IronDomeClassic (`Ammos.sbc`) | **Interceptor** ammo (`GuidedMissileSystem.InterceptorAmmoSubtypes`, test with `IsInterceptorAmmo`): targets missiles only, turns faster |
| `IronDomeXMissile` | IronDomeX | **Interceptor** ammo, same behaviour; its stats (identical to `IDMissile`) stay in IronDomeX |
| `AMissile` | missilSLBM-misterplace | **Aquatic** (SLBM) ammo, see below |
| `LauncherXMissile` | LauncherX | **Aquatic** (SLBM) ammo, see below. Its own stats (damage, speed, fire rate) stay in LauncherX |
| `Flare` | external | Decoy |
| `Missile`, `PMissile`, `MissileT2`–`T5` | vanilla / other mods | Guided missiles |
| `Torpedo`, `Torpedo_Heavy`, `Torpedo_Seeking`, `Torpedo_Piercing` | vanilla | Torpedoes. In the Iron Dome whitelist, so the turret and launchers target them like missiles |
| `MissileHoming` | vanilla | Homing rocket (`Missile200mmHoming`, fired by the advanced hand-held launcher). In the Iron Dome whitelist since 2026-11-17; the basic hand-held launcher and vanilla launchers fire `Missile` |
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

### Underwater-only launchers (`AquaticLauncher`)

The block mods' `WaterConfig.xml` (`FunctionalAboveWater=false`) is kept, but the Water Mod only tests one point of the
block (its origin cell) and does nothing where there is no water, so in space or on a dry planet the blocks would still
fire. `AquaticLauncher` is stricter, on the server only, every 10 frames:

- It samples the **muzzle** (front face of the block, forward = -Z, 0.5 m inside) with `WaterModAPI.IsUnderwater`.
- Muzzle not underwater for 2 checks in a row: it sets `Enabled = false`. If the player switches the block on again it is
  switched off again. Muzzle underwater for 2 checks: it restores the block to on.
- With the Water Mod **not** loaded (`WaterModAPI.Registered` false) it does nothing, since it cannot know where the
  water is. So these blocks work anywhere in a world without the Water Mod.
- It is attached by block subtype (`LargeMissileLauncherSLBM`, `LauncherX`): adding another underwater-only launcher is a
  new subtype in the `[MyEntityComponentDescriptor]` on the class.
- The "held off" state is not saved. A block that was saved switched off above water stays off after loading, even once
  submerged, until the player switches it on.

### Iron Dome whitelist diagnostics

`MissileTurret.NoteUnlistedMissile` writes `[IronDome] not in whitelist: <subtype>` to the game log, once per ammo subtype,
for every missile an Iron Dome block sees (within 3000 m) that is not in `missilesSubtypeIdWhitelist`. If an Iron Dome
ignores some missile, search the newest log for it and add the subtype to the whitelist (own missiles are also logged, since
the check comes before the enemy filter). In the vanilla game of 2026-11-17 the standard rocket's ammo is `Missile`.

If no such line appears, the block never scanned: a launcher scans only while `IsWorking`, so check that its grid has power
(this was the cause of the "Iron Dome X ignores rockets" report), that it is on and has ammo, and that the missile is an
enemy one (not owned by the block's owner or faction) within 3000 m.

## Blocks (none defined here)

This mod ships scripts only. The Iron Dome blocks it drives were moved to the mod
[IronDomeClassic](../../IronDomeClassic/docs/contents.md), which **requires** this one:

- `LargeMissileTurret/LargeMissileTurretIronDome`: weapon `LargeMissileTurretID`
- `SmallMissileLauncher/LargeMissileLauncherIronDome`: weapon `LargeMissileLauncherID`
- Magazine `IronDomeMissile`, ammo `IDMissile`, blueprint `IronDomeInterceptMissile`

The scripts here attach by those subtypes. Without IronDomeClassic loaded they find no block and stay idle.

The `IronDomeX` launcher (mod IronDomeX) is registered the same way: its block subtype is in the descriptor on
`MissileLauncher`, and its ammo `IronDomeXMissile` is in `InterceptorAmmoSubtypes`. To register another Iron Dome style
launcher, add its block subtype to that descriptor (or the turret's, for a turret) and its ammo subtype to
`InterceptorAmmoSubtypes`; the ammo's stats stay in the block mod. Interceptor behaviour that is not ammo-based (the
3000 m scan range, the 25 power draw, the interceptable whitelist) is shared by all registered launchers.
Worlds saved with the old brain (which still had the blocks) need IronDomeClassic loaded too.

## Known issues

- `MissileTurret.missilesSubtypeIdWhitelist` contains `"PMissike"`, a typo for `"PMissile"`. That means
  the turret never intercepts `PMissile`.
