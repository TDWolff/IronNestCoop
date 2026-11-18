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
        if (Pressed(Mod.HostKey.Value)) Net.StartHost();
        if (Pressed(Mod.JoinKey.Value)) Net.Join();
        if (Pressed(Mod.LeaveKey.Value)) Net.Stop("leave key");
        if (Mod.DebugLog.Value && Pressed(Mod.ReplayKey.Value)) ReplayLastPress();
        if (Mod.DebugLog.Value && Pressed(Mod.MissionReplayKey.Value)) MissionSync.DebugReplay();
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
