using UnityEngine;

namespace NestCoop;

/// <summary>
/// Milestone 9: version check. Both players must run the same NestCoop version AND the same game build (object names,
/// paths and methods we rely on can change between game updates).
///
/// The connection key is "NestCoop|&lt;mod version&gt;|&lt;game version&gt;/&lt;first 8 of buildGUID&gt;", e.g.
/// "NestCoop|0.8.0|1.0/1a2b3c4d". The host accepts only an identical key; for a different NestCoop key it rejects and
/// sends its own key back, so the guest can say exactly what differs. (Unity: Application.version, Application.buildGUID.)
/// </summary>
internal static class VersionCheck
{
    public const string Prefix = "NestCoop|";

    static string key;
    public static string Key => key ??= $"{Prefix}{Mod.Version}|{GameBuild}";

    public static string GameBuild
    {
        get
        {
            string v = Application.version ?? "?";
            string g = Application.buildGUID ?? "";
            return g.Length >= 8 ? $"{v}/{g[..8]}" : v;
        }
    }

    /// <summary>Reject payload meaning "session is full" (sent instead of the host key).</summary>
    public const string Full = "NestCoop|FULL";

    public static string ExplainReject(string payload) => payload == Full
        ? "The session is full (host's MaxPlayers)."
        : Explain(payload);

    public static bool LooksLikeNestCoop(string k) => k != null && (k.StartsWith(Prefix) || k.StartsWith("NestCoop-"));

    /// <summary>Human-readable explanation of why <paramref name="hostKey"/> and ours don't match.</summary>
    public static string Explain(string hostKey)
    {
        var (hm, hg) = Parse(hostKey);
        var (mm, mg) = Parse(Key);
        var parts = new List<string>();
        if (hm != mm) parts.Add($"NestCoop: host has {hm}, you have {mm}");
        if (hg != mg) parts.Add($"game build: host has {hg}, you have {mg}");
        if (parts.Count == 0) parts.Add($"host key '{hostKey}', yours '{Key}'");
        return "Version mismatch — " + string.Join("; ", parts) + ". Both players need the same versions.";
    }

    static (string mod, string game) Parse(string k)
    {
        if (string.IsNullOrEmpty(k)) return ("?", "?");
        if (k.StartsWith("NestCoop-")) return (k["NestCoop-".Length..], "unknown (older NestCoop)");
        var p = k.Split('|');
        return (p.Length > 1 ? p[1] : "?", p.Length > 2 ? p[2] : "?");
    }
}
