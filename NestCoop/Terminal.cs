using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;

namespace NestCoop;

/// <summary>
/// "NestCoop Terminal": a small box with a big orange button on the lid of the safe box next to the computer desk.
/// Pressing it opens the co-op menu.
///
/// Fixed in place (chosen in-game by the author, 2026-11-17, game 1.0 (1742)): it is a child of the safe box's
/// collision proxy under Turret, so it moves with the turret (rotation, MoveTurret). If a game update moves or renames
/// that object, the terminal simply isn't built (logged) and the ` key still opens the menu.
///
/// Built from the UI dump (ui-dump.txt):
///   * Clickable part: a clone of the table lamp's "Universal Button"
///     (- Lighting ------------------/Table Lamp/Table Lamp/Lamp Head/Universal Button:
///      Animator, LookAtTarget, BoxCollider, 4×AnimatorBoolToggler, Interactable). Cloning keeps the game's real
///     cursor/prompt settings, so hover, "Interact with NestCoop Terminal" and clicking behave like any game button.
///     Its serialized events (which point at the real lamp) are replaced with empty ones, its animators/togglers and
///     children are removed, and it gets its own collider.
///   * Look: primitives with the Requisition Console's metal material (Requisition Control Pannel/.Cover), an orange
///     button with a dark collar, and a 3D label cloned from the Esc clipboard's TextMeshPro text ("OPTIONS MENU (TMP)").
/// Pressing it: LookAtTarget.OnClickDown postfix → CoopMenu.Open(). It's excluded from sync (SyncPolicy), so your
/// terminal never opens anyone else's menu.
/// </summary>
internal static class Terminal
{
    public const string Name = "NestCoop Terminal";

    // Hardcoded placement: the safe-box lid by the computer desk.
    const string AnchorId = "Master Turret Scene:Turret/Colliders/.Collider.008";
    static readonly Vector3 LocalPosition = new(0.4703125f, 1.315998f, -1.20048f);
    static readonly Vector3 LocalEuler = new(87.80394f, 90.00027f, 270.0003f);

    static GameObject root;
    static LookAtTarget button;
    static bool failed;
    static float nextTry;

    public static bool Is(LookAtTarget t) => button != null && t != null && t.Pointer == button.Pointer;

    /// <summary>Settings tab: show/hide an already-built terminal when the Terminal setting changes.</summary>
    public static void ApplyEnabled()
    {
        if (root != null) root.SetActive(Mod.TerminalEnabled.Value);
    }

    /// <summary>Per frame: build once the turret scene is ready.</summary>
    public static void Update()
    {
        if (!Mod.TerminalEnabled.Value || failed || (root != null && button != null)) return;
        if (Time.unscaledTime < nextTry) return;
        nextTry = Time.unscaledTime + 2f;
        try { Build(); }
        catch (Exception e) { failed = true; Mod.Log.Error($"Terminal: build failed, disabled for this session: {e}"); }
    }

    static void Build()
    {
        var lampButton = Find("- Lighting ------------------/Table Lamp/Table Lamp/Lamp Head/Universal Button");
        if (lampButton == null) return; // turret scene not loaded yet; try again later
        var anchor = Ids.ResolveTransform(AnchorId);
        if (anchor == null)
        {
            failed = true;
            Mod.Log.Warning($"Terminal: anchor '{AnchorId}' not found (game update?) — no terminal; use the ` key for the menu");
            return;
        }

        root = new GameObject(Name);
        root.transform.SetParent(anchor, false);
        root.transform.localPosition = LocalPosition;
        root.transform.localRotation = Quaternion.Euler(LocalEuler);

        // ---- clickable part (cloned game button) ----
        var holder = new GameObject("NestCoop Terminal Holder");
        holder.SetActive(false); // no Awake on the clone until it's cleaned up
        var clone = UnityEngine.Object.Instantiate(lampButton.gameObject, holder.transform);
        clone.name = Name; // the prompt shows "Interact with {objectName}"
        for (int i = clone.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(clone.transform.GetChild(i).gameObject);
        foreach (var t in clone.GetComponents<AnimatorBoolToggler>()) UnityEngine.Object.DestroyImmediate(t);
        var anim = clone.GetComponent<Animator>();
        if (anim != null) UnityEngine.Object.DestroyImmediate(anim);
        var oldCol = clone.GetComponent<BoxCollider>();
        if (oldCol != null) UnityEngine.Object.DestroyImmediate(oldCol);

        button = clone.GetComponent<LookAtTarget>();
        button.onLookAt = new UnityEvent(); button.onLookAway = new UnityEvent();
        button.onClickDown = new UnityEvent(); button.onClickUp = new UnityEvent();
        button.onActivated = new UnityEvent(); button.onDeactivated = new UnityEvent();
        button.onDeadPress = new UnityEvent(); button.onDoublePress = new UnityEvent();
        button.useMalfunctions = false;
        button.animator = null;
        var inter = clone.GetComponent<Interactable>();
        if (inter != null) { inter.restrictToAllowedColliders = false; inter.isInteractable = true; }

        clone.transform.SetParent(root.transform, false);
        clone.transform.localPosition = new Vector3(0, 0.05f, 0);
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        var col = clone.AddComponent<BoxCollider>();
        col.size = new Vector3(0.26f, 0.1f, 0.2f); // the whole little box is the button
        UnityEngine.Object.Destroy(holder);

        // ---- look: small dark box, big orange button with a dark collar, label on the front ----
        var metal = Find("Requisition Console/Requisition Control Pannel/.Cover")?.GetComponent<MeshRenderer>()?.sharedMaterial;
        var t2 = root.transform;
        Part(PrimitiveType.Cube, t2, new Vector3(0, 0.035f, 0), new Vector3(0.26f, 0.07f, 0.2f), metal, new Color(0.25f, 0.27f, 0.3f));
        Part(PrimitiveType.Cylinder, t2, new Vector3(0, 0.08f, 0.01f), new Vector3(0.11f, 0.015f, 0.11f), null, new Color(0.95f, 0.55f, 0.1f));
        Part(PrimitiveType.Cylinder, t2, new Vector3(0, 0.072f, 0.01f), new Vector3(0.14f, 0.004f, 0.14f), metal, new Color(0.12f, 0.12f, 0.13f));
        Label(t2, new Vector3(0, 0.035f, -0.101f), 0.008f);

        Mod.Log.Msg($"Terminal: built on {anchor.name} at {root.transform.position}");
    }

    // ---------------- helpers ----------------

    static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material baseMat, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        var c = go.GetComponent<Collider>();
        if (c != null) UnityEngine.Object.Destroy(c); // only the cloned button's collider should catch the cursor
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        var mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? r.material.shader);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        r.material = mat;
        return go;
    }

    /// <summary>3D label, cloned from the game's own clipboard TextMeshPro so it uses the game font.</summary>
    static void Label(Transform parent, Vector3 localPos, float scale)
    {
        var src = Find("Main Camera/Options Menu Parent/Clipboard Main Menu/Canvas/Main Menu Cover/Play Tutorial Button/OPTIONS MENU (TMP)");
        if (src == null) { Mod.Log.Warning("Terminal: label template not found; no label"); return; }
        var holder = new GameObject("label holder"); holder.SetActive(false);
        var text = UnityEngine.Object.Instantiate(src.gameObject, holder.transform);
        foreach (var mb in text.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetIl2CppType().Name == "StaticLocalisedText") UnityEngine.Object.DestroyImmediate(mb);
        text.transform.SetParent(parent, false);
        // The player stands on the terminal's -Z side; TMP text reads from -Z.
        text.transform.localPosition = localPos;
        text.transform.localRotation = Quaternion.identity;
        text.transform.localScale = new Vector3(scale, scale, scale);
        text.SetActive(true);
        var tmp = text.GetComponent<TextMeshPro>();
        if (tmp != null) tmp.text = "NESTCOOP";
        UnityEngine.Object.Destroy(holder);
    }

    static Transform Find(string path)
    {
        var parts = path.Split('/');
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetSceneAt(s).GetRootGameObjects())
            {
                if (r.name != parts[0]) continue;
                var cur = r.transform;
                for (int i = 1; i < parts.Length && cur != null; i++) cur = cur.Find(parts[i]);
                if (cur != null) return cur;
            }
        return null;
    }
}
