# missil-misterplace (brain)

Guided-missile logic for the MisterWeapon modpack (Space Engineers 1). The block mods (SLBM, LauncherX,
IronDomeX, IronDomeClassic) depend on it. It defines no blocks of its own: the Iron Dome turret and launcher it
drives moved to [IronDomeClassic](../IronDome/IronDomeClassic/README.md).

| Target | Local folder in `%APPDATA%\SpaceEngineers\Mods` | Workshop ID |
|---|---|---|
| dev   | `missil-misterplace-dev` | 3514275550 |
| final | `missil-misterplace`     | 3496380795 |

## Quick start
```powershell
.\deploy.ps1 dev            # junction Mods\missil-misterplace-dev -> Mod\ (live edits)
.\deploy.ps1 final -DryRun  # preview a release
.\deploy.ps1 final          # needs a clean git tree; copies a snapshot, tags release/<date>
```
Then publish from the game's Mods screen. Never load dev and final in the same world.

## Layout
- `Mod/`: everything that ships (scripts under Data/Scripts, metadata.mod; no block, ammo or texture data)
- `deploy.json` / `deploy.ps1`: deploy targets and script
- `docs/`: documentation

## Docs
- [Architecture](docs/architecture.md)
- [Deployment](docs/deployment.md)
- [Development environment](docs/development-environment.md)
