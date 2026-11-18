using System.Net;
using LiteNetLib;

namespace NestCoop.Transport;

/// <summary>
/// LiteNetLib 2.1.5 transport for localhost testing. Binds to loopback only, so no Windows Firewall prompt and
/// nothing is reachable from other machines. API names checked against the package (dump/LiteNetLib.txt).
/// The connection key includes the mod version, so mismatched builds are rejected at connect time.
/// </summary>
internal sealed class LiteNetTransport : ITransport
{
    readonly string connectionKey;
    readonly EventBasedNetListener listener = new();
    readonly NetManager manager;
    readonly Dictionary<NetPeer, int> idOf = new();
    readonly Dictionary<int, NetPeer> peerOf = new();
    int nextId = 1;

    public event Action<int> PeerConnected;
    public event Action<int, string> PeerDisconnected;
    public event Action<int, byte[]> Received;
    public event Action<string> ConnectFailed;

    public LiteNetTransport(string connectionKey)
    {
        this.connectionKey = connectionKey;
        manager = new NetManager(listener) { AutoRecycle = true, DisconnectTimeout = 10000 };

        // Version check (M9): accept only our exact key; another NestCoop key gets rejected WITH our key attached,
        // so the guest can show what differs. Anything else is rejected silently.
        listener.ConnectionRequestEvent += req =>
        {
            string theirs = null;
            try { theirs = req.Data.GetString(); } catch { }
            if (theirs == this.connectionKey)
            {
                if (idOf.Count + 1 >= Mod.MaxPlayers.Value) { req.Reject(System.Text.Encoding.UTF8.GetBytes(VersionCheck.Full)); Mod.Log.Msg("Net: rejected a joiner (session full)"); return; }
                req.Accept(); return;
            }
            Mod.Log.Warning($"Net: rejected a connection with key '{theirs}' (ours '{this.connectionKey}')");
            if (VersionCheck.LooksLikeNestCoop(theirs)) req.Reject(System.Text.Encoding.UTF8.GetBytes(this.connectionKey));
            else req.Reject();
        };
        listener.PeerConnectedEvent += peer =>
        {
            int id = nextId++;
            idOf[peer] = id;
            peerOf[id] = peer;
            PeerConnected?.Invoke(id);
        };
        listener.PeerDisconnectedEvent += (peer, info) =>
        {
            if (!idOf.Remove(peer, out int id))
            {
                // Never connected: this was our join attempt failing.
                string why = info.Reason.ToString();
                if (info.Reason == DisconnectReason.ConnectionRejected && info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
                    why = VersionCheck.ExplainReject(System.Text.Encoding.UTF8.GetString(info.AdditionalData.GetRemainingBytes()));
                else why = Friendly(info.Reason);
                ConnectFailed?.Invoke(why);
                return;
            }
            peerOf.Remove(id);
            PeerDisconnected?.Invoke(id, Friendly(info.Reason));
        };
        listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            if (!idOf.TryGetValue(peer, out int id)) return;
            Received?.Invoke(id, reader.GetRemainingBytes());
        };
        listener.NetworkErrorEvent += (endPoint, error) => Mod.Log.Warning($"Net error from {endPoint}: {error}");
    }

    public bool IsRunning => manager.IsRunning;

    public bool Host(int port) => manager.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, port);

    public bool Connect(string address, int port)
    {
        if (!manager.IsRunning && !manager.Start()) return false;
        return manager.Connect(address, port, connectionKey) != null;
    }

    public void Poll() { if (manager.IsRunning) manager.PollEvents(); }

    public void Send(int peer, byte[] data, bool reliable)
    {
        if (peerOf.TryGetValue(peer, out var p))
            p.Send(data, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
    }

    public void Broadcast(byte[] data, bool reliable, int exceptPeer = -1)
    {
        var method = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;
        foreach (var (id, p) in peerOf)
            if (id != exceptPeer) p.Send(data, method);
    }

    static string Friendly(DisconnectReason r) => r switch
    {
        DisconnectReason.RemoteConnectionClose => "left the session",
        DisconnectReason.DisconnectPeerCalled => "disconnected",
        DisconnectReason.Timeout => "connection timed out",
        DisconnectReason.ConnectionFailed => "couldn't reach the host (is it hosting?)",
        DisconnectReason.ConnectionRejected => "connection rejected",
        DisconnectReason.HostUnreachable or DisconnectReason.NetworkUnreachable => "network unreachable",
        _ => r.ToString(),
    };

    public int PingMs(int peer) => peerOf.TryGetValue(peer, out var p) ? p.RoundTripTime : -1;

    public string DescribePeer(int peer) => $"Peer #{peer}";

    public void Stop()
    {
        if (manager.IsRunning) manager.Stop(true);
        idOf.Clear();
        peerOf.Clear();
    }
}
