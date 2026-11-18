# Development environment

## Machine

| Thing | Location / version |
|---|---|
| Space Engineers | `D:\SteamLibrary\steamapps\common\SpaceEngineers` (`Bin64\` holds the DLLs to reference) |
| Workshop downloads | `D:\SteamLibrary\steamapps\workshop\content\244850\` |
| Local mods (what the game loads) | `%APPDATA%\SpaceEngineers\Mods\` |
| Deploy backups | `%APPDATA%\SpaceEngineers\ModBackups\` |
| git / Git LFS | git + LFS 3.8 (`.mwm`, `.dds`, `.png`, `.jpg`, `.fbx`, `.blend` go to LFS) |
| .NET SDK | 10.x (for a future IDE project; the game compiles the mod itself) |

## Constraints

- The game compiles `Mod/Data/Scripts/**.cs` at world load. Syntax up to **C# 7.3**, using only types and members
  on the **Mod API whitelist**. A blocked API fails the world load with a compile error in
  `%APPDATA%\SpaceEngineers\SpaceEngineers_<date>_<time>.log` (newest file).
- Scripts run on the server and on every client, so check `MyAPIGateway.Session.IsServer` before doing
  authoritative work.

## Claude Code skills

- `se-dev-mod`: Mod API whitelist and code search across installed mods (its setup step hasn't been run yet).
- `se-dev-game-code`: decompiled game source, for exact signatures and behaviour (ready).

## Workflow

No IDE: all editing is done through Claude Code. There is no `.csproj`; the world-load compile in the game is the real
check, so after a change, reload a test world with the dev mod and read `%APPDATA%\SpaceEngineers\SpaceEngineers_<date>_<time>.log` (newest file)
for compile errors. See Phase 3 in `../../docs/modpack-plan.md`.
