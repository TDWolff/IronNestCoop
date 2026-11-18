using MelonLoader;

// Game name/developer verified from MelonLoader log + app.info (Steam build 26352746, Unity 6000.3.21f1).
[assembly: MelonInfo(typeof(NestCoop.Mod), "NestCoop", NestCoop.Mod.Version, "TDWolff")]
[assembly: MelonGame("Iron Nest", "Iron Nest Heavy Turret Simulator")]

namespace NestCoop;

public class Mod : MelonMod
{
    /// <summary>Also part of the network connection key: different versions refuse to connect.</summary>
    public const string Version = "0.4.0";

    internal static MelonLogger.Instance Log;
    internal static MelonPreferences_Entry<bool> DebugLog;
    internal static MelonPreferences_Entry<bool> ReconOnSceneLoad, ProbeTurret;
    internal static MelonPreferences_Entry<string> ReplayKey, HostKey, JoinKey, LeaveKey, JoinAddress;
    internal static MelonPreferences_Entry<int> Port, SyncHz, TurretHz;
    internal static MelonPreferences_Entry<float> TurretSnapDegrees;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        var cat = MelonPreferences.CreateCategory("NestCoop");
        DebugLog = cat.CreateEntry("DebugLog", true, description: "Log first hit of every Harmony patch, scene loads, network messages.");
        ReconOnSceneLoad = cat.CreateEntry("ReconOnSceneLoad", true, description: "Write UserData/NestCoop/recon/<scene>.txt listing controls and what they drive.");
        ProbeTurret = cat.CreateEntry("ProbeTurret", true, description: "Debug: log traverse wheel + turret internals while the wheel is dragged.");
        ReplayKey = cat.CreateEntry("ReplayKey", "Backslash", description: "Debug: replays your last button press as if it came from the network. Unity key name, e.g. Backslash, Insert, F9.");
        HostKey = cat.CreateEntry("HostKey", "Home", description: "Start hosting a co-op session.");
        JoinKey = cat.CreateEntry("JoinKey", "End", description: "Join the host at JoinAddress:Port.");
        LeaveKey = cat.CreateEntry("LeaveKey", "Delete", description: "Leave / stop the co-op session.");
        JoinAddress = cat.CreateEntry("JoinAddress", "127.0.0.1", description: "Host address to join (localhost only for now).");
        Port = cat.CreateEntry("Port", 27960, description: "UDP port for co-op.");
        SyncHz = cat.CreateEntry("SyncHz", 20, description: "How often dragged wheel/lever values are sent (per second).");
        TurretHz = cat.CreateEntry("TurretHz", 10, description: "How often the host sends turret angle and gun elevations (per second).");
        TurretSnapDegrees = cat.CreateEntry("TurretSnapDegrees", 5f, description: "Guest snaps to the host turret angle when further off than this; smaller errors are blended.");
        Log.Msg($"NestCoop {Version} loaded. Host={HostKey.Value} Join={JoinKey.Value} Leave={LeaveKey.Value} Replay={ReplayKey.Value}");
    }

    public override void OnLateInitializeMelon()
    {
        // Proves which patches Harmony actually applied (a failed patch is logged by MelonLoader as an error).
        int n = HarmonyInstance.GetPatchedMethods().Count();
        Log.Msg($"Harmony patched {n} methods.");
    }

    public override void OnUpdate()
    {
        DebugKeys.Update();
        Net.Poll();
        ControlSync.Update();
    }

    public override void OnGUI() => Overlay.Draw();

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Registry.Invalidate();
        if (DebugLog.Value) Log.Msg($"Scene loaded: [{buildIndex}] {sceneName}");
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) => Registry.Invalidate();

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (!ReconOnSceneLoad.Value) return;
        try { Recon.DumpScene(sceneName); }
        catch (System.Exception e) { Log.Error($"Recon failed for '{sceneName}': {e}"); }
    }

    /// Teardown safety: disconnect before the game starts destroying objects, so nothing gets sent on the way out.
    public override void OnApplicationQuit() => Net.Stop("quitting");
}
