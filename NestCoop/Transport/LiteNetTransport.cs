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

    public LiteNetTransport(string connectionKey)
    {
        this.connectionKey = connectionKey;
        manager = new NetManager(listener) { AutoRecycle = true, DisconnectTimeout = 10000 };

        listener.ConnectionRequestEvent += req => req.AcceptIfKey(this.connectionKey);
        listener.PeerConnectedEvent += peer =>
        {
            int id = nextId++;
            idOf[peer] = id;
            peerOf[id] = peer;
            PeerConnected?.Invoke(id);
        };
        listener.PeerDisconnectedEvent += (peer, info) =>
        {
            if (!idOf.Remove(peer, out int id)) return;
            peerOf.Remove(id);
            PeerDisconnected?.Invoke(id, info.Reason.ToString());
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

    public void Stop()
    {
        if (manager.IsRunning) manager.Stop(true);
        idOf.Clear();
        peerOf.Clear();
    }
}
