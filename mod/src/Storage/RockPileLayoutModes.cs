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

    /// <summary>The picker index of the turn entry, which sits after every layout.</summary>
    public static int RotateIndex => Enum.GetValues<RockPileLayoutMode>().Length;

    /// <summary>Built once per client and shared by every rock type, since the icons are drawn.</summary>
    public static SkillItem[] GetOrCreate(ICoreClientAPI capi)
    {
        return ObjectCacheUtil.GetOrCreate(capi, CacheKey, () =>
        {
            // Index must line up with RockPileLayoutMode, which the tool mode int maps straight onto.
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
                    Code = new AssetLocation("acervuslapidum", "scattered"),
                    Name = Lang.Get("acervuslapidum:rockpile-layout-scattered")
                }.WithIcon(capi, RockPileLayoutIcons.DrawScattered),
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

        var mode = RockPileUtil.ClampLayoutMode(index);

        // Apply locally so the pile redraws on the same frame; the server confirms or bounces it.
        pile.SetLayoutMode(mode);
        capi.Network.SendBlockEntityPacket(
            pile.Pos,
            BlockEntityRockPile.PacketIdSetLayout,
            BitConverter.GetBytes((int)mode));

        return true;
    }
}
