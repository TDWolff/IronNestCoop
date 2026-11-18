using HarmonyLib;
using Il2Cpp;

namespace NestCoop.Menu;

/// <summary>
/// Keeps the co-op clipboard and the game's Esc clipboard from being open at the same time (user report: pressing
/// ` then Esc, or Esc then `, raised both clipboards and they clipped into each other). They now swap:
///   * ` while the Esc menu is open → close the Esc menu the game's own way, then open ours.
///   * Esc while ours is open → our menu closes first, then the game opens its Esc menu as usual.
/// The game's Esc menu is EscapeMenuToggleUnityEvent (IsOpen, TrySetOpen, ForceClose, SetOpenState(open, invokeEvent);
/// HandleActionStarted is the Esc key) — names from the dump, build 26352746.
/// </summary>
internal static class EscMenuBridge
{
    static EscapeMenuToggleUnityEvent OpenEscMenu() =>
        Registry.All<EscapeMenuToggleUnityEvent>().FirstOrDefault(m => m != null && m.IsOpen);

    /// <summary>Called by CoopMenu.Open before raising our clipboard.</summary>
    public static void CloseEscMenuIfOpen()
    {
        var esc = OpenEscMenu();
        if (esc == null) return;
        Mod.Log.Msg("Menu: closing the Esc menu to show the co-op menu");
        try
        {
            if (!esc.TrySetOpen(false)) esc.ForceClose(true);
        }
        catch (Exception e) { Mod.Log.Warning($"Menu: couldn't close the Esc menu: {e.Message}"); }
    }

    /// <summary>The game is about to open its Esc menu: put ours away first so its cursor setup wins.</summary>
    public static void BeforeEscMenuOpens()
    {
        if (!CoopMenu.IsOpen) return;
        Mod.Log.Msg("Menu: Esc menu opening — closing the co-op menu");
        CoopMenu.Close();
    }
}

[HarmonyPatch(typeof(EscapeMenuToggleUnityEvent), nameof(EscapeMenuToggleUnityEvent.SetOpenState))]
static class EscMenu_SetOpenState
{
    static void Prefix(bool open)
    {
        PatchLog.Hit("EscapeMenuToggleUnityEvent.SetOpenState", $"open={open}");
        if (open) EscMenuBridge.BeforeEscMenuOpens();
    }
}

// Backstops in case SetOpenState is inlined into these (first-hit logs will show which ones fire).
[HarmonyPatch(typeof(EscapeMenuToggleUnityEvent), nameof(EscapeMenuToggleUnityEvent.TrySetOpen))]
static class EscMenu_TrySetOpen
{
    static void Prefix(bool open)
    {
        PatchLog.Hit("EscapeMenuToggleUnityEvent.TrySetOpen", $"open={open}");
        if (open) EscMenuBridge.BeforeEscMenuOpens();
    }
}

[HarmonyPatch(typeof(EscapeMenuToggleUnityEvent), nameof(EscapeMenuToggleUnityEvent.HandleActionStarted))]
static class EscMenu_HandleActionStarted
{
    // Esc pressed: if our menu is open, Esc means "swap to the Esc menu" — close ours first.
    static void Prefix(EscapeMenuToggleUnityEvent __instance)
    {
        PatchLog.Hit("EscapeMenuToggleUnityEvent.HandleActionStarted");
        if (!__instance.IsOpen) EscMenuBridge.BeforeEscMenuOpens();
    }
}
