using Il2Cpp;
using UnityEngine.InputSystem;

namespace NestCoop;

/// <summary>
/// Debug hotkeys (only when DebugLog is on).
///   ReplayKey (default Backslash, set in MelonPreferences.cfg) = replay the last button you pressed by calling LookAtTarget.OnClickDown() directly, flagged as remote.
///        This is exactly what a guest/host will do with a received press, so it tests: does a direct call do a
///        full press (animation + gameplay), and does echo suppression mark it [remote]?
/// Default is NOT an F-key: on the dev keyboard F9 sends media Play/Pause (paused Spotify) unless Fn is held,
/// so the game never received it. We read both the new Input System and legacy UnityEngine.Input, logging once
/// which one works.
/// </summary>
internal static class DebugKeys
{
    public static LookAtTarget LastPressed;

    static bool loggedNoKeyboard, loggedKeyboardOk, legacyBroken, loggedLegacyOk;

    public static void Update()
    {
        if (!Mod.DebugLog.Value) return;
        if (ReplayPressed()) ReplayLastPress();
    }

    static bool ReplayPressed()
    {
        bool pressed = false;
        string keyName = Mod.ReplayKey.Value;

        // New Input System
        try
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                if (!loggedNoKeyboard) { loggedNoKeyboard = true; Mod.Log.Warning("DebugKeys: Keyboard.current is null"); }
            }
            else
            {
                if (!loggedKeyboardOk && kb.anyKey.wasPressedThisFrame) { loggedKeyboardOk = true; Mod.Log.Msg("DebugKeys: new Input System sees key presses"); }
                if (Enum.TryParse<Key>(keyName, true, out var key) && kb[key].wasPressedThisFrame)
                { Mod.Log.Msg($"DebugKeys: {keyName} via Input System"); pressed = true; }
            }
        }
        catch (Exception e)
        {
            if (!loggedNoKeyboard) { loggedNoKeyboard = true; Mod.Log.Warning($"DebugKeys: Input System read failed: {e.Message}"); }
        }

        // Legacy input manager (may be disabled in this build — then it throws, and we stop trying)
        if (!pressed && !legacyBroken)
        {
            try
            {
                if (Enum.TryParse<UnityEngine.KeyCode>(keyName, true, out var code) && UnityEngine.Input.GetKeyDown(code))
                {
                    if (!loggedLegacyOk) { loggedLegacyOk = true; Mod.Log.Msg($"DebugKeys: {keyName} via legacy Input"); }
                    pressed = true;
                }
            }
            catch (Exception e)
            {
                legacyBroken = true;
                Mod.Log.Warning($"DebugKeys: legacy Input unavailable ({e.GetType().Name}); using Input System only");
            }
        }
        return pressed;
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
