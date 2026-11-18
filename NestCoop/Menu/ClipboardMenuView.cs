using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NestCoop.Menu;

/// <summary>
/// Game-style co-op menu: a clone of the game's own Esc-menu clipboard (world-space canvas under the camera), with the
/// game's own buttons, fonts and paper, but our labels and actions.
///
/// From the UI dump (ui-dump.txt, game version 1.0 (1742)):
///   Main Camera/Options Menu Parent/Clipboard Main Menu      [Animator, AnimatorBoolToggler, PickUpZoomTarget, …]
///     Canvas/ESC Menu Buttons/Return to Game Btn              [Button, SelectionEventListener, …] → Bg, BgShadow,
///                                                              SelectionUGUI/Selected, "Text (TMP)" (TextMeshProUGUI)
///     Canvas/ESC Menu Buttons/Text (TMP)                      title "IRON NEST"
///     Canvas/ESC Menu Buttons/BuildVersion                    small text bottom-left
/// Buttons sit at y = 128.8, 83.6, 33.6, … in canvas units.
///
/// Safety: the clone is made under an INACTIVE holder so none of the game scripts on it Awake, then every game script
/// except the animation/selection visuals is destroyed (no saving missions, no opening web pages, no settings logic),
/// and localisation components are removed so they can't overwrite our text. Clicks are caught by the Button patch in
/// ClipboardButtonPatch (no delegates through IL2CPP).
/// </summary>
internal sealed class ClipboardMenuView : IMenuView
{
    const string TemplatePath = "Main Camera/Options Menu Parent/Clipboard Main Menu";
    const int Slots = 9;
    const float TopY = 140f, Step = 35f, ButtonScale = 0.78f;

    /// <summary>Button instance pointer → action for the current frame's layout. Read by ClipboardButtonPatch.</summary>
    public static readonly Dictionary<IntPtr, Action> Actions = new();

    GameObject root;
    AnimatorBoolToggler toggler;
    TextMeshProUGUI title, status;
    readonly List<(GameObject go, Button button, TextMeshProUGUI label)> slots = new();
    bool shown;
    float nextRefresh;

    public bool UsesGameUi => true;

    /// <summary>Build the clone if needed. False if the game's clipboard isn't there (caller falls back to IMGUI).</summary>
    public bool EnsureBuilt()
    {
        if (root != null) return true;
        var template = FindByPath(TemplatePath);
        if (template == null) { Mod.Log.Warning($"Clipboard menu: template '{TemplatePath}' not found"); return false; }

        // 1. Clone under an inactive holder → no Awake/OnEnable on the game scripts inside.
        var holder = new GameObject("NestCoop Clipboard Holder");
        holder.SetActive(false);
        var clone = UnityEngine.Object.Instantiate(template.gameObject, holder.transform);
        clone.name = "NestCoop Clipboard";

        // 2. Strip game logic and unneeded pages.
        var canvas = clone.transform.Find("Canvas");
        var menu = canvas?.Find("ESC Menu Buttons");
        var templateButton = menu?.Find("Return to Game Btn");
        if (canvas == null || menu == null || templateButton == null)
        {
            Mod.Log.Warning("Clipboard menu: unexpected clipboard layout (game update?)");
            UnityEngine.Object.Destroy(holder);
            return false;
        }
        DestroyChild(canvas, "Main Menu Cover");
        DestroyChild(canvas, "Settings menu");
        for (int i = menu.childCount - 1; i >= 0; i--)
        {
            var c = menu.GetChild(i);
            if (c.name is "Return to Game Btn" or "Text (TMP)" or "BuildVersion" or "iron nest jumping" or "iron nest jumping (1)") continue;
            UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        StripGameScripts(clone);

        title = menu.Find("Text (TMP)")?.GetComponent<TextMeshProUGUI>();
        status = menu.Find("BuildVersion")?.GetComponent<TextMeshProUGUI>();
        toggler = clone.GetComponent<AnimatorBoolToggler>();

        // 3. A pool of buttons cloned from the game's own button.
        templateButton.gameObject.SetActive(false);
        for (int i = 0; i < Slots; i++)
        {
            var b = UnityEngine.Object.Instantiate(templateButton.gameObject, menu);
            b.name = $"NestCoop Button {i}";
            var rt = b.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0, TopY - i * Step);
            rt.localScale = new Vector3(ButtonScale, ButtonScale, ButtonScale);
            var button = b.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // drop the original "Return to Game" listeners
            slots.Add((b, button, b.transform.Find("Text (TMP)")?.GetComponent<TextMeshProUGUI>()));
        }
        UnityEngine.Object.DestroyImmediate(templateButton.gameObject);

        // 4. Move next to the original (under the camera) and wake up, still hidden.
        clone.transform.SetParent(template.parent, false);
        UnityEngine.Object.Destroy(holder);
        root = clone;
        root.SetActive(true);
        if (title != null) title.text = "NESTCOOP";
        Mod.Log.Msg($"Clipboard menu: built from the game's clipboard (animator param '{toggler?.parameterName}')");
        return true;
    }

    public void Show(bool show)
    {
        if (root == null) return;
        shown = show;
        if (toggler != null) toggler.SetBool(show);  // the game's own slide-in/out animation
        else root.SetActive(show);
        nextRefresh = 0;
        if (!show) Actions.Clear();
    }

    /// <summary>Per frame while open (from CoopMenu.Update): refresh labels/actions a few times per second.</summary>
    public void Refresh()
    {
        if (root == null || !shown || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;

        var items = new List<(string label, Action action)>();
        if (!CoopMenu.Connected)
        {
            items.Add(("Host co-op", CoopMenu.Host));
            items.Add((CoopMenu.UsingSteam ? "Join first friend" : "Join localhost", CoopMenu.Join));
            if (CoopMenu.UsingSteam)
                foreach (var f in CoopMenu.FriendsInGame.Take(3))
                {
                    ulong id = f.id;
                    items.Add(($"Join {f.name}", () => CoopMenu.JoinFriend(id)));
                }
            items.Add((CoopMenu.UsingSteam ? "Network: Steam" : "Network: Localhost", () => CoopMenu.SetUseSteam(!CoopMenu.UsingSteam)));
        }
        else
        {
            items.Add(("Leave session", CoopMenu.Leave));
            if (CoopMenu.Role == Role.Host && CoopMenu.UsingSteam) items.Add(("Invite Steam friends", CoopMenu.InviteFriends));
            if (CoopMenu.Role == Role.Guest) items.Add(("Resync from host", CoopMenu.Resync));
            foreach (var (_, name) in CoopMenu.Peers.Take(4)) items.Add(($"• {name}", null));
        }
        items.Add((CoopMenu.DebugLog ? "Debug log: On" : "Debug log: Off", () => CoopMenu.DebugLog = !CoopMenu.DebugLog));
        items.Add(("Close", CoopMenu.Close));

        Actions.Clear();
        for (int i = 0; i < slots.Count; i++)
        {
            var (go, button, label) = slots[i];
            bool used = i < items.Count;
            if (go.activeSelf != used) go.SetActive(used);
            if (!used) continue;
            if (label != null && label.text != items[i].label) label.text = items[i].label;
            button.interactable = items[i].action != null;
            if (items[i].action != null) Actions[button.Pointer] = items[i].action;
        }

        if (status != null)
        {
            string msg = CoopMenu.LastMessage;
            status.text = $"{CoopMenu.Status}{(msg != null ? "  —  " + msg : "")}";
        }
    }

    /// <summary>Nothing to draw with IMGUI — the clipboard is a real object in the scene.</summary>
    public void Draw() { }

    public void Destroy()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        root = null; slots.Clear(); Actions.Clear();
    }

    // ---------------- helpers ----------------

    static readonly HashSet<string> KeepScripts = new() { "AnimatorBoolToggler", "SelectionUGUI" };

    /// <summary>Destroy every game (Il2Cpp namespace) MonoBehaviour except pure visuals; keep Unity/TMP components.</summary>
    static void StripGameScripts(GameObject clone)
    {
        int removed = 0;
        foreach (var c in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (c == null) continue;
            var t = c.GetIl2CppType();
            string ns = t.Namespace ?? "";
            bool unityOrTmp = ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro");
            if (unityOrTmp || KeepScripts.Contains(t.Name)) continue;
            UnityEngine.Object.DestroyImmediate(c);
            removed++;
        }
        Mod.Log.Msg($"Clipboard menu: removed {removed} game scripts from the clone");
    }

    static void DestroyChild(Transform parent, string name)
    {
        var c = parent.Find(name);
        if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
    }

    static Transform FindByPath(string path)
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
