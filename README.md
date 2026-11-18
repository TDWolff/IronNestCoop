# NestCoop

Online co-op mod for **IRON NEST: Heavy Turret Simulator** (Unity 6, IL2CPP) built on **MelonLoader**. Several players
crew one Iron Nest: buttons, wheels, levers, reload, turret, missions, combat and player avatars are synced, with the
host's game as the source of truth.

Player-facing instructions: [packaging/README.md](packaging/README.md). Change history: [packaging/CHANGELOG.md](packaging/CHANGELOG.md).
Everything learned about the game's internals (class names, object paths, test results): [docs/game-map.md](docs/game-map.md).

## Layout
| Path | What |
|---|---|
| `NestCoop/` | The mod (MelonMod, .NET 6). |
| `NestCoop/Transport/` | `ITransport` + LiteNetLib (localhost testing) and Steam P2P (direct calls into the game's steam_api64.dll). |
| `NestCoop/Menu/` | Co-op menu: logic (`CoopMenu`) behind `IMenuView`; game-style clipboard view and an IMGUI fallback. |
| `tools/FakePeer/` | Console "second player" for testing — the game refuses to run twice on one PC. |
| `tools/AsmScan/` | Dumps types/members/enums from the IL2CPP proxy assemblies (`dump/`, gitignored). |
| `packaging/` | Thunderstore/r2modman package files (manifest template, README, CHANGELOG, icon). |

## Building
Requirements: .NET SDK 6+, the game, and an r2modman profile with MelonLoader 0.7.3 launched once (that generates
the IL2CPP proxy assemblies the project references).

```bash
dotnet build NestCoop.sln
```
The build copies `NestCoop.dll` into the r2modman profile's `Mods/` and `LiteNetLib.dll` into `UserLibs/`.
Paths default to `B:\SteamLibrary\…` and the `Default` profile; override them in `Directory.Build.props.user`
(gitignored) with `GamePath` / `ProfilePath`.

Release package (`dist/TDWolff-NestCoop-<version>.zip`, importable in r2modman):
```bash
dotnet build NestCoop -c Release -t:Package
```

## Releasing
1. Bump `Version` in `NestCoop/Mod.cs` (semantic versioning; below 1.0 = pre-release) and add a `## <version>` section
   to `packaging/CHANGELOG.md`. Commit and push `main`.
2. Check, then prepare the release (package, SHA-256, notes, annotated tag `v<version>`):
   ```bash
   powershell -ExecutionPolicy Bypass -File tools/release.ps1 -DryRun
   ```
   ```bash
   powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Publish
   ```
   `-Publish` pushes the tag and creates the GitHub Release (zip + `.sha256` attached, pre-release while below 1.0)
   with the GitHub CLI; it needs `gh` installed and `gh auth login` done once, and checks that before tagging.
3. Without `gh`: run with `-Push` instead and publish on github.com: Releases → Draft a new release → pick the tag →
   paste `dist/release-notes-<version>.md` → attach the zip and `.sha256` → tick pre-release while below 1.0.
   A tag that's pushed but not yet published can be published later with `-PublishOnly`.

The script refuses to run on a dirty tree, off `main`, with unpushed commits, without a changelog section, or if the
tag already exists. Releases are built locally because the build needs the game's IL2CPP proxy assemblies, which
can't (and mustn't) be on GitHub. Never move a published tag — release a new version instead.

## Testing without a second copy
Host in-game (Home, or the ` menu with Network: Localhost), then:
```bash
dotnet run --project tools/FakePeer
```
FakePeer can press buttons, turn wheels, walk around as an avatar and fake host-only messages (which must be ignored).

## Rules this project follows
- Every game class/method name comes from the dump or in-game recon; anything unverified is marked.
- Every Harmony patch logs its first hit (with `DebugLog = true`) so inlined/never-called targets are obvious.
- The host is authoritative; guests send intents and apply the host's state.

## License
CC0 1.0 — see [LICENSE](LICENSE). No code was taken from other IRON NEST mods.
