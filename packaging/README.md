# NestCoop

Online co-op for **IRON NEST: Heavy Turret Simulator**. Several players share one Iron Nest and run it as a crew.

> Early test build. Expect rough edges. Tested on Steam build 26352746 (Unity 6000.3.21f1) with MelonLoader 0.7.3.

## What's synced
- Physical buttons (reload, powder charges, fire, hatches, computer, lights, war horn)
- Wheels, levers, chains and pressure valves, including the turret traverse wheel
- Turret rotation and gun elevation (the host's turret is the truth)
- Reload state (host decides)
- Missions: the host picks the mission; everyone follows with the same mission seed
- Player positions, shown as coloured capsules with a visor

Not synced on purpose: operating manuals, the espresso machine, the record player, menus and settings.

## Playing with a Steam friend
Both players:
1. Install NestCoop with r2modman (it pulls in MelonLoader).
2. Launch once with **Start modded**, then quit.
3. Open `UserData/MelonPreferences.cfg` in the r2modman profile (Settings → Browse profile folder) and in the
   `[NestCoop]` section set:
   ```
   Transport = "Steam"
   ```
4. Start modded again.

Then:
- **Host:** be in the game, press **Home**. The top-left shows `HOST`.
- **Guest:** be friends with the host on Steam, be in the game, press **End**. It joins the first friend it finds
  playing IRON NEST. To pick someone specific, set `SteamJoinTarget` to part of their Steam name or their SteamID64.
- **Leave:** press **Delete**.

Both players need the **same NestCoop version**; otherwise the host refuses the connection.

## Keys (change them in `[NestCoop]` in MelonPreferences.cfg)
| Key | Action |
|---|---|
| Home | Host |
| End | Join |
| Delete | Leave |
| PageUp | Check Steam (your name, friends playing) |

## Known limitations
- Resuming from a checkpoint isn't shared: guests start the mission fresh.
- Only the host can start, leave or finish missions.
- Carried items (cards, map tokens, records) aren't synced yet.
- Leaderboard: don't submit scores while modded (protection is planned).

## Uninstall
Remove it in r2modman. It doesn't change any game files.
