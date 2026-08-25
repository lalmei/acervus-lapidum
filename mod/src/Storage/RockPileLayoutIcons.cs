using Cairo;
using Vintagestory.API.MathTools;

namespace AcervusLapidum.Storage;

/// <summary>
/// Tool-mode icons for the layout picker, each a side-on sketch of the pile it builds: a rough
/// mound for Heap, squared courses for Neat, a narrowing tower for Cairn, a long low run for
/// Wall, and a thin spread for Scattered. Stones carry their own width so a wide flat spread
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
