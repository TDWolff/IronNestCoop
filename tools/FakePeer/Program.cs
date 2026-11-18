// FakePeer: a console "guest" for testing NestCoop without a second game instance (the game is single-instance).
//   dotnet run --project tools/FakePeer [-- <address> <port> <modVersion>]
// Commands:
//   list            numbered list of every button / reload controller ID received so far
//   again           send back the last button press received
//   send <n>        send a press for button #n from 'list'
//   press <id>      send a press for any stable ID (e.g. a non-shared one, to check the host ignores it)
//   reload <n> <i>  pretend to be authoritative: send ReloadState i for reload controller #n
//                   (the host must IGNORE this; only the host owns reload state)
//   controls        numbered list of shared wheels/levers with their last known value
//   drag <n> <delta> [seconds]   act like a player turning control #n by <delta> (default over 2 s):
//                   Grab, values at 20 Hz, Release. Try the traverse wheel: the host turret should turn.
//   turret          print the latest host turret state
//   mission <phase> pretend to be authoritative: send MissionState (0 menu, 1 map, 2 mission) for the last
//                   mission seen — the host must IGNORE it (only the host drives missions)
//   walk [radius]   be a visible player: walk in a circle (default 2 m) around where the host stands now
//   stand           stand 1.5 m in front of the host, looking at them
//   still           stop sending a pose (the avatar should vanish after AvatarTimeout)
//   resync          ask the host for its full mission checkpoint (host must be in a mission)
//   quit
// Protocol must match NestCoop/Net.cs (connection key "NestCoop-<version>", BinaryWriter encoding):
//   1 ButtonPress:  string id, int32 malfunction
//   2 ReloadState:  string id, int32 stateIndex
//   3 ControlValue: string id, float value                      (unreliable)
//   4 ControlGrab:  string id, bool grabbed, float value
//   6 MissionState: byte phase, string operationId, string missionId, int32 seed
//   7 MissionEvent: byte kind (1 FinishMission, 2 MarkMissionComplete, 3 MarkMissionFailed), [bool force]
//  10 MissionCheckpoint: string op, string mission, int32 seed, int32 len, len bytes gzip(JSON)
//  11 ResyncRequest:     (empty)
//  12 ShellFired:        int32 gunIndex
//  13 EntityState:       string id, int32 state, int32 health, int32 armour
//  14 TurretMove:        3 x float worldPos, bool dodge
//  15 ValveDamage:       string valveId, float damage01
//  16 PlayerInfo:        int32 playerId, string name   (FakePeer sends its own name on connect)
//   9 PlayerLeft:   int32 playerId
//   8 PlayerPose:   int32 playerId, 3 x float feet, 4 x float head rotation (x,y,z,w), bool crouched (unreliable)
//   5 TurretState:  float angle, float desired, float velocity, int32 n, n x float gunElevation (unreliable)
using System.Collections.Concurrent;
using LiteNetLib;

string address = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : 27960;
// Key format (M9): "NestCoop|<mod version>|<game build>". FakePeer can't know the game build, so it connects with a
// placeholder, gets REJECTED with the host's key (that exercises the version-mismatch path), then reconnects with it.
string version = args.Length > 2 ? args[2] : "0.9.5";
string key = args.Length > 3 ? args[3] : $"NestCoop|{version}|fakepeer";
bool retriedWithHostKey = false;
string myName = args.Length > 4 ? args[4] : "FakePeer";

const byte ButtonPress = 1, ReloadState = 2, ControlValue = 3, ControlGrab = 4, TurretState = 5, MissionState = 6, MissionEvent = 7, PlayerPose = 8;
float hx = 0, hy = 0, hz = 0, hostYaw = 0, hostYawAtStart = 0; bool haveHostPose = false;
string poseMode = null; float cx = 0, cy = 0, cz = 0, radius = 2f;
var poseClock = System.Diagnostics.Stopwatch.StartNew(); double nextPose = 0;
string lastOp = "", lastMission = "";
int entityMsgs = 0;
var controlIds = new List<string>();
var controlValues = new Dictionary<string, float>();
string turretLine = "(none yet)";
int rxValues = 0, rxTurret = 0;
var stats = System.Diagnostics.Stopwatch.StartNew();
// active drag: control id, start value, delta, duration
string dragId = null; float dragFrom = 0, dragDelta = 0, dragSeconds = 2, dragNextSend = 0;
var dragClock = new System.Diagnostics.Stopwatch();
var buttons = new List<string>();
var reloads = new List<string>();
string lastId = null;
NetPeer host = null;

var listener = new EventBasedNetListener();
var manager = new NetManager(listener) { AutoRecycle = true, DisconnectTimeout = 10000 };

listener.PeerConnectedEvent += p => { host = p; SendName(); Console.WriteLine("[connected to host] commands: list | again | send <n> | press <id> | reload <n> <i> | controls | drag <n> <delta> [s] | turret | mission <p> | walk [r] | stand | still | resync | quit"); };
listener.PeerDisconnectedEvent += (p, info) =>
{
    host = null;
    if (info.Reason == DisconnectReason.ConnectionRejected && info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
    {
        string hostKey = System.Text.Encoding.UTF8.GetString(info.AdditionalData.GetRemainingBytes());
        Console.WriteLine($"[rejected: host key is '{hostKey}', ours was '{key}']");
        if (!retriedWithHostKey) { retriedWithHostKey = true; key = hostKey; Console.WriteLine("[reconnecting with the host's key]"); manager.Connect(address, port, key); }
        return;
    }
    Console.WriteLine($"[disconnected: {info.Reason}]");
};
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
        case ControlValue:
        {
            string id = r.ReadString(); float v = r.ReadSingle();
            if (!controlIds.Contains(id)) controlIds.Add(id);
            controlValues[id] = v; rxValues++;
            break;
        }
        case ControlGrab:
        {
            string id = r.ReadString(); bool g = r.ReadBoolean(); float v = r.ReadSingle();
            controlValues[id] = v;
            Console.WriteLine($"<- {(g ? "grab" : "release")} {Short(id)} = {v:0.###}");
            break;
        }
        case TurretState:
        {
            float a = r.ReadSingle(), d = r.ReadSingle(), vel = r.ReadSingle(); int n = r.ReadInt32();
            var e = new float[n]; for (int i = 0; i < n; i++) e[i] = r.ReadSingle();
            turretLine = $"angle {a:0.##}  desired {d:0.##}  vel {vel:0.##}  guns [{string.Join(", ", e.Select(x => x.ToString("0.##")))}]";
            rxTurret++;
            break;
        }
        case MissionState:
        {
            byte phase = r.ReadByte(); lastOp = r.ReadString(); lastMission = r.ReadString(); int seed = r.ReadInt32();
            string[] names = { "MainMenu", "BrowsingMap", "MissionActive" };
            Console.WriteLine($"<- mission state {(phase < names.Length ? names[phase] : phase.ToString())} op='{lastOp}' mission='{lastMission}' seed={seed}");
            break;
        }
        case MissionEvent:
            { byte k = r.ReadByte(); string[] kinds = { "?", "FinishMission", "MarkMissionComplete", "MarkMissionFailed" }; Console.WriteLine($"<- mission event {(k < kinds.Length ? kinds[k] : k.ToString())}"); }
            break;
        case 10: // MissionCheckpoint
        {
            string op = r.ReadString(), mis = r.ReadString(); int seed = r.ReadInt32(); int len = r.ReadInt32(); var gz = r.ReadBytes(len);
            string json;
            using (var g = new System.IO.Compression.GZipStream(new MemoryStream(gz), System.IO.Compression.CompressionMode.Decompress))
            using (var sr = new StreamReader(g)) json = sr.ReadToEnd();
            Console.WriteLine($"<- CHECKPOINT op='{op}' mission='{mis}' seed={seed}: {len / 1024} KB gzip, {json.Length / 1024} KB JSON, starts {json[..Math.Min(80, json.Length)]}");
            File.WriteAllText("fakepeer-checkpoint.json", json);
            Console.WriteLine("   (saved as fakepeer-checkpoint.json)");
            break;
        }
        case 12: Console.WriteLine($"<- shell fired by gun {r.ReadInt32()}"); break;
        case 13:
        {
            string id = r.ReadString(); int st = r.ReadInt32(), hp = r.ReadInt32(), ar = r.ReadInt32();
            entityMsgs++;
            if (st != 0 || entityMsgs <= 5) Console.WriteLine($"<- entity {id}: state {st}, health {hp}, armour {ar}");
            break;
        }
        case 14: Console.WriteLine($"<- turret move to ({r.ReadSingle():0.#}, {r.ReadSingle():0.#}, {r.ReadSingle():0.#}) dodge={r.ReadBoolean()}"); break;
        case 15: { string id = r.ReadString(); float d = r.ReadSingle(); if (d > 0.001f) Console.WriteLine($"<- valve {Short(id)} damage {d:0.00}"); break; }
        case 16: { int pid = r.ReadInt32(); Console.WriteLine($"<- player {pid} is '{r.ReadString()}'"); break; }
        case 9: // PlayerLeft
            Console.WriteLine($"<- player {r.ReadInt32()} left");
            break;
        case PlayerPose:
        {
            int id = r.ReadInt32(); hx = r.ReadSingle(); hy = r.ReadSingle(); hz = r.ReadSingle();
            float qx = r.ReadSingle(), qy = r.ReadSingle(), qz = r.ReadSingle(), qw = r.ReadSingle(); r.ReadBoolean();
            // yaw from quaternion (Unity: y-up, left-handed)
            hostYaw = (float)Math.Atan2(2 * (qw * qy + qx * qz), 1 - 2 * (qy * qy + qx * qx));
            if (!haveHostPose) Console.WriteLine($"<- first pose from player {id} at ({hx:0.##}, {hy:0.##}, {hz:0.##})");
            haveHostPose = true;
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
    if (stats.Elapsed.TotalSeconds >= 5 && rxValues + rxTurret > 0)
    {
        Console.WriteLine($"   [5s] rx {rxValues} control values, {rxTurret} turret states | turret {turretLine}");
        rxValues = rxTurret = 0; stats.Restart();
    }
    if (poseMode != null && host != null && poseClock.Elapsed.TotalSeconds >= nextPose)
    {
        nextPose = poseClock.Elapsed.TotalSeconds + 1.0 / 15;
        float t = (float)poseClock.Elapsed.TotalSeconds;
        float px, pz, yaw;
        if (poseMode == "walk")
        {
            float ang = t * 0.6f; // rad/s around the circle
            px = cx + radius * (float)Math.Sin(ang); pz = cz + radius * (float)Math.Cos(ang);
            yaw = ang + (float)Math.PI / 2; // face along the path
        }
        else
        {
            px = cx + 1.5f * (float)Math.Sin(hostYawAtStart); pz = cz + 1.5f * (float)Math.Cos(hostYawAtStart);
            yaw = hostYawAtStart + (float)Math.PI; // look back at the host
        }
        float qy = (float)Math.Sin(yaw / 2), qw = (float)Math.Cos(yaw / 2);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) { w.Write(PlayerPose); w.Write(-1); w.Write(px); w.Write(cy); w.Write(pz); w.Write(0f); w.Write(qy); w.Write(0f); w.Write(qw); w.Write(false); }
        host.Send(ms.ToArray(), DeliveryMethod.Sequenced);
    }
    if (dragId != null && host != null)
    {
        float t = (float)dragClock.Elapsed.TotalSeconds;
        float k = Math.Min(1f, t / dragSeconds);
        float v = dragFrom + dragDelta * k;
        if (t >= dragNextSend || k >= 1f) { SendValue(ControlValue, dragId, v); dragNextSend = t + 0.05f; }
        if (k >= 1f) { SendGrab(dragId, false, v); Console.WriteLine($"-> release {Short(dragId)} = {v:0.###}"); controlValues[dragId] = v; dragId = null; }
    }
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
            case "controls":
                for (int i = 0; i < controlIds.Count; i++) Console.WriteLine($"  control #{i}  {controlValues[controlIds[i]],10:0.###}  {Short(controlIds[i])}");
                if (controlIds.Count == 0) Console.WriteLine("  none yet (the host sends them when you join)");
                break;
            case "turret": Console.WriteLine("  turret " + turretLine); break;
            case "resync":
                if (host == null) { Console.WriteLine("not connected"); break; }
                host.Send(new byte[] { 11 }, DeliveryMethod.ReliableOrdered);
                Console.WriteLine("-> resync request");
                break;
            case "walk":
            case "stand":
                if (!haveHostPose) { Console.WriteLine("no host pose yet"); break; }
                cx = hx; cy = hy; cz = hz; hostYawAtStart = hostYaw;
                radius = parts[0] == "walk" && parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rr) ? rr : 2f;
                poseMode = parts[0];
                Console.WriteLine(poseMode == "walk" ? $"walking a {radius} m circle around ({cx:0.##}, {cy:0.##}, {cz:0.##})" : "standing in front of the host");
                break;
            case "still": poseMode = null; Console.WriteLine("stopped sending pose"); break;
            case "mission":
            {
                if (host == null) { Console.WriteLine("not connected"); break; }
                if (parts.Length < 2 || !byte.TryParse(parts[1], out byte ph)) { Console.WriteLine("usage: mission <0|1|2>"); break; }
                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms)) { w.Write(MissionState); w.Write(ph); w.Write(lastOp); w.Write(lastMission); w.Write(12345); }
                host.Send(ms.ToArray(), DeliveryMethod.ReliableOrdered);
                Console.WriteLine($"-> mission state {ph} (host should ignore)");
                break;
            }
            case "drag":
            {
                var a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                if (a.Length >= 2 && int.TryParse(a[0], out int cn) && cn >= 0 && cn < controlIds.Count && float.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float delta))
                {
                    dragId = controlIds[cn]; dragFrom = controlValues[dragId]; dragDelta = delta;
                    dragSeconds = a.Length >= 3 && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec) ? Math.Max(0.1f, sec) : 2f;
                    dragNextSend = 0; dragClock.Restart();
                    SendGrab(dragId, true, dragFrom);
                    Console.WriteLine($"-> grab {Short(dragId)} at {dragFrom:0.###}, turning by {delta} over {dragSeconds}s");
                }
                else Console.WriteLine("usage: drag <n> <delta> [seconds]  (see controls)");
                break;
            }
            default: Console.WriteLine("commands: list | again | send <n> | press <id> | reload <n> <i> | controls | drag <n> <delta> [s] | turret | mission <p> | walk [r] | stand | still | resync | quit"); break;
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

void SendValue(byte type, string id, float v)
{
    using var ms = new MemoryStream();
    using (var w = new BinaryWriter(ms)) { w.Write(type); w.Write(id); w.Write(v); }
    host.Send(ms.ToArray(), DeliveryMethod.Sequenced);
}

void SendGrab(string id, bool grabbed, float v)
{
    if (host == null) return;
    using var ms = new MemoryStream();
    using (var w = new BinaryWriter(ms)) { w.Write(ControlGrab); w.Write(id); w.Write(grabbed); w.Write(v); }
    host.Send(ms.ToArray(), DeliveryMethod.ReliableOrdered);
}

void SendName()
{
    using var ms = new MemoryStream();
    using (var w = new BinaryWriter(ms)) { w.Write((byte)16); w.Write(-1); w.Write(myName); }
    host?.Send(ms.ToArray(), DeliveryMethod.ReliableOrdered);
    Console.WriteLine($"-> my name is '{myName}'");
}

static string Short(string id) => id.Length <= 90 ? id : "…" + id[^89..];
