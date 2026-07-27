using System.Runtime.InteropServices;
using ClaudeWatcher.Core;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Turns the dominant fleet state into the tray glyph: a single filled dot in the
/// dominant-urgency color (red &gt; yellow &gt; green). The pixel math is in
/// <see cref="DotGlyph"/> (Core, tested); here we wrap the bytes in a real HICON.
///
/// A WriteableBitmap handed to <c>TaskbarIcon.IconSource</c> renders as a BLANK
/// tray slot (H.NotifyIcon only resolves image sources it can load from a URI), so
/// we build the icon with GDI and push the handle straight to the shell. Verified
/// on Windows 11 26200.
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

    /// <summary>
    /// Build a tray-sized HICON for the dominant state: a hollow ring when idle, a
    /// rotating spark while working, a bold exclamation when an agent needs you.
    /// <paramref name="frame"/> advances the animation. The caller owns the handle and
    /// must <see cref="DestroyIcon"/> it once the shell has a replacement.
    /// </summary>
    public static IntPtr CreateStateIcon(AgentState? dominant, int frame = 0)
    {
        var size = SmallIconSize();
        var bgra = TrayGlyph.Bgra(size, TrayGlyph.For(dominant), HexFor(dominant), frame);
        Premultiply(bgra);

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size,          // negative ⇒ top-down, matching DotGlyph's row order
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,         // BI_RGB
        };

        var color = CreateDIBSection(IntPtr.Zero, ref header, 0 /* DIB_RGB_COLORS */,
                                     out var bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) return IntPtr.Zero;

        // An all-zero mask means "fully opaque"; the 32bpp alpha channel does the
        // real shaping, which is how modern tray icons get antialiased edges.
        var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        try
        {
            Marshal.Copy(bgra, 0, bits, bgra.Length);
            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            DeleteObject(color);
            DeleteObject(mask);
        }
    }

    /// <summary>
    /// The shell composites tray icons with AC_SRC_ALPHA, which expects
    /// premultiplied channels. <see cref="DotGlyph"/> emits straight alpha (it's
    /// pure, portable math), so scale the color channels here — without this the
    /// dot's antialiased rim picks up a bright halo.
    /// </summary>
    private static void Premultiply(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255) continue;
            bgra[i + 0] = (byte)(bgra[i + 0] * a / 255);
            bgra[i + 1] = (byte)(bgra[i + 1] * a / 255);
            bgra[i + 2] = (byte)(bgra[i + 2] * a / 255);
        }
    }

    /// <summary>DPI-scaled small-icon size (16px at 100%), so the dot stays crisp on HiDPI.</summary>
    private static int SmallIconSize()
    {
        var px = GetSystemMetrics(SM_CXSMICON);
        return px is >= 8 and <= 256 ? px : 16;
    }

    private const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
                                                  out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>Release an icon created by <see cref="CreateDotIcon"/>.</summary>
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
