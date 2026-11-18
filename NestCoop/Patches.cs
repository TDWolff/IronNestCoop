using HarmonyLib;
using Il2Cpp;

namespace NestCoop;

// Milestone 2: logging-only patches. Every target method name comes from the Assembly-CSharp dump
// (Steam build 26352746); see docs/game-map.md. MelonLoader applies [HarmonyPatch] classes automatically.
// Nothing here changes game behaviour.

// ---------- Buttons (LookAtTarget) ----------

/// NOT a per-press hook: the cursor manager broadcasts every click to every subscribed button (~290 calls per real
/// press in testing); each one checks whether it was the target. Kept as first-hit only, for reference.
[HarmonyPatch(typeof(LookAtTarget), nameof(LookAtTarget.HandleClickDownFromManager))]
static class LookAtTarget_HandleClickDownFromManager
{
    static void Postfix() => PatchLog.Hit("LookAtTarget.HandleClickDownFromManager");
}

/// The actual press (fires onClickDown etc.) — this is the sync hook (34 calls = 34 real presses in testing).
/// Also reached by the debug replay key (DebugKeys), which is flagged [remote].
[HarmonyPatch(typeof(LookAtTarget), nameof(LookAtTarget.OnClickDown))]
static class LookAtTarget_OnClickDown
{
    static void Postfix(LookAtTarget __instance)
    {
        if (Terminal.Is(__instance)) { Mod.Log.Msg("Terminal: pressed"); Menu.CoopMenu.Open(); return; } // our terminal: open the menu, never sync
        if (!Net.ApplyingRemote) DebugKeys.LastPressed = __instance;
        PatchLog.Hit("LookAtTarget.OnClickDown", $"{Ids.Of(__instance)} active={__instance.isActive} malfunction={__instance.currentMalfunction}");
        Net.SendButtonPress(__instance); // no-op when offline or when this press came from the network
    }
}

/// Random dead/double press (returns LookAtTarget/MalfunctionType; called from inside OnClickDown).
/// When applying a remote press, skip the local roll and use the sender's result so both sides match.
[HarmonyPatch(typeof(LookAtTarget), nameof(LookAtTarget.EvaluateMalfunction))]
static class LookAtTarget_EvaluateMalfunction
{
    static bool Prefix(LookAtTarget __instance, ref LookAtTarget.MalfunctionType __result)
    {
        if (!Net.ApplyingRemote || Net.ForcedMalfunction is not { } forced) return true; // run original
        __instance.currentMalfunction = forced;
        __result = forced;
        return false; // skip the random roll
    }

    static void Postfix(LookAtTarget __instance) =>
        PatchLog.Hit("LookAtTarget.EvaluateMalfunction", $"{Ids.Of(__instance)} -> {__instance.currentMalfunction}");
}

// ---------- Wheels / dials ----------

[HarmonyPatch(typeof(DialInteractable), nameof(DialInteractable.BeginDialDrag))]
static class DialInteractable_BeginDialDrag
{
    static void Postfix(DialInteractable __instance)
    {
        PatchLog.Hit("DialInteractable.BeginDialDrag", $"{Ids.Of(__instance)} value={__instance.AccumulatedValue:0.###}");
        if (!Net.ApplyingRemote) ControlSync.OnLocalGrab(__instance, true);
    }
}

[HarmonyPatch(typeof(DialInteractable), nameof(DialInteractable.EndDialDrag))]
static class DialInteractable_EndDialDrag
{
    static void Postfix(DialInteractable __instance)
    {
        PatchLog.Hit("DialInteractable.EndDialDrag", $"{Ids.Of(__instance)} value={__instance.AccumulatedValue:0.###}");
        if (!Net.ApplyingRemote) ControlSync.OnLocalGrab(__instance, false);
    }
}

// ---------- Levers / sliders ----------

[HarmonyPatch(typeof(LinearSliderInteractable), nameof(LinearSliderInteractable.BeginSliderDrag))]
static class LinearSliderInteractable_BeginSliderDrag
{
    static void Postfix(LinearSliderInteractable __instance)
    {
        PatchLog.Hit("LinearSliderInteractable.BeginSliderDrag", $"{Ids.Of(__instance)} value={__instance.Value:0.###}");
        if (!Net.ApplyingRemote) ControlSync.OnLocalGrab(__instance, true);
    }
}

[HarmonyPatch(typeof(LinearSliderInteractable), nameof(LinearSliderInteractable.EndSliderDrag))]
static class LinearSliderInteractable_EndSliderDrag
{
    static void Postfix(LinearSliderInteractable __instance)
    {
        PatchLog.Hit("LinearSliderInteractable.EndSliderDrag", $"{Ids.Of(__instance)} value={__instance.Value:0.###}");
        if (!Net.ApplyingRemote) ControlSync.OnLocalGrab(__instance, false);
    }
}

// ---------- Systems ----------

[HarmonyPatch(typeof(TurretController), nameof(TurretController.OnBeginRotationDialDrag))]
static class TurretController_OnBeginRotationDialDrag
{
    static void Postfix() => PatchLog.Hit("TurretController.OnBeginRotationDialDrag", "");
}

[HarmonyPatch(typeof(TurretController), nameof(TurretController.OnEndRotationDialDrag))]
static class TurretController_OnEndRotationDialDrag
{
    static void Postfix() => PatchLog.Hit("TurretController.OnEndRotationDialDrag", "");
}

[HarmonyPatch(typeof(ArtilleryReloadController), nameof(ArtilleryReloadController.SetState))]
static class ArtilleryReloadController_SetState
{
    static void Prefix(ArtilleryReloadController __instance, out int __state) => __state = __instance.CurrentStateIndex;
    static void Postfix(ArtilleryReloadController __instance, int __state)
    {
        PatchLog.Hit("ArtilleryReloadController.SetState", $"{Ids.Of(__instance)} {__state} -> {__instance.CurrentStateIndex}");
        if (__instance.CurrentStateIndex != __state) Net.SendReloadState(__instance); // host only; no-op otherwise
    }
}

[HarmonyPatch(typeof(GunController), nameof(GunController.FireShell))]
static class GunController_FireShell
{
    static void Postfix(GunController __instance) => PatchLog.Hit("GunController.FireShell", Ids.Of(__instance));
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.LoadMission))]
static class MissionManager_LoadMission
{
    static void Postfix() => PatchLog.Hit("MissionManager.LoadMission", "");
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartMissionRuntime))]
static class MissionManager_StartMissionRuntime
{
    static void Postfix() => PatchLog.Hit("MissionManager.StartMissionRuntime", "");
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.FinishMission))]
static class MissionManager_FinishMission
{
    static void Postfix() => PatchLog.Hit("MissionManager.FinishMission", "");
}
