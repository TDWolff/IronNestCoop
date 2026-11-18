namespace NestCoop;

/// <summary>
/// Which objects are shared between players. Default: everything in the turret is shared; the list below is
/// excluded. Based on the Milestone 1 recon inventory of button (LookAtTarget) objects by area.
/// Checked on send AND on receive, so a peer can't press an excluded button on us either.
/// </summary>
internal static class SyncPolicy
{
    static readonly string[] NotShared =
    {
        // Menus and mission selection: mission flow is Milestone 6. Syncing these now would let a guest start,
        // quit or change missions for everyone.
        "Barbet/ - Main Menu - /",
        "Barbet/Campaign 1/",
        "Barbet/Campaign map camera/",
        "Main Camera/",                 // options menu clipboard, static notepad
        "Cam/",

        // Per-player: reading material and personal comforts.
        "Operating Manual",             // any manual, anywhere in the path
        "Barbet/Expresso Machine/",
        "Record Player/",
        "Barbet/CatObjects/",
        "Barbet/Base - Bedroom/",
    };

    /// <summary>
    /// A LookAtTarget that is part of a drag control (a chain/lever slider or a wheel/dial on the same object).
    /// Its OnClickDown fires repeatedly while dragging (M4 test: trigger chain + starter chain "presses" every
    /// ~150 ms, in pairs), so it must not be synced as a button press. Milestone 5 syncs these by value.
    /// </summary>
    public static bool IsDragControl(UnityEngine.Component c) =>
        c.GetComponent<Il2Cpp.LinearSliderInteractable>() != null || c.GetComponent<Il2Cpp.DialInteractable>() != null;

    public static bool IsShared(string id)
    {
        int colon = id.IndexOf(':');
        string path = colon >= 0 ? id[(colon + 1)..] : id;
        foreach (var rule in NotShared)
        {
            bool match = rule.EndsWith("/") ? path.StartsWith(rule) || path.Contains("/" + rule) : path.Contains(rule);
            if (match) return false;
        }
        return true;
    }
}
