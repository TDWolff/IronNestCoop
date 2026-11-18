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
/// their co-op menu under "Join &lt;name&gt;". Accepting while the game already runs raises a Steam callback we don't hook.
/// </summary>
internal static class SteamInvite
{
    const string Token = "+nestcoop_join";
    static bool checkedLaunch;

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

    /// <summary>Main menu reached: if we were launched from an invite / Join Game, join that host.</summary>
    public static void CheckLaunchJoin()
    {
        if (checkedLaunch || Net.Active) return;
        checkedLaunch = true;
        string line = string.Join(" ", Environment.GetCommandLineArgs());
        try { if (SteamApi.Init(out _)) line += " " + SteamApi.LaunchCommandLine(); } catch { }
        int i = line.IndexOf(Token, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return;
        var rest = line[(i + Token.Length)..].Trim().Split(' ', 2)[0];
        if (!ulong.TryParse(rest, out ulong host)) { Mod.Log.Warning($"SteamInvite: bad join string '{rest}'"); return; }
        Mod.Log.Msg($"SteamInvite: launched from an invite — joining {host}");
        Mod.TransportName.Value = "Steam";
        Net.Join(host.ToString());
    }
}
