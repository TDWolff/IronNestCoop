using Il2CppInterop.Runtime;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Stable ID → live component lookup, one cache per component type. Built lazily by scanning loaded scenes;
/// cleared on every scene load/unload because objects come and go with missions.
/// </summary>
internal static class Registry
{
    static readonly Dictionary<Type, Dictionary<string, Component>> caches = new();

    public static void Invalidate() => caches.Clear();

    public static T Find<T>(string id) where T : Component
    {
        if (!caches.TryGetValue(typeof(T), out var map) || !map.TryGetValue(id, out var c) || c == null)
        {
            map = Rebuild<T>();
            map.TryGetValue(id, out c);
        }
        return c?.TryCast<T>();
    }

    /// <summary>All live instances of T in loaded scenes (prefabs/assets excluded).</summary>
    public static IEnumerable<T> All<T>() where T : Component
    {
        if (!caches.TryGetValue(typeof(T), out var map)) map = Rebuild<T>();
        foreach (var c in map.Values)
            if (c != null) yield return c.TryCast<T>();
    }

    static Dictionary<string, Component> Rebuild<T>() where T : Component
    {
        var map = new Dictionary<string, Component>();
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
        {
            var c = o.TryCast<T>();
            if (c == null || !c.gameObject.scene.IsValid()) continue; // skip prefabs/assets
            var id = Ids.Of(c);
            if (!map.TryAdd(id, c)) Mod.Log.Warning($"Registry: duplicate {typeof(T).Name} id {id}");
        }
        caches[typeof(T)] = map;
        if (Mod.DebugLog.Value) Mod.Log.Msg($"Registry: {map.Count} {typeof(T).Name}");
        return map;
    }
}
