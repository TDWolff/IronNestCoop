using Il2CppInterop.Runtime;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Stable IDs: "&lt;scene&gt;:&lt;name path&gt;", e.g.
/// "Master Turret Scene:Turret/Rotation Console/.Wheel Parent/.Spur Gear 12 DRIVER".
/// Raw sibling indices are NOT stable: Milestone 2 testing saw "Turret/Elevation Console" move from index 14 to 13
/// during play. So we use names, and only when siblings share a name do we append "#n" = the n-th sibling with
/// that name (n ≥ 1). Recon: only the table lamp's two "Universal Button"s and runtime map tokens collide.
/// If one GameObject has several components of the same type (seen: two LookAtTargets on
/// "Barbet/Campaign 1/InMission Interaction blocker"), the 2nd+ get "~n" (component order is fixed by the prefab).
/// Never use GetInstanceID() — it differs per machine.
/// </summary>
internal static class Ids
{
    public static string Of(Component c)
    {
        if (c == null) return "<null>";
        string id = $"{c.gameObject.scene.name}:{NamePath(c.transform)}";
        int n = SameTypeIndex(c);
        return n == 0 ? id : $"{id}~{n}";
    }

    /// Index of c among components of exactly its type on its GameObject (0 for the first/only one).
    static int SameTypeIndex(Component c)
    {
        var type = c.GetIl2CppType();
        int n = 0;
        foreach (var other in c.GetComponents(type))
        {
            if (other == c) return n;
            n++;
        }
        return 0;
    }

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

    /// <summary>
    /// Reverse of <see cref="Of"/> for transforms: "scene:A/B#1/C" → the Transform (inactive objects included), or null.
    /// A trailing "#n" on a segment means the n-th sibling with that name; a trailing "~n" (component index) is ignored.
    /// </summary>
    public static Transform ResolveTransform(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        int colon = id.IndexOf(':');
        if (colon < 0) return null;
        string sceneName = id[..colon];
        string path = id[(colon + 1)..];
        int tilde = path.LastIndexOf('~');
        if (tilde > path.LastIndexOf('/')) path = path[..tilde];

        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded) return null;
        var segs = path.Split('/');

        var (rootName, rootIdx) = Segment(segs[0]);
        Transform cur = null;
        int seen = 0;
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == rootName && seen++ == rootIdx) { cur = r.transform; break; }

        for (int s = 1; s < segs.Length && cur != null; s++)
        {
            var (name, idx) = Segment(segs[s]);
            Transform next = null;
            int k = 0;
            for (int i = 0; i < cur.childCount; i++)
            {
                var c = cur.GetChild(i);
                if (c.name == name && k++ == idx) { next = c; break; }
            }
            cur = next;
        }
        return cur;
    }

    static (string name, int index) Segment(string seg)
    {
        int hash = seg.LastIndexOf('#');
        if (hash > 0 && int.TryParse(seg[(hash + 1)..], out int n)) return (seg[..hash], n);
        return (seg, 0);
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
