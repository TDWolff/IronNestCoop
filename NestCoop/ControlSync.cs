using Il2Cpp;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Milestone 5: continuous controls (wheels/dials and levers/chains) and host-authoritative turret state.
///
/// Controls — "whoever is dragging drives it":
///   * Guest dragging a control: sends its value to the host at SyncHz (unreliable), plus reliable Grab/Release.
///     The host applies it, remembers that guest holds the control, and relays to the other guests.
///   * Host: every tick, broadcasts any shared control whose value changed and that no guest is holding
///     (this covers the host's own drags AND values the game moves by itself, e.g. the traverse wheel backdrive).
///   * Guests ignore incoming values for a control they are dragging themselves (local hand wins while held).
///
/// Turret (lesson from another mod: guest-driven rotation overshot at 130 ms+ ping): the host's simulation is the
/// truth. Guests only feed the host the wheel value while turning it; the host sends turret angle, desired
/// rotation, velocity and gun elevations at TurretHz; guests snap when far off and blend when close.
///
/// API names from the dump (build 26352746): DialInteractable.AccumulatedValue/IsDragging/dialMode/SetDialValue/
/// SetAccumulatedValueUnlimited, LinearSliderInteractable.Value/IsDragging/SetSliderValue,
/// TurretController._CurrentAngle_k__BackingField/_DesiredRotation_k__BackingField/rotationVelocity/
/// ApplyRotationToTransforms/guns, GunController._CurrentElevation_k__BackingField.
/// ASSUMPTION (to verify in test): for Unlimited dials, AccumulatedValue is in the same units that
/// SetAccumulatedValueUnlimited(angleDegrees, ...) takes. The first remote apply per dial logs before/after.
/// </summary>
internal static class ControlSync
{
    const float Epsilon = 0.01f;

    sealed class Control
    {
        public string Id;
        public DialInteractable Dial;
        public LinearSliderInteractable Slider;
        public float LastSent = float.NaN;
        public bool Verified;

        public float Get() => Dial != null ? Dial.AccumulatedValue : Slider.Value;
        public bool Dragging => Dial != null ? Dial.IsDragging : Slider.IsDragging;
        public bool Alive => Dial != null || Slider != null; // UnityEngine.Object == treats destroyed objects as null
    }

    static readonly Dictionary<string, Control> controls = new();
    static int builtForRegistry = -1;

    /// <summary>Host side: control id → guest peer currently dragging it.</summary>
    static readonly Dictionary<string, int> heldBy = new();

    static float nextControlTick, nextTurretTick, nextStatsLog;
    static int statSent, statApplied, statSnaps, statBlends;
    static float statMaxErr;

    // ---------------- Control list ----------------

    static void EnsureControls()
    {
        if (builtForRegistry == Registry.Version && controls.Count > 0) return;
        controls.Clear();
        foreach (var d in Registry.All<DialInteractable>())
            Add(new Control { Id = Ids.Of(d), Dial = d });
        foreach (var s in Registry.All<LinearSliderInteractable>())
            Add(new Control { Id = Ids.Of(s), Slider = s });
        builtForRegistry = Registry.Version;
        if (Mod.DebugLog.Value) Mod.Log.Msg($"ControlSync: {controls.Count} shared controls");
    }

    static void Add(Control c)
    {
        if (SyncPolicy.IsShared(c.Id)) controls.TryAdd(c.Id, c);
    }

    static Control Find(string id)
    {
        EnsureControls();
        return controls.TryGetValue(id, out var c) ? c : null;
    }

    // ---------------- Per-frame ----------------

    public static void Update()
    {
        if (!Net.Active) return;
        float now = Time.unscaledTime;
        bool remoteTraverse = RemoteTraverseDrag();
        TurretProbe.Update(remoteTraverse);

        if (now >= nextControlTick)
        {
            nextControlTick = now + 1f / Mathf.Max(1, Mod.SyncHz.Value);
            SendControlValues();
        }
        if (Net.Role == Role.Host && now >= nextTurretTick)
        {
            nextTurretTick = now + 1f / Mathf.Max(1, Mod.TurretHz.Value);
            SendTurretState();
        }
        if (Mod.DebugLog.Value && now >= nextStatsLog)
        {
            nextStatsLog = now + 5f;
            if (statSent + statApplied + statSnaps + statBlends > 0)
                Mod.Log.Msg($"ControlSync 5s: sent {statSent}, applied {statApplied}, turret snaps {statSnaps}, blends {statBlends}, max err {statMaxErr:0.##}°");
            statSent = statApplied = statSnaps = statBlends = 0; statMaxErr = 0;
        }
    }

    static void SendControlValues()
    {
        if (!Net.CanSend) return;
        EnsureControls();
        foreach (var c in controls.Values)
        {
            if (!c.Alive) continue;
            bool mine = Net.Role == Role.Host ? !heldBy.ContainsKey(c.Id) : c.Dragging;
            if (!mine) continue;
            float v = c.Get();
            if (!float.IsNaN(c.LastSent) && Mathf.Abs(v - c.LastSent) < Epsilon) continue;
            c.LastSent = v;
            Net.SendToSession(Net.Write(MsgType.ControlValue, w => { w.Write(c.Id); w.Write(v); }), reliable: false);
            statSent++;
        }
    }

    // ---------------- Grab / release (called from the drag patches) ----------------

    public static void OnLocalGrab(Component control, bool grabbed)
    {
        if (!Net.CanSend) return;
        string id = Ids.Of(control);
        var c = Find(id);
        if (c == null) return; // not shared
        float v = c.Get();
        c.LastSent = v;
        Net.SendToSession(Net.Write(MsgType.ControlGrab, w => { w.Write(id); w.Write(grabbed); w.Write(v); }), reliable: true);
        if (Mod.DebugLog.Value) Mod.Log.Msg($"ControlSync: {(grabbed ? "grab" : "release")} {id} = {v:0.###}");
    }

    /// <summary>Host: is this control held by the host itself or by a different guest than <paramref name="peer"/>?</summary>
    static bool HeldByOther(Control c, int peer) =>
        c.Dragging || (heldBy.TryGetValue(c.Id, out int owner) && owner != peer);

    public static void OnPeerLeft(int peer)
    {
        foreach (var id in heldBy.Where(kv => kv.Value == peer).Select(kv => kv.Key).ToList())
        {
            heldBy.Remove(id);
            if (controls.TryGetValue(id, out var c))
            {
                SetRemoteDrag(c, false); // don't leave the turret stuck mid-drag
                float v = c.Get();
                c.LastSent = v;
                Net.Broadcast(Net.Write(MsgType.ControlGrab, w => { w.Write(id); w.Write(false); w.Write(v); }), reliable: true); // other guests too
            }
        }
    }

    /// <summary>Session ended: release anything a remote player was holding.</summary>
    public static void Reset()
    {
        foreach (var id in remoteDragging.ToList())
            if (controls.TryGetValue(id, out var c)) SetRemoteDrag(c, false);
        remoteDragging.Clear();
        heldBy.Clear();
    }

    // ---------------- Remote drag mode ----------------
    // M5 test: remote values reached the traverse wheel but the turret ignored them and its backdrive pulled the wheel
    // back ("glitchy spin"). The turret only follows its wheel between the dial's OnBeginDialDrag/OnEndDialDrag events
    // (TurretController.OnBeginRotationDialDrag, rotationDialDragActive), which remote values never raised.
    // So on a remote grab/release we raise the control's own drag events (C# Action: reaches TurretController,
    // ValveController, ...) and its OnGrab/OnRelease UnityEvents (grab sound, motor loop). We do NOT set the dial's
    // isDragging, because then the dial's Update would follow the local mouse.

    static readonly HashSet<string> remoteDragging = new();

    // ---------------- Remote traverse wheel → turret ----------------
    // Probe (M5): during a LOCAL drag the turret sets DesiredRotation = MapDialToTurretRotation(wheel) (≈ wheel / 720),
    // but only while the wheel's own IsDragging is true (local mouse). Otherwise TurretController.Update backdrives the
    // wheel to turret angle × 720 every frame (inlined: a BackdriveRotationDial patch never fired), erasing remote values
    // and causing the "glitchy spin". So the traverse wheel is special: remote values are never written to the wheel.
    // The host turns them into the turret target; the turret turns; the game's backdrive spins the wheel along with it.
    // Guests get the resulting turret angle from TurretState.

    /// <summary>True while a remote player holds the turret's traverse wheel.</summary>
    public static bool RemoteTraverseDrag()
    {
        if (remoteDragging.Count == 0) return false;
        var t = Turret();
        return t != null && t.rotationDial != null && remoteDragging.Contains(Ids.Of(t.rotationDial));
    }

    static bool IsTraverseWheel(Control c)
    {
        if (c.Dial == null) return false;
        var t = Turret();
        return t != null && t.rotationDial != null && c.Id == Ids.Of(t.rotationDial);
    }

    static bool loggedDriveCheck;

    /// <summary>A remote player's traverse wheel value: host sets the turret target from it.</summary>
    static void OnRemoteTraverseValue(float wheel)
    {
        if (Net.Role != Role.Host) return; // guests follow the host turret via TurretState
        var t = Turret();
        float desired = t.MapDialToTurretRotation(wheel);
        t._DesiredRotation_k__BackingField = desired;
        if (!loggedDriveCheck)
        {
            loggedDriveCheck = true;
            Mod.Log.Msg($"ControlSync: remote traverse drive: wheel {wheel:0.##} -> desired {desired:0.##} (wheel/720 = {wheel / 720f:0.##})");
        }
    }

    static void SetRemoteDrag(Control c, bool begin)
    {
        if (begin ? !remoteDragging.Add(c.Id) : !remoteDragging.Remove(c.Id)) return; // no double begin/end
        Net.ApplyingRemote = true;
        try
        {
            if (c.Dial != null)
            {
                // Traverse wheel: skip the drag Actions. The turret only follows the wheel's local IsDragging anyway, and
                // its OnEndRotationDialDrag could reset the target and stop a remote turn halfway. Sounds still play.
                if (!IsTraverseWheel(c)) (begin ? c.Dial.OnBeginDialDrag : c.Dial.OnEndDialDrag)?.Invoke();
                (begin ? c.Dial.OnGrab : c.Dial.OnRelease)?.Invoke();
            }
            else
            {
                (begin ? c.Slider.OnBeginSliderDrag : c.Slider.OnEndSliderDrag)?.Invoke();
                (begin ? c.Slider.OnGrab : c.Slider.OnRelease)?.Invoke();
            }
        }
        catch (Exception e) { Mod.Log.Error($"ControlSync: drag event {c.Id} failed: {e.Message}"); }
        finally { Net.ApplyingRemote = false; }
    }

    // ---------------- Join snapshot ----------------

    public static void SendSnapshot(int peer)
    {
        EnsureControls();
        int n = 0;
        foreach (var c in controls.Values)
        {
            if (!c.Alive) continue;
            float v = c.Get();
            Net.SendTo(peer, Net.Write(MsgType.ControlValue, w => { w.Write(c.Id); w.Write(v); }), reliable: true);
            n++;
        }
        Mod.Log.Msg($"ControlSync: sent {n} control values to guest #{peer}");
    }

    // ---------------- Receive ----------------

    public static void Handle(MsgType type, int from, BinaryReader r, byte[] raw)
    {
        switch (type)
        {
            case MsgType.ControlValue:
            {
                string id = r.ReadString();
                float v = r.ReadSingle();
                var c = Find(id);
                if (c == null) { Mod.Log.Warning($"ControlSync: unknown/non-shared control {id}"); return; }
                if (Net.Role == Role.Host && HeldByOther(c, from)) return; // someone else has it
                if (IsTraverseWheel(c)) { OnRemoteTraverseValue(v); if (Net.Role == Role.Host) { heldBy.TryAdd(id, from); c.LastSent = v; Net.Broadcast(raw, reliable: false, exceptPeer: from); } return; }
                if (Net.Role == Role.Host)
                {
                    heldBy.TryAdd(id, from); // a value from a guest means they're driving it
                    Apply(c, v);
                    c.LastSent = v;
                    Net.Broadcast(raw, reliable: false, exceptPeer: from);
                }
                else if (!c.Dragging) Apply(c, v);
                break;
            }
            case MsgType.ControlGrab:
            {
                string id = r.ReadString();
                bool grabbed = r.ReadBoolean();
                float v = r.ReadSingle();
                var c = Find(id);
                if (c == null) return;
                if (Net.Role == Role.Host && grabbed && HeldByOther(c, from))
                {
                    Mod.Log.Msg($"ControlSync: guest #{from} tried to grab {id}, already held");
                    return;
                }
                if (Net.Role == Role.Host && !grabbed && heldBy.TryGetValue(id, out int owner) && owner != from) return; // not theirs
                bool traverse = IsTraverseWheel(c);
                if (traverse) OnRemoteTraverseValue(v);
                if (Net.Role == Role.Host)
                {
                    if (grabbed) heldBy[id] = from; else heldBy.Remove(id);
                    if (!traverse) Apply(c, v);
                    c.LastSent = v;
                    Net.Broadcast(raw, reliable: true, exceptPeer: from);
                }
                else if (!c.Dragging && !traverse) Apply(c, v);
                if (!c.Dragging) SetRemoteDrag(c, grabbed);
                if (Mod.DebugLog.Value) Mod.Log.Msg($"ControlSync: remote {(grabbed ? "grab" : "release")} {id} = {v:0.###}");
                break;
            }
            case MsgType.TurretState:
                ApplyTurretState(r);
                break;
        }
    }

    static void Apply(Control c, float v)
    {
        float before = c.Get();
        Net.ApplyingRemote = true;
        try
        {
            if (c.Slider != null) c.Slider.SetSliderValue(v);
            else if (c.Dial.dialMode == DialInteractable.DialMode.Unlimited) c.Dial.SetAccumulatedValueUnlimited(v, true, true);
            else c.Dial.SetDialValue(v);
        }
        catch (Exception e) { Mod.Log.Error($"ControlSync: apply {c.Id} failed: {e.Message}"); }
        finally { Net.ApplyingRemote = false; }
        statApplied++;
        if (!c.Verified)
        {
            c.Verified = true;
            Mod.Log.Msg($"ControlSync: first apply {c.Id} ({(c.Slider != null ? "slider" : c.Dial.dialMode.ToString())}): {before:0.###} -> target {v:0.###}, now {c.Get():0.###}");
        }
    }

    // ---------------- Turret (host-authoritative) ----------------

    static TurretController Turret() => Registry.All<TurretController>().FirstOrDefault();

    static void SendTurretState()
    {
        if (!Net.CanSend) return;
        var t = Turret();
        if (t == null) return;
        float angle = t._CurrentAngle_k__BackingField;
        float desired = t._DesiredRotation_k__BackingField;
        float vel = t.rotationVelocity;
        var guns = t.guns;
        int n = guns?.Count ?? 0;
        var elev = new float[n];
        for (int i = 0; i < n; i++) elev[i] = guns[i]._CurrentElevation_k__BackingField;
        Net.Broadcast(Net.Write(MsgType.TurretState, w =>
        {
            w.Write(angle); w.Write(desired); w.Write(vel); w.Write(n);
            foreach (var e in elev) w.Write(e);
        }), reliable: false);
    }

    static void ApplyTurretState(BinaryReader r)
    {
        float angle = r.ReadSingle(), desired = r.ReadSingle(), vel = r.ReadSingle();
        int n = r.ReadInt32();
        var elev = new float[n];
        for (int i = 0; i < n; i++) elev[i] = r.ReadSingle();
        if (Net.Role != Role.Guest) return;

        var t = Turret();
        if (t == null) return;
        float err = Mathf.DeltaAngle(t._CurrentAngle_k__BackingField, angle);
        statMaxErr = Mathf.Max(statMaxErr, Mathf.Abs(err));

        t._DesiredRotation_k__BackingField = desired;
        if (Mathf.Abs(err) > Mod.TurretSnapDegrees.Value)
        {
            t._CurrentAngle_k__BackingField = angle;
            t.rotationVelocity = vel;
            statSnaps++;
        }
        else if (Mathf.Abs(err) > 0.05f)
        {
            t._CurrentAngle_k__BackingField += err * 0.3f;
            statBlends++;
        }
        t.ApplyRotationToTransforms();

        var guns = t.guns;
        for (int i = 0; i < n && guns != null && i < guns.Count; i++)
        {
            float cur = guns[i]._CurrentElevation_k__BackingField;
            float e = elev[i] - cur;
            if (Mathf.Abs(e) > 1f) guns[i]._CurrentElevation_k__BackingField = elev[i];
            else if (Mathf.Abs(e) > 0.02f) guns[i]._CurrentElevation_k__BackingField = cur + e * 0.3f;
        }
    }
}
