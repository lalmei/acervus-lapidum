using AcervusLapidum.Storage;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace AcervusLapidum.Items;

/// <summary>
/// Sneak + Ctrl + RMB puts a stone into an Acervus Lapidum rock pile, replacing vanilla's
/// Stacking ground storage for new placements. Hold the button to keep feeding the pile.
/// Pre-existing vanilla stone piles are converted on load by
/// <see cref="BlockEntityBehaviorRockPileConverter"/> rather than being interacted with here.
/// F while looking at a pile (or at placeable ground) picks the layout.
///
/// Ctrl is not decoration. Sneak + RMB alone is how you start knapping a hard stone, and vanilla
/// keeps its own stone ground storage out of the way by setting <c>ctrlKey: true</c> on the
/// GroundStorable properties — see BlockEntityGroundStorage.OnPlayerInteractStart, which refuses
/// a non-empty hand unless Ctrl is down. Claiming sneak + RMB here made hard stones unknappable.
/// </summary>
public sealed class CollectibleBehaviorRockPileable : CollectibleBehavior
{
    private SkillItem[]? layoutModes;

    public CollectibleBehaviorRockPileable(CollectibleObject collObj) : base(collObj)
    {
    }

    private const string LayoutModeCacheKey = "acervuslapidum-rockpile-layout-modes";

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is not ICoreClientAPI capi)
        {
            return;
        }

        layoutModes = ObjectCacheUtil.GetOrCreate(capi, LayoutModeCacheKey, () =>
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
                }.WithIcon(capi, RockPileLayoutIcons.DrawScattered)
            };
        });
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);

        if (api is not ICoreClientAPI capi || ObjectCacheUtil.TryGet<SkillItem[]>(capi, LayoutModeCacheKey) is null)
        {
            return;
        }

        // Shared across every rock type, so tear the cached textures down exactly once.
        foreach (var mode in layoutModes ?? [])
        {
            mode?.Dispose();
        }

        ObjectCacheUtil.Delete(capi, LayoutModeCacheKey);
        layoutModes = null;
    }

    public override void OnHeldInteractStart(
        ItemSlot itemslot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handHandling,
        ref EnumHandling handling)
    {
        if (!TryInteract(itemslot, byEntity, blockSel, ref handHandling))
        {
            return;
        }

        // secondsUsed restarts at zero for this hold, so a marker left by the last one would sit
        // in the future and stall the repeat forever.
        byEntity.Attributes.SetFloat(StepAttr, 0f);
        handling = EnumHandling.PreventSubsequent;
    }

    /// <summary>Seconds between stones while the place button is held down.</summary>
    private const float RepeatSeconds = 0.22f;

    private const string StepAttr = "acervuslapidum:lastPileStep";

    /// <summary>
    /// Keeps feeding the pile while the button is held.
    ///
    /// A course is 32 stones and Ctrl is spoken for by the add gesture itself, so there is no key
    /// left to hang a bulk transfer on. Holding the button is the better answer anyway: the pile
    /// grows a stone at a time under the cursor and you stop when it looks right, which is rather
    /// the point of placing stones one by one.
    /// </summary>
    public override bool OnHeldInteractStep(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandling handling)
    {
        if (blockSel is null
            || !byEntity.Controls.ShiftKey
            || !byEntity.Controls.CtrlKey
            || !RockPileUtil.IsPileableStone(slot.Itemstack))
        {
            return false;
        }

        handling = EnumHandling.PreventSubsequent;

        var last = byEntity.Attributes.GetFloat(StepAttr);
        if (secondsUsed - last < RepeatSeconds)
        {
            return true;
        }

        byEntity.Attributes.SetFloat(StepAttr, secondsUsed);

        var handHandling = EnumHandHandling.NotHandled;
        TryInteract(slot, byEntity, blockSel, ref handHandling);

        // Keep going even when that stone did not land — the pile may have just filled, and the
        // player is still holding the button over a spot where the next course can start.
        return true;
    }

    public override void OnHeldInteractStop(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandling handling)
    {
        byEntity.Attributes.SetFloat(StepAttr, 0f);
    }

    public override bool OnHeldInteractCancel(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumItemUseCancelReason cancelReason,
        ref EnumHandling handled)
    {
        byEntity.Attributes.SetFloat(StepAttr, 0f);
        return true;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
    {
        handling = EnumHandling.PassThrough;
        return
        [
            new WorldInteraction
            {
                HotKeyCodes = ["shift", "ctrl"],
                ActionLangCode = "acervuslapidum:heldhelp-rockpile-place",
                MouseButton = EnumMouseButton.Right
            },
            new WorldInteraction
            {
                ActionLangCode = "acervuslapidum:heldhelp-rockpile-layout",
                HotKeyCode = "toolmodeselect",
                MouseButton = EnumMouseButton.None
            }
        ];
    }

    public override SkillItem[]? GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if (blockSel is null || !RockPileUtil.IsPileableStone(slot.Itemstack))
        {
            return null;
        }

        if (FindTargetPile(forPlayer.Entity.World, blockSel) is not null)
        {
            return layoutModes;
        }

        // Also allow choosing the layout before the first stone goes down.
        if (blockSel.Face == BlockFacing.UP)
        {
            return layoutModes;
        }

        return null;
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        if (blockSelection is not null)
        {
            var pile = FindTargetPile(byPlayer.Entity.World, blockSelection);
            if (pile is not null)
            {
                return (int)pile.LayoutMode;
            }
        }

        return (int)RockPileUtil.GetHeldLayoutMode(slot.Itemstack);
    }

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        var mode = RockPileUtil.ClampLayoutMode(toolMode);

        RockPileUtil.SetHeldLayoutMode(slot.Itemstack, mode);
        slot.MarkDirty();

        if (blockSelection is null)
        {
            return;
        }

        var pile = FindTargetPile(byPlayer.Entity.World, blockSelection);
        if (pile is null)
        {
            return;
        }

        if (!byPlayer.Entity.World.Claims.TryAccess(byPlayer, pile.Pos, EnumBlockAccessFlags.BuildOrBreak))
        {
            return;
        }

        pile.SetLayoutMode(mode);
    }

    public static bool TryInteract(
        ItemSlot itemslot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        ref EnumHandHandling handHandling)
    {
        var world = byEntity?.World;

        // ShiftKey, not Sneak: they are separate controls, and ShiftKey is the one the client
        // routes a right-click by and the one vanilla's Throwable checks before it starts aiming.
        // Stones are throwable, so reading the other flag would let one click both aim and place.
        //
        // CtrlKey as well, and this one is load-bearing: without it we swallow the sneak + RMB
        // that starts knapping, and every hard stone in the game becomes unknappable.
        if (blockSel is null || world is null || !byEntity!.Controls.ShiftKey || !byEntity.Controls.CtrlKey)
        {
            return false;
        }

        if (!RockPileUtil.IsPileableStone(itemslot.Itemstack))
        {
            return false;
        }

        if (byEntity is not EntityPlayer entityPlayer)
        {
            return false;
        }

        var byPlayer = world.PlayerByUid(entityPlayer.PlayerUID);
        if (byPlayer is null)
        {
            return false;
        }

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
        {
            itemslot.MarkDirty();
            return false;
        }

        var pile = FindTargetPile(world, blockSel);
        if (pile is not null)
        {
            // A full pile does not swallow more stones — it carries the next course instead, so
            // fall through and let the click start a new segment on top of it.
            if (!pile.IsFull || blockSel.Face != BlockFacing.UP)
            {
                if (pile.OnPlayerInteract(byPlayer, blockSel))
                {
                    StopAiming(byEntity);
                    handHandling = EnumHandHandling.PreventDefault;
                    return true;
                }

                return false;
            }
        }

        // Never build on top of a vanilla ground storage pile; the converter turns those into
        // rock piles on load, and until it has, they are not ours to stack on.
        if (pile is null && FindTargetGroundStorage(world, blockSel) is not null)
        {
            return false;
        }

        if (blockSel.Face != BlockFacing.UP)
        {
            return false;
        }

        if (world.GetBlock(RockPileUtil.BlockCode) is not BlockRockPile pileBlock)
        {
            return false;
        }

        var onBlock = world.BlockAccessor.GetBlock(blockSel.Position);
        if (!onBlock.CanAttachBlockAt(world.BlockAccessor, pileBlock, blockSel.Position, BlockFacing.UP))
        {
            return false;
        }

        var above = blockSel.Position.AddCopy(blockSel.Face);
        if (world.BlockAccessor.GetBlock(above).Replaceable < 6000)
        {
            return false;
        }

        if (pileBlock.CreatePile(world, blockSel, byPlayer))
        {
            StopAiming(byEntity);
            handHandling = EnumHandHandling.PreventDefault;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Stones carry vanilla's Throwable behavior, which starts an aim animation on the same
    /// button. Cancel it, or a player who just placed a stone is left winding up to throw one.
    /// </summary>
    private static void StopAiming(EntityAgent byEntity)
    {
        byEntity.Attributes.SetInt("aiming", 0);
        byEntity.StopAnimation("aim");
    }

    public static BlockEntityRockPile? FindTargetPile(IWorldAccessor world, BlockSelection blockSel)
    {
        return world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityRockPile
               ?? world.BlockAccessor.GetBlockEntity(blockSel.Position.UpCopy()) as BlockEntityRockPile;
    }

    /// <summary>Vanilla stone piles that the converter has not reached yet.</summary>
    public static BlockEntityGroundStorage? FindTargetGroundStorage(IWorldAccessor world, BlockSelection blockSel)
    {
        return world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityGroundStorage
               ?? world.BlockAccessor.GetBlockEntity(blockSel.Position.UpCopy()) as BlockEntityGroundStorage;
    }
}
