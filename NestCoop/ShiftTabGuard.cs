using UnityEngine.InputSystem;

namespace NestCoop;

/// <summary>
/// 0.9.7: Shift+Tab is Steam's overlay key (friends list, invites), but the game also reacts to Tab (raises the notes
/// clipboard), so Shift+Tab did both.
///
/// Patching ClipboardStateController.OnRaisePerformed didn't work (never called on Tab, tested 2026-11-18), and which of
/// the game's ~20 input handlers Tab drives isn't known. So instead, generically: when Shift goes down, every enabled
/// Input System action with a binding on &lt;Keyboard&gt;/tab is disabled; when Shift comes up, exactly those actions
/// are enabled again (only if they're still disabled — i.e. nobody else changed them). Tab alone is untouched.
/// The affected actions are logged once (always), which identifies what Tab really does.
/// Found in-game (2026-11-18): the only Tab action is 'Universal/Focuse Clipboard' (sic); pausing it works.
/// </summary>
internal static class ShiftTabGuard
{
    static readonly List<InputAction> disabled = new();
    static readonly HashSet<string> logged = new();
    static bool shiftWasDown;

    public static void Update()
    {
        var kb = Keyboard.current;
        bool shift = kb != null && kb.shiftKey.isPressed;
        if (shift == shiftWasDown) return;
        shiftWasDown = shift;
        try { if (shift) DisableTabActions(); else Restore(); }
        catch (Exception e) { Mod.Log.Warning($"ShiftTabGuard: {e.Message}"); }
    }

    static void DisableTabActions()
    {
        disabled.Clear();
        var enabled = InputSystem.ListEnabledActions();
        for (int i = 0; i < enabled.Count; i++)
        {
            var a = enabled[i];
            if (a == null || !BoundToTab(a)) continue;
            a.Disable();
            disabled.Add(a);
            string name = $"{a.actionMap?.name}/{a.name}";
            if (logged.Add(name)) Mod.Log.Msg($"ShiftTabGuard: Tab action '{name}' is paused while Shift is held (Steam overlay)");
        }
    }

    static void Restore()
    {
        foreach (var a in disabled)
            if (a != null && !a.enabled) a.Enable();
        disabled.Clear();
    }

    static bool BoundToTab(InputAction a)
    {
        var bindings = a.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            string path = bindings[i].effectivePath;
            if (path != null && path.Equals("<Keyboard>/tab", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
