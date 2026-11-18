using Il2Cpp;
using NestCoop.Transport;

namespace NestCoop;

internal enum Role { None, Host, Guest }

internal enum MsgType : byte
{
    ButtonPress = 1,
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
    static int hostPeer = -1; // guest side: the host's peer id
    static readonly HashSet<int> peers = new();

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
        if (!transport.Host(Mod.Port.Value)) { Say($"Could not host on port {Mod.Port.Value} (in use?)"); transport = null; return; }
        Role = Role.Host;
        Say($"Hosting on localhost:{Mod.Port.Value}");
    }

    public static void Join()
    {
        if (Active) { Say($"Already {Role}"); return; }
        transport = CreateTransport();
        if (!transport.Connect(Mod.JoinAddress.Value, Mod.Port.Value)) { Say("Join failed to start"); transport = null; return; }
        Role = Role.Guest;
        Say($"Joining {Mod.JoinAddress.Value}:{Mod.Port.Value} ...");
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
        t.Stop();
    }

    public static void Poll() => transport?.Poll();

    static ITransport CreateTransport()
    {
        var t = new LiteNetTransport(ConnectionKey);
        t.PeerConnected += id =>
        {
            peers.Add(id);
            if (Role == Role.Guest) { hostPeer = id; Say("Connected to host"); }
            else Say($"Guest #{id} joined");
        };
        t.PeerDisconnected += (id, reason) =>
        {
            peers.Remove(id);
            Say(Role == Role.Guest ? $"Host left ({reason})" : $"Guest #{id} left ({reason})");
            if (Role == Role.Guest) Stop("lost host");
        };
        t.Received += OnReceived;
        return t;
    }

    // ---------------- Buttons ----------------

    /// <summary>Called for a press made by the local player.</summary>
    public static void SendButtonPress(LookAtTarget button)
    {
        if (!Active || ApplyingRemote) return;
        if (Role == Role.Guest && hostPeer < 0) return; // not connected yet
        var data = WriteButtonPress(Ids.Of(button), button.currentMalfunction);
        if (Role == Role.Host) transport.Broadcast(data, reliable: true);
        else transport.Send(hostPeer, data, reliable: true);
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Net: sent press {Ids.Of(button)} ({button.currentMalfunction})");
    }

    static byte[] WriteButtonPress(string id, LookAtTarget.MalfunctionType malfunction)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)MsgType.ButtonPress);
        w.Write(id);
        w.Write((int)malfunction);
        return ms.ToArray();
    }

    static void OnReceived(int from, byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            var type = (MsgType)r.ReadByte();
            switch (type)
            {
                case MsgType.ButtonPress:
                    string id = r.ReadString();
                    var malfunction = (LookAtTarget.MalfunctionType)r.ReadInt32();
                    ApplyButtonPress(id, malfunction);
                    if (Role == Role.Host) transport.Broadcast(data, reliable: true, exceptPeer: from); // relay
                    break;
                default:
                    Mod.Log.Warning($"Net: unknown message {type} from #{from}");
                    break;
            }
        }
        catch (Exception e) { Mod.Log.Error($"Net: bad message from #{from}: {e}"); }
    }

    static void ApplyButtonPress(string id, LookAtTarget.MalfunctionType malfunction)
    {
        var button = Registry.FindButton(id);
        if (button == null) { Mod.Log.Warning($"Net: no button with id {id}"); return; }
        ApplyingRemote = true;
        ForcedMalfunction = malfunction;
        try { button.OnClickDown(); }
        finally { ApplyingRemote = false; ForcedMalfunction = null; }
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Net: applied press {id} ({malfunction})");
    }
}
