using MelonLoader;

// Game name/developer verified from MelonLoader log + app.info (Steam build 26352746, Unity 6000.3.21f1).
[assembly: MelonInfo(typeof(NestCoop.Mod), "NestCoop", "0.1.0", "Torin")]
[assembly: MelonGame("Iron Nest", "Iron Nest Heavy Turret Simulator")]

namespace NestCoop;

public class Mod : MelonMod
{
    internal static MelonLogger.Instance Log;
    internal static MelonPreferences_Entry<bool> DebugLog;
    internal static MelonPreferences_Entry<bool> ReconOnSceneLoad;
    internal static MelonPreferences_Entry<string> ReplayKey;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        var cat = MelonPreferences.CreateCategory("NestCoop");
        DebugLog = cat.CreateEntry("DebugLog", true, description: "Log first hit of every Harmony patch, scene loads, etc.");
        ReconOnSceneLoad = cat.CreateEntry("ReconOnSceneLoad", true, description: "Write UserData/NestCoop/recon/<scene>.txt listing controls and what they drive.");
        ReplayKey = cat.CreateEntry("ReplayKey", "Backslash", description: "Debug: replays your last button press as if it came from the network. Unity key name, e.g. Backslash, Insert, F9.");
        Log.Msg($"NestCoop loaded. Debug replay key: {ReplayKey.Value}");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (DebugLog.Value) Log.Msg($"Scene loaded: [{buildIndex}] {sceneName}");
    }

    public override void OnLateInitializeMelon()
    {
        // Proves which patches Harmony actually applied (a failed patch is logged by MelonLoader as an error).
        int n = HarmonyInstance.GetPatchedMethods().Count();
        Log.Msg($"Harmony patched {n} methods.");
    }

    public override void OnUpdate() => DebugKeys.Update();

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (!ReconOnSceneLoad.Value) return;
        try { Recon.DumpScene(sceneName); }
        catch (System.Exception e) { Log.Error($"Recon failed for '{sceneName}': {e}"); }
    }
}
