using Il2Cpp;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Milestone 7: remote player avatars.
///
/// Every machine sends its own pose (feet position, head rotation, crouch) at PoseHz, unreliable. Guests send to the
/// host; the host stamps the sender's peer id and relays to the other guests, and sends its own pose as player 0.
/// Nobody ever receives their own pose, so a guest doesn't need to know its own id.
/// Receivers draw a placeholder per player — capsule body, sphere head, dark "visor" showing look direction — and
/// smooth toward the latest pose. Avatars are removed when the player leaves, the session stops, or after
/// AvatarTimeout seconds without a pose.
///
/// Positions are world space in the Master Turret Scene. That's fine while turret angles match (host-authoritative,
/// M5); a player standing on the rotating turret is carried by it on both machines.
/// Names from the dump: FirstPersonController.cameraRoot / isCrouched; Unity: GameObject.CreatePrimitive,
/// Shader.Find, Material.SetColor, Object.DontDestroyOnLoad.
/// </summary>
internal static class Avatars
{
    const int HostPlayerId = 0;

    sealed class Avatar
    {
        public GameObject Root, Body, Head;
        public Vector3 TargetFeet;
        public Quaternion TargetHead = Quaternion.identity;
        public bool Crouched;
        public float LastSeen;
    }

    static readonly Dictionary<int, Avatar> avatars = new();
    static float nextSend;
    static bool loggedFirstPose;

    // ---------------- Send own pose ----------------

    public static void Update()
    {
        if (Net.Active) SendOwnPose();
        Smooth();
    }

    static FirstPersonController Player() => Registry.All<FirstPersonController>().FirstOrDefault();

    static void SendOwnPose()
    {
        if (!Net.CanSend || Time.unscaledTime < nextSend) return;
        nextSend = Time.unscaledTime + 1f / Mathf.Max(1, Mod.PoseHz.Value);
        var p = Player();
        if (p == null || p.cameraRoot == null) return;
        var feet = p.transform.position;
        var head = p.cameraRoot.rotation;
        bool crouch = p.isCrouched;
        // Guests send -1 (host fills in the sender id); the host is player 0.
        int id = Net.Role == Role.Host ? HostPlayerId : -1;
        Net.SendToSession(WritePose(id, feet, head, crouch), reliable: false);
    }

    static byte[] WritePose(int id, Vector3 feet, Quaternion head, bool crouch) => Net.Write(MsgType.PlayerPose, w =>
    {
        w.Write(id);
        w.Write(feet.x); w.Write(feet.y); w.Write(feet.z);
        w.Write(head.x); w.Write(head.y); w.Write(head.z); w.Write(head.w);
        w.Write(crouch);
    });

    // ---------------- Receive ----------------

    public static void Handle(int from, BinaryReader r)
    {
        int id = r.ReadInt32();
        var feet = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var head = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        bool crouch = r.ReadBoolean();

        if (Net.Role == Role.Host)
        {
            id = from; // trust the transport, not the message
            Net.Broadcast(WritePose(id, feet, head, crouch), reliable: false, exceptPeer: from);
        }

        var a = GetOrCreate(id);
        if (a == null) return;
        bool first = a.LastSeen == 0;
        a.TargetFeet = feet;
        a.TargetHead = head;
        a.Crouched = crouch;
        a.LastSeen = Time.unscaledTime;
        if (first) { a.Root.transform.position = feet; ApplyHead(a, 1f); }
        if (!loggedFirstPose)
        {
            loggedFirstPose = true;
            Mod.Log.Msg($"Avatars: first pose from player {id} at {feet} (my position {Player()?.transform.position})");
        }
    }

    // ---------------- Visuals ----------------

    static Avatar GetOrCreate(int id)
    {
        if (avatars.TryGetValue(id, out var a) && a.Root != null) return a;
        try
        {
            var root = new GameObject($"NestCoop Avatar {id}");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var color = ColorFor(id);

            var body = Part(PrimitiveType.Capsule, root.transform, new Vector3(0, 0.9f, 0), new Vector3(0.5f, 0.9f, 0.5f), color);
            var head = Part(PrimitiveType.Sphere, root.transform, new Vector3(0, 1.65f, 0), new Vector3(0.3f, 0.3f, 0.3f), color);
            // Visor: a dark slab on the front of the head, so you can see where they're looking.
            Part(PrimitiveType.Cube, head.transform, new Vector3(0, 0.05f, 0.42f), new Vector3(0.8f, 0.3f, 0.3f), Color.black);

            a = new Avatar { Root = root, Body = body, Head = head };
            avatars[id] = a;
            Mod.Log.Msg($"Avatars: created avatar for player {id}");
            Overlay.Toast($"Player {id} avatar spawned");
            return a;
        }
        catch (Exception e)
        {
            Mod.Log.Error($"Avatars: could not create avatar: {e}");
            return null;
        }
    }

    static GameObject Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = type.ToString();
        // No collider: the avatar must not block movement or catch the interaction raycasts.
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        Tint(go, color);
        return go;
    }

    static Shader litShader;
    static bool shaderLookedUp;

    static void Tint(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        if (!shaderLookedUp)
        {
            shaderLookedUp = true;
            litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            Mod.Log.Msg($"Avatars: shader {(litShader != null ? litShader.name : "not found, using primitive default")}");
        }
        var mat = litShader != null ? new Material(litShader) : rend.material;
        mat.SetColor("_BaseColor", color); // URP
        mat.SetColor("_Color", color);     // built-in fallback
        rend.material = mat;
    }

    static Color ColorFor(int id) => (id % 6) switch
    {
        0 => new Color(0.95f, 0.55f, 0.1f),  // host: orange
        1 => new Color(0.2f, 0.6f, 1f),
        2 => new Color(0.3f, 0.85f, 0.3f),
        3 => new Color(0.9f, 0.25f, 0.6f),
        4 => new Color(0.95f, 0.9f, 0.2f),
        _ => new Color(0.6f, 0.4f, 1f),
    };

    static void Smooth()
    {
        if (avatars.Count == 0) return;
        float t = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        List<int> stale = null;
        foreach (var (id, a) in avatars)
        {
            if (a.Root == null || Time.unscaledTime - a.LastSeen > Mod.AvatarTimeout.Value) { (stale ??= new()).Add(id); continue; }
            var tr = a.Root.transform;
            tr.position = Vector3.Lerp(tr.position, a.TargetFeet, t);
            ApplyHead(a, t);
            float bodyY = a.Crouched ? 0.6f : 0.9f;
            a.Body.transform.localScale = new Vector3(0.5f, bodyY, 0.5f);
            a.Body.transform.localPosition = new Vector3(0, bodyY, 0);
        }
        if (stale != null) foreach (var id in stale) Remove(id, "timed out");
    }

    static void ApplyHead(Avatar a, float t)
    {
        // Body turns with the head's yaw only; the head also pitches.
        var yawOnly = Quaternion.Euler(0, a.TargetHead.eulerAngles.y, 0);
        a.Root.transform.rotation = Quaternion.Slerp(a.Root.transform.rotation, yawOnly, t);
        a.Head.transform.rotation = Quaternion.Slerp(a.Head.transform.rotation, a.TargetHead, t);
        a.Head.transform.localPosition = new Vector3(0, a.Crouched ? 1.15f : 1.65f, 0);
    }

    // ---------------- Cleanup ----------------

    public static void OnPeerLeft(int peer)
    {
        if (Net.Role == Role.Host) Remove(peer, "left");
        // Guests: the host doesn't announce other guests leaving yet; their avatar times out instead.
    }

    public static void Reset()
    {
        foreach (var id in avatars.Keys.ToList()) Remove(id, "session ended");
    }

    public static void RemovePlayer(int id, string why) => Remove(id, why);

    static void Remove(int id, string why)
    {
        if (!avatars.Remove(id, out var a)) return;
        if (a.Root != null) UnityEngine.Object.Destroy(a.Root);
        Mod.Log.Msg($"Avatars: removed player {id} ({why})");
    }
}
