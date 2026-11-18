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
