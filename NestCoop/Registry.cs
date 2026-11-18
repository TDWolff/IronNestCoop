using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Stable ID → live component lookup. Built lazily by scanning loaded scenes; cleared on every scene load/unload
/// because objects come and go with missions.
/// </summary>
internal static class Registry
{
    static Dictionary<string, LookAtTarget> buttons;

    public static void Invalidate() => buttons = null;

    public static LookAtTarget FindButton(string id)
    {
        if (buttons == null || !buttons.TryGetValue(id, out var b) || b == null)
        {
            Rebuild();
            buttons.TryGetValue(id, out b);
        }
        return b;
    }

    static void Rebuild()
    {
        buttons = new Dictionary<string, LookAtTarget>();
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<LookAtTarget>()))
        {
            var c = o.TryCast<LookAtTarget>();
            if (c == null || !c.gameObject.scene.IsValid()) continue; // skip prefabs/assets
            var id = Ids.Of(c);
            if (!buttons.TryAdd(id, c)) Mod.Log.Warning($"Registry: duplicate button id {id}");
        }
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Registry: {buttons.Count} buttons");
    }
}
