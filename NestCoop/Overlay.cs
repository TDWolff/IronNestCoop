using UnityEngine;

namespace NestCoop;

/// <summary>
/// Tiny on-screen status (IMGUI) in the top-left: session role and peer count, plus short-lived toasts
/// ("Hosting", "Guest joined", ...) so key presses give visible feedback. GUI.Box/Label exist in the
/// UnityEngine.IMGUIModule proxy; if the native side turns out stripped, we log once and stop drawing.
/// </summary>
internal static class Overlay
{
    static string toast;
    static float toastUntil;
    static bool broken;

    /// <summary>Most recent toast text and when it was shown (the menu displays it, since it covers the corner).</summary>
    public static string LastMessage { get; private set; }
    public static float LastMessageTime { get; private set; }

    public static void Toast(string msg, float seconds = 4f)
    {
        toast = msg;
        LastMessage = msg;
        LastMessageTime = Time.unscaledTime;
        toastUntil = Time.unscaledTime + seconds;
    }

    public static void Draw()
    {
        if (broken) return;
        try
        {
            string status = Net.StatusLine;
            bool showToast = toast != null && Time.unscaledTime < toastUntil;
            if (status == null && !showToast) return;

            string text = status ?? "";
            if (showToast) text = status == null ? toast : status + "\n" + toast;
            int lines = text.Split('\n').Length;
            GUI.Box(new Rect(10, 10, 360, 8 + 22 * lines), "");
            GUI.Label(new Rect(18, 13, 350, 22 * lines), text);
        }
        catch (Exception e)
        {
            broken = true;
            Mod.Log.Warning($"Overlay disabled (IMGUI not usable here): {e.GetType().Name}: {e.Message}");
        }
    }
}
