# missil-misterplace (brain)

Guided-missile logic for the MisterWeapon modpack. The block mods (SLBM, LauncherX, IronDomeX) depend on it.

| Target | Local folder in `%APPDATA%\SpaceEngineers\Mods` | Workshop ID |
|---|---|---|
| dev   | `missil-misterplace-dev` | 3514275550 |
| final | `missil-misterplace`     | 3496380795 |

## Layout
- `Mod/` – everything that ships (Data, Textures, metadata.mod)
- `deploy.json` – dev/final targets
- `deploy.ps1` – deploy script

## Deploy
```powershell
.\deploy.ps1 dev            # junction Mods\missil-misterplace-dev -> Mod\ (live edits)
.\deploy.ps1 final -DryRun  # preview
.\deploy.ps1 final          # needs a clean git tree; copies a snapshot, tags release/<date>
```
Then publish from the game's Mods screen. Never load dev and final in the same world.
Folders replaced by a deploy are moved to `%APPDATA%\SpaceEngineers\ModBackups\`.
