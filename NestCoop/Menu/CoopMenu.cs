using Il2Cpp;
using MelonLoader;
using NestCoop.Transport;
using UnityEngine;

namespace NestCoop.Menu;

/// <summary>
/// The co-op menu's logic, with NO drawing code. It owns: open/closed state, freeing/restoring the mouse and freezing
/// the player while open, the data the menu shows, and the actions its buttons trigger.
///
/// Drawing is delegated to an <see cref="IMenuView"/>. Today that's <see cref="ImguiMenuView"/>; a uGUI or
/// game-style (Kamgam settings page) menu later only needs a new IMenuView that reads these properties and calls these
/// methods — nothing here changes.
/// </summary>
internal static class CoopMenu
{
    static readonly ImguiMenuView imgui = new();
    static ClipboardMenuView clipboard;

    /// <summary>The view in use for the current open menu (MenuStyle "Game" → clipboard, else IMGUI).</summary>
    public static IMenuView View { get; private set; } = imgui;

    static IMenuView PickView()
    {
        if (string.Equals(Mod.MenuStyle.Value, "Game", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                clipboard ??= new ClipboardMenuView();
                if (clipboard.EnsureBuilt()) return clipboard;
            }
            catch (Exception e) { Mod.Log.Error($"Clipboard menu failed, using the IMGUI menu: {e}"); clipboard = null; }
        }
        return imgui;
    }

    public static bool IsOpen { get; private set; }

    // ---------------- Data for views ----------------

    public static string Status => Net.StatusLine ?? "Not connected";
    public static Role Role => Net.Role;
    public static bool Connected => Net.Active;
    public static bool UsingSteam => Net.UseSteam;
    public static string Version => Mod.Version;
    public static IEnumerable<(int id, string name)> Peers => Net.Peers;

    /// <summary>Steam friends currently in IRON NEST (refreshed every 3 s while the menu is open).</summary>
    public static IReadOnlyList<(ulong id, string name)> FriendsInGame => friends;
    public static string SteamProblem { get; private set; }

    public static bool DebugLog
    {
        get => Mod.DebugLog.Value;
        set { if (Mod.DebugLog.Value == value) return; Clicked($"debug logging {(value ? "on" : "off")}"); Mod.DebugLog.Value = value; MelonPreferences.Save(); }
    }

    static List<(ulong id, string name)> friends = new();
    static float nextFriendRefresh;

    // ---------------- Actions for views ----------------

    public static void Host() { Clicked("Host"); Net.StartHost(); }
    public static void Join() { Clicked("Join"); Net.Join(); }
    public static void JoinFriend(ulong steamId) { Clicked($"Join friend {steamId}"); Net.Join(steamId.ToString()); }
    public static void InviteFriends() { Clicked("Invite Steam friends"); SteamInvite.OpenDialog(); }
    public static void Resync() { Clicked("Resync from host"); Snapshot.RequestResync(); }
    public static void Leave() { Clicked("Leave"); Net.Stop("left via menu"); }

    // ---------------- Kick (host) ----------------
    // Two clicks: the first arms it for 4 s ("Click again to kick <name>"), the second kicks.

    static int armedKick = -1;
    static float armedUntil;

    /// <summary>Host: the guests that can be kicked (id, plain name).</summary>
    public static IEnumerable<(int id, string name)> Kickable => Net.Role == Role.Host
        ? Net.Peers.Select(p => (p.id, Avatars.NameOf(p.id))).ToList()
        : Enumerable.Empty<(int, string)>();

    public static bool KickArmed(int id) => armedKick == id && Time.unscaledTime < armedUntil;

    public static string KickLabel(int id, string name) => KickArmed(id) ? $"Click again to kick {name}" : $"Kick {name}";

    public static void Kick(int id)
    {
        if (!KickArmed(id)) { armedKick = id; armedUntil = Time.unscaledTime + 4f; Clicked($"kick {id} (armed)"); return; }
        armedKick = -1;
        Clicked($"kick {id}");
        Net.Kick(id);
    }

    /// <summary>Latest result message ("Hosting via Steam", "No Steam friends are playing ..."), shown in the menu.</summary>
    public static string LastMessage => Overlay.LastMessage != null && Time.unscaledTime - Overlay.LastMessageTime < 15f ? Overlay.LastMessage : null;

    /// <summary>Every menu click is logged, so a button that "does nothing" can be told apart from one with no visible result.</summary>
    static void Clicked(string what) => Mod.Log.Msg($"Menu: clicked {what}");

    /// <summary>Switch transport (only while not connected).</summary>
    public static void SetUseSteam(bool steam)
    {
        if (Net.Active || steam == UsingSteam) return;
        Clicked(steam ? "switch to Steam" : "switch to Localhost");
        Mod.TransportName.Value = steam ? "Steam" : "LiteNetLib";
        MelonPreferences.Save();
        nextFriendRefresh = 0;
    }

    // ---------------- Open / close ----------------

    /// <summary>
    /// The ` key: closes the co-op menu if it's open; if the game's Esc menu is open, closes just that (press ` again
    /// for the co-op menu); otherwise opens the co-op menu.
    /// </summary>
    public static void Toggle()
    {
        if (IsOpen) { Close(); return; }
        if (EscMenuBridge.CloseEscMenuIfOpen()) return;
        Open();
    }

    public static void Open()
    {
        if (IsOpen) return;
        EscMenuBridge.CloseEscMenuIfOpen(); // swap with the game's Esc clipboard instead of stacking on it
        IsOpen = true;
        nextFriendRefresh = 0;
        View = PickView();
        Cursor.Grab(View.UsesGameUi);
        try { View.Show(true); } catch (Exception e) { Mod.Log.Error($"Menu view Show failed: {e}"); }
    }

    public static void Close()
    {
        if (!IsOpen) return;
        Clicked("Close");
        IsOpen = false;
        try { View.Show(false); } catch (Exception e) { Mod.Log.Error($"Menu view Show(false) failed: {e}"); }
        Cursor.Release();
        // Menu test 4: the click on "Close" itself pressed the lamp behind it 3 ms later — the game handles that same
        // mouse-down once we stop blocking. Keep blocking world clicks briefly after closing.
        BlockWorldUntil = Time.unscaledTime + 0.3f;
    }

    /// <summary>World clicks stay blocked until this time (unscaled), see Close().</summary>
    public static float BlockWorldUntil { get; private set; }

    /// <summary>True while the game must ignore the mouse: menu open, or just closed.</summary>
    public static bool BlocksWorld => IsOpen || Time.unscaledTime < BlockWorldUntil;

    // ---------------- Per frame ----------------

    public static void Update()
    {
        if (!IsOpen) return;
        if (!View.UsesGameUi) Cursor.KeepFree();
        try { View.Refresh(); } catch (Exception e) { Mod.Log.Error($"Menu view Refresh failed: {e}"); }
        if (UsingSteam && Time.unscaledTime >= nextFriendRefresh)
        {
            nextFriendRefresh = Time.unscaledTime + 3f;
            if (SteamApi.Init(out var why)) { friends = SteamApi.FriendsInThisGame(); SteamProblem = null; }
            else { friends = new(); SteamProblem = why; }
        }
    }

    /// <summary>Called from MelonMod.OnGUI.</summary>
    public static void OnGUI()
    {
        if (!IsOpen) return;
        try { View.Draw(); }
        catch (Exception e)
        {
            Mod.Log.Error($"Menu view failed, closing menu: {e}");
            Close();
        }
    }

    /// <summary>
    /// Mouse + player handling, using the game's own systems so its state comes back exactly as it was:
    /// DynamicCursorManager._currentMode is saved and restored with SwitchToPresentationMode; SetUIBlocking stops the
    /// game reacting to clicks behind the menu; FirstPersonController.SetFrozen stops walking/looking.
    /// (All names from the dump, build 26352746.)
    /// </summary>
    static class Cursor
    {
        static DynamicCursorManager.PresentationMode savedMode;
        static bool haveSaved, froze;

        static DynamicCursorManager Manager() => Registry.All<DynamicCursorManager>().FirstOrDefault();
        static FirstPersonController Player() => Registry.All<FirstPersonController>().FirstOrDefault();

        /// <param name="gameUi">Game UI (clipboard) uses the game's menu cursor; IMGUI needs the OS cursor.</param>
        public static void Grab(bool gameUi)
        {
            try
            {
                var cm = Manager();
                if (cm != null)
                {
                    savedMode = cm._currentMode;
                    haveSaved = true;
                    if (gameUi) cm.RestoreMouseForMenu(); else cm.SwitchToFreeMouse();
                    cm.SetUIBlocking(true, gameUi);
                }
                var p = Player();
                if (p != null && p.gameObject.activeInHierarchy) { p.SetFrozen(true); froze = true; }
            }
            catch (Exception e) { Mod.Log.Warning($"Menu: couldn't free the mouse: {e.Message}"); }
            if (!gameUi) KeepFree();
        }

        /// <summary>The game may re-lock the cursor; keep it usable while the menu is open.</summary>
        public static void KeepFree()
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        public static void Release()
        {
            try
            {
                var cm = Manager();
                if (cm != null)
                {
                    cm.SetUIBlocking(false, false);
                    if (haveSaved) cm.SwitchToPresentationMode(savedMode);
                }
                if (froze) { Player()?.SetFrozen(false); froze = false; }
            }
            catch (Exception e) { Mod.Log.Warning($"Menu: couldn't restore the mouse: {e.Message}"); }
            haveSaved = false;
        }
    }
}
