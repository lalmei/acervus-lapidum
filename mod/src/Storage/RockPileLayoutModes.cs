using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace AcervusLapidum.Storage;

/// <summary>A labelled row of picker entries — the layouts that are the same kind of thing.</summary>
/// <param name="Code">Lang suffix and grid key; see <c>rockpile-layout-group-*</c> in en.json.</param>
/// <param name="PickerIndices">Slots in <see cref="RockPileLayoutModes.PickerModes"/> order.</param>
public sealed record RockPileLayoutGroup(string Code, int[] PickerIndices)
{
    public string Title => Lang.Get($"acervuslapidum:rockpile-layout-group-{Code}");
}

/// <summary>
/// The one list of things you can do to a pile from the picker: every layout, in enum order, then
/// the turn entry — plus the rows it is shown in.
///
/// There is one picker now, <see cref="GuiDialogRockPileLayout"/>, and F opens it whether or not
/// you are holding a stone. Vanilla's tool mode dialog used to take the stone-in-hand case, but it
/// can only draw one flat grid of every entry, and eleven layouts in a row read as a wall of grey
/// icons. <see cref="LayoutGroups"/> is what a player is actually choosing between: how you want
/// the stones to sit, not which of eleven pictures looks closest.
/// </summary>
public static class RockPileLayoutModes
{
    private const string CacheKey = "acervuslapidum-rockpile-layout-modes";

    /// <summary>
    /// The layouts a picker offers, in the order it offers them.
    ///
    /// A picker slot is a position in this list, not an enum value cast to an int. The two used to
    /// be the same number, and that is what a withdrawn layout breaks: <c>RockPileLayoutMode</c>
    /// keeps the numbers existing saves wrote, so it now has a gap in it, and counting slots from
    /// zero past that gap would hand every layout after it the wrong icon and the wrong name.
    ///
    /// <c>Enum.GetValues</c> returns the members in value order, which is the order they are
    /// declared and the order the icons below are built in.
    /// </summary>
    public static readonly RockPileLayoutMode[] PickerModes = Enum.GetValues<RockPileLayoutMode>();

    /// <summary>The picker index of the turn entry, which sits after every layout.</summary>
    public static int RotateIndex => PickerModes.Length;

    /// <summary>
    /// Every layout, sorted into the rows the picker shows.
    ///
    /// A layout left out of this table is a layout nobody can pick — the picker walks these rows
    /// rather than the flat list — so the wiring test counts the two against each other.
    /// </summary>
    private static readonly (string Code, RockPileLayoutMode[] Modes)[] GroupedModes =
    [
        // Stone tipped on the ground: no shape to it beyond staying out of the way.
        ("loose", [RockPileLayoutMode.Heap, RockPileLayoutMode.Neat]),

        // Piles you build to be read from a distance — which way to go, and that someone came by.
        ("waymark", [RockPileLayoutMode.Cairn, RockPileLayoutMode.NicheCairn, RockPileLayoutMode.Arrow,
                     RockPileLayoutMode.TwinColumns]),

        // Stone laid as building: a course, a filled block, a flight to climb.
        ("masonry", [RockPileLayoutMode.Wall, RockPileLayoutMode.Masonry, RockPileLayoutMode.Steps]),

        // Piles whose point is the shape itself.
        ("ornament", [RockPileLayoutMode.Ring, RockPileLayoutMode.Spiral, RockPileLayoutMode.Balanced])
    ];

    /// <summary>The layout rows, in the order the picker stacks them.</summary>
    public static RockPileLayoutGroup[] LayoutGroups { get; } = GroupedModes
        .Select(group => new RockPileLayoutGroup(group.Code, group.Modes.Select(IndexForMode).ToArray()))
        .ToArray();

    /// <summary>
    /// The turn, on a row of its own. It is not a layout — it leaves the stones as they are and
    /// swings the pile round — so it sits apart from the four that are.
    /// </summary>
    public static RockPileLayoutGroup ActionGroup { get; } = new("actions", [RotateIndex]);

    /// <summary>Rows for a picker opened on a pile, or on bare ground where there is none to turn.</summary>
    public static RockPileLayoutGroup[] GroupsFor(bool hasPile)
    {
        return hasPile ? [.. LayoutGroups, ActionGroup] : LayoutGroups;
    }

    /// <summary>The layout a picker slot stands for. Out-of-range slots fall back to a heap.</summary>
    public static RockPileLayoutMode ModeForIndex(int index)
    {
        return index >= 0 && index < PickerModes.Length ? PickerModes[index] : RockPileLayoutMode.Heap;
    }

    /// <summary>Which slot a layout sits in, for marking the one a pile is already wearing.</summary>
    public static int IndexForMode(RockPileLayoutMode mode)
    {
        var index = Array.IndexOf(PickerModes, mode);
        return index < 0 ? 0 : index;
    }

    /// <summary>Built once per client and shared by every rock type, since the icons are drawn.</summary>
    public static SkillItem[] GetOrCreate(ICoreClientAPI capi)
    {
        return ObjectCacheUtil.GetOrCreate(capi, CacheKey, () =>
        {
            // Order must line up with PickerModes, which is what turns a slot back into a layout.
            return new SkillItem[]
            {
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "heap"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-heap")
                }.WithIcon(capi, RockPileLayoutIcons.DrawHeap),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "neat"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-neat")
                }.WithIcon(capi, RockPileLayoutIcons.DrawNeat),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "cairn"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-cairn")
                }.WithIcon(capi, RockPileLayoutIcons.DrawCairn),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "wall"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-wall")
                }.WithIcon(capi, RockPileLayoutIcons.DrawWall),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "masonry"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-masonry")
                }.WithIcon(capi, RockPileLayoutIcons.DrawMasonry),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "ring"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-ring")
                }.WithIcon(capi, RockPileLayoutIcons.DrawRing),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "spiral"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-spiral")
                }.WithIcon(capi, RockPileLayoutIcons.DrawSpiral),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "steps"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-steps")
                }.WithIcon(capi, RockPileLayoutIcons.DrawSteps),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "balanced"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-balanced")
                }.WithIcon(capi, RockPileLayoutIcons.DrawBalanced),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "twincolumns"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-twincolumns")
                }.WithIcon(capi, RockPileLayoutIcons.DrawTwinColumns),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "arrow"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-arrow")
                }.WithIcon(capi, RockPileLayoutIcons.DrawArrow),
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "nichecairn"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-nichecairn")
                }.WithIcon(capi, RockPileLayoutIcons.DrawNicheCairn),

                // Last entry, past every layout: picking it turns the pile 45 degrees instead of
                // restyling it. RotateIndex is what tells the two apart in Apply.
                new SkillItem
                {
                    Code = new AssetLocation("acervuslapidum", "rotate"),
                    Name = Lang.Get("acervuslapidum:rockpile-rotate")
                }.WithIcon(capi, RockPileLayoutIcons.DrawRotate)
            };
        });
    }

    /// <summary>Tears the shared textures down. Safe to call from every holder; the first wins.</summary>
    public static void Dispose(ICoreClientAPI capi)
    {
        if (ObjectCacheUtil.TryGet<SkillItem[]>(capi, CacheKey) is not { } modes)
        {
            return;
        }

        ObjectCacheUtil.Delete(capi, CacheKey);

        foreach (var mode in modes)
        {
            mode?.Dispose();
        }
    }

    /// <summary>
    /// Carries out a picker choice, client-side, and asks the server to agree.
    ///
    /// Client-only on purpose. The layout is an absolute value and lands the same however often
    /// it is applied, but a turn is relative: letting the server pick its own next orientation
    /// on top of the packet turned 45 degrees into 90 and put every second orientation out of
    /// reach. So the client decides where the pile ends up and sends that destination.
    ///
    /// <paramref name="pile"/> is null when the picker was opened over bare ground with a stone in
    /// hand. There is nothing to restyle yet, so the choice is only remembered — and the next pile
    /// that stone starts comes out laid that way.
    /// </summary>
    public static bool Apply(ICoreClientAPI capi, BlockEntityRockPile? pile, int index)
    {
        var player = capi.World?.Player;
        if (player is null)
        {
            return false;
        }

        if (index == RotateIndex)
        {
            // A turn belongs to a pile, not to a player: there is nothing to remember and nothing
            // to turn when the picker is standing over bare ground.
            if (pile is null || !CanBuildAt(capi, player, pile))
            {
                return false;
            }

            var target = pile.Orientation + 1;
            pile.TurnTo(target);

            capi.Network.SendBlockEntityPacket(
                pile.Pos,
                BlockEntityRockPile.PacketIdRotate,
                BitConverter.GetBytes(target));

            return true;
        }

        var mode = ModeForIndex(index);
        Remember(capi, player, mode);

        if (pile is null)
        {
            return true;
        }

        if (!CanBuildAt(capi, player, pile))
        {
            return false;
        }

        // Apply locally so the pile redraws on the same frame; the server confirms or bounces it.
        pile.SetLayoutMode(mode);
        capi.Network.SendBlockEntityPacket(
            pile.Pos,
            BlockEntityRockPile.PacketIdSetLayout,
            BitConverter.GetBytes((int)mode));

        return true;
    }

    /// <summary>
    /// Keeps the choice for the next pile this player starts, on both sides.
    ///
    /// Vanilla's tool mode dialog used to do this for us by running SetToolMode on the server too;
    /// with our own picker the preference has to be sent — see <see cref="RockPileLayoutSync"/>.
    /// </summary>
    private static void Remember(ICoreClientAPI capi, IPlayer player, RockPileLayoutMode mode)
    {
        RockPileUtil.SetPreferredLayoutMode(player.Entity, mode);
        RockPileLayoutSync.SendPreference(capi, mode);

        // Scrub the old on-stack marker if the held stone still carries one, so it goes back to
        // stacking with every other loose rock.
        if (player.InventoryManager?.ActiveHotbarSlot is { Itemstack: not null } held
            && RockPileUtil.IsPileableStone(held.Itemstack))
        {
            RockPileUtil.ClearHeldLayoutMode(held.Itemstack);
            held.MarkDirty();
        }
    }

    private static bool CanBuildAt(ICoreClientAPI capi, IPlayer player, BlockEntityRockPile pile)
    {
        return capi.World!.Claims.TryAccess(player, pile.Pos, EnumBlockAccessFlags.BuildOrBreak);
    }
}
