using Cairo;
using Vintagestory.API.MathTools;

namespace AcervusLapidum.Storage;

/// <summary>
/// Tool-mode icons for the layout picker, each a side-on sketch of the pile it builds: a rough
/// mound for Heap, squared courses for Neat, a narrowing tower for Cairn, a long low run for
/// Wall, a thin spread for Scattered, and — for Arrow, the one layout read from above rather
/// than from the side — a chevron and shaft. Stones carry their own width so a wide flat spread
/// reads differently from a tall narrow stack at icon size.
/// </summary>
public static class RockPileLayoutIcons
{
    private const double StoneHeight = 0.1;
    private const double Wide = 0.30;
    private const double Narrow = 0.22;

    /// <summary>Offset from the icon's horizontal middle, baseline, tilt, and stone width.</summary>
    private static readonly (double dx, double y, double angle, double width)[] HeapStones =
    [
        (-0.290, 0.840, 4, Wide),
        (0.010, 0.845, -3, Wide),
        (0.300, 0.835, 6, Wide),
        (-0.150, 0.720, -6, Wide),
        (0.165, 0.725, 5, Wide),
        (-0.020, 0.605, -4, Wide),
        (0.215, 0.600, 9, Narrow),
        (0.055, 0.485, 7, Narrow)
    ];

    private static readonly (double dx, double y, double angle, double width)[] NeatStones =
    [
        (-0.170, 0.840, 0, Wide),
        (0.170, 0.840, 0, Wide),
        (-0.170, 0.720, 0, Wide),
        (0.170, 0.720, 0, Wide),
        (-0.170, 0.600, 0, Wide),
        (0.170, 0.600, 0, Wide),
        (-0.170, 0.480, 0, Wide),
        (0.170, 0.480, 0, Wide)
    ];

    /// <summary>Courses drawing in as they rise, so the silhouette is the cone itself.</summary>
    private static readonly (double dx, double y, double angle, double width)[] CairnStones =
    [
        (-0.310, 0.870, 3, Wide),
        (0.000, 0.875, 0, Wide),
        (0.310, 0.870, -3, Wide),
        (-0.195, 0.750, -4, Wide),
        (0.195, 0.750, 4, Wide),
        (-0.130, 0.630, 5, Narrow),
        (0.130, 0.630, -5, Narrow),
        (0.000, 0.510, 0, Narrow),
        (0.000, 0.395, 3, 0.16)
    ];

    /// <summary>Two courses running the full width, joints broken between them.</summary>
    private static readonly (double dx, double y, double angle, double width)[] WallStones =
    [
        (-0.330, 0.760, 0, Wide),
        (0.000, 0.760, 0, Wide),
        (0.330, 0.760, 0, Wide),
        (-0.165, 0.645, 0, Wide),
        (0.165, 0.645, 0, Wide),
        (-0.330, 0.530, 0, Wide),
        (0.000, 0.530, 0, Wide),
        (0.330, 0.530, 0, Wide)
    ];

    private static readonly (double dx, double y, double angle, double width)[] ScatteredStones =
    [
        (-0.360, 0.790, 5, Narrow),
        (-0.130, 0.800, -4, Narrow),
        (0.130, 0.795, 7, Narrow),
        (0.365, 0.785, -6, Narrow),
        (-0.235, 0.685, -3, Narrow),
        (0.020, 0.690, 6, Narrow),
        (0.255, 0.680, -5, Narrow)
    ];

    /// <summary>Coursed stone filling the whole icon: the layout that hands back a solid block.</summary>
    private static readonly (double dx, double y, double angle, double width)[] MasonryStones =
    [
        (-0.330, 0.860, 0, Wide), (0.000, 0.860, 0, Wide), (0.330, 0.860, 0, Wide),
        (-0.165, 0.745, 0, Wide), (0.165, 0.745, 0, Wide), (-0.440, 0.745, 0, 0.16), (0.440, 0.745, 0, 0.16),
        (-0.330, 0.630, 0, Wide), (0.000, 0.630, 0, Wide), (0.330, 0.630, 0, Wide),
        (-0.165, 0.515, 0, Wide), (0.165, 0.515, 0, Wide), (-0.440, 0.515, 0, 0.16), (0.440, 0.515, 0, 0.16),
        (-0.330, 0.400, 0, Wide), (0.000, 0.400, 0, Wide), (0.330, 0.400, 0, Wide)
    ];

    /// <summary>A hearth seen slightly from above: a hollow ring of stones.</summary>
    private static readonly (double dx, double y, double angle, double width)[] RingStones =
    [
        (0.000, 0.430, 0, Narrow),
        (-0.250, 0.480, 22, Narrow),
        (0.250, 0.480, -22, Narrow),
        (-0.345, 0.610, 68, Narrow),
        (0.345, 0.610, -68, Narrow),
        (-0.250, 0.740, 158, Narrow),
        (0.250, 0.740, -158, Narrow),
        (0.000, 0.800, 0, Narrow)
    ];

    /// <summary>Courses stepping sideways as they rise, tracing the helical seam.</summary>
    private static readonly (double dx, double y, double angle, double width)[] SpiralStones =
    [
        (-0.150, 0.860, 6, Wide),
        (0.150, 0.780, -6, Wide),
        (0.180, 0.680, 10, Wide),
        (-0.060, 0.590, -12, Wide),
        (-0.190, 0.490, 8, Wide),
        (0.060, 0.400, -8, Wide),
        (0.170, 0.310, 12, Narrow),
        (-0.080, 0.230, -10, Narrow)
    ];

    /// <summary>A stair in profile, each tread one step higher than the last.</summary>
    private static readonly (double dx, double y, double angle, double width)[] StepsStones =
    [
        (-0.345, 0.865, 0, Narrow), (-0.115, 0.865, 0, Narrow), (0.115, 0.865, 0, Narrow), (0.345, 0.865, 0, Narrow),
        (-0.115, 0.745, 0, Narrow), (0.115, 0.745, 0, Narrow), (0.345, 0.745, 0, Narrow),
        (0.115, 0.625, 0, Narrow), (0.345, 0.625, 0, Narrow),
        (0.345, 0.505, 0, Narrow)
    ];

    /// <summary>A trail marker: a few stones stacked centrally, each turned off the last.</summary>
    private static readonly (double dx, double y, double angle, double width)[] BalancedStones =
    [
        (0.000, 0.860, 3, 0.44),
        (0.020, 0.745, -5, 0.36),
        (-0.025, 0.630, 6, 0.30),
        (0.015, 0.515, -4, Narrow),
        (-0.010, 0.400, 5, 0.17)
    ];

    /// <summary>Two slender columns with daylight between them.</summary>
    private static readonly (double dx, double y, double angle, double width)[] TwinColumnStones =
    [
        (-0.260, 0.865, 0, Narrow), (0.260, 0.865, 0, Narrow),
        (-0.260, 0.745, 0, 0.16), (0.260, 0.745, 0, 0.16),
        (-0.260, 0.625, 0, Narrow), (0.260, 0.625, 0, Narrow),
        (-0.260, 0.505, 0, 0.16), (0.260, 0.505, 0, 0.16),
        (-0.260, 0.385, 0, Narrow), (0.260, 0.385, 0, Narrow)
    ];

    /// <summary>
    /// A waypoint arrow seen from above: two barbs meeting at a point, and a shaft behind it.
    /// Drawn pointing up the icon, which is the heading an unturned pile is built with.
    /// </summary>
    private static readonly (double dx, double y, double angle, double width)[] ArrowStones =
    [
        (-0.101, 0.361, -45, Wide), (0.101, 0.361, 45, Wide),
        (-0.259, 0.519, -45, Wide), (0.259, 0.519, 45, Wide),
        (0.000, 0.470, 90, 0.24),
        (0.000, 0.650, 90, 0.24),
        (0.000, 0.830, 90, 0.24)
    ];

    /// <summary>
    /// The turn entry. Drawn as one stone stepped round through part of a circle rather than as a
    /// drawn arrow, so it sits in the same visual language as the layouts beside it in the picker
    /// — and so it is not mistaken for the Arrow layout two slots along.
    /// </summary>
    private static readonly (double dx, double y, double angle, double width)[] RotateStones =
    [
        (0.000, 0.320, 0, Narrow),
        (0.230, 0.400, 45, Narrow),
        (0.320, 0.600, 90, Narrow),
        (0.230, 0.800, 135, Narrow),
        (0.000, 0.880, 180, Narrow),
        (-0.230, 0.800, 225, Narrow)
    ];

    public static void DrawMasonry(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, MasonryStones);

    public static void DrawRing(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, RingStones);

    public static void DrawSpiral(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, SpiralStones);

    public static void DrawSteps(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, StepsStones);

    public static void DrawBalanced(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, BalancedStones);

    public static void DrawTwinColumns(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, TwinColumnStones);

    public static void DrawArrow(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, ArrowStones);

    public static void DrawRotate(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, RotateStones);

    public static void DrawHeap(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, HeapStones);

    public static void DrawNeat(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, NeatStones);

    public static void DrawCairn(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, CairnStones);

    public static void DrawWall(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, WallStones);

    public static void DrawScattered(Context cr, int x, int y, float w, float h, double[] rgba) =>
        Draw(cr, x, y, w, h, rgba, ScatteredStones);

    private static void Draw(
        Context cr,
        int x,
        int y,
        float width,
        float height,
        double[] rgba,
        (double dx, double y, double angle, double width)[] stones)
    {
        cr.Save();
        cr.Translate(x, y);

        // Stones are filled rather than stroked so the rotation never distorts a line width.
        foreach (var (dx, baseline, angle, stoneWidth) in stones)
        {
            var sw = width * stoneWidth;
            var sh = height * StoneHeight;

            cr.Save();
            cr.Translate(width * (0.5 + dx), height * baseline);
            cr.Rotate(angle * GameMath.DEG2RAD);
            cr.Rectangle(-sw / 2, -sh / 2, sw, sh);
            cr.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
            cr.Fill();
            cr.Restore();
        }

        cr.Restore();
    }
}
