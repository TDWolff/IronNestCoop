# Changelog

## 0.9.5
- **Session logs are saved automatically:** when a co-op session ends (leave, disconnect, failed join, or quitting
  during a session) a copy of the log goes to `UserData/NestCoop/logs/` with a short header (versions, role,
  players). The last 20 are kept, so a test session's log is never lost.

## 0.9.4
- **Smoother remote controls:** wheels and levers turned by another player glide instead of stepping.
- **Ping over Steam:** shown next to each player in the co-op menu and in the guest's status line.
- **Steam invites:** while hosting over Steam, friends see *Join Game* on you, and the co-op menu has
  **Invite Steam friends** (Steam's own invite dialog). Friends should start the game through r2modman first; you then
  appear in their co-op menu under *Join <your name>*.

## 0.9.3
- **Name tags** above every player's avatar (your Steam name, or set `PlayerName`), in the game's own font and
  the player's colour, always facing you. The co-op menu lists players by name.

## 0.9.2
- A guest who picks **Return to Main Menu / Return to Map** in the Esc menu now leaves the co-op session and goes
  back as normal (before, they were stuck with "Only the host can change missions").

## 0.9.1
- **Co-op doesn't pause:** during a session, menus and alt-tab no longer freeze just your own game (that made the
  players' battles drift apart). Outside a session the game pauses as usual.
- **The host decides mission win/fail;** guests follow its verdict.
- **Button malfunctions** are only rolled for the host's presses, so a guest can't make the host's button dead-press.

## 0.9.0
- **Mission state from the host:** guests receive the host's full mission checkpoint (objectives, map, messages,
  punchcards, enemies) when they join mid-mission and periodically to correct drift, using the game's own
  checkpoint system.
- **Host-authoritative combat:** only the host's game fires shells and decides hits; guests see the host's shots.
- **Turret position** is synced along with its rotation.
- **One player per control:** a wheel or lever someone is holding can't be grabbed by another player.
- **Session limit:** `MaxPlayers` (default 4); extra joiners are told the session is full.
- Debug logging and debug keys are now off by default.

## 0.8.0
- Version check: both players need the same NestCoop version and game build; a mismatch says exactly what differs.
- Clear disconnect messages; a player who leaves disappears immediately for everyone.
- Co-op runs are kept off the leaderboards (the game's own opt-out); solo runs still count.

## 0.7.0
- Steam friends transport (no port forwarding), co-op menu in the game's own clipboard style (` key),
  NestCoop Terminal on the safe box by the computer desk, Esc/` menu handling.

## 0.6.0 and earlier
- Player avatars, mission flow with a shared mission seed, wheel/lever/turret sync, button and reload sync.
