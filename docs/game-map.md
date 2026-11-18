# Game map — IRON NEST: Heavy Turret Simulator

Recon notes for NestCoop. **Source:** static scan of the Il2CppInterop proxy `Assembly-CSharp.dll`
(`tools/AsmScan`, full dump in `dump/Assembly-CSharp.txt`, gitignored).

- Game build: Steam build **26352746**, Unity **6000.3.21f1**, IL2CPP, MelonLoader **0.7.3**.
- All game types below are in the `Il2Cpp` namespace in C# (e.g. `Il2Cpp.TurretController`).
- **Status legend:** ✅ exists in the dump. 🔍 needs UnityExplorer to confirm *which objects use it / how it's wired*.
  Nothing here has been hooked with Harmony yet — every patch still needs its first-hit log line.

Regenerate the dump after a game update:
```bash
tools/AsmScan/bin/out/AsmScan.exe "%APPDATA%/r2modmanPlus-local/IronNestHeavyTurretSimulator/profiles/Default/MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll" dump/Assembly-CSharp.txt
```

## Big picture

The controls are **composition-based**, not one class per lever. A physical control is a GameObject with a
generic input component (dial / slider / click) whose UnityEvent drives a "system" controller:

```
DialInteractable ──OnValueChanged──▶ TurretController / GunController / ValveController / ...
LinearSliderInteractable ──OnValueChanged──▶ GunElevationSliderBinding / ...
ClickEvent (OnPointerClick) / Interactable ──▶ ArtilleryReloadController / PowderChargeController / ArtilleryComputer ...
```

So instead of patching dozens of lever types, we can sync **three generic input components** plus a few system
controllers. That's the plan for Milestone 3–4.

Almost every system also has **`CaptureMissionState()` / `RestoreMissionState(saveData)`** (the game's checkpoint
system, driven by `MissionSaveManager`). That's a ready-made full-state snapshot for late-join / resync.

## Input components (the things players touch)

| Class | Kind | Key members | Sync notes |
|---|---|---|---|
| `DialInteractable` ✅ | rotary wheel/knob/dial | `SetDialValue(value)`, `SetAccumulatedValueUnlimited(angleDegrees, fireValueChangedEvent, smoothToTarget)`, `BeginDialDrag()`, `EndDialDrag()`, `Update()`; props `AccumulatedValue`, `IsDragging`, `currentRotationAngle`; events `OnValueChanged`, `OnGrab`, `OnRelease`, `OnBeginDialDrag`, `OnEndDialDrag` | Continuous. Drag logic is in `Update()` (not patchable per-change) → poll `AccumulatedValue` while `IsDragging`, apply remotely via `SetAccumulatedValueUnlimited`. |
| `LinearSliderInteractable` ✅ | linear lever/slider | `SetSliderValue(value)`, `BeginSliderDrag()`, `EndSliderDrag()`; props `Value`, `IsDragging`; events `OnValueChanged`, `OnBeginSliderDrag`, `OnEndSliderDrag` | Same pattern as dials. |
| `Interactable` ✅ | marker/prompt/cursor for anything clickable | `SetInteractable(value)`, `IsInteractable`, `IsPassive`, `GetResolvedPrompt()` | Not the click handler itself. |
| `ClickEvent` ✅🔍 | click → UnityEvent | `OnPointerClick(eventData)`, `m_OnClick` | Likely the physical push-buttons. Confirm in UnityExplorer. Candidate for the first synced "lever". |
| `SystemDependantClick` ✅🔍 | keyboard/gamepad click variant | `SystemDependantClickCheck()` | |
| `InteractableGroupTrigger` ✅ | fires when children interacted | `HandleInteractableEvent(target)` | |
| `DynamicCursorManager` ✅ | hover/raycast/grab dispatch for all of the above | `BeginDrag(draggable)`, `HandleGrabInput()`, `PerformHoverDetection()` | Central place if we need "who is grabbing what". |
| `InteractionLockBroker` ✅ | exclusive locks while dragging | `Acquire(request)`, `Release(handle)` | Might be reused for "one player per control". |

## Turret & guns

| Class | Members of interest |
|---|---|
| `TurretController` ✅ | `OnBeginRotationDialDrag()`, `OnEndRotationDialDrag()`, `OnBeginElevationDialDrag()`, `OnEndElevationDialDrag()`, `MapDialToTurretRotation(dialDegrees)`, `UpdateDesiredRotationTargetDynamics()`, `UpdateRotationPhysics()`, `BackdriveRotationDial()`, `FireControlledGun()`, `FireGunByIndex(gunIndex)`, `SetPowderChargeForAllGuns(chargeLevel)`, `MoveTurret(worldPos, dodgeCounterBattery)`, `ResetTurretRotation()`, `CaptureMissionState()`, `RestoreMissionState(saveData)`. Nested value providers: `FloatValueProvider_DesiredRotation`, `_CurrentAngle`, `_DesiredElevation`, `_CurrentElevation`, `_PowderCharge`. |
| `GunController` ✅ | `SetDesiredElevation(elevationAngle)`, `SetDesiredElevationFromDial(...)`, `SetDesiredElevationFromSlider(...)`, `SetPowderCharge(chargeLevel)`, `RequestFire()`, `FireShell()`, `FireShellDelayed()`, `OnShellLoaded()`, `OnShellUnloaded()`, `CaptureMissionState()`/`RestoreMissionState`. Enum `GunController/CommandSource`. |
| `GunElevationDialBinding`, `GunElevationSliderBinding`, `GunElevationLinkCoordinator` ✅ | Glue between elevation inputs and guns. |

**Rotation plan (host-authoritative target):** "desired rotation" is a separate concept from current angle
(`UpdateDesiredRotationTargetDynamics`, `FloatValueProvider_DesiredRotation`) — that's the value to make host-owned.
`OnBegin/EndRotationDialDrag` tell us when a guest is actively turning.

## Ammo / reload

| Class | Members of interest |
|---|---|
| `ArtilleryReloadController` ✅ | State machine: `AdvanceState()`, `RegressState()`, `SetState(newIndex, force, triggerAnimations)`, `GoToState(stateKey)`, `OnUserInput_Advance()`, `OnUserInput_Regress()`, `TryLoadShell()`, `EjectChamberedShell()`; props `CurrentStateIndex`, state keys `BreechOpenStateKey`, `ShellRammingStateKey`, `SelectPowderChargeStateKey`, `RamChargesStateKey`; event `OnStateChanged`. Note `OnAnimationEvent_AdvanceState()` — some transitions come from animations. |
| `PowderChargeController` ✅ | `OnChargeButtonPressed(index)`, `OnLoadChargesPressed()`, `BeginChargeSelection()`, `EndChargeSelection()` |
| `CylinderShellSelector`, `ShellSlotPool`, `ShellLoadoutApplier`, `AutoReloadManager` ✅ | Shell selection / magazine. |

`OnUserInput_*` and `OnChargeButtonPressed(index)` are good discrete-event hooks (reliable messages).

## Artillery computer

`ArtilleryComputer` ✅: `OnRangeInputChanged(value)`, `OnPowderChargeChanged(value)`, `OnCalculateButtonPressed()`,
events `CalculationSuccessEvent` / `CalculationErrorEvent`. Inputs are probably dials feeding `OnRangeInputChanged` 🔍.

## Pressure / valves (damage system)

`ValveController` ✅ (`HandleDialValueChanged`, `DamageValve()`, `ForceFixValve()`, `SetDamage01(damage01)`),
`HighPressureSystemManager` ✅. `DialInteractable` ranges get clamped by system health.

## Missions

| Class | Members of interest |
|---|---|
| `MissionManager` ✅ | `StartOperation(operation, mission[, checkpoint])`, `ResumeOperation`, `LoadMission(mission, forceReload, checkpoint)`, `StartMissionRuntime(checkpoint)`, `FinishMission()`, `MarkMissionComplete`, `MarkMissionFailed`, `ReloadCurrentMission()`, `ReturnToMap()`, `EndOperationAndReturnToMenu()`, `LoadMainMenu()`, `SetPhase(next)` |
| `FireMission` ✅ | Spawns/moves enemies (`CreateMapEntity`, `MoveMapEntity`, `SetEntityState`), `GenerateMission()`, `SampleAreaPosition(zone, rng)`, `RandomPointWorldInside(zone, rng)` — **RNG is passed in**, so seed sync is plausible. `Get/SetMissionVariable(key, value)`. |
| `MissionSaveManager` ✅ | Checkpoint capture/restore of the whole mission incl. node graphs (`CaptureGraphs`, `RestoreGraphs`), map tokens, punchcards, transforms. Also `GetHierarchyPath(transform)` — the game already has a path-ID scheme. |
| `Il2CppSleepyNodes.*` ✅ | Node-graph scripting for missions: `MissionGraph`, `OperationGraph`, `ObjectiveGraph`, `PunchcardGraph`, `StateGraph`. Guests should probably *not* run these; host runs them. |

Scenes seen: `[0] Master Turret Scene` (boot), `[1] Main Menu`. Mission scenes are loaded additively by `MissionManager` 🔍.

## Player / camera

`FirstPersonController` ✅ (`HandleMovement()`, `HandleCamera()`, `SetPitch(pitch)`, `SetFrozen(frozen)`),
`PlayerSpawnPoint`, `CameraFollower`, `FreeCameraController`, `RTSMapCameraController` (map view).

## Leaderboard (Milestone 9: disable while modded)

`LeaderboardManager` ✅ (`Leaderboard_StartRun(gamemode)`, `Leaderboard_CompleteRun(onCompleted)`, `RecordAction`,
`RetryPendingSubmissions()`), `SteamLeaderboardScoreController` ✅ (`SubmitScore()`, `SubmitScoreInternal(forceUpdate)`),
`LeaderboardClient`, `PostLeaderboardScoreRequest`.

## Networking available in-game

`Il2Cppcom.rlabrecque.steamworks.net.dll` (Steamworks.NET) and `Il2CppHeathen.Steamworks.dll` are shipped and proxied.

## Live recon findings (in-game, 2026-11-17)

From `NestCoop/Recon.cs` output (`UserData/NestCoop/recon/<scene>.txt`), tutorial mission 1.

- **The whole turret lives in `Master Turret Scene`** (build index 0, loaded at boot and kept). Missions such as
  `Mission tutorial 1` (index 4) load *additively* and contain mission-only objects (tarps, newspapers, win trigger).
  So stable IDs for controls = paths inside `Master Turret Scene`; mission objects need the scene name as a prefix.
- **No duplicate hierarchy paths** among 551 components (path + sibling index). Note the right gun reuses the left's
  child name `PressureSystem_ChargeRammerLeft[20]`, but the root `Gun System Right[0]` keeps it unique.
- Inspector-wired `UnityEvent`s on dials/sliders only drive **sound/FX** (FMOD `StudioEventEmitter.Play`,
  `GameObject.SetActive`). Gameplay wiring is done **in code** (controllers hold references to the dials and read
  them). ⇒ Sync the input component's value; the controllers on each machine will follow it.
- `ClickEvent` is **only used by the settings menu UI** — not a gameplay button.
- Physical buttons are `Interactable` objects named `Universal Button …` (e.g. `Gun System Left[0]/--Reloading Console[3]/Universal Button Load shell Rammer[7]`,
  `Artillery Computer Console[0]/Calculate Universal Button[3]`). The click handler is **`LookAtTarget`** — see below.
- **UnityExplorer 4.13.6 does not work on this game** (Unity 6000.3): its LateInit throws
  `NotSupportedException: Method unstripping failed` in `SceneManager.GetAllScenes()`, so no UI / F7. Our own recon replaces it for now.

### Key controls (Master Turret Scene)

| Control | Path | Component | Range |
|---|---|---|---|
| Turret traverse wheel | `Turret[0]/Rotation Console[13]/.Wheel Parent[1]/.Spur Gear 12 DRIVER[0]` | `DialInteractable` | unlimited (value was -25200) |
| Elevation wheel L / R | `Turret[0]/Elevation Console[14]/.Wheel Parent Elevation Left[2]/.Spur Gear 12 DRIVER.002[0]` (Right: `…Right[3]/….001[0]`) | `DialInteractable` | 0..60 |
| Elevation lever L / R | `Turret[0]/Elevation Console[14]/.Elevation Lever Baseplate[1]/.Elevation Lever Left[4]` / `Right[5]` | `LinearSliderInteractable` | 0..60 |
| Elevation indicators (Desired/Current L/R) | same baseplate, `[0]..[3]` | `LinearSliderInteractable` (display only?) | 0..60 |
| Locking levers (rotation / elevation) | `Aiming Console[0]/Locking Lever Rotation[21]/Lever[0]`, `…Elevation[22]/Lever[0]` | `DialInteractable` | -1..1 |
| Trigger chain | `Trigger Console[0]/.Trigger Rail Cart[9]/.Trigger Core[1]/.Trigger Chain Track[9]/.Trigger chain parent[0]` | `LinearSliderInteractable` | 0..100 |
| Engine starter chain | `EngineControls[0]/Engine Controls[11]/.Starter Chain Track.001[2]/.Starter chain parent.001[0]` | `LinearSliderInteractable` | 0..100 |
| Ballistic computer charge / range | `Artillery Computer Console[0]/Balistic Calculator Controls[4]/.Charge Dial Parent[1]/.Charge Dial[0]` / `.Range Dial Parent[4]/.Range Dial[0]` | `DialInteractable` | 1..6 / 0..30 |
| Pressure valves (×18) | `…/PressureSystem_*/PressureValve*/Dial[4]` | `DialInteractable` (+ `ValveController`) | 0..100 |
| Reload buttons | `Gun System {Left,Right}[0]/--Reloading Console[3]/Universal Button {Move Cylinder[6], Load shell Rammer[7], Charge Rammer (1)[8]}` | `Interactable` + `LookAtTarget` | — |
| Powder charge buttons | `…/--Reloading Console[3]/PowderChargeController[10]/Button Dispencer (1..6)` | `Interactable` + `LookAtTarget` | — |

Controllers: `TurretController` at `Tactical Map[0]/Canvas[0]/TurretSystem[4]`, `GunController` ×2 at `…/TurretSystem[4]/GunLeft[0]` and `GunRight[1]`,
`ArtilleryReloadController` at `Gun System {Left,Right}[0]/--Reloading Console[3]`, `ArtilleryComputer` at `Artillery Computer Console[0]`,
`MissionManager` at `- Game Wireing -----[0]/MissionManager[1]`, `FirstPersonController` at `FirstPersonController[0]` (root).

Per-player only (never sync): `Record Player[0]/…/.Volume Dial[0]`, espresso machine dials, settings UI.

## Buttons: `LookAtTarget` ✅ (recon v2)

Every physical button GameObject = `Interactable` + `LookAtTarget` + 4× `AnimatorBoolToggler` (115 of them in the scene).

- Click path: `DynamicCursorManager` → `LookAtTarget.HandleClickDownFromManager(pressTarget)` / `HandleClickUpFromManager(...)`
  → `OnClickDown()` / `OnClickUp()` → UnityEvents `onClickDown`, `onClickUp`, `onActivated`, `onDeactivated`.
- Inspector wiring is only visuals/animation (`AnimatorBoolToggler.ToggleBool`, steam particles, hover highlight).
  **Gameplay subscribes from code** via `RegisterOnClickDown(action)` / `RegisterOnClickUp` / `RegisterOnActivated`
  (e.g. `ArtilleryReloadController.RegisterAdvanceButtonListeners()`).
- **Malfunctions are random:** `useMalfunctions`, `EvaluateMalfunction()`, `onDeadPress`, `onDoublePress`, chance curves
  linked to the pressure system. ⇒ the host must decide the outcome of a press, or machines will diverge.
- State: `isActive`, `SetActive(active)`, `GetActive()`, `ResetButton()`, click cooldown (`useClickCooldown`).

**Sync plan:** hook **`OnClickDown()`** (fires once per real press). `HandleClickDownFromManager` is *broadcast to
every subscribed button* on every click (9840 calls for 34 presses in testing), so it can't detect a press.
Remote presses get applied with `OnClickDown()` ✅ — verified with the debug replay key (Backslash; F9 is a media key on
the dev keyboard): calling it directly runs the press and is tagged `[remote]` by echo suppression.
**Gotcha:** `OnClickDown()` calls `EvaluateMalfunction()` itself, so a replayed press **re-rolls the malfunction
locally**. For co-op the receiver must be forced to the host's result (e.g. prefix `EvaluateMalfunction` to set
`currentMalfunction` from the message and skip the roll while `Net.ApplyingRemote`).

## Verified Harmony hooks (Milestone 2, tutorial mission 1, 2026-11-17) ✅

All patches applied (`Harmony patched 14 methods`) and all fired — none inlined.

| Hook | Fires when | Notes |
|---|---|---|
| `LookAtTarget.OnClickDown` | each real button press | sync hook for buttons |
| `LookAtTarget.EvaluateMalfunction` | presses on malfunction-enabled buttons | outcome in `currentMalfunction` |
| `LookAtTarget.HandleClickDownFromManager` | every click × every button | ❌ not usable as a press hook |
| `DialInteractable.BeginDialDrag` / `EndDialDrag` | grab / release a dial or wheel | |
| `LinearSliderInteractable.BeginSliderDrag` / `EndSliderDrag` | grab / release a lever | End fired 1× vs Begin 5× — 🔍 release may go another path |
| `TurretController.OnBeginRotationDialDrag` / `OnEndRotationDialDrag` | grab / release traverse wheel | |
| `ArtilleryReloadController.SetState` | every reload step (32 in tutorial) | logs before → after index |
| `GunController.FireShell` | each shot | |
| `MissionManager.LoadMission` / `StartMissionRuntime` / `FinishMission` | mission start / end | |

**Stable IDs changed:** raw sibling indices are *not* stable (`Turret/Elevation Console` went from `[14]` to `[13]`
mid-session). IDs are now `scene:name/path`, with `#n` only for same-named siblings (`Ids.cs`). Name-path collisions in
`Master Turret Scene`: the table lamp's two `Universal Button`s (handled by `#n`) and 8 runtime `MapToken_Artillery`
draggables (spawned at runtime — will need `MissionSaveIdentity` instead).

## Next: open questions (🔍)

1. Visually: did the replayed lamp press animate/toggle the lamp like a real click?
4. Why `EndSliderDrag` fired once for five `BeginSliderDrag`s.
2. Are the "Elevation Desired/Current" sliders player-draggable or just indicators?
3. How the turret wheel's unlimited value maps to rotation (`TurretController.MapDialToTurretRotation`).

## Milestone 3 notes (2026-11-17)

- **The game is single-instance.** A second copy exits cleanly at startup (no crash event, empty MelonLoader log,
  no `Player.log`), even with its own r2modman profile. Local multiplayer tests use `tools/FakePeer` (console
  guest speaking the NestCoop protocol) instead.
- Button sync verified both directions with FakePeer: game → guest (`sent press`), guest → game (`applied press`,
  `EvaluateMalfunction [remote]` forced, lamp toggled).
- Two `LookAtTarget`s can sit on one GameObject (`Barbet/Campaign 1/InMission Interaction blocker`) — IDs now add
  `~n` for the 2nd+ component of the same type.

## Milestone 4: discrete sync (2026-11-17, v0.3.0)

- **What's shared (`SyncPolicy.cs`):** every `LookAtTarget` button except main menu / campaign map (mission flow is
  M6), options/notepad (`Main Camera/`), operating manuals, espresso machine, record player, cat, bedroom.
  Checked on send *and* receive.
- **Reload state is host-authoritative:** host broadcasts every `ArtilleryReloadController.SetState` change
  (`ReloadState` msg); guests snap to it with `SetState(index, force: true, triggerAnimations: true)` if they differ.
  Guests still run their own state machine from button presses so it feels instant.
- **Join snapshot:** a new guest receives the current state of both reload controllers.
- **Not covered yet:** physical items you carry (`DraggableItem`: shells?, cards, map tokens, records — 30+ objects),
  locking levers / valves / wheels (dials → M5), map/teleprinter interactions.
- **M4 test (tutorial 1, FakePeer):** join snapshot ✅ (2 reload states), host reload changes broadcast ✅, guest
  press advanced host reload ✅ (`applied press Move Cylinder` → state 4→5).
- **Fix:** the trigger chain and engine starter chain are `LinearSliderInteractable`s that also carry a
  `LookAtTarget`; dragging fired `OnClickDown` every ~150 ms (and the two chains always together). Buttons on
  objects with a slider/dial are now excluded from press sync (`SyncPolicy.IsDragControl`) — M5 syncs them by value.

## Milestone 5: continuous controls (2026-11-17, v0.4.0) — built, untested

- `ControlSync.cs`: every shared `DialInteractable` / `LinearSliderInteractable` syncs by value.
  Dragger drives it (guest → host 20 Hz unreliable + reliable Grab/Release; host relays); host broadcasts any
  other change (own drags + game-driven changes such as the traverse wheel backdrive). Guests ignore values for a
  control they're holding. New guests get a snapshot of all control values.
- Apply: `SetSliderValue`, `SetDialValue` (Limited dials), `SetAccumulatedValueUnlimited(v, true, true)` (Unlimited).
  `DialMode`: Unlimited = 0, Limited = 1. **Unverified:** that Unlimited `AccumulatedValue` uses the same units as
  `SetAccumulatedValueUnlimited(angleDegrees…)` — first apply per control logs before/target/after.
- Turret: host sends `_CurrentAngle`, `_DesiredRotation`, `rotationVelocity`, gun `_CurrentElevation` at 10 Hz;
  guests snap if > `TurretSnapDegrees` (5°) off, else blend 30%, then `ApplyRotationToTransforms()`.
  Guest-side correction can't be tested locally (single-instance game) — needs a real second player.
- AsmScan now prints property types and enum values.
- **M5 test 1 (FakePeer):** snapshot of 50 controls ✅; elevation lever (slider) drag ✅ — left gun went to 30;
  units assumption ✅ (`first apply … Unlimited: -25200 -> target -25200, now -25200`).
  ❌ Traverse/elevation **wheels spun glitchily and the turret didn't follow**: after release the traverse wheel was
  back at -25200. Cause: `TurretController` only follows its dial between the dial's `OnBeginDialDrag`/`OnEndDialDrag`
  (C# `Action`) events; otherwise `BackdriveRotationDial` drives the wheel from the turret → tug-of-war.
  **Fix:** on remote grab/release, invoke the control's `OnBeginDialDrag`/`OnEndDialDrag` (or slider equivalents) and
  `OnGrab`/`OnRelease` UnityEvents. Released on peer leave / session stop so the turret can't stay stuck mid-drag.
- **M5 probe (TurretProbe.cs):** local drag → `DesiredRotation` follows `MapDialToTurretRotation(wheel)` (= wheel / 720:
  -27496 → -38.19), `desiredRotationVelocity*` stay 0 (speed is NOT what drives it). Remote drag → wheel
  `IsDragging` false, our value is overwritten next frame by `BackdriveRotationDial` (turret angle × 720 = -27576).
  So the turret only reads its wheel while the wheel's own `IsDragging` (local mouse) is true.
  **Fix 2:** while a remote player holds the traverse wheel, set `_DesiredRotation = MapDialToTurretRotation(wheel)`
  each frame and skip `BackdriveRotationDial` (Harmony prefix). Watch for its FIRST HIT — if it never fires, it's
  inlined into `TurretController.Update`.
- **M5 retest:** the `BackdriveRotationDial` prefix **never fired** (no FIRST HIT) — it's inlined into
  `TurretController.Update`; patch removed. **Fix 3:** the traverse wheel is special-cased: remote values are never
  written to the wheel; the host sets `_DesiredRotation = MapDialToTurretRotation(value)` and lets the game backdrive the
  wheel as the turret turns. Drag Actions aren't raised for it (only grab/release sounds), so `OnEndRotationDialDrag`
  can't cancel a remote turn. Guests follow via `TurretState`.
- **M5 retest 3 ✅:** FakePeer `drag 23 7200` (wheel -25200 → -18000) turned the host turret -35° → -25° exactly
  (= -18000 / 720), peaking at 2.99°/s, and the turret settled with desired = angle. Guest-side turret correction
  (TurretState snap/blend) still needs a real second player to test.

## Milestone 6: mission flow (2026-11-17, v0.5.0) — built, untested

- Recon: `MissionManager.Instance`, `CurrentPhase` (`GamePhase`: MainMenu 0, BrowsingMap 1, MissionActive 2),
  `CurrentOperation.OperationID`, `CurrentMission.MissionID`; `StartOperation` has 2- and 3-arg (checkpoint) overloads.
  `FireMission` has `useFixedSeed`, `fixedSeed`, `seed`, `GenerateMission()`; its placement helpers take a `System.Random`.
- `MissionSync.cs` / `MissionPatches.cs`: host broadcasts `MissionState` (phase, op, mission, seed) on any change and in
  the join snapshot; `FinishMission` as `MissionEvent`. Guests apply via the same MissionManager calls and are blocked
  (`MissionGate`) from StartOperation/Resume/LoadMission/Reload/EnterBrowsingMap/ReturnToMap/EndOperation/FinishMission.
- Seed: host picks one before a mission loads; `GenerateMission` is forced to it on both sides (unless designer-fixed).
- Local test without a second game: `MissionReplayKey` (Insert) re-applies the last MissionActive state through the
  guest apply path. Guest gating itself needs a real second player.
- Known gaps: checkpoints/resume aren't transferred; `ReloadCurrentMission` on the host doesn't reload guests (same id);
  other randomness (UnityEngine.Random) isn't seeded.
- **M6 test 1 (tutorial 1 = OperationID `Base_Campaign`, MissionID `Hospital False Flag`):** host states ✅
  (MainMenu → BrowsingMap → MissionActive), join snapshot ✅, seed forced into `GenerateMission` ✅, guest-sent
  MissionState ignored ✅. Starting from the campaign map goes `ResumeOperation` → `StartOperation(op, mission,
  checkpoint)` → `LoadMission` in one frame. ❌ Insert replay: NullReferenceException in LoadMission.
  Causes + fixes: (1) transitional states (`MissionActive` with empty mission id, while starting and while returning to
  menu) were sent/recorded → now skipped; (2) a new seed was picked at each nested call → only once per frame.
- **M6 retest ✅:** one seed per start (42872608), transitional states skipped, and the Insert replay ran the guest path
  `StartOperation(Base_Campaign, Hospital False Flag)` from the main menu → tutorial 1 loaded and
  `GenerateMission seed=42872608` matched the original run. Still untested with a real guest: MissionGate blocking and
  applying BrowsingMap/MainMenu/FinishMission.

## Milestone 7: player avatars (2026-11-17, v0.6.0) — built, untested

- Recon: `FirstPersonController` (root object) has `cameraRoot` (head/look), `yaw`, `pitch`, `isCrouched`,
  `controller` (CharacterController), moving-platform support. Unity proxies keep `GameObject.CreatePrimitive`,
  `Shader.Find`, `Material.SetColor`, `Object.DontDestroyOnLoad`.
- `Avatars.cs`: everyone sends feet position + `cameraRoot` rotation + crouch at `PoseHz` (15) unreliable; the host
  stamps the sender's peer id and relays (host = player 0), so nobody gets their own pose back. Placeholder: capsule
  body, sphere head, black visor for look direction, colour per player, no colliders, `DontDestroyOnLoad`; smoothed
  toward the latest pose; removed on leave / session stop / `AvatarTimeout` (5 s) without updates.
- World-space positions in Master Turret Scene; relies on turret angles matching (M5).
- FakePeer `walk [r]` / `stand` / `still` acts as a visible remote player around the host.
- **M7 test (log):** URP Lit shader found ✅; avatar created on first pose, exactly 2.0 m from the host for `walk` ✅;
  removed after `still` (timed out) ✅, re-created, removed when FakePeer quit (left) ✅. Visual check by the user pending.
- **M7 visual check ✅:** capsule looked right and faced correctly. It clipped through objects during FakePeer `walk`,
  expected: FakePeer moves in a perfect circle ignoring walls. Real players send positions from their own
  CharacterController, so they can't walk through walls.

## Milestone 8: Steam transport (2026-11-17, v0.7.0) — built, untested

- Recon: the shipped `Plugins/x86_64/steam_api64.dll` exports the flat API we need: `SteamAPI_SteamNetworking_v006`,
  `SteamAPI_SteamFriends_v018`, `SteamAPI_SteamUser_v023`, `SteamAPI_SteamUtils_v010`, `ISteamNetworking_SendP2PPacket /
  IsP2PPacketAvailable / ReadP2PPacket / AcceptP2PSessionWithUser / CloseP2PSessionWithUser`, `ISteamFriends_GetFriendCount /
  GetFriendByIndex / GetFriendGamePlayed / GetFriendPersonaName / GetPersonaName`, `ISteamUser_GetSteamID`,
  `ISteamUtils_GetAppID`, `SteamAPI_IsSteamRunning`. (Listed with a small PE export parser.)
- `Transport/SteamApi.cs`: P/Invoke into that DLL (not the IL2CPP Steamworks.NET proxies, which may be stripped).
- `Transport/SteamTransport.cs`: friends-only, no lobbies/callbacks, all polled. Guest sends Hello(key) to a friend in
  IRON NEST (`SteamJoinTarget` name/SteamID64 or first found); host AcceptP2PSessionWithUser's every friend in the game
  every 2 s and answers Welcome/Reject (version check). Ping 1 s, drop after 10 s silence, Bye on stop.
- Settings: `Transport` = LiteNetLib (default, FakePeer) | Steam; `SteamJoinTarget`; `SteamCheckKey` (PageUp) logs
  Steam identity + friends in game locally. Steam can't P2P to yourself → full test needs a friend who owns the game.
- **M8 local check ✅:** PageUp → `Steam check: OK as <name> (<SteamID>), app 2950790. Friends in IRON NEST: none` —
  P/Invoke into the game's steam_api64.dll works (interfaces, SteamID, AppID, friend scan). P2P session untested
  (needs a friend who owns the game).

## Co-op menu (2026-11-17) — built, untested

- Swappable by design: `Menu/CoopMenu.cs` = state + actions + mouse/player handling (no drawing);
  `Menu/IMenuView.cs` = `Draw()`; `Menu/ImguiMenuView.cs` = current look. A uGUI or game-style view is a new
  `IMenuView` assigned to `CoopMenu.View`; nothing else changes.
- Opens with `MenuKey` (Backquote). Shows status, Host/Join/Leave, Steam/localhost switch (when offline), connected
  players (Steam names / LiteNetLib ping), Steam friends in IRON NEST with per-friend Join, debug-log toggle.
- Mouse/player: saves `DynamicCursorManager._currentMode`, calls `SwitchToFreeMouse()` + `SetUIBlocking(true, false)`,
  `FirstPersonController.SetFrozen(true)`; on close `SetUIBlocking(false, false)` + `SwitchToPresentationMode(saved)`
  and unfreeze. Forces `Cursor.lockState = None` / `visible = true` every frame while open.
- IMGUI uses only `GUI.*` with explicit rects (no `GUILayout`/`GUI.Window`: option arrays / delegates through IL2CPP).
- Transports gained `DescribePeer(id)`; `Net.Join(target)` takes an explicit friend.
- **Menu test 3 findings:** (1) clicks inside the menu still hit the world (table lamp `OnClickDown` with
  `inWindow True`): the game's `DynamicCursorManager` raycasts the raw mouse; `SetUIBlocking(true, …)` doesn't stop it.
  (2) `GUI.Button` never returned true for Host although mouse down/up were inside it and IMGUI set `hotControl` — IMGUI
  control logic is unreliable here (likely stripped and rebuilt by Il2CppInterop). Drawing (`GUI.Box/Label`) works.
  **Fix:** `ImguiMenuView` draws boxes and hit-tests `Event.current` MouseDown itself (no GUI.Button/Toggle/ScrollView);
  `MenuInputBlock.cs` skips `LookAtTarget.HandleClickDownFromManager`, `Dial/LinearSliderInteractable.HandlePrimaryClickDown`,
  `DynamicCursorManager.PerformHoverDetection/HandleGrabInput` while the menu is open.
- **Menu test 4 ✅:** every button/toggle registers (`Menu: clicked …` + action), all five MenuBlock prefixes fire.
  One leak: the Close click pressed the lamp 3 ms later (input unblocked within the same click) → world clicks stay
  blocked 0.3 s after closing (`CoopMenu.BlocksWorld`).

## Game-style co-op menu (2026-11-17) — built, untested

- UI dump (`UiDump.cs`, PageDown; game reports **Version 1.0 (1742)**): the Esc menu is
  `Main Camera/Options Menu Parent/Clipboard Main Menu` [Animator, AnimatorBoolToggler, PickUpZoomTarget, FailSafeActionActivator]
  → `Canvas` (world-space, GraphicRaycaster) → `ESC Menu Buttons` (MissionSaveMenuController…) → Unity UI `Button`s
  (`Bg`, `BgShadow`, `SelectionUGUI/Selected`, `Text (TMP)` TextMeshProUGUI + StaticLocalisedText) at y 128.8, 83.6, …
  Table-lamp "Universal Button" = Animator + LookAtTarget + BoxCollider + 4×AnimatorBoolToggler + Interactable (no mesh
  of its own — the switch mesh is a sibling).
- `Menu/ClipboardMenuView.cs`: clones that clipboard under an inactive holder (no Awake), destroys every game script
  except AnimatorBoolToggler/SelectionUGUI (and all localisation), keeps one game button as template for a pool of 9,
  slides in/out with the clone's own AnimatorBoolToggler. Clicks: Harmony prefix on `UnityEngine.UI.Button.OnPointerClick`
  / `OnSubmit` matching our buttons by pointer (`ClipboardButtonPatch.cs`) — no delegates into onClick.
- `MenuStyle` = Game (default) | Imgui; Game falls back to IMGUI if the clipboard can't be built. Game UI uses the
  game's `RestoreMouseForMenu()` cursor mode; IMGUI uses `SwitchToFreeMouse()` + forced OS cursor.
- **Clipboard menu test 1 ✅ (log):** built from the clipboard (11 game scripts removed, animator param `IsRaised`),
  button clicks arrive via `Button.OnPointerClick` (Host, Leave, Network switch both ways, Close), Steam and localhost
  hosting from the menu, world clicks blocked while open. User confirmed it looks right (slides in like the Esc menu).

## NestCoop Terminal (2026-11-17) — built, untested

- `Terminal.cs`: physical object that opens the co-op menu. Clickable part = clone of the table lamp's
  "Universal Button" (keeps the game's cursor/prompt config → "Interact with NestCoop Terminal"), with its serialized
  UnityEvents replaced by empty ones (they pointed at the real lamp), animators/togglers/children removed, malfunctions
  off, own BoxCollider over the top panel. Look = primitives with the Requisition Console `.Cover` material, orange
  button, 3D label cloned from the Esc clipboard's `OPTIONS MENU (TMP)` TextMeshPro (localisation removed).
- Default spot: originally 1.6 m left of the Requisition Console — bad, it is covered by a tarp in early levels; now the author's hand-placed spot (0.344, -0.118, -20.661), yaw 181.35. "Place terminal here" (both menus) raycasts 5 m from
  `Camera.main` and saves `TerminalPlaced/X/Y/Z/Yaw`. `Terminal = false` hides it.
- Press → `LookAtTarget.OnClickDown` postfix → `CoopMenu.Open()`; excluded from sync in `SyncPolicy`.
- Proxy notes: `Physics.Raycast(..., out RaycastHit, ...)` (out, not ref); `Animator` needs UnityEngine.AnimationModule.
- **Anchored placement:** "Place terminal here" now parents the terminal to the object it was placed on (the raycast
  hit, walked up to the first uniformly scaled ancestor) and saves `TerminalAnchor` (stable ID) + local
  position/rotation + `TerminalScale` (0.45 desk unit when the surface is > 0.4 m above the player's feet). So it moves
  with turret rotation / `MoveTurret`. Only anchors in Master Turret Scene (mission scenes unload).
  `Ids.ResolveTransform(id)` turns a stable ID back into a Transform. The default spot will become an anchored table
  spot once one is picked in-game.
- **Placement fix:** the first anchored placement hit the menu clipboard (raycast ran while it was still in front of the
  camera) and saved `…/Main Camera/Options Menu Parent/NestCoop Clipboard/ClipBoard` as the anchor. Now placement runs
  0.6 s after closing the menu, uses `Physics.RaycastAll` and ignores hits under the camera or the terminal itself, and
  saved anchors under `Main Camera/` or containing "NestCoop" are rejected on load.
- **Styles:** `TerminalStyle` = Pedestal (floor) | Button (compact box with a big orange button, picked automatically for
  surfaces > 0.4 m above the player's feet — e.g. the safe-box lid by the computer desk). Visuals rebuild on placement.
- **Placement test 2:** anchored to `Master Turret Scene:Turret/Colliders/.Collider.008` (the safe box's collision
  proxy, under `Turret` → moves with the turret), local (0.47, 1.32, -1.20) rot (87.8, 90, 270); pressing works. But
  style came out Pedestal: `FirstPersonController.transform.position` isn't at the feet. Now uses
  `controller.bounds.min.y` (CharacterController) as feet height, and the menu has "Terminal look: Pedestal/Button".
- **Final (user decision):** button look only, fixed place. The terminal is a child of
  `Master Turret Scene:Turret/Colliders/.Collider.008` (safe box by the computer desk) at local position
  (0.4703, 1.3160, -1.2005), rotation (87.804, 90.000, 270.000) — the spot the author placed in-game. Pedestal style,
  "Place terminal here", "Terminal look" and all Terminal* placement settings were removed; only `Terminal` (on/off)
  remains. If the anchor is missing after a game update, no terminal is built (logged) and ` still opens the menu.
- **Esc menu swap:** the game's Esc menu is `EscapeMenuToggleUnityEvent` (IsOpen, TrySetOpen, ForceClose,
  SetOpenState(open, invokeEvent); HandleActionStarted = Esc key). `Menu/EscMenuBridge.cs`: opening the co-op menu
  closes an open Esc menu via `TrySetOpen(false)` (ForceClose fallback); prefixes on SetOpenState/TrySetOpen(open)/
  HandleActionStarted close the co-op menu before the Esc menu opens. Fixes both clipboards raised at once.
- **Swap test ✅:** both directions work (`Esc menu opening — closing the co-op menu` / `closing the Esc menu to show
  the co-op menu` + `TrySetOpen open=False`). `HandleActionStarted` and `TrySetOpen` fire; `SetOpenState` never did
  (inlined) — the backstop patches were the ones that mattered.
