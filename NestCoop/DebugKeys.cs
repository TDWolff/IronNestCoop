using Il2Cpp;
using UnityEngine.InputSystem;

namespace NestCoop;

/// <summary>
/// Hotkeys. Key names are Unity Input System names, configurable in UserData/MelonPreferences.cfg [NestCoop].
///   HostKey  (default Home)      start hosting on localhost
///   JoinKey  (default End)       join JoinAddress:Port
///   LeaveKey (default Delete)    disconnect
///   ReplayKey (default Backslash, DebugLog only) replay your last button press as if it came from the network
/// Defaults avoid the F-row: on the dev keyboard F-keys are media keys unless Fn is held (F9 paused Spotify).
/// </summary>
internal static class DebugKeys
{
    public static LookAtTarget LastPressed;

    static bool loggedNoKeyboard, loggedKeyboardOk;

    public static void Update()
    {
        if (Pressed(Mod.MenuKey.Value)) Menu.CoopMenu.Toggle();
        if (Mod.DebugLog.Value && Pressed(Mod.UiDumpKey.Value)) { try { UiDump.Run(); } catch (Exception e) { Mod.Log.Error($"UI dump failed: {e}"); } }
        if (Pressed(Mod.HostKey.Value)) Net.StartHost();
        if (Pressed(Mod.JoinKey.Value)) Net.Join();
        if (Pressed(Mod.LeaveKey.Value)) Net.Stop("leave key");
        if (Mod.DebugLog.Value && Pressed(Mod.ReplayKey.Value)) ReplayLastPress();
        if (Mod.DebugLog.Value && Pressed(Mod.MissionReplayKey.Value)) MissionSync.DebugReplay();
        if (Mod.DebugLog.Value && Pressed(Mod.CheckpointTestKey.Value)) Snapshot.DebugRoundTrip();
        if (Mod.DebugLog.Value && Pressed(Mod.SteamCheckKey.Value)) SteamCheck();
    }

    /// <summary>True on the frame the named key goes down (Input System; legacy Input proved unnecessary).</summary>
    static bool Pressed(string keyName)
    {
        try
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                if (!loggedNoKeyboard) { loggedNoKeyboard = true; Mod.Log.Warning("DebugKeys: Keyboard.current is null"); }
                return false;
            }
            if (!loggedKeyboardOk && kb.anyKey.wasPressedThisFrame) { loggedKeyboardOk = true; Mod.Log.Msg("DebugKeys: keyboard input OK"); }
            return Enum.TryParse<Key>(keyName, true, out var key) && kb[key].wasPressedThisFrame;
        }
        catch (Exception e)
        {
            if (!loggedNoKeyboard) { loggedNoKeyboard = true; Mod.Log.Warning($"DebugKeys: Input System read failed: {e.Message}"); }
            return false;
        }
    }

    /// <summary>Local Steam test (P2P with yourself is impossible): can we reach steam_api64, who are we, who's in game.</summary>
    static void SteamCheck()
    {
        if (!Transport.SteamApi.Init(out var why)) { Mod.Log.Warning($"Steam check: {why}"); Overlay.Toast($"Steam: {why}"); return; }
        var friends = Transport.SteamApi.FriendsInThisGame();
        string me = $"{Transport.SteamApi.MyName} ({Transport.SteamApi.MySteamId}), app {Transport.SteamApi.AppId}";
        string list = friends.Count == 0 ? "none" : string.Join(", ", friends.Select(f => $"{f.name} ({f.id})"));
        Mod.Log.Msg($"Steam check: OK as {me}. Friends in IRON NEST: {list}");
        Overlay.Toast($"Steam OK: {Transport.SteamApi.MyName}. Friends in game: {friends.Count}", 6f);
    }

    static void ReplayLastPress()
    {
        if (LastPressed == null) { Mod.Log.Msg("Replay: press a button in-game first."); return; }
        Mod.Log.Msg($"Replay: replaying OnClickDown on {Ids.Of(LastPressed)}");
        Net.ApplyingRemote = true;
        try { LastPressed.OnClickDown(); }
        catch (Exception e) { Mod.Log.Error($"Replay failed: {e}"); }
        finally { Net.ApplyingRemote = false; }
    }
}
