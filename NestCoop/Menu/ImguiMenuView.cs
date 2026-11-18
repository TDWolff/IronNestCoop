using UnityEngine;

namespace NestCoop.Menu;

/// <summary>
/// IMGUI drawing of the co-op menu. Draws with GUI.Box / GUI.Label only and does its OWN click detection.
///
/// Why not GUI.Button/Toggle/BeginScrollView: menu test 3 logged a mouse-down + mouse-up inside the Host button with
/// IMGUI's hotControl set, yet GUI.Button never returned true (Leave/Close sometimes did). The game doesn't use IMGUI
/// controls, so their code is likely stripped and rebuilt by Il2CppInterop — not something to rely on. Drawing
/// (Box/Label) works fine, so buttons here are boxes + our own hit test on Event.current MouseDown.
/// All behaviour lives in CoopMenu; this class only lays things out.
/// </summary>
internal sealed class ImguiMenuView : IMenuView
{
    const float W = 420, Pad = 12, Row = 26, Gap = 6;
    const int MaxFriendsShown = 6;

    /// <summary>The window rectangle drawn last frame, in GUI coordinates (CoopMenu uses it to block game clicks).</summary>
    public static Rect LastWindow;

    public bool UsesGameUi => false;
    public bool EnsureBuilt() => true;
    public void Show(bool show) { }
    public void Refresh() { }

    // tiny vertical layout cursor
    float x, y, w;
    Rect Next(float h = Row) { var r = new Rect(x, y, w, h); y += h + Gap; return r; }
    void Space(float h = 8) => y += h;

    // ---------------- own controls ----------------

    static Event Ev => Event.current;
    static bool Hover(Rect r) => Ev != null && r.Contains(Ev.mousePosition);

    /// <summary>A box that acts as a button: true on left mouse-down inside it (event consumed).</summary>
    static bool Button(Rect r, string label, bool enabled = true)
    {
        if (!enabled) { GUI.Box(r, $"<color=#888888>{label}</color>"); return false; }
        GUI.Box(r, label);
        if (Hover(r)) GUI.Box(r, "");               // second box = highlight on hover
        if (Ev != null && Ev.type == EventType.MouseDown && Ev.button == 0 && r.Contains(Ev.mousePosition))
        {
            Ev.Use();
            return true;
        }
        return false;
    }

    /// <summary>Checkbox-style toggle built from Button.</summary>
    static bool Toggle(Rect r, bool value, string label) =>
        Button(r, $"{(value ? "[x]" : "[  ]")}  {label}") ? !value : value;

    // ---------------- layout ----------------

    public void Draw()
    {
        var friendsList = CoopMenu.UsingSteam ? CoopMenu.FriendsInGame : null;
        int friendRows = friendsList == null ? 0 : Math.Max(1, Math.Min(friendsList.Count, MaxFriendsShown));
        float h = 330 + (CoopMenu.UsingSteam ? 40 + friendRows * (Row + Gap) : 0);
        var win = new Rect(40, 60, W, h);
        LastWindow = win;

        GUI.Box(win, "");
        GUI.Box(win, "");          // twice = darker background with the default skin
        x = win.x + Pad; y = win.y + Pad; w = win.width - 2 * Pad;

        GUI.Label(Next(), $"<b>NestCoop {CoopMenu.Version}</b>   —   {CoopMenu.Status}");
        GUI.Label(Next(22), CoopMenu.LastMessage != null ? $"<color=#f0b040>» {CoopMenu.LastMessage}</color>" : "");
        Space(2);

        // --- Session ---
        if (!CoopMenu.Connected)
        {
            bool steam = Toggle(Next(), CoopMenu.UsingSteam, CoopMenu.UsingSteam ? "Steam (play with friends)" : "Localhost (testing with FakePeer)");
            if (steam != CoopMenu.UsingSteam) CoopMenu.SetUseSteam(steam);

            // Steam: joining is per friend (list below), so Host takes the whole row.
            var row = Next(32);
            if (CoopMenu.UsingSteam) { if (Button(row, "Host")) CoopMenu.Host(); }
            else
            {
                float bw = (w - Gap) / 2;
                if (Button(new Rect(row.x, row.y, bw, row.height), "Host")) CoopMenu.Host();
                if (Button(new Rect(row.x + bw + Gap, row.y, bw, row.height), "Join localhost")) CoopMenu.Join();
            }
        }
        else
        {
            if (Button(Next(32), "Leave session")) CoopMenu.Leave();
            if (CoopMenu.Role == Role.Host && CoopMenu.UsingSteam && Button(Next(), "Invite Steam friends")) CoopMenu.InviteFriends();
            if (CoopMenu.Role == Role.Guest && Button(Next(), "Resync from host")) CoopMenu.Resync();
        }
        Space();

        // --- Players ---
        GUI.Label(Next(), CoopMenu.Role == Role.Host ? "<b>Connected guests</b>" : "<b>Connected to</b>");
        int n = 0;
        var kickNames = CoopMenu.Kickable.ToDictionary(k => k.id, k => k.name);
        foreach (var (id, name) in CoopMenu.Peers)
        {
            var r = Next(22);
            n++;
            if (!kickNames.TryGetValue(id, out var plain)) { GUI.Label(r, $"   • {name}"); continue; }
            const float bw = 190;
            GUI.Label(new Rect(r.x, r.y, r.width - bw - Gap, r.height), $"   • {name}");
            if (Button(new Rect(r.xMax - bw, r.y, bw, r.height), CoopMenu.KickArmed(id) ? "Click again to kick" : "Kick")) CoopMenu.Kick(id);
        }
        if (n == 0) GUI.Label(Next(22), CoopMenu.Connected ? "   (nobody yet)" : "   (not connected)");
        Space();

        // --- Friends in game (Steam) ---
        if (friendsList != null)
        {
            GUI.Label(Next(), "<b>Steam friends playing IRON NEST</b>");
            if (CoopMenu.SteamProblem != null) GUI.Label(Next(), $"   Steam unavailable: {CoopMenu.SteamProblem}");
            else if (friendsList.Count == 0) GUI.Label(Next(), "   (none right now)");
            else
            {
                for (int i = 0; i < Math.Min(friendsList.Count, MaxFriendsShown); i++)
                {
                    var r = Next();
                    GUI.Label(new Rect(r.x + 8, r.y, r.width - 100, r.height), friendsList[i].name);
                    if (Button(new Rect(r.xMax - 84, r.y, 84, r.height), "Join", enabled: !CoopMenu.Connected))
                        CoopMenu.JoinFriend(friendsList[i].id);
                }
                if (friendsList.Count > MaxFriendsShown) GUI.Label(Next(22), $"   … and {friendsList.Count - MaxFriendsShown} more");
            }
            Space();
        }

        // --- Options ---
        bool dbg = Toggle(Next(), CoopMenu.DebugLog, "Debug logging (patch hits, network messages)");
        if (dbg != CoopMenu.DebugLog) CoopMenu.DebugLog = dbg;

        GUI.Label(new Rect(win.x + Pad, win.yMax - Pad - 30 - 26, w, 22),
            $"Keys: {Mod.MenuKey.Value} menu · {Mod.HostKey.Value} host · {Mod.JoinKey.Value} join · {Mod.LeaveKey.Value} leave");
        if (Button(new Rect(win.x + Pad, win.yMax - Pad - 30, w, 30), "Close")) CoopMenu.Close();
    }
}
