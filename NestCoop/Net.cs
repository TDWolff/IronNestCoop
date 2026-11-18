using Il2Cpp;
using NestCoop.Transport;

namespace NestCoop;

internal enum Role { None, Host, Guest }

internal enum MsgType : byte
{
    ButtonPress = 1,   // string id, int malfunction        (any → host → others)
    ReloadState = 2,   // string id, int stateIndex         (host → guests, also in join snapshot)
    ControlValue = 3,  // string id, float value            (dragger → host → others; host → guests) unreliable
    ControlGrab = 4,   // string id, bool grabbed, float v  (dragger → host → others) reliable
    MissionState = 6,  // byte phase, string operationId, string missionId, int seed (host → guests, join snapshot)
    MissionEvent = 7,  // byte kind (1 = FinishMission)                         (host → guests)
    PlayerPose = 8,    // int playerId, 3×float feet, 4×float head rotation, bool crouched (unreliable; host relays)
    TurretState = 5,   // float angle, float desired, float velocity, int n, n×float gunElevation (host → guests)
}

/// <summary>
/// Session layer (Milestone 3). Host/guest roles over an ITransport.
///
/// Button flow (one hop through the host):
///   local press → OnClickDown postfix → SendButtonPress
///     host:  broadcast to all guests
///     guest: send to host → host applies it and relays to the other guests
///   receive → ApplyButtonPress: OnClickDown() with ApplyingRemote=true and ForcedMalfunction set, so the
///   receiver doesn't re-send it and doesn't re-roll the malfunction.
///
/// M3 simplification: whoever presses rolls the malfunction. Fully host-authoritative presses come later.
/// M4: SyncPolicy filters non-shared objects; reload state is host-authoritative; new guests get a snapshot.
/// </summary>
internal static class Net
{
    /// <summary>True while applying a change that came from the network, so patches don't re-send it.</summary>
    public static bool ApplyingRemote;

    /// <summary>While applying a remote press: the malfunction result to use instead of rolling.</summary>
    public static LookAtTarget.MalfunctionType? ForcedMalfunction;

    public static Role Role { get; private set; } = Role.None;
    public static bool Active => Role != Role.None && transport != null;

    static ITransport transport;
    internal static int hostPeer = -1; // guest side: the host's peer id
    static readonly HashSet<int> peers = new();

    /// <summary>Connected peers with display names (for the menu). Guests see just the host.</summary>
    public static IEnumerable<(int id, string name)> Peers => transport == null
        ? Enumerable.Empty<(int, string)>()
        : peers.Select(id => (id, transport.DescribePeer(id))).ToList();

    /// <summary>Overlay text; null when offline.</summary>
    public static string StatusLine => Role switch
    {
        Role.Host => $"NestCoop: HOST  ({peers.Count} guest{(peers.Count == 1 ? "" : "s")})",
        Role.Guest => hostPeer >= 0 ? "NestCoop: GUEST  (connected)" : "NestCoop: GUEST  (connecting...)",
        _ => null,
    };

    static void Say(string msg)
    {
        Mod.Log.Msg("Net: " + msg);
        Overlay.Toast(msg);
    }

    static string ConnectionKey => "NestCoop-" + Mod.Version;

    public static void StartHost()
    {
        if (Active) { Say($"Already {Role}"); return; }
        transport = CreateTransport();
        if (!transport.Host(Mod.Port.Value)) { Say(UseSteam ? "Could not host via Steam (see log)" : $"Could not host on port {Mod.Port.Value} (in use?)"); transport = null; return; }
        Role = Role.Host;
        Say(UseSteam ? "Hosting via Steam (friends can join)" : $"Hosting on localhost:{Mod.Port.Value}");
    }

    /// <summary>Join. <paramref name="target"/> overrides SteamJoinTarget / JoinAddress (e.g. a friend picked in the menu).</summary>
    public static void Join(string target = null)
    {
        if (Active) { Say($"Already {Role}"); return; }
        transport = CreateTransport();
        string address = target ?? (UseSteam ? Mod.SteamJoinTarget.Value : Mod.JoinAddress.Value);
        if (!transport.Connect(address, Mod.Port.Value)) { Say("Join failed to start"); transport = null; return; }
        Role = Role.Guest;
        if (!UseSteam) Say($"Joining {address}:{Mod.Port.Value} ...");
    }

    public static void Stop(string why)
    {
        if (transport == null) return;
        Say($"Disconnected ({why})");
        var t = transport;
        transport = null;   // first, so nothing can send during teardown
        Role = Role.None;
        hostPeer = -1;
        peers.Clear();
        ControlSync.Reset();
        MissionSync.Reset();
        Avatars.Reset();
        t.Stop();
    }

    public static void Poll()
    {
        if (transport == null) return;
        transport.Poll();
        // A transport can give up on its own (Steam: host rejected us / never answered).
        if (transport != null && !transport.IsRunning) Stop("connection closed");
    }

    internal static bool UseSteam => string.Equals(Mod.TransportName.Value, "Steam", StringComparison.OrdinalIgnoreCase);

    static ITransport CreateTransport()
    {
        ITransport t = UseSteam ? new SteamTransport(ConnectionKey) : new LiteNetTransport(ConnectionKey);
        t.PeerConnected += id =>
        {
            peers.Add(id);
            if (Role == Role.Guest) { hostPeer = id; Say("Connected to host"); }
            else { Say($"Guest #{id} joined"); MissionSync.SendSnapshot(id); SendSnapshot(id); ControlSync.SendSnapshot(id); }
        };
        t.PeerDisconnected += (id, reason) =>
        {
            peers.Remove(id);
            ControlSync.OnPeerLeft(id);
            Avatars.OnPeerLeft(id);
            Say(Role == Role.Guest ? $"Host left ({reason})" : $"Guest #{id} left ({reason})");
            if (Role == Role.Guest) Stop("lost host");
        };
        t.Received += OnReceived;
        return t;
    }

    // ---------------- Sending helpers ----------------

    internal static bool CanSend => Active && !ApplyingRemote && !(Role == Role.Guest && hostPeer < 0);

    /// <summary>Host → all guests; guest → host.</summary>
    internal static void SendToSession(byte[] data, bool reliable)
    {
        if (Role == Role.Host) transport.Broadcast(data, reliable);
        else transport.Send(hostPeer, data, reliable);
    }

    internal static void Broadcast(byte[] data, bool reliable, int exceptPeer = -1) => transport?.Broadcast(data, reliable, exceptPeer);
    internal static void SendTo(int peer, byte[] data, bool reliable) => transport?.Send(peer, data, reliable);

    internal static byte[] Write(MsgType type, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) { w.Write((byte)type); body(w); }
        return ms.ToArray();
    }

    // ---------------- Buttons (any player) ----------------

    /// <summary>Called for a press made by the local player.</summary>
    public static void SendButtonPress(LookAtTarget button)
    {
        if (!CanSend) return;
        if (SyncPolicy.IsDragControl(button)) return; // chains/wheels: synced by value in M5, not as presses
        string id = Ids.Of(button);
        if (!SyncPolicy.IsShared(id)) return;
        var malfunction = button.currentMalfunction;
        SendToSession(Write(MsgType.ButtonPress, w => { w.Write(id); w.Write((int)malfunction); }), reliable: true);
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Net: sent press {id} ({malfunction})");
    }

    static void ApplyButtonPress(string id, LookAtTarget.MalfunctionType malfunction)
    {
        if (!SyncPolicy.IsShared(id)) { Mod.Log.Warning($"Net: ignored press on non-shared {id}"); return; }
        var button = Registry.Find<LookAtTarget>(id);
        if (button == null) { Mod.Log.Warning($"Net: no button with id {id}"); return; }
        if (SyncPolicy.IsDragControl(button)) { Mod.Log.Warning($"Net: ignored press on drag control {id}"); return; }
        ApplyingRemote = true;
        ForcedMalfunction = malfunction;
        try { button.OnClickDown(); }
        finally { ApplyingRemote = false; ForcedMalfunction = null; }
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Net: applied press {id} ({malfunction})");
    }

    // ---------------- Reload state (host-authoritative) ----------------
    // Guests still run their own reload state machine from button presses (so it feels instant), but the
    // host's state wins: every host change is broadcast, and guests snap to it if they differ.

    /// <summary>Host only: called after ArtilleryReloadController.SetState changed the state.</summary>
    public static void SendReloadState(ArtilleryReloadController c)
    {
        if (Role != Role.Host || !CanSend) return;
        string id = Ids.Of(c);
        int index = c.CurrentStateIndex;
        transport.Broadcast(Write(MsgType.ReloadState, w => { w.Write(id); w.Write(index); }), reliable: true);
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Net: sent reload state {id} = {index}");
    }

    static void ApplyReloadState(string id, int index)
    {
        if (Role != Role.Guest) { Mod.Log.Warning($"Net: ignored reload state from a guest ({id} = {index}); host owns it"); return; }
        var c = Registry.Find<ArtilleryReloadController>(id);
        if (c == null) { Mod.Log.Warning($"Net: no reload controller {id}"); return; }
        if (c.CurrentStateIndex == index) return; // already agree
        Mod.Log.Msg($"Net: reload {id} corrected {c.CurrentStateIndex} -> {index} (host)");
        ApplyingRemote = true;
        try { c.SetState(index, true, true); }
        finally { ApplyingRemote = false; }
    }

    // ---------------- Join snapshot ----------------

    /// <summary>Host: bring a new guest up to date with everything that has discrete state.</summary>
    static void SendSnapshot(int peer)
    {
        int n = 0;
        foreach (var c in Registry.All<ArtilleryReloadController>())
        {
            string id = Ids.Of(c);
            int index = c.CurrentStateIndex;
            transport.Send(peer, Write(MsgType.ReloadState, w => { w.Write(id); w.Write(index); }), reliable: true);
            n++;
        }
        Mod.Log.Msg($"Net: sent snapshot to guest #{peer} ({n} reload states)");
    }

    // ---------------- Receive ----------------

    static void OnReceived(int from, byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            var type = (MsgType)r.ReadByte();
            switch (type)
            {
                case MsgType.ButtonPress:
                {
                    string id = r.ReadString();
                    var malfunction = (LookAtTarget.MalfunctionType)r.ReadInt32();
                    ApplyButtonPress(id, malfunction);
                    if (Role == Role.Host) transport.Broadcast(data, reliable: true, exceptPeer: from); // relay
                    break;
                }
                case MsgType.ReloadState:
                {
                    string id = r.ReadString();
                    int index = r.ReadInt32();
                    ApplyReloadState(id, index);
                    break;
                }
                case MsgType.ControlValue:
                case MsgType.ControlGrab:
                case MsgType.TurretState:
                    ControlSync.Handle(type, from, r, data);
                    break;
                case MsgType.PlayerPose:
                    Avatars.Handle(from, r);
                    break;
                case MsgType.MissionState:
                case MsgType.MissionEvent:
                    MissionSync.Handle(type, r);
                    break;
                default:
                    Mod.Log.Warning($"Net: unknown message {type} from #{from}");
                    break;
            }
        }
        catch (Exception e) { Mod.Log.Error($"Net: bad message from #{from}: {e}"); }
    }
}
