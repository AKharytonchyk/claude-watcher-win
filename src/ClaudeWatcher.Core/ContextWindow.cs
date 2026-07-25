using System.Globalization;

namespace ClaudeWatcher.Core;

/// <summary>
/// Context-window inference and model/token formatting. Port of the macOS
/// context-window helpers in <c>Status.swift</c>.
/// </summary>
public static class ContextWindow
{
    /// <summary>
    /// Best-effort context window for a session. Claude Code doesn't record the
    /// window, so infer it from the model. Opus 4.x offers a 1M window; those
    /// sessions are measured against 1M. Other/unknown models fall back to the
    /// observed-size heuristic (200K until usage proves larger). Override with
    /// CWATCH_CONTEXT_WINDOW (e.g. "1m" or "1000000").
    /// </summary>
    public static int For(int observedTokens, string? model)
    {
        var raw = Environment.GetEnvironmentVariable("CWATCH_CONTEXT_WINDOW")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(raw))
        {
            if (raw.EndsWith('m') &&
                double.TryParse(raw.AsSpan(0, raw.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                return (int)(m * 1_000_000);
            if (int.TryParse(raw, out var n)) return n;
        }
        if (SupportsMillionTokenWindow(model)) return 1_000_000;
        return observedTokens > 200_000 ? 1_000_000 : 200_000;
    }

    /// <summary>
    /// Models offering a 1M-token window (Opus 4.x today). Matched on the family
    /// so point releases and the "[1m]" variant keep working.
    /// </summary>
    public static bool SupportsMillionTokenWindow(string? model) =>
        model?.ToLowerInvariant().Contains("opus-4") ?? false;

    /// <summary>
    /// Human-readable model name from an API id:
    /// "claude-opus-4-8" → "Opus 4.8", "claude-haiku-4-5-20251001" → "Haiku 4.5",
    /// "claude-opus-4-8[1m]" → "Opus 4.8". Unknown shapes pass through unchanged.
    /// </summary>
    public static string? HumanModel(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var s = raw.ToLowerInvariant();
        var bracket = s.IndexOf('[');
        if (bracket >= 0) s = s[..bracket];                       // drop "[1m]" etc.
        s = s.Replace("claude-", "");
        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries)
                     .Where(p => !(p.Length == 8 && int.TryParse(p, out _)))  // drop yyyymmdd
                     .ToArray();
        var words = parts.Where(p => !int.TryParse(p, out _)).ToArray();      // e.g. ["opus"]
        var version = string.Join(".", parts.Where(p => int.TryParse(p, out _))); // e.g. "4.8"
        if (words.Length == 0) return raw;                        // all-numeric: unusual
        var name = string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        return string.IsNullOrEmpty(version) ? name : $"{name} {version}";
    }

    /// <summary>Compact token count: 142000 → "142K", 1000000 → "1M".</summary>
    public static string FormatTokens(int n)
    {
        if (n >= 1_000_000)
        {
            var m = n / 1_000_000.0;
            return m == Math.Round(m) ? $"{(int)m}M" : m.ToString("0.0", CultureInfo.InvariantCulture) + "M";
        }
        if (n >= 1_000) return $"{(int)Math.Round(n / 1_000.0)}K";
        return n.ToString(CultureInfo.InvariantCulture);
    }
}
