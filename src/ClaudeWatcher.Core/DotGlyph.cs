namespace ClaudeWatcher.Core;

/// <summary>
/// Renders the tray "traffic-light" dot as a raw BGRA8 pixel buffer — pure math,
/// no GDI/WinUI — so the Platform layer just wraps the bytes in an icon. A single
/// antialiased filled circle in the dominant-state color; transparent outside.
/// </summary>
public static class DotGlyph
{
    /// <summary>
    /// BGRA8 pixels (row-major, top-down), length <c>size*size*4</c>. <paramref name="inset"/>
    /// is the margin as a fraction of size so the dot doesn't touch the edges.
    /// </summary>
    public static byte[] Bgra(int size, byte r, byte g, byte b, double inset = 0.14)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var px = new byte[size * size * 4];
        var center = (size - 1) / 2.0;
        var radius = center - size * inset;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            double dx = x - center, dy = y - center;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var alpha = Math.Clamp(radius + 0.5 - dist, 0, 1); // ~1px soft edge
            var i = (y * size + x) * 4;
            px[i + 0] = b;
            px[i + 1] = g;
            px[i + 2] = r;
            px[i + 3] = (byte)Math.Round(alpha * 255);
        }
        return px;
    }

    /// <summary>Convenience overload from a hex string like "#E5484D".</summary>
    public static byte[] Bgra(int size, string hex, double inset = 0.14)
    {
        var (r, g, b) = ParseHex(hex);
        return Bgra(size, r, g, b, inset);
    }

    public static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6) throw new FormatException($"expected #RRGGBB, got '{hex}'");
        return (Convert.ToByte(s[..2], 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16));
    }
}
