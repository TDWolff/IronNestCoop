using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace NestCoop;

// 0.9.1: three things that still let one machine decide on its own.
//
// 1. Pause. The game's PauseManager pauses (time scale) when a pausing menu opens (RequestPause / RequestGlobalPause,
//    TogglePause) and when the window loses focus (OnApplicationFocus, e.g. alt-tab); TimePause freezes time while
//    enabled. In co-op that froze ONE player's battle while the others kept going. During a session these are
//    skipped — like most co-op games, co-op doesn't pause. Skipped requests are counted so the matching releases are
//    skipped too and the game's pause counter stays balanced.
// 2. Mission win/fail: MissionManager.MarkMissionComplete / MarkMissionFailed run on guests only for the host's
//    verdict (MissionEvent Complete/Failed). FinishMission was already host-only (MissionPatches).
// 3. Button malfunctions: a guest's own presses never roll a malfunction (and the host applies them with the sent
//    "None"), so a guest can't decide that the host's button dead-presses. Host presses roll as normal and the
//    result is forced on everyone (M3).
// Names from the dump (build 26352746); patches log their first hit with DebugLog on.

static class CoopPause
{
    public static bool Block => Net.Active;
    public static int skippedRequests, skippedGlobal;
    public static readonly HashSet<IntPtr> skippedTimePause = new();

    /// <summary>A session ended: forget skipped pauses (the matching releases may never come).</summary>
    public static void Reset() { skippedRequests = skippedGlobal = 0; skippedTimePause.Clear(); }
}

[HarmonyPatch(typeof(PauseManager), nameof(PauseManager.RequestPause))]
static class Pause_Request
{
    static bool Prefix()
    {
        if (!CoopPause.Block) return true;
        CoopPause.skippedRequests++;
        PatchLog.Hit("PauseManager.RequestPause (skipped: co-op doesn't pause)");
        return false;
    }
}

[HarmonyPatch(typeof(PauseManager), nameof(PauseManager.ReleasePause))]
static class Pause_Release
{
    static bool Prefix()
    {
        if (CoopPause.skippedRequests <= 0) return true;
        CoopPause.skippedRequests--;
        return false;
    }
}

[HarmonyPatch(typeof(PauseManager), nameof(PauseManager.RequestGlobalPause))]
static class Pause_RequestGlobal
{
    static bool Prefix()
    {
        if (!CoopPause.Block) return true;
        CoopPause.skippedGlobal++;
        PatchLog.Hit("PauseManager.RequestGlobalPause (skipped: co-op doesn't pause)");
        return false;
    }
}

[HarmonyPatch(typeof(PauseManager), nameof(PauseManager.ReleaseGlobalPause))]
static class Pause_ReleaseGlobal
{
    static bool Prefix()
    {
        if (CoopPause.skippedGlobal <= 0) return true;
        CoopPause.skippedGlobal--;
        return false;
    }
}

[HarmonyPatch(typeof(PauseManager), nameof(PauseManager.TogglePause))]
static class Pause_Toggle
{
    static bool Prefix()
    {
        if (!CoopPause.Block) return true;
        PatchLog.Hit("PauseManager.TogglePause (skipped: co-op doesn't pause)");
        return false;
    }
}

[HarmonyPatch(typeof(PauseManager), nameof(PauseManager.OnApplicationFocus))]
static class Pause_Focus
{
    // Alt-tab must not pause a co-op battle.
    static bool Prefix(bool hasFocus)
    {
        if (!CoopPause.Block) return true;
        PatchLog.Hit("PauseManager.OnApplicationFocus (skipped: co-op doesn't pause on focus loss)", $"hasFocus={hasFocus}");
        return false;
    }
}

[HarmonyPatch(typeof(TimePause), nameof(TimePause.OnEnable))]
static class TimePause_Enable
{
    static bool Prefix(TimePause __instance)
    {
        if (!CoopPause.Block) return true;
        CoopPause.skippedTimePause.Add(__instance.Pointer);
        PatchLog.Hit("TimePause.OnEnable (skipped: co-op doesn't pause)", __instance.gameObject.name);
        return false;
    }
}

[HarmonyPatch(typeof(TimePause), nameof(TimePause.OnDisable))]
static class TimePause_Disable
{
    // If we skipped this TimePause's freeze, also skip its "restore" (it would restore a time scale it never saved).
    static bool Prefix(TimePause __instance) => !CoopPause.skippedTimePause.Remove(__instance.Pointer);
}

// ---------------- mission win / fail ----------------

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.MarkMissionComplete))]
static class MM_MarkComplete
{
    static bool Prefix()
    {
        if (!Combat.GuestLocal) return true;
        PatchLog.Hit("MarkMissionComplete (guest: waiting for the host's verdict)");
        return false;
    }
    static void Postfix(bool forceSetState) { if (!Net.ApplyingRemote) MissionSync.OnHostVerdict(MissionSync.EventKind.Complete, forceSetState); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.MarkMissionFailed))]
static class MM_MarkFailed
{
    static bool Prefix()
    {
        if (!Combat.GuestLocal) return true;
        PatchLog.Hit("MarkMissionFailed (guest: waiting for the host's verdict)");
        return false;
    }
    static void Postfix(bool forceSetState) { if (!Net.ApplyingRemote) MissionSync.OnHostVerdict(MissionSync.EventKind.Failed, forceSetState); }
}
