using HarmonyLib;
using UnityEngine.UI;

namespace NestCoop.Menu;

// Clicks on the clipboard menu's buttons. Unity UI calls Button.OnPointerClick (mouse/virtual cursor) or
// Button.OnSubmit (keyboard/gamepad); both are in the UnityEngine.UI proxy. If the button is one of ours, run its
// action and skip the original (its onClick is empty anyway). All other buttons in the game are untouched.
// Avoids passing C# delegates into Button.onClick through IL2CPP.

[HarmonyPatch(typeof(Button), nameof(Button.OnPointerClick))]
static class ClipboardButton_OnPointerClick
{
    static bool Prefix(Button __instance) => !ClipboardButtons.Run(__instance, "click");
}

[HarmonyPatch(typeof(Button), nameof(Button.OnSubmit))]
static class ClipboardButton_OnSubmit
{
    static bool Prefix(Button __instance) => !ClipboardButtons.Run(__instance, "submit");
}

static class ClipboardButtons
{
    /// <summary>True if this was one of our buttons (handled).</summary>
    public static bool Run(Button b, string how)
    {
        if (b == null || !ClipboardMenuView.Actions.TryGetValue(b.Pointer, out var action)) return false;
        PatchLog.Hit($"Clipboard menu button {how}");
        try { action(); }
        catch (Exception e) { Mod.Log.Error($"Clipboard menu action failed: {e}"); }
        return true;
    }
}
