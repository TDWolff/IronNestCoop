using System.Text;
using MelonLoader.Utils;

namespace NestCoop;

/// <summary>
/// Automatic log copies (0.9.5). MelonLoader overwrites MelonLoader/Latest.log every launch and keeps only the last
/// few launches in MelonLoader/Logs (max_logs = 10 in UserData/Loader.cfg), so a co-op test's log was easy to lose.
/// Whenever a co-op session ends (leave, disconnect, or quitting during a session) the current log is copied to
///   UserData/NestCoop/logs/&lt;yyyy-MM-dd_HH-mm-ss&gt;_&lt;host|guest&gt;_v&lt;version&gt;.log
/// with a short header (NestCoop version + version key, transport, role, players, session length). The newest
/// <see cref="Keep"/> copies are kept. Paths come from MelonEnvironment (MelonLoaderDirectory, UserDataDirectory).
/// </summary>
internal static class LogSaver
{
    const int Keep = 20;
    static DateTime sessionStart;
    static string role = "", players = "";

    public static string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "NestCoop", "logs");

    /// <summary>Net: a session started (host or guest).</summary>
    public static void OnSessionStarted(Role r)
    {
        sessionStart = DateTime.Now;
        role = r.ToString().ToLowerInvariant();
        players = "";
    }

    /// <summary>Net: remember who was there, for the header (called before the session state is cleared).</summary>
    public static void NotePlayers(IEnumerable<(int id, string name)> peers) =>
        players = string.Join(", ", peers.Select(p => p.name));

    /// <summary>Net: the session ended — save a copy of the log.</summary>
    public static void OnSessionEnded(string why)
    {
        if (string.IsNullOrEmpty(role)) return;
        try { Save(why); }
        catch (Exception e) { Mod.Log.Warning($"LogSaver: couldn't save the log: {e.Message}"); }
        role = "";
    }

    static void Save(string why)
    {
        string src = Path.Combine(MelonEnvironment.MelonLoaderDirectory, "Latest.log");
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(Folder);

        // MelonLoader still has Latest.log open for writing: read it with shared access.
        string text;
        using (var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs, Encoding.UTF8)) text = sr.ReadToEnd();

        var header = new StringBuilder();
        header.AppendLine("==== NestCoop session log ====");
        header.AppendLine($"Saved:      {DateTime.Now:yyyy-MM-dd HH:mm:ss}  (session ended: {why})");
        header.AppendLine($"NestCoop:   {Mod.Version}   key {VersionCheck.Key}");
        header.AppendLine($"Role:       {role}   transport {Mod.TransportName.Value}");
        header.AppendLine($"Players:    {(players.Length > 0 ? players : "(none connected at the end)")}");
        header.AppendLine($"Session:    {sessionStart:HH:mm:ss} – {DateTime.Now:HH:mm:ss} ({(DateTime.Now - sessionStart).TotalMinutes:0.#} min)");
        header.AppendLine("==== MelonLoader Latest.log ====");

        string name = $"{sessionStart:yyyy-MM-dd_HH-mm-ss}_{role}_v{Mod.Version}.log";
        string dst = Path.Combine(Folder, name);
        File.WriteAllText(dst, header + text, new UTF8Encoding(false));
        Mod.Log.Msg($"LogSaver: session log saved to {dst}");
        Overlay.Toast($"Session log saved: UserData/NestCoop/logs/{name}", 6f);

        // Keep the newest few.
        var old = new DirectoryInfo(Folder).GetFiles("*.log").OrderByDescending(f => f.CreationTimeUtc).Skip(Keep);
        foreach (var f in old) { try { f.Delete(); } catch { } }
    }
}
