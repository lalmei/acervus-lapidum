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

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI capi)
        {
            layoutModes = RockPileLayoutModes.GetOrCreate(capi);
        }
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);

        // Shared across every rock type, so tear the cached textures down exactly once.
        if (api is ICoreClientAPI capi)
        {
            RockPileLayoutModes.Dispose(capi);
            layoutModes = null;
        }
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

        // Remember which column this hold is building in, so the repeat below cannot wander.
        byEntity.Attributes.SetInt(AnchorXAttr, blockSel.Position.X);
        byEntity.Attributes.SetInt(AnchorZAttr, blockSel.Position.Z);

        handling = EnumHandling.PreventSubsequent;
    }

    /// <summary>Seconds between stones while the place button is held down.</summary>
    private const float RepeatSeconds = 0.22f;

    private const string StepAttr = "acervuslapidum:lastPileStep";

    // The column the current hold is building in. A hold feeds one pile and then carries on up
    // the same column; it must never wander off and start a new pile somewhere else.
    private const string AnchorXAttr = "acervuslapidum:pileAnchorX";
    private const string AnchorZAttr = "acervuslapidum:pileAnchorZ";

    /// <summary>
    /// Keeps feeding the pile while the button is held.
    ///
    /// A course can be ninety-six stones and Ctrl is spoken for by the add gesture itself, so
    /// there is no key left to hang a bulk transfer on. Holding the button is the better answer
    /// anyway: the pile grows a stone at a time under the cursor and you stop when it looks right,
    /// which is rather the point of placing stones one by one.
    ///
    /// The repeat stays in the column the hold began in. It used to re-run the whole placement
    /// path, so the instant a pile filled up and the cursor drifted onto the ground beside it —
    /// easy to do while still holding the button — the next stone started a fresh pile on the
    /// floor next door. Starting a pile somewhere new is a thing you do with a new click.
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

        // Same column the hold started in, or nothing. A cairn grows straight up, so matching X
        // and Z still lets a hold carry on into the course above without letting it stray.
        if (blockSel.Position.X != byEntity.Attributes.GetInt(AnchorXAttr, blockSel.Position.X)
            || blockSel.Position.Z != byEntity.Attributes.GetInt(AnchorZAttr, blockSel.Position.Z))
        {
            // Keep the hold alive rather than ending it: the player is still holding the button,
            // and swinging back over the pile should carry on where it left off.
            handling = EnumHandling.PreventSubsequent;
            return true;
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

        return (int)RockPileUtil.GetPreferredLayoutMode(byPlayer.Entity);
    }

    /// <summary>The picker index of the turn entry, which sits after every layout.</summary>
    public static int RotateModeIndex => RockPileLayoutModes.RotateIndex;

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        var world = byPlayer.Entity.World;

        if (toolMode != RotateModeIndex)
        {
            // Remember the choice on the player, so the next pile they start is laid the same
            // way. Rotation is deliberately not remembered: it belongs to a pile, not to a player.
            RockPileUtil.SetPreferredLayoutMode(byPlayer.Entity, RockPileUtil.ClampLayoutMode(toolMode));

            // Scrub the old on-stack marker if this stone still carries one, so it goes back to
            // stacking with every other loose rock.
            RockPileUtil.ClearHeldLayoutMode(slot.Itemstack);
            slot.MarkDirty();
        }

        if (blockSelection is null)
        {
            return;
        }

        var pile = FindTargetPile(world, blockSelection);
        if (pile is null)
        {
            return;
        }

        // The picker calls this on both sides. The client drives the change and tells the server
        // where the pile ends up — see RockPileLayoutModes.Apply for why a turn cannot be left to
        // both sides to work out for themselves.
        if (world.Api is ICoreClientAPI capi)
        {
            RockPileLayoutModes.Apply(capi, pile, toolMode);
            return;
        }

        if (toolMode != RotateModeIndex
            && world.Claims.TryAccess(byPlayer, pile.Pos, EnumBlockAccessFlags.BuildOrBreak))
        {
            pile.SetLayoutMode(RockPileUtil.ClampLayoutMode(toolMode));
        }
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
