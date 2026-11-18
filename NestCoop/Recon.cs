using System.Text;
using Il2CppInterop.Runtime;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.Events;
using UObject = UnityEngine.Object;

namespace NestCoop;

/// <summary>
/// Recon helper (Milestone 1). After each scene loads, lists every input component we care about in that
/// scene: hierarchy path, active state, current value, and the persistent (Inspector-wired) UnityEvent
/// listeners, i.e. what the control drives. Listeners added from code at runtime are NOT visible here.
/// Output: UserData/NestCoop/recon/&lt;scene&gt;.txt in the r2modman profile.
/// Class names below all come from the Assembly-CSharp dump (build 26352746); see docs/game-map.md.
/// </summary>
internal static class Recon
{
    public static void DumpScene(string sceneName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Recon for scene '{sceneName}'  ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
        int total = 0;

        total += Section<Il2Cpp.DialInteractable>(sb, sceneName, d =>
            $"value={d.AccumulatedValue:0.###} angle={d.currentRotationAngle:0.#} range=[{d.minOutputValue}..{d.maxOutputValue}]",
            d => new (string, UnityEventBase)[] { ("OnValueChanged", d.OnValueChanged), ("OnGrab", d.OnGrab), ("OnRelease", d.OnRelease) });

        total += Section<Il2Cpp.LinearSliderInteractable>(sb, sceneName, s =>
            $"value={s.Value:0.###} range=[{s.minOutputValue}..{s.maxOutputValue}]",
            s => new (string, UnityEventBase)[] { ("OnValueChanged", s.OnValueChanged), ("OnGrab", s.OnGrab), ("OnRelease", s.OnRelease) });

        total += Section<Il2Cpp.ClickEvent>(sb, sceneName, _ => "",
            c => new (string, UnityEventBase)[] { ("m_OnClick", c.m_OnClick) });

        // Physical buttons ("Universal Button ...") are plain Interactables; the script that reacts to the click
        // is a sibling component. List the siblings and every UnityEvent field on them (found via IL2CPP reflection).
        total += Section<Il2Cpp.Interactable>(sb, sceneName, i =>
            $"interactable={i.IsInteractable} passive={i.IsPassive} comps=[{SiblingTypes(i)}]",
            i => SiblingEvents(i));

        // System controllers: just where they live (for stable IDs / ownership).
        total += Section<Il2Cpp.TurretController>(sb, sceneName, _ => "", _ => System.Array.Empty<(string, UnityEventBase)>());
        total += Section<Il2Cpp.GunController>(sb, sceneName, _ => "", _ => System.Array.Empty<(string, UnityEventBase)>());
        total += Section<Il2Cpp.ArtilleryReloadController>(sb, sceneName, r => $"state={r.CurrentStateIndex}", _ => System.Array.Empty<(string, UnityEventBase)>());
        total += Section<Il2Cpp.ArtilleryComputer>(sb, sceneName, _ => "", _ => System.Array.Empty<(string, UnityEventBase)>());
        total += Section<Il2Cpp.MissionManager>(sb, sceneName, _ => "", _ => System.Array.Empty<(string, UnityEventBase)>());
        total += Section<Il2Cpp.FirstPersonController>(sb, sceneName, _ => "", _ => System.Array.Empty<(string, UnityEventBase)>());

        if (total == 0) return;

        var dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "NestCoop", "recon");
        Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, Safe(sceneName) + ".txt");
        File.WriteAllText(file, sb.ToString());
        Mod.Log.Msg($"Recon: {total} components in '{sceneName}' -> {file}");
    }

    static int Section<T>(StringBuilder sb, string sceneName, Func<T, string> describe,
        Func<T, (string name, UnityEventBase ev)[]> events) where T : Component
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>());
        var found = new List<T>();
        foreach (var o in all)
        {
            var c = o.TryCast<T>();
            if (c == null) continue;
            var scene = c.gameObject.scene;
            if (!scene.IsValid() || scene.name != sceneName) continue; // skips prefabs/assets
            found.Add(c);
        }
        if (found.Count == 0) return 0;

        sb.AppendLine().AppendLine($"## {typeof(T).Name} ({found.Count})");
        var seen = new Dictionary<string, int>();
        foreach (var c in found.OrderBy(c => Path(c.transform)))
        {
            string path = Path(c.transform);
            seen[path] = seen.TryGetValue(path, out var n) ? n + 1 : 1;
            string dup = seen[path] > 1 ? "  !!DUPLICATE PATH" : "";
            string active = c.gameObject.activeInHierarchy ? "" : "  (inactive)";
            string desc;
            try { desc = describe(c); } catch (Exception e) { desc = "describe failed: " + e.GetType().Name; }
            sb.AppendLine($"- {path}{active}{dup}  {desc}");

            (string name, UnityEventBase ev)[] evs;
            try { evs = events(c); } catch (Exception e) { sb.AppendLine($"    events failed: {e.Message}"); continue; }
            foreach (var (name, ev) in evs)
            {
                if (ev == null) continue;
                int count = ev.GetPersistentEventCount();
                for (int i = 0; i < count; i++)
                {
                    var target = ev.GetPersistentTarget(i);
                    string tdesc = target == null ? "null"
                        : $"{target.GetIl2CppType().Name} on '{TargetPath(target)}'";
                    sb.AppendLine($"    {name} -> {tdesc}.{ev.GetPersistentMethodName(i)}()");
                }
            }
        }
        return found.Count;
    }

    static readonly HashSet<string> BoringComponents = new()
    {
        "Transform", "MeshFilter", "MeshRenderer", "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider",
        "Rigidbody", "Animator", "Interactable", "StudioEventEmitter", "LODGroup", "SkinnedMeshRenderer",
    };

    static string SiblingTypes(Component c)
    {
        var names = new List<string>();
        foreach (var comp in c.GetComponents<Component>())
        {
            if (comp == null) continue;
            var n = comp.GetIl2CppType().Name;
            if (!BoringComponents.Contains(n)) names.Add(n);
        }
        return string.Join(", ", names);
    }

    static (string, UnityEventBase)[] SiblingEvents(Component c)
    {
        var result = new List<(string, UnityEventBase)>();
        var flags = Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.Public
                  | Il2CppSystem.Reflection.BindingFlags.NonPublic;
        var baseType = Il2CppType.Of<UnityEventBase>();
        foreach (var comp in c.GetComponents<Component>())
        {
            if (comp == null) continue;
            var t = comp.GetIl2CppType();
            if (BoringComponents.Contains(t.Name) || (t.Namespace ?? "").StartsWith("UnityEngine")) continue;
            foreach (var f in t.GetFields(flags))
            {
                if (!baseType.IsAssignableFrom(f.FieldType)) continue;
                var ev = f.GetValue(comp)?.TryCast<UnityEventBase>();
                if (ev != null) result.Add(($"{t.Name}.{f.Name}", ev));
            }
        }
        return result.ToArray();
    }

    static string TargetPath(UObject target)
    {
        var comp = target.TryCast<Component>();
        if (comp != null) return Path(comp.transform);
        var go = target.TryCast<GameObject>();
        return go != null ? Path(go.transform) : target.name;
    }

    /// <summary>Hierarchy path with sibling index, e.g. "Turret[0]/Wheel[3]". Candidate stable ID.</summary>
    public static string Path(Transform t)
    {
        var parts = new List<string>();
        for (var cur = t; cur != null; cur = cur.parent)
            parts.Add($"{cur.name}[{cur.GetSiblingIndex()}]");
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string Safe(string s)
    {
        foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s;
    }
}
