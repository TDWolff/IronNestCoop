// FakePeer: a console "guest" for testing NestCoop without a second game instance (the game is single-instance).
//   dotnet run --project tools/FakePeer [-- <address> <port> <modVersion>]
// Commands:
//   list            numbered list of every button / reload controller ID received so far
//   again           send back the last button press received
//   send <n>        send a press for button #n from 'list'
//   press <id>      send a press for any stable ID (e.g. a non-shared one, to check the host ignores it)
//   reload <n> <i>  pretend to be authoritative: send ReloadState i for reload controller #n
//                   (the host must IGNORE this; only the host owns reload state)
//   quit
// Protocol must match NestCoop/Net.cs (connection key "NestCoop-<version>", BinaryWriter encoding):
//   1 ButtonPress:  string id, int32 malfunction
//   2 ReloadState:  string id, int32 stateIndex
using System.Collections.Concurrent;
using LiteNetLib;

string address = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : 27960;
string version = args.Length > 2 ? args[2] : "0.3.0";
string key = "NestCoop-" + version;

const byte ButtonPress = 1, ReloadState = 2;
var buttons = new List<string>();
var reloads = new List<string>();
string lastId = null;
NetPeer host = null;

var listener = new EventBasedNetListener();
var manager = new NetManager(listener) { AutoRecycle = true, DisconnectTimeout = 10000 };

listener.PeerConnectedEvent += p => { host = p; Console.WriteLine("[connected to host] commands: list | again | send <n> | press <id> | reload <n> <i> | quit"); };
listener.PeerDisconnectedEvent += (p, info) => { host = null; Console.WriteLine($"[disconnected: {info.Reason}]"); };
listener.NetworkReceiveEvent += (p, reader, ch, method) =>
{
    using var r = new BinaryReader(new MemoryStream(reader.GetRemainingBytes()));
    byte type = r.ReadByte();
    switch (type)
    {
        case ButtonPress:
        {
            lastId = r.ReadString();
            int malfunction = r.ReadInt32();
            if (!buttons.Contains(lastId)) buttons.Add(lastId);
            Console.WriteLine($"<- press #{buttons.IndexOf(lastId)} {Short(lastId)} (malfunction {malfunction})");
            break;
        }
        case ReloadState:
        {
            string id = r.ReadString();
            int index = r.ReadInt32();
            if (!reloads.Contains(id)) reloads.Add(id);
            Console.WriteLine($"<- reload #{reloads.IndexOf(id)} {Short(id)} = {index}");
            break;
        }
        default: Console.WriteLine($"<- unknown message type {type}"); break;
    }
};

manager.Start();
manager.Connect(address, port, key);
Console.WriteLine($"Connecting to {address}:{port} with key '{key}' ... (host must have pressed Home)");

var input = new ConcurrentQueue<string>();
new Thread(() => { while (true) { var l = Console.ReadLine(); if (l == null) break; input.Enqueue(l.Trim()); } }) { IsBackground = true }.Start();

while (true)
{
    manager.PollEvents();
    while (input.TryDequeue(out var line))
    {
        var parts = line.Split(' ', 2);
        switch (parts[0])
        {
            case "quit": manager.Stop(true); return;
            case "list":
                for (int i = 0; i < buttons.Count; i++) Console.WriteLine($"  button #{i}  {buttons[i]}");
                for (int i = 0; i < reloads.Count; i++) Console.WriteLine($"  reload #{i}  {reloads[i]}");
                if (buttons.Count + reloads.Count == 0) Console.WriteLine("  nothing received yet");
                break;
            case "again": SendPress(lastId); break;
            case "send":
                if (parts.Length > 1 && int.TryParse(parts[1], out int n) && n >= 0 && n < buttons.Count) SendPress(buttons[n]);
                else Console.WriteLine("usage: send <n>  (see list)");
                break;
            case "press": SendPress(parts.Length > 1 ? parts[1].Trim() : null); break;
            case "reload":
            {
                var a = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
                if (a.Length == 2 && int.TryParse(a[0], out int rn) && rn >= 0 && rn < reloads.Count && int.TryParse(a[1], out int idx))
                    Send(ReloadState, reloads[rn], idx, $"reload #{rn} = {idx} (host should ignore)");
                else Console.WriteLine("usage: reload <n> <stateIndex>  (see list)");
                break;
            }
            default: Console.WriteLine("commands: list | again | send <n> | press <id> | reload <n> <i> | quit"); break;
        }
    }
    Thread.Sleep(15);
}

void SendPress(string id)
{
    if (id == null) { Console.WriteLine("nothing to send"); return; }
    Send(ButtonPress, id, 0, $"press {Short(id)}");
}

void Send(byte type, string id, int value, string what)
{
    if (host == null) { Console.WriteLine("not connected"); return; }
    using var ms = new MemoryStream();
    using (var w = new BinaryWriter(ms)) { w.Write(type); w.Write(id); w.Write(value); }
    host.Send(ms.ToArray(), DeliveryMethod.ReliableOrdered);
    Console.WriteLine("-> " + what);
}

static string Short(string id) => id.Length <= 90 ? id : "…" + id[^89..];
