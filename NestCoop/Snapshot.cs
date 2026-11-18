using System.IO.Compression;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppNewtonsoft.Json;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Full mission state from the host, using the game's OWN checkpoint system (0.9.0).
///
/// Host: MissionSaveManager.Instance.Capture(MissionManager, contentHash) → MissionSaveData (mission state, FireMission
/// entities, economy, counter-battery, pressure, turret, mission graphs, punchcards, teleprinters, map tokens/markers,
/// notepad, ballistic calculator, and RandomStateJson = UnityEngine.Random state) → JSON with the game's own Newtonsoft
/// settings (ProgressionSaveUtility.JsonSettings, the ones its checkpoint files use) → gzip → MissionCheckpoint message.
/// Content hash: MissionCheckpointFingerprint.TryGetCurrentHash(mission).
/// Guest: parse with the same settings, then load it through the game's own resume path:
///   not in that mission → MissionManager.StartOperation(op, mission, checkpoint)
///   already in it       → MissionManager.LoadMission(mission, forceReload: true, checkpoint)
/// Sent: to a guest joining mid-mission; to everyone after the host resumes/reloads a mission; on a guest's
/// "Resync from host"; and every ResyncMinutes if set (0 = off, since a resync reloads the mission scene).
/// All names from the dump (build 26352746). Guests never write the checkpoint to disk.
/// </summary>
internal static class Snapshot
{
    static float broadcastAt = -1, nextPeriodic;

    // ---------------- host ----------------

    /// <summary>Capture the current mission as gzipped JSON, or null if not in a mission / capture failed.</summary>
    static byte[] CaptureBytes(out string op, out string mission)
    {
        op = mission = null;
        var mm = MissionManager.Instance;
        var msm = MissionSaveManager.Instance;
        if (mm == null || msm == null || mm.CurrentPhase != MissionManager.GamePhase.MissionActive || mm.CurrentMission == null) return null;
        op = mm.CurrentOperation?.OperationID ?? "";
        mission = mm.CurrentMission.MissionID;
        try
        {
            string hash = "";
            MissionCheckpointFingerprint.TryGetCurrentHash(mm.CurrentMission, out hash);
            var data = msm.Capture(mm, hash ?? "");
            if (data == null) { Mod.Log.Warning("Snapshot: Capture returned nothing"); return null; }
            string json = JsonConvert.SerializeObject(data, ProgressionSaveUtility.JsonSettings);
            var raw = Encoding.UTF8.GetBytes(json);
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) gz.Write(raw, 0, raw.Length);
            var bytes = ms.ToArray();
            Mod.Log.Msg($"Snapshot: captured '{mission}' ({raw.Length / 1024} KB JSON → {bytes.Length / 1024} KB)");
            return bytes;
        }
        catch (Exception e) { Mod.Log.Error($"Snapshot: capture failed: {e}"); return null; }
    }

    static byte[] Message(byte[] gz, string op, string mission) => Net.Write(MsgType.MissionCheckpoint, w =>
    {
        w.Write(op); w.Write(mission); w.Write(MissionSync.HasPendingSeed ? MissionSync.PendingSeed : 0);
        w.Write(gz.Length); w.Write(gz);
    });

    /// <summary>Host: send the current mission to one guest (join mid-mission / resync request). False if not in a mission.</summary>
    public static bool SendTo(int peer)
    {
        if (Net.Role != Role.Host) return false;
        var gz = CaptureBytes(out var op, out var mission);
        if (gz == null) return false;
        Net.SendTo(peer, Message(gz, op, mission), reliable: true);
        Mod.Log.Msg($"Snapshot: sent '{mission}' to guest #{peer}");
        return true;
    }

    static void Broadcast(string why)
    {
        if (Net.Role != Role.Host || !Net.Active) return;
        var gz = CaptureBytes(out var op, out var mission);
        if (gz == null) return;
        Net.Broadcast(Message(gz, op, mission), reliable: true);
        Mod.Log.Msg($"Snapshot: broadcast '{mission}' ({why})");
    }

    /// <summary>Host: broadcast after a delay (lets a resumed mission finish restoring first).</summary>
    public static void BroadcastSoon(float seconds = 2f) => broadcastAt = Time.unscaledTime + seconds;

    public static void Update()
    {
        if (Net.Role != Role.Host) { broadcastAt = -1; return; }
        float now = Time.unscaledTime;
        if (broadcastAt >= 0 && now >= broadcastAt) { broadcastAt = -1; Broadcast("mission resumed/reloaded"); }
        int minutes = Mod.ResyncMinutes.Value;
        if (minutes > 0 && now >= nextPeriodic)
        {
            if (nextPeriodic > 0) Broadcast($"periodic, every {minutes} min");
            nextPeriodic = now + minutes * 60f;
        }
    }

    // ---------------- guest ----------------

    /// <summary>Guest: ask the host for a fresh full state (menu "Resync from host").</summary>
    public static void RequestResync()
    {
        if (Net.Role != Role.Guest || !Net.CanSend) { Overlay.Toast("Resync: join a host first"); return; }
        Net.SendToSession(Net.Write(MsgType.ResyncRequest, _ => { }), reliable: true);
        Overlay.Toast("Asked the host for a resync…");
    }

    /// <summary>
    /// Debug (CheckpointTestKey): run the whole guest path on this machine — capture the current mission, gzip it,
    /// parse it back with the game's settings and reload the mission from it, exactly as a guest would.
    /// </summary>
    public static void DebugRoundTrip()
    {
        var gz = CaptureBytes(out var op, out var mission);
        if (gz == null) { Overlay.Toast("Checkpoint test: start a mission first"); return; }
        Mod.Log.Msg("Snapshot: debug round trip — applying the captured checkpoint through the guest path");
        Overlay.Toast("Checkpoint test: reloading from a captured checkpoint…");
        ApplyBytes(op, mission, MissionSync.HasPendingSeed ? MissionSync.PendingSeed : 0, gz);
    }

    public static void Handle(MsgType type, int from, BinaryReader r)
    {
        if (type == MsgType.ResyncRequest)
        {
            if (Net.Role == Role.Host && !SendTo(from)) Mod.Log.Msg($"Snapshot: guest #{from} asked for a resync but no mission is running");
            return;
        }
        // MissionCheckpoint
        string op = r.ReadString(), mission = r.ReadString();
        int seed = r.ReadInt32();
        var gz = r.ReadBytes(r.ReadInt32());
        if (Net.Role != Role.Guest) { Mod.Log.Warning("Snapshot: ignored a checkpoint from a guest"); return; }
        ApplyBytes(op, mission, seed, gz);
    }

    static void ApplyBytes(string op, string mission, int seed, byte[] gz)
    {
        try
        {
            string json;
            using (var gzs = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress))
            using (var sr = new StreamReader(gzs, Encoding.UTF8)) json = sr.ReadToEnd();
            var obj = JsonConvert.DeserializeObject(json, Il2CppType.Of<MissionSaveData>(), ProgressionSaveUtility.JsonSettings);
            var data = obj?.TryCast<MissionSaveData>();
            if (data == null) { Mod.Log.Error("Snapshot: couldn't read the host's checkpoint"); return; }
            Mod.Log.Msg($"Snapshot: received '{mission}' ({gz.Length / 1024} KB)");
            MissionSync.ApplyCheckpoint(op, mission, seed, data);
        }
        catch (Exception e) { Mod.Log.Error($"Snapshot: applying the host's checkpoint failed: {e}"); }
    }
}
