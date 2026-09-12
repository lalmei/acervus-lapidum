using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace AcervusLapidum.Storage;

/// <summary>
/// The one list of things you can do to a pile from a picker: every layout, in enum order, then
/// the turn entry.
///
/// Two pickers show it. Holding a stone opens vanilla's tool mode dialog through
/// <see cref="Items.CollectibleBehaviorRockPileable"/>; empty hands open
/// <see cref="GuiDialogRockPileLayout"/> from the F hotkey. They share the list — and the
/// textures behind it — so a layout added here appears in both, at the same index.
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
    /// Carries out a picker choice on a pile, client-side, and asks the server to agree.
    ///
    /// Client-only on purpose. The layout is an absolute value and lands the same however often
    /// it is applied, but a turn is relative: letting the server pick its own next orientation
    /// on top of the packet turned 45 degrees into 90 and put every second orientation out of
    /// reach. So the client decides where the pile ends up and sends that destination.
    /// </summary>
    public static bool Apply(ICoreClientAPI capi, BlockEntityRockPile pile, int index)
    {
        var player = capi.World?.Player;
        if (player is null
            || !capi.World!.Claims.TryAccess(player, pile.Pos, EnumBlockAccessFlags.BuildOrBreak))
        {
            return false;
        }

        if (index == RotateIndex)
        {
            var target = pile.Orientation + 1;
            pile.TurnTo(target);

            capi.Network.SendBlockEntityPacket(
                pile.Pos,
                BlockEntityRockPile.PacketIdRotate,
                BitConverter.GetBytes(target));

            return true;
        }

        var mode = ModeForIndex(index);

        // Apply locally so the pile redraws on the same frame; the server confirms or bounces it.
        pile.SetLayoutMode(mode);
        capi.Network.SendBlockEntityPacket(
            pile.Pos,
            BlockEntityRockPile.PacketIdSetLayout,
            BitConverter.GetBytes((int)mode));

        return true;
    }
}
