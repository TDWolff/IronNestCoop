# NestCoop

Online co-op for **IRON NEST: Heavy Turret Simulator**. Several players share one Iron Nest and run it as a crew.

> Early test build. Expect rough edges. Tested on Steam build 26352746 (Unity 6000.3.21f1) with MelonLoader 0.7.3.

## What's synced
- Physical buttons (reload, powder charges, fire, hatches, computer, lights, war horn)
- Wheels, levers, chains and pressure valves, including the turret traverse wheel. Only one player can hold a
  control at a time.
- Turret rotation, position and gun elevation (the host's turret is the truth)
- Reload state and combat: the host's game fires the shells and decides hits; everyone sees the same shots
- Missions: the host picks the mission and decides win or fail; everyone follows with the same mission seed
- Mission state: guests get the host's full mission state (objectives, map, messages, punchcards, enemies) when
  they join, including mid-mission or when the host resumes from a checkpoint
- Carried items: punchcards, map tokens and cards you move, slot or put in a deck
- Players, shown as floating crew heads with a name tag

Not synced on purpose: operating manuals, the espresso machine, the record player, menus and settings.

## Playing with a Steam friend
Both players:
1. Install NestCoop with r2modman (it pulls in MelonLoader) and launch with **Start modded**.
2. Open the game's **Settings → Co-op** tab and set **Network** to *Steam (friends)*. Optionally set your name there
   (empty = your Steam name).

Then:
- **Host:** be in the game, press **`** (the key left of 1) → **Host co-op**. Use ` → **Invite Steam friends**
  to send Steam invites. The top-left shows `HOST`.
- **Guest:** be friends with the host on Steam and either accept their invite, or be in the game and pick
  ` → **Join <host's name>**.
- **Leave:** ` → **Leave session**. A guest who picks *Return to Main Menu / Return to Map* in the Esc menu also leaves.

Instead of the ` key you can use the **NestCoop Terminal**, an orange button on the safe box by the computer desk.

Both players need the **same NestCoop version and the same game version**; otherwise the host refuses the connection
and the guest is told exactly what differs.

## Settings
In the game's **Settings → Co-op** tab (main menu or Esc menu). Changes save immediately.

| Setting | What it does |
|---|---|
| Your name | Shown above your avatar. Empty = your Steam name. Sent from the next session. |
| Network | *Steam (friends)* to play online; *Localhost* is for testing. Can't change during a session. |
| Max players | Host: most players in a session, including the host. |
| Resync guests every N min | Host: resend the full mission state to correct drift (reloads the mission on guests). 0 = off. |
| Show the NestCoop Terminal | The orange button on the safe box. |
| Co-op menu style | The game's clipboard, or a simple overlay. |
| Debug logging | Extra detail in the log, for bug reports. |

Everything else (keys, network rates) is in the `[NestCoop]` section of `UserData/MelonPreferences.cfg` in the
r2modman profile (Settings → Browse profile folder).

## Keys
| Key | Action |
|---|---|
| ` | Open/close the co-op menu |
| Home | Host |
| End | Join (the first Steam friend playing IRON NEST, or `SteamJoinTarget`) |
| Delete | Leave |
| PageUp | Check Steam (your name, friends playing) |

## Known limitations
- Co-op doesn't pause: during a session, the Esc menu and alt-tab don't freeze the game.
- Only the host picks, starts and finishes missions.
- Records and the record player aren't synced (on purpose). An item on someone's clipboard is hidden for the others
  until they put it down.
- Leaderboards: any run that had a co-op session is automatically kept off the leaderboards (the game's own opt-out is
  switched on for that run). Solo runs with the mod installed still count.

## Reporting problems
Every co-op session saves its log automatically to `UserData/NestCoop/logs/` in your r2modman profile
(Settings → Browse profile folder). Send the file from around the time it happened, ideally with **Debug logging**
on.

## Uninstall
Remove it in r2modman. It doesn't change any game files.
