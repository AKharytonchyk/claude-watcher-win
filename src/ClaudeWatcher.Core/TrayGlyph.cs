namespace ClaudeWatcher.Core;

/// <summary>What the tray glyph draws. One shape per state, chosen to survive 16x16.</summary>
public enum TrayShape
{
    /// <summary>Filled disc — the fallback when there are no agents.</summary>
    Dot,
    /// <summary>Hollow ring — at rest. Reads as "not doing anything" without needing letters.</summary>
    Ring,
    /// <summary>Four-spoke spark, rotated per frame — working.</summary>
    Spark,
    /// <summary>Filled disc with a knocked-out exclamation mark — needs you.</summary>
    Alert,
}

/// <summary>
/// The tray glyph as a raw BGRA8 buffer — pure math, no GDI, so it stays testable and
/// builds anywhere.
///
/// Everything here is shaped by one constraint: a tray icon is 16x16 at 100% scaling.
/// At that size large blocks of colour and a single bold mark read clearly, while fine
/// detail turns to mush — lettering ("zzz"), texture ("matrix rain") and counting dots
/// were all tried and were illegible. Motion survives where detail does not, which is
/// why "working" is a rotating shape rather than a busier one.
/// </summary>
public static class TrayGlyph
{
    /// <summary>Frames in one full animation cycle.</summary>
    public const int Frames = 4;

    /// <summary>A spark has 8 arms, so it repeats every 45°; a cycle covers exactly that.</summary>
    private const double SparkPeriod = Math.PI / 4;

    /// <summary>BGRA8 pixels (row-major, top-down), length <c>size*size*4</c>.</summary>
    public static byte[] Bgra(int size, TrayShape shape, byte r, byte g, byte b, int frame = 0)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

        if (shape == TrayShape.Dot) return DotGlyph.Bgra(size, r, g, b);

        var px = new byte[size * size * 4];
        var centre = (size - 1) / 2.0;
        var radius = centre - size * 0.06;      // a hair of breathing room inside the cell

        // Rotate through one period across the cycle, so the spin looks continuous.
        var turn = SparkPeriod * (((frame % Frames) + Frames) % Frames) / Frames;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            double dx = x - centre, dy = y - centre;

            var (alpha, mark) = shape switch
            {
                TrayShape.Ring  => (Ring(dx, dy, radius, size), 0.0),
                TrayShape.Spark => (Spark(dx, dy, radius, size, turn), 0.0),
                _               => (Disc(dx, dy, radius), Exclamation(dx, dy, size)),
            };

            // The mark is knocked out in white over the state colour.
            var i = (y * size + x) * 4;
            px[i + 0] = Mix(b, mark);
            px[i + 1] = Mix(g, mark);
            px[i + 2] = Mix(r, mark);
            px[i + 3] = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        }
        return px;
    }

    /// <summary>Convenience overload from a hex string like "#E5484D".</summary>
    public static byte[] Bgra(int size, TrayShape shape, string hex, int frame = 0)
    {
        var (r, g, b) = DotGlyph.ParseHex(hex);
        return Bgra(size, shape, r, g, b, frame);
    }

    /// <summary>Which shape represents a state. Null (no agents) is a plain dot.</summary>
    public static TrayShape For(AgentState? state) => state switch
    {
        AgentState.Waiting => TrayShape.Alert,
        AgentState.Working => TrayShape.Spark,
        AgentState.Idle    => TrayShape.Ring,
        _                  => TrayShape.Dot,
    };

    private static byte Mix(byte channel, double towardsWhite) =>
        (byte)Math.Round(channel + (255 - channel) * Math.Clamp(towardsWhite, 0, 1));

    /// <summary>Coverage of a filled disc, with a ~1px soft edge.</summary>
    private static double Disc(double dx, double dy, double radius) =>
        Math.Clamp(radius + 0.5 - Math.Sqrt(dx * dx + dy * dy), 0, 1);

    /// <summary>Coverage of an annulus — thick enough that the hole survives at 16px.</summary>
    private static double Ring(double dx, double dy, double radius, int size)
    {
        var thickness = Math.Max(2.0, size / 5.5);
        var mid = radius - thickness / 2;
        var dist = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - mid);
        return Math.Clamp(thickness / 2 + 0.5 - dist, 0, 1);
    }

    /// <summary>
    /// Coverage of four rounded bars through the centre, 45° apart and rotated by
    /// <paramref name="turn"/>. Distance-to-segment gives the round caps for free.
    /// </summary>
    private static double Spark(double dx, double dy, double radius, int size, double turn)
    {
        var half = Math.Max(1.0, size / 7.0) / 2;
        var nearest = double.MaxValue;

        for (var i = 0; i < 4; i++)
        {
            var a = turn + i * SparkPeriod;
            double ex = Math.Cos(a) * (radius - half), ey = Math.Sin(a) * (radius - half);
            nearest = Math.Min(nearest, DistanceToSegment(dx, dy, -ex, -ey, ex, ey));
        }
        return Math.Clamp(half + 0.5 - nearest, 0, 1);
    }

    /// <summary>
    /// Coverage of an exclamation mark: a tapering bar with a dot beneath. Drawn as
    /// geometry because Core has no text rendering — and at this size a font would be
    /// hinted into mush anyway.
    /// </summary>
    private static double Exclamation(double dx, double dy, int size)
    {
        var halfWidth = Math.Max(0.9, size / 14.0);
        var barTop = -size * 0.30;
        var barBottom = size * 0.08;
        var dotCentre = size * 0.24;
        var dotRadius = Math.Max(1.0, size / 12.0);

        var bar = dy >= barTop && dy <= barBottom
            ? Math.Clamp(halfWidth + 0.5 - Math.Abs(dx), 0, 1)
            : 0;

        var dot = Math.Clamp(dotRadius + 0.5 - Math.Sqrt(dx * dx + (dy - dotCentre) * (dy - dotCentre)), 0, 1);

        return Math.Max(bar, dot);
    }

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        double vx = x2 - x1, vy = y2 - y1;
        var lengthSquared = vx * vx + vy * vy;
        if (lengthSquared <= double.Epsilon) return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));

        var t = Math.Clamp(((px - x1) * vx + (py - y1) * vy) / lengthSquared, 0, 1);
        double cx = x1 + t * vx, cy = y1 + t * vy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
