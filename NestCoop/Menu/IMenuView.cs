namespace NestCoop.Menu;

/// <summary>
/// How the co-op menu is presented. A view only reads <see cref="CoopMenu"/>'s properties and calls its action
/// methods; it holds no game or network logic. Implementations: <see cref="ImguiMenuView"/> (drawn overlay) and
/// <see cref="ClipboardMenuView"/> (clone of the game's own Esc clipboard). Pick with the MenuStyle setting.
/// </summary>
internal interface IMenuView
{
    /// <summary>True if the view is real game UI (EventSystem + the game's cursor); false for an IMGUI overlay.</summary>
    bool UsesGameUi { get; }

    /// <summary>Create whatever the view needs. False = can't be used right now (CoopMenu falls back to IMGUI).</summary>
    bool EnsureBuilt();

    /// <summary>Menu opened/closed.</summary>
    void Show(bool show);

    /// <summary>Every frame while open (from Update): sync labels/state from CoopMenu.</summary>
    void Refresh();

    /// <summary>Every OnGUI while open: immediate-mode drawing (IMGUI views only).</summary>
    void Draw();
}
