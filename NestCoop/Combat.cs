using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Host-authoritative combat (0.9.0). Before this, every game simulated its own shots and impacts, so hits, kills and
/// breakdowns drifted apart between players.
///
///   Shots:   on a guest, GunController.FireShell only runs when the host says so (ShellFired message with the gun's
///            index in TurretController.guns). The guest's shell still flies and lands visibly — gun angle, elevation,
///            charge and loaded shell are already synced — but its outcome doesn't count.
///   Enemies: on a guest, FireMission.SetEntityState only runs for host messages. The host sends every state change
///            at once (EntityState: MapEntity.ID, State, Health, Armour) and, once a second, any entity whose
///            health/armour/state changed (FireMission.Entities).
///   Turret:  TurretController.MoveTurret(worldPos, dodge) — a guest's move becomes a request to the host; the host's
///            moves are broadcast and replayed on guests.
///   Valves:  ValveController.Damage/DamageValve are random breakdowns; on a guest they're skipped, and the host sends
///            each valve's GetDamage01() when it changes (SetDamage01 on the guest).
/// The checkpoint snapshot (Snapshot.cs) corrects anything that still drifts (entity movement, objectives).
/// All names from the dump (build 26352746); every patch logs its first hit with DebugLog on.
/// </summary>
internal static class Combat
{
    /// <summary>True when this game must not decide combat outcomes itself (a guest, not applying a host message).</summary>
    public static bool GuestLocal => Net.Role == Role.Guest && Net.Active && !Net.ApplyingRemote;

    static readonly Dictionary<string, (int state, int health, int armour)> sentEntities = new();
    static readonly Dictionary<string, float> sentValves = new();
    static float nextScan;

    // ---------------- shots ----------------

    public static void OnHostFired(GunController gun)
    {
        if (Net.Role != Role.Host || !Net.CanSend) return;
        int index = GunIndex(gun);
        if (index < 0) return;
        Net.Broadcast(Net.Write(MsgType.ShellFired, w => w.Write(index)), reliable: true);
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Combat: host fired gun {index}");
    }

    static int GunIndex(GunController gun)
    {
        var guns = Turret()?.guns;
        if (guns == null) return -1;
        for (int i = 0; i < guns.Count; i++) if (guns[i] != null && guns[i].Pointer == gun.Pointer) return i;
        return -1;
    }

    static TurretController Turret() => Registry.All<TurretController>().FirstOrDefault();

    // ---------------- entities ----------------

    public static void OnHostEntityState(MapEntity e)
    {
        if (Net.Role != Role.Host || !Net.CanSend || e == null) return;
        SendEntity(e, force: true);
    }

    static void SendEntity(MapEntity e, bool force)
    {
        var now = ((int)e.State, e.Health, e.Armour);
        if (!force && sentEntities.TryGetValue(e.ID, out var last) && last == now) return;
        sentEntities[e.ID] = now;
        string id = e.ID;
        Net.Broadcast(Net.Write(MsgType.EntityState, w => { w.Write(id); w.Write(now.Item1); w.Write(now.Item2); w.Write(now.Item3); }), reliable: true);
    }

    // ---------------- turret relocation ----------------

    /// <summary>Guest tried to move the turret: ask the host instead.</summary>
    public static void RequestMove(Vector3 pos, bool dodge)
    {
        Net.SendToSession(Net.Write(MsgType.TurretMove, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); w.Write(dodge); }), reliable: true);
        Overlay.Toast("Asked the host to move the turret");
    }

    public static void OnHostMoved(Vector3 pos, bool dodge)
    {
        if (Net.Role != Role.Host || !Net.Active) return;
        Net.Broadcast(Net.Write(MsgType.TurretMove, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); w.Write(dodge); }), reliable: true);
    }

    // ---------------- periodic host scan ----------------

    public static void Update()
    {
        if (Net.Role != Role.Host || !Net.CanSend || Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1f;

        var fm = FireMission.Instance;
        if (fm != null && fm.Entities != null)
            foreach (var kv in fm.Entities)
                if (kv.Value != null) SendEntity(kv.Value, force: false);

        foreach (var v in Registry.All<ValveController>())
        {
            if (v == null) continue;
            string id = Ids.Of(v);
            float d = v.GetDamage01();
            if (sentValves.TryGetValue(id, out float last) && Mathf.Abs(last - d) < 0.01f) continue;
            sentValves[id] = d;
            Net.Broadcast(Net.Write(MsgType.ValveDamage, w => { w.Write(id); w.Write(d); }), reliable: true);
        }
    }

    public static void Reset() { sentEntities.Clear(); sentValves.Clear(); }

    /// <summary>A new guest needs every entity and valve again.</summary>
    public static void OnGuestJoined() => Reset();

    // ---------------- receive ----------------

    public static void Handle(MsgType type, int from, BinaryReader r)
    {
        switch (type)
        {
            case MsgType.ShellFired:
            {
                int index = r.ReadInt32();
                if (Net.Role != Role.Guest) return;
                var guns = Turret()?.guns;
                if (guns == null || index < 0 || index >= guns.Count) return;
                Remote(() => guns[index].FireShell());
                if (Mod.DebugLog.Value) Mod.Log.Msg($"Combat: replayed host shot from gun {index}");
                break;
            }
            case MsgType.EntityState:
            {
                string id = r.ReadString(); int state = r.ReadInt32(), health = r.ReadInt32(), armour = r.ReadInt32();
                if (Net.Role != Role.Guest) return;
                var fm = FireMission.Instance;
                if (fm == null) return;
                MapEntity e = null;
                if (!fm.TryGetMapEntity(id, out e) || e == null) return;
                e.Health = health;
                e.Armour = armour;
                if ((int)e.State != state) Remote(() => fm.SetEntityState(e, (MapEntityStates)state));
                break;
            }
            case MsgType.TurretMove:
            {
                var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                bool dodge = r.ReadBoolean();
                var t = Turret();
                if (t == null) return;
                // Host: a guest's request → do it (the MoveTurret postfix then broadcasts to everyone).
                // Guest: the host's move → replay it.
                Remote(() => t.MoveTurret(pos, dodge));
                break;
            }
            case MsgType.ValveDamage:
            {
                string id = r.ReadString(); float d = r.ReadSingle();
                if (Net.Role != Role.Guest) return;
                var v = Registry.Find<ValveController>(id);
                if (v != null && Mathf.Abs(v.GetDamage01() - d) >= 0.01f) Remote(() => v.SetDamage01(d));
                break;
            }
        }
    }

    static void Remote(Action a)
    {
        Net.ApplyingRemote = true;
        try { a(); }
        catch (Exception e) { Mod.Log.Error($"Combat: applying host state failed: {e.Message}"); }
        finally { Net.ApplyingRemote = false; }
    }
}

// ---------------- patches ----------------

[HarmonyPatch(typeof(GunController), nameof(GunController.FireShell))]
static class Combat_FireShell
{
    static bool Prefix()
    {
        if (!Combat.GuestLocal) return true;
        PatchLog.Hit("Combat: guest FireShell skipped (host fires)");
        return false;
    }
    static void Postfix(GunController __instance) { if (!Net.ApplyingRemote) Combat.OnHostFired(__instance); }
}

[HarmonyPatch(typeof(FireMission), nameof(FireMission.SetEntityState))]
static class Combat_SetEntityState
{
    static bool Prefix()
    {
        if (!Combat.GuestLocal) return true;
        PatchLog.Hit("Combat: guest SetEntityState skipped (host decides)");
        return false;
    }
    static void Postfix(MapEntity entity) { PatchLog.Hit("FireMission.SetEntityState"); Combat.OnHostEntityState(entity); }
}

[HarmonyPatch(typeof(TurretController), nameof(TurretController.MoveTurret))]
static class Combat_MoveTurret
{
    static bool Prefix(Vector3 worldPos, bool dodgeCounterBattery)
    {
        PatchLog.Hit("TurretController.MoveTurret");
        if (!Combat.GuestLocal) return true;
        Combat.RequestMove(worldPos, dodgeCounterBattery);
        return false;
    }
    static void Postfix(Vector3 worldPos, bool dodgeCounterBattery) => Combat.OnHostMoved(worldPos, dodgeCounterBattery);
}

[HarmonyPatch(typeof(ValveController), nameof(ValveController.Damage))]
static class Combat_ValveDamage
{
    static bool Prefix() { if (!Combat.GuestLocal) return true; PatchLog.Hit("Combat: guest valve Damage skipped"); return false; }
}

[HarmonyPatch(typeof(ValveController), nameof(ValveController.DamageValve))]
static class Combat_ValveDamageValve
{
    static bool Prefix() { if (!Combat.GuestLocal) return true; PatchLog.Hit("Combat: guest DamageValve skipped"); return false; }
}
