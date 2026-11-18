// FakePeer: a console "guest" for testing NestCoop without a second game instance.
//   dotnet run --project tools/FakePeer [-- <address> <port> <modVersion>]
// Commands:
//   again        send back the last button press we received (host should see/hear that button press again)
//   press <id>   send a press for a stable ID, e.g. "Master Turret Scene:…/Universal Button"
//   quit
// Protocol must match NestCoop/Net.cs: connection key "NestCoop-<version>", messages written with BinaryWriter:
//   ButtonPress = byte 1, string id, int32 malfunction
using System.Collections.Concurrent;
using LiteNetLib;

string address = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : 27960;
string version = args.Length > 2 ? args[2] : "0.2.0";
string key = "NestCoop-" + version;

const byte ButtonPress = 1;
string lastId = null;
NetPeer host = null;

var listener = new EventBasedNetListener();
var manager = new NetManager(listener) { AutoRecycle = true, DisconnectTimeout = 10000 };

listener.PeerConnectedEvent += p => { host = p; Console.WriteLine("[connected to host] type 'again' after pressing a button in-game"); };
listener.PeerDisconnectedEvent += (p, info) => { host = null; Console.WriteLine($"[disconnected: {info.Reason}]"); };
listener.NetworkReceiveEvent += (p, reader, ch, method) =>
{
    using var r = new BinaryReader(new MemoryStream(reader.GetRemainingBytes()));
    byte type = r.ReadByte();
    if (type == ButtonPress)
    {
        lastId = r.ReadString();
        int malfunction = r.ReadInt32();
        Console.WriteLine($"<- press {lastId} (malfunction {malfunction})");
    }
    else Console.WriteLine($"<- unknown message type {type}");
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
        if (line == "quit") { manager.Stop(true); return; }
        string id = line == "again" ? lastId : line.StartsWith("press ") ? line[6..].Trim() : null;
        if (id == null) { Console.WriteLine(line == "again" ? "nothing received yet" : "commands: again | press <id> | quit"); continue; }
        if (host == null) { Console.WriteLine("not connected"); continue; }
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) { w.Write(ButtonPress); w.Write(id); w.Write(0); }
        host.Send(ms.ToArray(), DeliveryMethod.ReliableOrdered);
        Console.WriteLine($"-> press {id}");
    }
    Thread.Sleep(15);
}
