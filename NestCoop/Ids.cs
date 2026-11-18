using UnityEngine;

namespace NestCoop;

/// <summary>
/// Stable IDs: "&lt;scene&gt;:&lt;name path&gt;", e.g.
/// "Master Turret Scene:Turret/Rotation Console/.Wheel Parent/.Spur Gear 12 DRIVER".
/// Raw sibling indices are NOT stable: Milestone 2 testing saw "Turret/Elevation Console" move from index 14 to 13
/// during play. So we use names, and only when siblings share a name do we append "#n" = the n-th sibling with
/// that name (n ≥ 1). Recon: only the table lamp's two "Universal Button"s and runtime map tokens collide.
/// Never use GetInstanceID() — it differs per machine.
/// </summary>
internal static class Ids
{
    public static string Of(Component c) => c == null ? "<null>" : $"{c.gameObject.scene.name}:{NamePath(c.transform)}";

    public static string NamePath(Transform t)
    {
        var parts = new List<string>();
        for (var cur = t; cur != null; cur = cur.parent)
            parts.Add(cur.name + Occurrence(cur));
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// "" if no earlier sibling shares this name, else "#n".
    static string Occurrence(Transform t)
    {
        var parent = t.parent;
        int n = 0;
        if (parent == null)
        {
            foreach (var root in t.gameObject.scene.GetRootGameObjects())
            {
                if (root.transform == t) break;
                if (root.name == t.name) n++;
            }
        }
        else
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var sib = parent.GetChild(i);
                if (sib == t) break;
                if (sib.name == t.name) n++;
            }
        }
        return n == 0 ? "" : "#" + n;
    }
}

/// <summary>
/// Patch hit logging. The first hit of every patch is always logged (when DebugLog is on) so a patch that
/// never fires (inlined / wrong overload) is obvious: you pressed the thing and saw no "FIRST HIT" line.
/// </summary>
internal static class PatchLog
{
    static readonly HashSet<string> hit = new();

    public static void Hit(string patch, string detail = null)
    {
        if (!Mod.DebugLog.Value) return;
        bool first = hit.Add(patch);
        string origin = Net.ApplyingRemote ? " [remote]" : "";
        if (first) Mod.Log.Msg($"FIRST HIT {patch}{origin} {detail}");
        else if (detail != null) Mod.Log.Msg($"{patch}{origin} {detail}");
    }
}

/// <summary>Placeholder until Milestone 3 adds a transport. Holds the echo-suppression flag.</summary>
internal static class Net
{
    /// <summary>True while we apply a change that came from the network, so patches don't re-send it.</summary>
    public static bool ApplyingRemote;
}
