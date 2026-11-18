using System.Text;
using MelonLoader.Utils;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Recon for the game-style menu / in-world terminal: dump selected object subtrees with every component type,
/// active state, local position/scale and any text, to UserData/NestCoop/recon/ui-dump.txt. Triggered by UiDumpKey.
/// Roots come from the Milestone 1 recon (the main-menu clipboard canvas, the options clipboard, and a physical
/// "Universal Button" to clone for a terminal).
/// </summary>
internal static class UiDump
{
    static readonly string[] Roots =
    {
        "Barbet/ - Main Menu - /MainMenu Interactable objects/Clipboard Menu",
        "Main Camera/Options Menu Parent/Clipboard Main Menu",
        "- Lighting ------------------/Table Lamp",
        "Requisition Console",
    };

    const int MaxDepth = 9, MaxNodes = 1500;
    static int nodes;

    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# UI dump {DateTime.Now:yyyy-MM-dd HH:mm:ss}  screen {Screen.width}x{Screen.height}");
        nodes = 0;
        foreach (var path in Roots)
        {
            var t = FindByPath(path);
            sb.AppendLine().AppendLine($"## {path}  {(t == null ? "NOT FOUND" : "")}");
            if (t != null) Walk(sb, t, 0);
        }
        var dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "NestCoop", "recon");
        Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "ui-dump.txt");
        File.WriteAllText(file, sb.ToString());
        Mod.Log.Msg($"UI dump: {nodes} objects -> {file}");
        Overlay.Toast($"UI dump written ({nodes} objects)");
    }

    /// <summary>Find "Root/Child/..." among root objects of all loaded scenes (including inactive children).</summary>
    static Transform FindByPath(string path)
    {
        var parts = path.Split('/');
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                var cur = root.transform;
                for (int i = 1; i < parts.Length && cur != null; i++)
                {
                    Transform next = null;
                    for (int c = 0; c < cur.childCount; c++)
                        if (cur.GetChild(c).name == parts[i]) { next = cur.GetChild(c); break; }
                    cur = next;
                }
                if (cur != null) return cur;
            }
        }
        return null;
    }

    static void Walk(StringBuilder sb, Transform t, int depth)
    {
        if (++nodes > MaxNodes) return;
        string indent = new string(' ', depth * 2);
        var go = t.gameObject;
        var comps = new List<string>();
        string text = null;
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            string n = c.GetIl2CppType().Name;
            if (n == "Transform") continue;
            comps.Add(n);
            text ??= TryText(c);
        }
        sb.AppendLine($"{indent}- {go.name}{(go.activeSelf ? "" : " (inactive)")}  pos {t.localPosition} scale {t.localScale}  [{string.Join(", ", comps)}]{(text != null ? $"  text=\"{text}\"" : "")}");
        if (depth >= MaxDepth) { if (t.childCount > 0) sb.AppendLine($"{indent}  … {t.childCount} children not shown"); return; }
        for (int i = 0; i < t.childCount; i++) Walk(sb, t.GetChild(i), depth + 1);
    }

    /// <summary>Read a "text" property via IL2CPP reflection (TextMeshPro, legacy Text) without referencing TMP.</summary>
    static string TryText(Component c)
    {
        try
        {
            var p = c.GetIl2CppType().GetProperty("text");
            if (p == null) return null;
            var v = p.GetValue(c)?.ToString();
            if (string.IsNullOrEmpty(v)) return null;
            v = v.Replace("\n", "\\n");
            return v.Length > 60 ? v[..60] + "…" : v;
        }
        catch { return null; }
    }
}
