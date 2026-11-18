using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppSleepyNodes;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Milestone 6: host-driven mission flow + shared mission RNG seed.
///
/// Host: whenever MissionManager's phase / operation / mission changes, broadcast MissionState
///   (phase, OperationID, MissionID, seed). FinishMission is broadcast as a MissionEvent. New guests get the
///   current state in their join snapshot.
/// Guest: applies MissionState by calling the same MissionManager methods (with ApplyingRemote set), and is blocked
///   from changing missions on its own (MissionGate patches) — only the host decides.
/// Seed: before a mission loads, the host picks a seed and sends it in MissionState; on both machines the
///   FireMission.GenerateMission prefix forces useFixedSeed/fixedSeed to that seed (unless the mission already uses a
///   designer-fixed seed), so enemy/target placement from FireMission's System.Random matches.
///   Other randomness (UnityEngine.Random elsewhere) is NOT covered — host-authoritative entity sync comes later.
/// Checkpoints (Resume) are not transferred yet: a host resuming a checkpoint puts guests in a fresh run.
///
/// All names from the dump (build 26352746): MissionManager.Instance/CurrentPhase/CurrentOperation/CurrentMission/
/// StartOperation/LoadMission/EnterBrowsingMap/ReturnToMap/EndOperationAndReturnToMenu/FinishMission/SetPhase,
/// GamePhase {MainMenu=0, BrowsingMap=1, MissionActive=2}, OperationGraph.OperationID, MissionGraph.MissionID,
/// FireMission.useFixedSeed/fixedSeed/seed/GenerateMission.
/// </summary>
internal static class MissionSync
{
    public enum EventKind : byte { Finish = 1 }

    readonly struct State : IEquatable<State>
    {
        public readonly MissionManager.GamePhase Phase;
        public readonly string Op, Mission;
        public readonly int Seed;
        public State(MissionManager.GamePhase p, string op, string m, int seed) { Phase = p; Op = op; Mission = m; Seed = seed; }
        public bool Equals(State o) => Phase == o.Phase && Op == o.Op && Mission == o.Mission && Seed == o.Seed;
        public override string ToString() => $"{Phase} op='{Op}' mission='{Mission}' seed={Seed}";
    }

    static State? lastSent;
    static State? lastMissionActive; // for the debug replay key

    /// <summary>Seed to force into the next FireMission.GenerateMission (both host and guest).</summary>
    public static int PendingSeed;
    public static bool HasPendingSeed;

    static readonly System.Random seedSource = new();

    // ---------------- Host: observe ----------------

    /// <summary>Host/offline: a mission is about to load — pick its seed (not when applying a remote change).</summary>
    public static void BeforeLocalMissionLoad()
    {
        if (Net.ApplyingRemote || Net.Role != Role.Host) return; // solo play keeps the game's own randomness
        // Resume → StartOperation → LoadMission all hit this in one frame (M6 test picked 3 seeds); keep the first.
        if (seedFrame == Time.frameCount) return;
        seedFrame = Time.frameCount;
        PendingSeed = seedSource.Next(1, int.MaxValue);
        HasPendingSeed = true;
        if (Mod.DebugLog.Value) Mod.Log.Msg($"MissionSync: picked seed {PendingSeed}");
    }

    static int seedFrame = -1;

    /// <summary>Called after anything that may change phase/operation/mission.</summary>
    public static void OnMissionManagerChanged(string why)
    {
        var s = Current();
        if (s == null) return;
        var st = s.Value;
        // Mid-transition states (M6 test: "MissionActive op='Base_Campaign' mission=''" while starting, and
        // "MissionActive op='' mission=''" while returning to menu) are not real states: never send or record them.
        if (st.Phase == MissionManager.GamePhase.MissionActive && string.IsNullOrEmpty(st.Mission))
        {
            if (Mod.DebugLog.Value) Mod.Log.Msg($"MissionSync: {why} -> skipped transitional {st}");
            return;
        }
        if (st.Phase == MissionManager.GamePhase.MissionActive) lastMissionActive = st;
        if (lastSent is { } prev && prev.Equals(st)) return;
        lastSent = st;
        Mod.Log.Msg($"MissionSync: {why} -> {st}");
        if (Net.Role == Role.Host && Net.CanSend)
            Net.Broadcast(WriteState(st), reliable: true);
    }

    public static void OnLocalFinish()
    {
        if (Net.Role != Role.Host || !Net.CanSend) return;
        Net.Broadcast(Net.Write(MsgType.MissionEvent, w => w.Write((byte)EventKind.Finish)), reliable: true);
        Mod.Log.Msg("MissionSync: sent FinishMission");
    }

    public static void SendSnapshot(int peer)
    {
        var s = Current();
        if (s == null) return;
        Net.SendTo(peer, WriteState(s.Value), reliable: true);
        Mod.Log.Msg($"MissionSync: sent state to guest #{peer}: {s.Value}");
    }

    static State? Current()
    {
        var mm = MissionManager.Instance;
        if (mm == null) return null;
        return new State(mm.CurrentPhase, mm.CurrentOperation?.OperationID ?? "", mm.CurrentMission?.MissionID ?? "",
                         HasPendingSeed ? PendingSeed : 0);
    }

    static byte[] WriteState(State st) => Net.Write(MsgType.MissionState, w =>
    {
        w.Write((byte)st.Phase); w.Write(st.Op); w.Write(st.Mission); w.Write(st.Seed);
    });

    // ---------------- Receive ----------------

    public static void Handle(MsgType type, BinaryReader r)
    {
        if (type == MsgType.MissionState)
        {
            var st = new State((MissionManager.GamePhase)r.ReadByte(), r.ReadString(), r.ReadString(), r.ReadInt32());
            if (Net.Role != Role.Guest) { Mod.Log.Warning($"MissionSync: ignored mission state from a guest ({st})"); return; }
            Apply(st, "host");
        }
        else if (type == MsgType.MissionEvent)
        {
            var kind = (EventKind)r.ReadByte();
            if (Net.Role != Role.Guest) return;
            if (kind == EventKind.Finish) Run("FinishMission", mm => mm.FinishMission());
        }
    }

    /// <summary>Make this machine match a mission state (guest receiving the host's, or the debug replay key).</summary>
    static void Apply(State st, string source)
    {
        var mm = MissionManager.Instance;
        if (mm == null) { Mod.Log.Warning("MissionSync: no MissionManager yet"); return; }
        Mod.Log.Msg($"MissionSync: apply ({source}) {st}; local is {mm.CurrentPhase} mission='{mm.CurrentMission?.MissionID}'");

        switch (st.Phase)
        {
            case MissionManager.GamePhase.MainMenu:
                if (mm.CurrentPhase != MissionManager.GamePhase.MainMenu)
                    Run("EndOperationAndReturnToMenu", m => m.EndOperationAndReturnToMenu());
                break;

            case MissionManager.GamePhase.BrowsingMap:
                if (mm.CurrentPhase == MissionManager.GamePhase.MissionActive) Run("ReturnToMap", m => m.ReturnToMap());
                else if (mm.CurrentPhase == MissionManager.GamePhase.MainMenu) Run("EnterBrowsingMap", m => m.EnterBrowsingMap());
                break;

            case MissionManager.GamePhase.MissionActive:
            {
                if (mm.CurrentPhase == MissionManager.GamePhase.MissionActive && mm.CurrentMission?.MissionID == st.Mission) break;
                if (string.IsNullOrEmpty(st.Mission)) { Mod.Log.Warning("MissionSync: MissionActive without a mission id; ignored"); break; }
                var mission = FindMission(st.Mission);
                if (mission == null) { Mod.Log.Error($"MissionSync: mission '{st.Mission}' not found on this machine"); break; }
                var op = FindOperation(st.Op);
                PendingSeed = st.Seed;
                HasPendingSeed = st.Seed != 0;
                if (op != null && mm.CurrentOperation?.OperationID != st.Op)
                    Run($"StartOperation({st.Op}, {st.Mission})", m => m.StartOperation(op, mission));
                else
                    Run($"LoadMission({st.Mission})", m => m.LoadMission(mission, false, null));
                break;
            }
        }
    }

    static void Run(string what, Action<MissionManager> action)
    {
        var mm = MissionManager.Instance;
        if (mm == null) return;
        Mod.Log.Msg($"MissionSync: -> {what}");
        Overlay.Toast($"Host: {what}");
        Net.ApplyingRemote = true;
        try { action(mm); }
        catch (Exception e) { Mod.Log.Error($"MissionSync: {what} failed: {e}"); }
        finally { Net.ApplyingRemote = false; }
    }

    static MissionGraph FindMission(string id) => FindAsset<MissionGraph>(g => g.MissionID == id);
    static OperationGraph FindOperation(string id) => string.IsNullOrEmpty(id) ? null : FindAsset<OperationGraph>(g => g.OperationID == id);

    static T FindAsset<T>(Func<T, bool> match) where T : UnityEngine.Object
    {
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
        {
            var g = o.TryCast<T>();
            if (g != null && match(g)) return g;
        }
        return null;
    }

    // ---------------- Seed ----------------

    public static void BeforeGenerate(FireMission fm, out (bool useFixed, int fixedSeed, bool forced) saved)
    {
        saved = (fm.useFixedSeed, fm.fixedSeed, false);
        if (!HasPendingSeed || fm.useFixedSeed) return; // designer-fixed seeds are already identical everywhere
        fm.useFixedSeed = true;
        fm.fixedSeed = PendingSeed;
        saved.forced = true;
    }

    public static void AfterGenerate(FireMission fm, (bool useFixed, int fixedSeed, bool forced) saved)
    {
        Mod.Log.Msg($"MissionSync: FireMission.GenerateMission seed={fm.seed} (forced={saved.forced}, designerFixed={saved.useFixed && !saved.forced})");
        if (!saved.forced) return;
        fm.useFixedSeed = saved.useFixed;
        fm.fixedSeed = saved.fixedSeed;
    }

    // ---------------- Debug ----------------

    /// <summary>MissionReplayKey: replay the last MissionActive state through the guest apply path (local test).</summary>
    public static void DebugReplay()
    {
        if (lastMissionActive is not { } st) { Mod.Log.Msg("Mission replay: start a mission first."); Overlay.Toast("Mission replay: start a mission first"); return; }
        Apply(st, "debug replay");
    }

    public static void Reset() { lastSent = null; HasPendingSeed = false; }
}
