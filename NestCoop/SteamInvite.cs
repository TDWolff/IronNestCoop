using NestCoop.Transport;

namespace NestCoop;

/// <summary>
/// Steam invites (0.9.4), using the game's own steam_api64.dll (exports verified: ActivateGameOverlayInviteDialogConnectString,
/// SetRichPresence, ClearRichPresence, SteamApps_v008 GetLaunchCommandLine).
///
///  * While hosting over Steam: rich presence "connect" = "+nestcoop_join &lt;host SteamID&gt;", so Steam shows "Join Game"
///    on the host in friends' lists, and the invite dialog sends that string.
///  * Menu "Invite Steam friends": opens Steam's own invite dialog (Steam overlay must be enabled).
///  * Receiving side: if the game was launched with that string (process args or Steam's launch command line), it
///    switches to the Steam transport and joins that host once the main menu is up.
/// Limit: a friend whose game ISN'T running gets launched by Steam without r2modman's MelonLoader arguments, so the
/// mod doesn't load there. The reliable path: the friend starts the game through r2modman; the host then appears in
/// their co-op menu under "Join &lt;name&gt;".
///  * 0.9.9: accepting an invite / "Join Game" while the game already runs: Steam sends GameRichPresenceJoinRequested_t.
///    The game's Heathen toolkit exposes it as the static event Il2CppSteamTools.Events.OnRichPresenceJoinRequested
///    (UserData friend, string connect) — subscribing also makes it register the Steamworks.NET callback, and the game
///    pumps Steam callbacks itself. The join runs on our next Update (leaving the current session first if needed).
/// </summary>
internal static class SteamInvite
{
    const string Token = "+nestcoop_join";
    static bool checkedLaunch, subscribed;
    static ulong pendingJoin;
    static int pendingStep;
    // Kept alive for the game's lifetime: the IL2CPP side holds the delegate.
    static Il2CppSteamTools.SteamRichPresenceJoinRequestedDelegate joinRequested;

    public static string ConnectString => $"{Token} {SteamApi.MySteamId}";

    public static void OnHostingStarted()
    {
        SteamApi.SetPresence("connect", ConnectString);
        SteamApi.SetPresence("status", "Hosting NestCoop co-op");
        Mod.Log.Msg("SteamInvite: friends can now use Join Game / invites");
    }

    public static void OnStopped() { try { SteamApi.ClearPresence(); } catch { } }

    /// <summary>Menu button (host): Steam's invite dialog.</summary>
    public static void OpenDialog()
    {
        if (Net.Role != Role.Host || !Net.UseSteam) { Overlay.Toast("Host over Steam first (Network: Steam → Host)"); return; }
        if (!SteamApi.Init(out var why)) { Overlay.Toast($"Steam unavailable: {why}"); return; }
        SteamApi.OpenInviteDialog(ConnectString);
        Overlay.Toast("Steam invite dialog opened (needs the Steam overlay). Friends must start the game with r2modman.", 8f);
    }

    /// <summary>Main menu reached: listen for invites accepted while running (once).</summary>
    public static void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        try
        {
            joinRequested = (Action<Il2CppHeathen.SteamworksIntegration.UserData, string>)OnJoinRequested;
            Il2CppSteamTools.Events.add_OnRichPresenceJoinRequested(joinRequested);
            Mod.Log.Msg("SteamInvite: listening for Steam invites / Join Game while running");
        }
        catch (Exception e) { Mod.Log.Warning($"SteamInvite: couldn't listen for Steam join requests ({e.Message}); use the co-op menu to join"); }
    }

    static void OnJoinRequested(Il2CppHeathen.SteamworksIntegration.UserData friend, string connect)
    {
        try
        {
            Mod.Log.Msg($"SteamInvite: Steam join request '{connect}'");
            if (!TryParse(connect, out ulong host)) { Mod.Log.Warning("SteamInvite: not a NestCoop join string; ignored"); return; }
            if (SteamApi.Init(out _) && host == SteamApi.MySteamId) return; // our own session
            pendingJoin = host;
            pendingStep = 0;
        }
        catch (Exception e) { Mod.Log.Error($"SteamInvite: join request failed: {e}"); }
    }

    /// <summary>Per frame: carry out an accepted invite (leave the current session first, then join next frame).</summary>
    public static void Update()
    {
        if (pendingJoin == 0) return;
        if (Net.Active)
        {
            if (pendingStep == 0) { Overlay.Toast("Leaving this session to join the Steam invite"); Net.Stop("joining a Steam invite", quiet: true); }
            pendingStep++;
            if (pendingStep > 120) { Mod.Log.Warning("SteamInvite: couldn't leave the current session; invite dropped"); pendingJoin = 0; }
            return;
        }
        ulong host = pendingJoin;
        pendingJoin = 0;
        Mod.Log.Msg($"SteamInvite: joining {host} from a Steam invite");
        Mod.TransportName.Value = "Steam";
        MelonLoader.MelonPreferences.Save();
        Overlay.Toast("Joining from Steam invite ...");
        Net.Join(host.ToString());
    }

    static bool TryParse(string line, out ulong host)
    {
        host = 0;
        if (string.IsNullOrEmpty(line)) return false;
        int i = line.IndexOf(Token, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return false;
        var rest = line[(i + Token.Length)..].Trim().Split(' ', 2)[0];
        return ulong.TryParse(rest, out host);
    }

    /// <summary>Main menu reached: if we were launched from an invite / Join Game, join that host.</summary>
    public static void CheckLaunchJoin()
    {
        Subscribe();
        if (checkedLaunch || Net.Active) return;
        checkedLaunch = true;
        string line = string.Join(" ", Environment.GetCommandLineArgs());
        try { if (SteamApi.Init(out _)) line += " " + SteamApi.LaunchCommandLine(); } catch { }
        if (line.IndexOf(Token, StringComparison.OrdinalIgnoreCase) < 0) return;
        if (!TryParse(line, out ulong host)) { Mod.Log.Warning("SteamInvite: bad join string on the command line"); return; }
        Mod.Log.Msg($"SteamInvite: launched from an invite — joining {host}");
        Mod.TransportName.Value = "Steam";
        Net.Join(host.ToString());
    }
}
