using HarmonyLib;
using Il2Cpp;
using Il2CppSleepyNodes;

namespace NestCoop;

// Milestone 6 patches on MissionManager / FireMission (names + overload signatures from the dump, build 26352746).
// - Gate:   a connected guest can't change missions itself (only via ApplyingRemote from the host's message).
// - Observe: after any change, MissionSync compares phase/operation/mission and the host broadcasts differences.
// - Seed:   the host picks a seed before a mission loads; GenerateMission is forced to it on both machines.

static class MissionGate
{
    public static bool Allow(string what)
    {
        if (Net.Role != Role.Guest || Net.ApplyingRemote) return true;
        Mod.Log.Msg($"MissionGate: blocked guest {what} (host decides missions)");
        Overlay.Toast("Only the host can change missions");
        return false;
    }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartOperation), new[] { typeof(OperationGraph), typeof(MissionGraph) })]
static class MM_StartOperation2
{
    static bool Prefix() { if (!MissionGate.Allow("StartOperation")) return false; MissionSync.BeforeLocalMissionLoad(); return true; }
    static void Postfix() { PatchLog.Hit("MissionManager.StartOperation(op, mission)"); MissionSync.OnMissionManagerChanged("StartOperation"); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartOperation), new[] { typeof(OperationGraph), typeof(MissionGraph), typeof(MissionSaveData) })]
static class MM_StartOperation3
{
    static bool Prefix(MissionSaveData checkpoint)
    {
        if (!MissionGate.Allow("StartOperation")) return false;
        MissionSync.BeforeLocalMissionLoad();
        if (checkpoint != null) MissionSync.NoteHostResuming("starting from a checkpoint");
        return true;
    }
    static void Postfix() { PatchLog.Hit("MissionManager.StartOperation(op, mission, checkpoint)"); MissionSync.OnMissionManagerChanged("StartOperation+checkpoint"); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.ResumeOperation))]
static class MM_ResumeOperation
{
    static bool Prefix()
    {
        if (!MissionGate.Allow("ResumeOperation")) return false;
        MissionSync.BeforeLocalMissionLoad();
        MissionSync.NoteHostResuming("resuming an operation");
        return true;
    }
    static void Postfix() { PatchLog.Hit("MissionManager.ResumeOperation"); MissionSync.OnMissionManagerChanged("ResumeOperation"); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.LoadMission))]
static class MM_LoadMission_Sync
{
    static bool Prefix() { if (!MissionGate.Allow("LoadMission")) return false; MissionSync.BeforeLocalMissionLoad(); return true; }
    static void Postfix() => MissionSync.OnMissionManagerChanged("LoadMission");
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.ReloadCurrentMission))]
static class MM_ReloadCurrentMission
{
    static bool Prefix()
    {
        if (!MissionGate.Allow("ReloadCurrentMission")) return false;
        MissionSync.NoteHostResuming("reloading the mission");
        return true;
    }
    static void Postfix() { PatchLog.Hit("MissionManager.ReloadCurrentMission"); MissionSync.OnMissionManagerChanged("ReloadCurrentMission"); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.EnterBrowsingMap))]
static class MM_EnterBrowsingMap
{
    static bool Prefix() => MissionGate.Allow("EnterBrowsingMap");
    static void Postfix() { PatchLog.Hit("MissionManager.EnterBrowsingMap"); MissionSync.OnMissionManagerChanged("EnterBrowsingMap"); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.ReturnToMap))]
static class MM_ReturnToMap
{
    static bool Prefix() => MissionGate.Allow("ReturnToMap");
    static void Postfix() { PatchLog.Hit("MissionManager.ReturnToMap"); MissionSync.OnMissionManagerChanged("ReturnToMap"); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.EndOperationAndReturnToMenu))]
static class MM_EndOperationAndReturnToMenu
{
    static bool Prefix() => MissionGate.Allow("EndOperationAndReturnToMenu");
    static void Postfix() { PatchLog.Hit("MissionManager.EndOperationAndReturnToMenu"); MissionSync.OnMissionManagerChanged("EndOperationAndReturnToMenu"); }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.FinishMission))]
static class MM_FinishMission_Sync
{
    static bool Prefix() => MissionGate.Allow("FinishMission");
    static void Postfix()
    {
        if (!Net.ApplyingRemote) MissionSync.OnLocalFinish();
        MissionSync.OnMissionManagerChanged("FinishMission");
    }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.SetPhase))]
static class MM_SetPhase
{
    static void Postfix() { PatchLog.Hit("MissionManager.SetPhase"); MissionSync.OnMissionManagerChanged("SetPhase"); }
}

[HarmonyPatch(typeof(FireMission), nameof(FireMission.GenerateMission))]
static class FireMission_GenerateMission
{
    static void Prefix(FireMission __instance, out (bool, int, bool) __state)
    {
        MissionSync.BeforeGenerate(__instance, out var saved);
        __state = saved;
    }

    static void Postfix(FireMission __instance, (bool, int, bool) __state)
    {
        PatchLog.Hit("FireMission.GenerateMission");
        MissionSync.AfterGenerate(__instance, __state);
    }
}
