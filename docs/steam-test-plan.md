# Two-player Steam test plan (NestCoop 0.9.3)

Everything below can only be checked with **two PCs and two Steam accounts that both own IRON NEST** (the game won't
run twice on one PC, and Steam can't connect you to yourself). Tick items off as you go; after the session, send the
`MelonLoader/Latest.log` from **both** profiles.

## Setup (both players)
- [ ] Same game build (log line `NestCoop 0.9.3 loaded (key NestCoop|0.9.3|<game build>)` must show the same key).
- [ ] Import `TDWolff-NestCoop-0.9.3.zip` in r2modman (Settings → Import local mod) into a **fresh** profile.
- [ ] Launch once, quit, set in `UserData/MelonPreferences.cfg` under `[NestCoop]`:
      `Transport = "Steam"` and `DebugLog = true` (for the test logs).
- [ ] Be Steam friends; both in the game's main menu.

## 1. Connecting
- [ ] Host: ` → Host co-op (or Home). Top-left shows `HOST (0 guests)`.
- [ ] Guest: ` → the host's name under "Join …" (or Join first friend). Guest shows `GUEST (connected)`; host shows
      `Guest #1 joined`.
- [ ] Version mismatch (optional): guest on 0.8.0 → host refuses; guest sees "Version mismatch — NestCoop: host has 0.9.0…".

## 2. Lobby → mission
- [ ] Guest presses Play/campaign buttons → "Only the host can change missions".
- [ ] Host starts a mission → guest follows into the same mission; log `GenerateMission seed=` identical on both.
- [ ] Host returns to map / main menu → guest follows.

## 3. Controls
- [ ] Each player presses buttons (reload, powder charges, Calculate) → the other sees them press.
- [ ] Guest cranks the traverse wheel → **host's** turret turns; guest's turret follows smoothly (log `ControlSync 5s …
      turret snaps/blends, max err`; snaps should be rare).
- [ ] Both grab the same wheel → the second grabber has no effect while the first holds it.
- [ ] Elevation wheels/levers, valves, chains move on both.

## 4. Combat (host-authoritative, new in 0.9.0)
- [ ] Guest pulls the trigger chain → the **host's** gun fires; the guest sees its shell fire right after
      (log `Combat: replayed host shot`).
- [ ] Enemy hit/destroyed on the host → same state on the guest (`<- entity …` / entity disappears for both).
- [ ] The guest never fires on its own (`Combat: guest FireShell skipped` in the guest log is expected).
- [ ] Valve breakdowns appear on both at the same time.
- [ ] Turret relocation (counter-battery dodge / map move) happens on both.

## 5. Checkpoint snapshot (new in 0.9.0)
- [ ] Guest joins **mid-mission** → guest loads straight into the host's mission with the same enemies/objectives
      (host log `Snapshot: sent '…' to guest #1`, guest log `Snapshot: received`).
- [ ] Host resumes a saved mission → guest shows "Host is loading a checkpoint…" and ends up in the same state.
- [ ] Guest: ` → Resync from host → mission reloads matching the host.
- [ ] Note the checkpoint size from the log (`captured … KB`).

## 6. Players
- [ ] Each sees the other's avatar (orange host, blue guest), walking, looking, crouching.
- [ ] Each sees the other's **Steam name** above their avatar, readable from any side; the co-op menu lists names.
- [ ] Guest quits the game → host sees "Guest #1 left the session", avatar gone immediately.
- [ ] Guest uses Esc → Return to Main Menu mid-mission → "You left the co-op session", guest goes to the menu; host sees the guest leave.
- [ ] Host quits → guest sees "Host left the session"; guest's game stays usable.

## 7. Pause, win/fail, malfunctions (0.9.1)
- [ ] Host opens Esc / alt-tabs mid-battle → the battle keeps running on both (no freeze on either side).
- [ ] Mission is won/failed → both get the same result at the same time (guest log `MarkMissionComplete`/`Failed` from host).
- [ ] Guest presses buttons a lot → never a dead/double press caused by the guest; host presses can still malfunction.

## 8. Leaderboards
- [ ] Finish a mission in co-op → log `Leaderboard: co-op session active` and `LeaderboardManager.OptOut (forced on…)`;
      no leaderboard submission.

## Report back
Both `Latest.log` files, plus anything that looked wrong (with the time it happened).
