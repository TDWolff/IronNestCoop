namespace NestCoop.Transport;

/// <summary>
/// Network transport. Everything above this (sessions, messages, sync) only talks to this interface, so the
/// LiteNetLib (localhost) implementation can later be swapped for Steam lobbies + P2P (Milestone 8).
/// Peers are identified by small ints assigned by the transport. Events fire only from inside Poll(),
/// i.e. on Unity's main thread, so handlers may touch game objects.
/// </summary>
internal interface ITransport
{
    bool IsRunning { get; }

    /// <summary>Start listening for guests.</summary>
    bool Host(int port);

    /// <summary>Connect to a host. PeerConnected fires once accepted.</summary>
    bool Connect(string address, int port);

    /// <summary>Process network events. Call once per frame.</summary>
    void Poll();

    void Send(int peer, byte[] data, bool reliable);

    /// <summary>Send to every connected peer, optionally skipping one (e.g. the peer the message came from).</summary>
    void Broadcast(byte[] data, bool reliable, int exceptPeer = -1);

    /// <summary>Disconnect everyone and close the socket.</summary>
    void Stop();

    event Action<int> PeerConnected;
    event Action<int, string> PeerDisconnected;
    event Action<int, byte[]> Received;
}
