namespace NestCoop.Transport;

/// <summary>
/// Milestone 8: Steam P2P transport (ISteamNetworking, via the game's steam_api64.dll — see SteamApi).
/// No lobbies and no Steam callbacks (callback delegates through IL2CPP are fragile), everything is polled:
///   * Guest: picks a friend who is playing IRON NEST (SteamJoinTarget = name/SteamID64, or the first one found) and
///     sends Hello(connection key) every second until the host answers Welcome (gives up after 20 s).
///   * Host: every 2 s calls AcceptP2PSessionWithUser for every friend playing this game, so their packets get through
///     (friends-only by design). Hello with the right key → Welcome + PeerConnected; wrong key → Reject.
///   * Both: Ping every second; a peer silent for 10 s is dropped. Bye on Stop, then CloseP2PSessionWithUser.
/// Frame: first byte 0 = control, 1 = data (the NestCoop message follows).
/// Steam can't P2P with yourself, so this can only be fully tested with a second Steam account (a friend).
/// </summary>
internal sealed class SteamTransport : ITransport
{
    enum Ctl : byte { Hello = 1, Welcome = 2, Reject = 3, Ping = 4, Bye = 5 }
    const byte FrameControl = 0, FrameData = 1;

    readonly string key;
    readonly Dictionary<ulong, int> idOf = new();
    readonly Dictionary<int, ulong> steamOf = new();
    readonly Dictionary<ulong, float> lastHeard = new();
    int nextId = 1;
    bool running, isHost;

    // guest connect state
    ulong target;
    float connectStarted, nextHello;

    float nextAccept, nextPing;

    public event Action<int> PeerConnected;
    public event Action<int, string> PeerDisconnected;
    public event Action<int, byte[]> Received;
    public event Action<string> ConnectFailed;

    public SteamTransport(string connectionKey) { key = connectionKey; }

    public bool IsRunning => running;

    static float Now => UnityEngine.Time.unscaledTime;

    bool Ready()
    {
        if (SteamApi.Init(out var why)) return true;
        Mod.Log.Error($"Steam: {why}");
        Overlay.Toast($"Steam unavailable: {why}");
        return false;
    }

    public bool Host(int port)
    {
        if (!Ready()) return false;
        isHost = running = true;
        Mod.Log.Msg($"Steam: hosting as {SteamApi.MyName} ({SteamApi.MySteamId}), app {SteamApi.AppId}. Friends can join from their game.");
        LogFriendsInGame();
        return true;
    }

    public bool Connect(string address, int port)
    {
        if (!Ready()) return false;
        var friendsInGame = LogFriendsInGame();
        target = PickTarget(address, friendsInGame);
        if (target == 0)
        {
            Overlay.Toast(friendsInGame.Count == 0 ? "No Steam friends are playing IRON NEST right now" : $"No friend matches '{address}'");
            return false;
        }
        if (target == SteamApi.MySteamId) { Overlay.Toast("Can't join yourself"); return false; }
        isHost = false; running = true;
        connectStarted = Now; nextHello = 0;
        Mod.Log.Msg($"Steam: joining {SteamApi.NameOf(target)} ({target})");
        Overlay.Toast($"Joining {SteamApi.NameOf(target)} via Steam ...");
        return true;
    }

    static ulong PickTarget(string address, List<(ulong id, string name)> inGame)
    {
        address = (address ?? "").Trim();
        if (address == "" || address == "127.0.0.1") return inGame.Count > 0 ? inGame[0].id : 0;
        if (ulong.TryParse(address, out ulong sid) && sid > 76561190000000000UL) return sid;
        foreach (var f in inGame)
            if (f.name.IndexOf(address, StringComparison.OrdinalIgnoreCase) >= 0) return f.id;
        return 0;
    }

    List<(ulong id, string name)> LogFriendsInGame()
    {
        var list = SteamApi.FriendsInThisGame();
        Mod.Log.Msg(list.Count == 0
            ? "Steam: no friends are playing IRON NEST right now"
            : "Steam: friends in IRON NEST: " + string.Join(", ", list.Select(f => $"{f.name} ({f.id})")));
        return list;
    }

    public void Poll()
    {
        if (!running) return;
        float now = Now;

        if (isHost && now >= nextAccept)
        {
            nextAccept = now + 2f;
            foreach (var f in SteamApi.FriendsInThisGame()) SteamApi.Accept(f.id);
        }

        if (!isHost && !idOf.ContainsKey(target))
        {
            if (now - connectStarted > 20f) { Mod.Log.Warning("Steam: host didn't answer in 20 s"); ConnectFailed?.Invoke("Host didn't answer (are they hosting with NestCoop, same version?)"); Stop(); return; }
            if (now >= nextHello) { nextHello = now + 1f; SendControl(target, Ctl.Hello, key); }
        }

        for (int i = 0; i < 256 && running; i++) // drain, but never lock up a frame
        {
            var pkt = SteamApi.Read(out ulong from);
            if (pkt == null) break;
            if (pkt.Length == 0) continue;
            if (pkt[0] == FrameControl) HandleControl(from, pkt);
            else if (pkt[0] == FrameData && idOf.TryGetValue(from, out int pid))
            {
                lastHeard[from] = now;
                Received?.Invoke(pid, pkt.AsSpan(1).ToArray());
            }
        }

        if (now >= nextPing)
        {
            nextPing = now + 1f;
            foreach (var sid in idOf.Keys.ToList())
            {
                if (now - lastHeard.GetValueOrDefault(sid, now) > 10f) { Drop(sid, "timed out"); continue; }
                SendControl(sid, Ctl.Ping, "");
            }
        }
    }

    void HandleControl(ulong from, byte[] pkt)
    {
        if (pkt.Length < 2) return;
        var ctl = (Ctl)pkt[1];
        string text = pkt.Length > 2 ? System.Text.Encoding.UTF8.GetString(pkt, 2, pkt.Length - 2) : "";
        lastHeard[from] = Now;

        switch (ctl)
        {
            case Ctl.Hello when isHost:
                if (text != key)
                {
                    Mod.Log.Warning($"Steam: rejected {SteamApi.NameOf(from)} ({from}): their version '{text}', ours '{key}'");
                    SendControl(from, Ctl.Reject, key); // our key: the guest explains the difference
                    break;
                }
                SendControl(from, Ctl.Welcome, "");
                if (!idOf.ContainsKey(from)) AddPeer(from);
                break;
            case Ctl.Welcome when !isHost && from == target:
                if (!idOf.ContainsKey(from)) AddPeer(from);
                break;
            case Ctl.Reject when !isHost && from == target:
                Mod.Log.Warning($"Steam: host rejected us: {text}");
                ConnectFailed?.Invoke(VersionCheck.LooksLikeNestCoop(text) ? VersionCheck.Explain(text) : $"Host rejected: {text}");
                Stop();
                break;
            case Ctl.Bye:
                if (idOf.ContainsKey(from)) Drop(from, "left");
                break;
            case Ctl.Ping:
                break;
        }
    }

    void AddPeer(ulong sid)
    {
        int id = nextId++;
        idOf[sid] = id;
        steamOf[id] = sid;
        lastHeard[sid] = Now;
        Mod.Log.Msg($"Steam: connected {SteamApi.NameOf(sid)} ({sid}) as peer #{id}");
        PeerConnected?.Invoke(id);
    }

    void Drop(ulong sid, string reason)
    {
        if (!idOf.Remove(sid, out int id)) return;
        steamOf.Remove(id);
        lastHeard.Remove(sid);
        SteamApi.Close(sid);
        PeerDisconnected?.Invoke(id, $"{SteamApi.NameOf(sid)} {reason}");
    }

    void SendControl(ulong to, Ctl ctl, string text)
    {
        var t = System.Text.Encoding.UTF8.GetBytes(text ?? "");
        var buf = new byte[2 + t.Length];
        buf[0] = FrameControl; buf[1] = (byte)ctl;
        t.CopyTo(buf, 2);
        SteamApi.Send(to, buf, reliable: true);
    }

    static byte[] Frame(byte[] data)
    {
        var buf = new byte[data.Length + 1];
        buf[0] = FrameData;
        data.CopyTo(buf, 1);
        return buf;
    }

    public void Send(int peer, byte[] data, bool reliable)
    {
        if (steamOf.TryGetValue(peer, out ulong sid)) SteamApi.Send(sid, Frame(data), reliable);
    }

    public void Broadcast(byte[] data, bool reliable, int exceptPeer = -1)
    {
        var framed = Frame(data);
        foreach (var (id, sid) in steamOf)
            if (id != exceptPeer) SteamApi.Send(sid, framed, reliable);
    }

    public string DescribePeer(int peer) =>
        steamOf.TryGetValue(peer, out ulong sid) ? SteamApi.NameOf(sid) : $"Peer #{peer}";

    public void Stop()
    {
        if (!running) return;
        running = false;
        foreach (var sid in idOf.Keys.ToList())
        {
            SendControl(sid, Ctl.Bye, "");
            SteamApi.Close(sid);
        }
        if (!isHost && target != 0) SteamApi.Close(target);
        idOf.Clear(); steamOf.Clear(); lastHeard.Clear();
    }
}
