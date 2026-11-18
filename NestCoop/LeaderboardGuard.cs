using HarmonyLib;
using Il2Cpp;

namespace NestCoop;

/// <summary>
/// Milestone 9: keep co-op runs off the leaderboards.
///
/// The game has its own leaderboard opt-out: LeaderboardManager.OptOut (static bool getter, the setting behind its
/// opt-out toggle). While a run is "tainted" by co-op we make that getter return true, so the game itself skips
/// submitting. Steam leaderboard submits (SteamLeaderboardScoreController.SubmitScoreInternal) are skipped too.
/// A run is tainted once a co-op session is active at any point during it; starting a mission while not connected
/// clears it, so solo play with the mod installed still counts normally.
/// Names from the dump (build 26352746); both patches log their first hit.
/// </summary>
internal static class LeaderboardGuard
{
    public static bool Tainted { get; private set; }

    public static void Update()
    {
        if (Net.Active && !Tainted)
        {
            Tainted = true;
            Mod.Log.Msg("Leaderboard: co-op session active — this run won't be submitted to leaderboards");
        }
    }

    /// <summary>A mission run starts: solo runs count again.</summary>
    public static void OnMissionRuntimeStarted()
    {
        if (!Net.Active && Tainted)
        {
            Tainted = false;
            Mod.Log.Msg("Leaderboard: solo run — leaderboard submission back to the game's own setting");
        }
    }
}

[HarmonyPatch(typeof(LeaderboardManager), "get_OptOut")]
static class Leaderboard_OptOut
{
    static void Postfix(ref bool __result)
    {
        if (!LeaderboardGuard.Tainted || __result) return;
        PatchLog.Hit("LeaderboardManager.OptOut (forced on for co-op)");
        __result = true;
    }
}

[HarmonyPatch(typeof(SteamLeaderboardScoreController), nameof(SteamLeaderboardScoreController.SubmitScoreInternal))]
static class Leaderboard_SteamSubmit
{
    static bool Prefix()
    {
        if (!LeaderboardGuard.Tainted) return true;
        Mod.Log.Msg("Leaderboard: skipped a Steam leaderboard submit (co-op run)");
        return false;
    }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartMissionRuntime))]
static class Leaderboard_RunStart
{
    static void Postfix()
    {
        LeaderboardGuard.OnMissionRuntimeStarted();
        MissionSync.OnMissionRuntimeStarted();
    }
}
