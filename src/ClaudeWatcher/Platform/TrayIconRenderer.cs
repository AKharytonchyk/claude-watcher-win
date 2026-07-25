using System.Runtime.InteropServices.WindowsRuntime;
using ClaudeWatcher.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Turns the dominant fleet state into the tray glyph: a single filled dot in the
/// dominant-urgency color (red &gt; yellow &gt; green). The pixel math is in
/// <see cref="DotGlyph"/> (Core, tested); here we only wrap the bytes in a
/// <see cref="WriteableBitmap"/> for H.NotifyIcon's <c>IconSource</c>.
///
/// UNVERIFIED (Windows-only): confirm H.NotifyIcon accepts a WriteableBitmap
/// IconSource, and that BGRA (vs. premultiplied) is correct for the tray.
/// </summary>
public static class TrayIconRenderer
{
    /// <summary>Semantic state color → tray dot hex.</summary>
    public static string HexFor(AgentState? dominant) => dominant?.Color() switch
    {
        StateColor.Red    => "#E5484D",  // needs you
        StateColor.Yellow => "#F5A623",  // working
        StateColor.Green  => "#30A46C",  // idle
        _                 => "#8B8B8B",  // no agents
    };

    public static WriteableBitmap DotBitmap(AgentState? dominant, int size = 32)
    {
        var bgra = DotGlyph.Bgra(size, HexFor(dominant));
        var bmp = new WriteableBitmap(size, size);
        using var stream = bmp.PixelBuffer.AsStream();
        stream.Write(bgra, 0, bgra.Length);
        return bmp;
    }
}
