# missil-misterplace (brain)

Space Engineers 1 mod: the guided-missile logic the other MisterWeapon block mods depend on.
Workspace overview: [../CLAUDE.md](../CLAUDE.md).

## Docs (read the relevant one before changing things)
- [README.md](README.md): quick start
- [docs/architecture.md](docs/architecture.md): components, hard-coded ammo subtypes, known issues
- [docs/deployment.md](docs/deployment.md): dev/final deploy, publishing, rules
- [docs/development-environment.md](docs/development-environment.md): paths, tools, Mod API constraints

## Rules
- Edit only inside `Mod/`. Never edit the copies in `%APPDATA%\SpaceEngineers\Mods` directly.
- Scripts must compile under the game's Mod API: C# 7.3 and whitelisted APIs only. Use the `se-dev-mod` and
  `se-dev-game-code` skills to check APIs.
- Deploy with `.\deploy.ps1 dev`. Run `.\deploy.ps1 final` only when the user asks for a release; it needs a
  clean tree and creates a `release/*` tag.
- `Mod/modinfo.sbmi` is generated and git-ignored. Workshop IDs live in `deploy.json`.
- When adding or renaming an ammo/missile subtype, update `docs/architecture.md` too.
- Keep the docs current whenever behaviour, layout or the workflow changes.
