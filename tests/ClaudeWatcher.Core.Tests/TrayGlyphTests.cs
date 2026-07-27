using ClaudeWatcher.Core;
using Xunit;

namespace ClaudeWatcher.Core.Tests;

/// <summary>
/// The glyphs only work because of specific geometry — a ring that is actually hollow,
/// an alert whose mark is actually visible, a spark that actually moves. Each of those
/// can regress into an indistinct blob without anything failing to compile.
/// </summary>
public class TrayGlyphTests
{
    private const int Size = 16;   // the hard case: 100% scaling

    private static byte Alpha(byte[] px, int size, int x, int y) => px[(y * size + x) * 4 + 3];

    private static int Opaque(byte[] px) =>
        Enumerable.Range(0, px.Length / 4).Count(i => px[i * 4 + 3] > 127);

    [Theory]
    [InlineData(TrayShape.Dot)]
    [InlineData(TrayShape.Ring)]
    [InlineData(TrayShape.Spark)]
    [InlineData(TrayShape.Alert)]
    public void Buffer_is_the_expected_size_and_has_visible_pixels(TrayShape shape)
    {
        var px = TrayGlyph.Bgra(Size, shape, "#30A46C");

        Assert.Equal(Size * Size * 4, px.Length);
        Assert.InRange(Opaque(px), 12, Size * Size);   // something is drawn, but not everything
    }

    [Fact]
    public void Ring_is_hollow_at_the_centre_and_solid_on_the_band()
    {
        var px = TrayGlyph.Bgra(Size, TrayShape.Ring, "#30A46C");

        // The hole is the whole point: filled in, it is indistinguishable from a dot.
        Assert.Equal(0, Alpha(px, Size, Size / 2, Size / 2));
        Assert.True(Alpha(px, Size, Size / 2, 1) > 127, "top of the band should be drawn");
    }

    [Fact]
    public void Dot_is_solid_at_the_centre_so_it_reads_differently_from_the_ring()
    {
        var dot = TrayGlyph.Bgra(Size, TrayShape.Dot, "#8B8B8B");
        var ring = TrayGlyph.Bgra(Size, TrayShape.Ring, "#8B8B8B");
        var mid = Size / 2;

        // Filled versus hollow at the centre is the whole distinction. Pixel *counts*
        // are not a useful invariant here — the ring is drawn on a wider radius, so a
        // thick band can legitimately cover more pixels than the smaller disc.
        Assert.True(Alpha(dot, Size, mid, mid) > 200, "the dot must be solid");
        Assert.Equal(0, Alpha(ring, Size, mid, mid));
    }

    [Fact]
    public void Alert_is_a_filled_disc_with_a_white_mark_knocked_out()
    {
        var px = TrayGlyph.Bgra(Size, TrayShape.Alert, "#E5484D");
        var mid = Size / 2;

        // Opaque across the disc...
        Assert.True(Alpha(px, Size, mid, mid) > 200);

        // ...and the bar runs down the middle in white, well above the centre.
        var barY = (int)(Size * 0.30);
        var i = (barY * Size + mid) * 4;
        Assert.True(px[i + 0] > 200 && px[i + 1] > 200 && px[i + 2] > 200,
                    "the exclamation bar should be near-white against the red");

        // The disc edge stays the state colour, so it still reads as red at a glance.
        var edge = (mid * Size + 1) * 4;
        Assert.True(px[edge + 1] < 160, "the rim must not be washed out to white");
    }

    [Fact]
    public void Spark_rotates_between_frames_but_returns_after_a_full_cycle()
    {
        var frames = Enumerable.Range(0, TrayGlyph.Frames)
                               .Select(f => TrayGlyph.Bgra(Size, TrayShape.Spark, "#F5A623", f))
                               .ToList();

        // Motion is what makes "working" legible at 16px; identical frames would be a
        // silent regression to a static icon.
        for (var f = 1; f < frames.Count; f++)
            Assert.False(frames[0].SequenceEqual(frames[f]), $"frame {f} matches frame 0");

        // One cycle covers exactly the spark's symmetry period, so it loops seamlessly.
        var wrapped = TrayGlyph.Bgra(Size, TrayShape.Spark, "#F5A623", TrayGlyph.Frames);
        Assert.Equal(frames[0], wrapped);
    }

    [Fact]
    public void Negative_and_large_frame_numbers_are_wrapped_not_rejected()
    {
        var zero = TrayGlyph.Bgra(Size, TrayShape.Spark, "#F5A623", 0);

        Assert.Equal(zero, TrayGlyph.Bgra(Size, TrayShape.Spark, "#F5A623", TrayGlyph.Frames * 5));
        Assert.Equal(zero, TrayGlyph.Bgra(Size, TrayShape.Spark, "#F5A623", -TrayGlyph.Frames));
    }

    [Fact]
    public void Static_shapes_ignore_the_frame_so_they_never_flicker()
    {
        foreach (var shape in new[] { TrayShape.Dot, TrayShape.Ring, TrayShape.Alert })
            Assert.Equal(TrayGlyph.Bgra(Size, shape, "#30A46C", 0),
                         TrayGlyph.Bgra(Size, shape, "#30A46C", 2));
    }

    [Theory]
    [InlineData(null, TrayShape.Dot)]
    [InlineData(AgentState.Idle, TrayShape.Ring)]
    [InlineData(AgentState.Working, TrayShape.Spark)]
    [InlineData(AgentState.Waiting, TrayShape.Alert)]
    public void State_maps_to_its_shape(AgentState? state, TrayShape expected) =>
        Assert.Equal(expected, TrayGlyph.For(state));

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void Shapes_stay_inside_the_cell_at_every_tray_size(int size)
    {
        foreach (var shape in Enum.GetValues<TrayShape>())
        {
            var px = TrayGlyph.Bgra(size, shape, "#E5484D");

            // Corners must stay clear, or the glyph looks like a square smudge and
            // collides visually with its neighbours in the tray.
            foreach (var (x, y) in new[] { (0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1) })
                Assert.Equal(0, Alpha(px, size, x, y));
        }
    }

    [Fact]
    public void Rejects_a_nonsensical_size() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayGlyph.Bgra(0, TrayShape.Ring, "#30A46C"));
}
