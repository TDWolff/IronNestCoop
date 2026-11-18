using Il2Cpp;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Milestone 7: remote player avatars.
///
/// Every machine sends its own pose (feet position, head rotation, crouch) at PoseHz, unreliable. Guests send to the
/// host; the host stamps the sender's peer id and relays to the other guests, and sends its own pose as player 0.
/// Nobody ever receives their own pose, so a guest doesn't need to know its own id.
/// Receivers draw a floating head per player (0.9.8: AvatarModel, a helmet band in the player's colour, plus a dark
/// shadow disc on the floor; falls back to the old capsule + sphere if the model can't load) and smooth toward the
/// latest pose. Avatars are removed when the player leaves, the session stops, or after
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
        public GameObject Root, Body, Head; // Body: capsule fallback only (null with the head model)
        public GameObject Shadow;           // head model only; placed on the floor below by raycast
        public Il2CppTMPro.TextMeshPro Label;
        public Color Color;
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

            GameObject body = null, head, shadow = null;
            head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 1.65f, 0);
            head.transform.localScale = Vector3.one * HeadScale;
            if (AvatarModel.Build(head.transform, color, MakeMaterial))
            {
                // Shadow under the floating head so it reads as standing somewhere.
                // Shadow on the floor under the floating head. The pose position is the player's body centre, not the
                // feet, so the floor is found by a raycast each frame (PlaceShadow).
                shadow = Part(PrimitiveType.Cylinder, root.transform, Vector3.zero, new Vector3(0.45f, 0.004f, 0.45f), new Color(0.04f, 0.04f, 0.04f));
            }
            else
            {
                UnityEngine.Object.Destroy(head);
                body = Part(PrimitiveType.Capsule, root.transform, new Vector3(0, 0.9f, 0), new Vector3(0.5f, 0.9f, 0.5f), color);
                head = Part(PrimitiveType.Sphere, root.transform, new Vector3(0, 1.65f, 0), new Vector3(0.3f, 0.3f, 0.3f), color);
                // Visor: a dark slab on the front of the head, so you can see where they're looking.
                Part(PrimitiveType.Cube, head.transform, new Vector3(0, 0.05f, 0.42f), new Vector3(0.8f, 0.3f, 0.3f), Color.black);
            }

            a = new Avatar { Root = root, Body = body, Head = head, Shadow = shadow, Color = color };
            ApplyName(a, id);
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

    /// <summary>The head model is life-size (23 cm); a bit bigger reads better across the cockpit.</summary>
    const float HeadScale = 1.25f;

    static Shader litShader;
    static bool shaderLookedUp;

    static Shader LitShader()
    {
        if (!shaderLookedUp)
        {
            shaderLookedUp = true;
            litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            Mod.Log.Msg($"Avatars: shader {(litShader != null ? litShader.name : "not found")}");
        }
        return litShader;
    }

    /// <summary>A lit material in <paramref name="color"/> (URP property names, plus built-in fallbacks).</summary>
    static Material MakeMaterial(Color color, float metallic, float smoothness)
    {
        var mat = new Material(LitShader());
        mat.SetColor("_BaseColor", color); // URP
        mat.SetColor("_Color", color);     // built-in fallback
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Glossiness", smoothness);
        return mat;
    }

    static void Tint(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        if (LitShader() == null)
        {
            rend.material.SetColor("_Color", color);
            return;
        }
        rend.material = MakeMaterial(color, 0f, 0.2f);
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
        var cam = Camera.main;
        List<int> stale = null;
        foreach (var (id, a) in avatars)
        {
            if (a.Root == null || Time.unscaledTime - a.LastSeen > Mod.AvatarTimeout.Value) { (stale ??= new()).Add(id); continue; }
            var tr = a.Root.transform;
            tr.position = Vector3.Lerp(tr.position, a.TargetFeet, t);
            ApplyHead(a, t);
            if (a.Label != null && cam != null)
            {
                // Name tag: above the head, always facing this player's camera (TMP reads from -Z).
                var lt = a.Label.transform;
                lt.localPosition = new Vector3(0, EyeHeight(a.Crouched) + 0.35f, 0);
                lt.rotation = Quaternion.LookRotation(lt.position - cam.transform.position);
            }
            PlaceShadow(a);
            if (a.Body != null)
            {
                float bodyY = a.Crouched ? 0.6f : 0.9f;
                a.Body.transform.localScale = new Vector3(0.5f, bodyY, 0.5f);
                a.Body.transform.localPosition = new Vector3(0, bodyY, 0);
            }
        }
        if (stale != null) foreach (var id in stale) Remove(id, "timed out");
    }

    static void ApplyHead(Avatar a, float t)
    {
        // Body turns with the head's yaw only; the head also pitches.
        var yawOnly = Quaternion.Euler(0, a.TargetHead.eulerAngles.y, 0);
        a.Root.transform.rotation = Quaternion.Slerp(a.Root.transform.rotation, yawOnly, t);
        a.Head.transform.rotation = Quaternion.Slerp(a.Head.transform.rotation, a.TargetHead, t);
        a.Head.transform.localPosition = new Vector3(0, EyeHeight(a.Crouched), 0);
    }

    /// <summary>Put the shadow on whatever is below the player (floor, stairs, turret deck); hide it if nothing is.</summary>
    static void PlaceShadow(Avatar a)
    {
        if (a.Shadow == null) return;
        var from = a.Root.transform.position + Vector3.up * 0.1f;
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!a.Shadow.activeSelf) a.Shadow.SetActive(true);
            a.Shadow.transform.position = hit.point + Vector3.up * 0.01f;
        }
        else if (a.Shadow.activeSelf) a.Shadow.SetActive(false);
    }

    // Eye height above the pose position (the player's body centre), measured from our own player (everyone uses the same FirstPersonController, so the
    // remote players' cameras sit at the same height). Defaults until measured: the 0.9.7 guesses.
    static float standEye = 1.65f, crouchEye = 1.15f;
    static bool loggedStand, loggedCrouch;

    static float EyeHeight(bool crouched)
    {
        var p = Player();
        if (p != null && p.cameraRoot != null)
        {
            float h = p.cameraRoot.position.y - p.transform.position.y;
            if (h > 0.3f && h < 2.5f)
            {
                if (p.isCrouched) { crouchEye = h; if (!loggedCrouch) { loggedCrouch = true; Mod.Log.Msg($"Avatars: crouched eye height {h:0.00} m"); } }
                else { standEye = h; if (!loggedStand) { loggedStand = true; Mod.Log.Msg($"Avatars: standing eye height {h:0.00} m"); } }
            }
        }
        return crouched ? crouchEye : standEye;
    }

    // ---------------- Names ----------------
    // Each player's display name: the PlayerName setting, else their Steam name, else "Player". A guest sends its own
    // name to the host on connect (PlayerInfo, id -1); the host stamps the peer id, relays it to the other guests and
    // sends the newcomer every known name including its own (id 0). Shown as a tag above the avatar.

    static readonly Dictionary<int, string> names = new();

    public static string MyName
    {
        get
        {
            string n = Mod.PlayerName.Value?.Trim();
            if (string.IsNullOrEmpty(n) && Transport.SteamApi.Init(out _)) n = Transport.SteamApi.MyName;
            if (string.IsNullOrEmpty(n)) n = "Player";
            return n.Length > 24 ? n[..24] : n;
        }
    }

    static byte[] WriteInfo(int id, string name) => Net.Write(MsgType.PlayerInfo, w => { w.Write(id); w.Write(name); });

    /// <summary>Guest: just connected to the host — introduce ourselves.</summary>
    public static void SendMyName() => Net.SendToSession(WriteInfo(-1, MyName), reliable: true);

    /// <summary>Host: a guest joined — send them our name and everyone else's.</summary>
    public static void SendNamesTo(int peer)
    {
        Net.SendTo(peer, WriteInfo(HostPlayerId, MyName), reliable: true);
        foreach (var (id, n) in names)
            if (id != peer) Net.SendTo(peer, WriteInfo(id, n), reliable: true);
    }

    public static void HandleInfo(int from, BinaryReader r)
    {
        int id = r.ReadInt32();
        string name = r.ReadString();
        if (name.Length > 24) name = name[..24];
        if (Net.Role == Role.Host)
        {
            id = from; // trust the transport, not the message
            Net.Broadcast(WriteInfo(id, name), reliable: true, exceptPeer: from);
        }
        names[id] = name;
        Mod.Log.Msg($"Avatars: player {id} is '{name}'");
        if (avatars.TryGetValue(id, out var a)) ApplyName(a, id);
    }

    /// <summary>Name for a player id (for the menu); falls back to "Player n".</summary>
    public static string NameOf(int id) => names.TryGetValue(id, out var n) ? n : $"Player {id}";

    static void ApplyName(Avatar a, int id)
    {
        if (a.Root == null) return;
        string text = NameOf(id);
        if (a.Label == null)
        {
            a.Label = Text3D.Create(a.Root.transform, text, 0.03f, Color.Lerp(a.Color, Color.white, 0.35f));
            if (a.Label == null) Mod.Log.Warning("Avatars: name tag template not found; no name tags");
        }
        else a.Label.text = text;
    }

    // ---------------- Cleanup ----------------

    public static void OnPeerLeft(int peer)
    {
        if (Net.Role == Role.Host) Remove(peer, "left");
        // Guests: the host doesn't announce other guests leaving yet; their avatar times out instead.
    }

    public static void Reset()
    {
        names.Clear();
        foreach (var id in avatars.Keys.ToList()) Remove(id, "session ended");
    }

    public static void RemovePlayer(int id, string why) => Remove(id, why);

    static void Remove(int id, string why)
    {
        if (why != "timed out") names.Remove(id); // a timed-out avatar comes back with the same name
        if (!avatars.Remove(id, out var a)) return;
        if (a.Root != null) UnityEngine.Object.Destroy(a.Root);
        Mod.Log.Msg($"Avatars: removed player {id} ({why})");
    }
}
