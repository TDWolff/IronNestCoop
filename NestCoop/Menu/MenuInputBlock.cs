using HarmonyLib;
using Il2Cpp;

namespace NestCoop.Menu;

// While the co-op menu is open the game must not react to the mouse. Menu test 3: clicks inside the menu still
// pressed the table lamp behind it — the game's DynamicCursorManager raycasts the raw mouse into the world and
// DynamicCursorManager.SetUIBlocking(true, …) did not stop it. So we skip the game's own click/hover entry points
// (all confirmed to fire in earlier tests, names from the dump). Each logs its first block with DebugLog on.

static class MenuBlock
{
    public static bool Allow(string what)
    {
        if (!CoopMenu.BlocksWorld) return true;
        PatchLog.Hit($"MenuBlock: {what}");
        return false;
    }
}

[HarmonyPatch(typeof(LookAtTarget), nameof(LookAtTarget.HandleClickDownFromManager))]
static class Block_LookAtTarget_ClickDown { static bool Prefix() => MenuBlock.Allow("LookAtTarget click"); }

[HarmonyPatch(typeof(DialInteractable), nameof(DialInteractable.HandlePrimaryClickDown))]
static class Block_Dial_ClickDown { static bool Prefix() => MenuBlock.Allow("dial grab"); }

[HarmonyPatch(typeof(LinearSliderInteractable), nameof(LinearSliderInteractable.HandlePrimaryClickDown))]
static class Block_Slider_ClickDown { static bool Prefix() => MenuBlock.Allow("lever grab"); }

[HarmonyPatch(typeof(DynamicCursorManager), nameof(DynamicCursorManager.PerformHoverDetection))]
static class Block_Cursor_Hover { static bool Prefix() => MenuBlock.Allow("hover detection"); }

[HarmonyPatch(typeof(DynamicCursorManager), nameof(DynamicCursorManager.HandleGrabInput))]
static class Block_Cursor_Grab { static bool Prefix() => MenuBlock.Allow("grab input"); }
