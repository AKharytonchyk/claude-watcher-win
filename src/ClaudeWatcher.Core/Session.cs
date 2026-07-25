using System.Text.Json.Serialization;

namespace ClaudeWatcher.Core;

/// <summary>
/// One entry from ~/.claude/sessions/&lt;pid&gt;.json — a live Claude Code session.
/// Mirror of the macOS <c>Session</c> struct; the JSON contract is in SCHEMA.md.
/// </summary>
public sealed record Session
{
    [JsonPropertyName("pid")]       public int Pid { get; init; }
    [JsonPropertyName("sessionId")] public string SessionId { get; init; } = "";
    [JsonPropertyName("cwd")]       public string Cwd { get; init; } = "";
    [JsonPropertyName("name")]      public string? Name { get; init; }
    [JsonPropertyName("version")]   public string? Version { get; init; }
    [JsonPropertyName("status")]    public string? Status { get; init; }          // "busy" | "idle" | "waiting"
    [JsonPropertyName("waitingFor")] public string? WaitingFor { get; init; }      // e.g. "permission prompt"
    [JsonPropertyName("kind")]      public string? Kind { get; init; }
    [JsonPropertyName("startedAt")] public double? StartedAt { get; init; }        // epoch ms
    [JsonPropertyName("updatedAt")] public double? UpdatedAt { get; init; }        // epoch ms
    [JsonPropertyName("statusUpdatedAt")] public double? StatusUpdatedAt { get; init; } // epoch ms
}

public static class SessionExtensions
{
    /// <summary>Project folder name derived from the working directory.</summary>
    public static string ProjectName(this Session s) =>
        Path.GetFileName(s.Cwd.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : s.Cwd;

    public static string DisplayName(this Session s) => s.Name ?? s.ProjectName();

    public static bool IsBusy(this Session s) => s.Status == "busy";

    /// <summary>When the status last changed, if known.</summary>
    public static DateTimeOffset? StatusDate(this Session s)
    {
        var ms = s.StatusUpdatedAt ?? s.UpdatedAt;
        return ms is null ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)ms.Value);
    }

    /// <summary>When the process started, if known.</summary>
    public static DateTimeOffset? StartedDate(this Session s) =>
        s.StartedAt is null ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)s.StartedAt.Value);
}

/// <summary>
/// Compact elapsed time since <paramref name="from"/>: "&lt;1m", "4m", "2h 3m", "1d 4h".
/// </summary>
public static class Elapsed
{
    public static string Short(DateTimeOffset? from, DateTimeOffset? now = null)
    {
        if (from is null) return "";
        var reference = now ?? DateTimeOffset.Now;
        var seconds = Math.Max(0, (reference - from.Value).TotalSeconds);
        var minutes = (int)(seconds / 60);
        if (minutes < 1) return "<1m";
        if (minutes < 60) return $"{minutes}m";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h {minutes % 60}m";
        var days = hours / 24;
        return $"{days}d {hours % 24}h";
    }
}
