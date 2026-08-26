using AcervusLapidum.Items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace AcervusLapidum.Storage;

public class BlockRockPile : Block
{
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is BlockEntityRockPile pile)
        {
            return pile.GetCollisionBoxes();
        }

        return base.GetCollisionBoxes(blockAccessor, pos);
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is BlockEntityRockPile pile)
        {
            return pile.GetSelectionBoxes();
        }

        return base.GetSelectionBoxes(blockAccessor, pos);
    }

    public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return GetCollisionBoxes(blockAccessor, pos);
    }

    /// <summary>
    /// A full pile carries the next course; a part-built one does not.
    ///
    /// This is the whole cairn mechanic. You cannot start a second course on a pile with gaps in
    /// it, which is both how drystone actually works and a clear rule to read in game: fill the
    /// course you are on, then keep going up.
    /// </summary>
    public override bool CanAttachBlockAt(
        IBlockAccessor blockAccessor,
        Block block,
        BlockPos pos,
        BlockFacing blockFace,
        Cuboidi? attachmentArea = null)
    {
        if (blockFace == BlockFacing.UP)
        {
            return blockAccessor.GetBlockEntity(pos) is BlockEntityRockPile { IsFull: true };
        }

        return base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
        {
            byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
            return false;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityRockPile pile)
        {
            return pile.OnPlayerInteract(byPlayer, blockSel);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityRockPile pile)
        {
            return pile.GetContentStacks();
        }

        return [];
    }

    /// <summary>
    /// Breaking a course out from under a cairn leaves the segments above it re-reading their own
    /// height, so what is left still tapers correctly instead of keeping a stale profile.
    /// </summary>
    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        RefreshColumnAbove(world, pos);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (neibpos.Y != pos.Y
            && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityRockPile pile)
        {
            pile.RecalcSegmentIndex();
        }
    }

    private static void RefreshColumnAbove(IWorldAccessor world, BlockPos pos)
    {
        var probe = pos.UpCopy();
        while (probe.Y < world.BlockAccessor.MapSizeY
               && world.BlockAccessor.GetBlockEntity(probe) is BlockEntityRockPile above)
        {
            above.RecalcSegmentIndex();
            probe = probe.UpCopy();
        }
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayer forPlayer)
    {
        return new WorldInteraction[]
        {
            new()
            {
                // Shift alone belongs to knapping, so adding takes Ctrl as well — the same
                // modifier vanilla's own stone ground storage asks for.
                ActionLangCode = "acervuslapidum:blockhelp-rockpile-add",
                MouseButton = EnumMouseButton.Right,
                HotKeyCodes = ["shift", "ctrl"],
                Itemstacks = GetExampleStoneStacks(world)
            },
            new()
            {
                ActionLangCode = "acervuslapidum:blockhelp-rockpile-take",
                MouseButton = EnumMouseButton.Right
            },
            new()
            {
                ActionLangCode = "acervuslapidum:blockhelp-rockpile-takebulk",
                MouseButton = EnumMouseButton.Right,
                HotKeyCodes = ["ctrl"]
            },
            new()
            {
                // Our own hotkey, not "toolmodeselect": vanilla's picker only opens with a stone
                // in hand, and this line is what you read while standing there empty-handed.
                ActionLangCode = "acervuslapidum:blockhelp-rockpile-layout",
                HotKeyCode = RockPileLayoutHotkey.HotkeyCode,
                MouseButton = EnumMouseButton.None
            }
        }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    private static ItemStack[]? GetExampleStoneStacks(IWorldAccessor world)
    {
        var item = world.GetItem(new AssetLocation("game", "stone-granite"));
        return item is null ? null : [new ItemStack(item)];
    }

    /// <summary>
    /// Places a new rock pile above the targeted face and deposits the held stone(s).
    /// </summary>
    public bool CreatePile(IWorldAccessor world, BlockSelection blockSel, IPlayer player)
    {
        if (!world.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
        {
            player.InventoryManager.ActiveHotbarSlot.MarkDirty();
            return false;
        }

        var pos = blockSel.Position.Copy();
        if (blockSel.Face != null)
        {
            pos = pos.AddCopy(blockSel.Face);
        }

        if (pos.Y >= world.BlockAccessor.MapSizeY)
        {
            return false;
        }

        var below = world.BlockAccessor.GetBlock(pos.DownCopy());
        if (!below.CanAttachBlockAt(world.BlockAccessor, this, pos.DownCopy(), BlockFacing.UP))
        {
            return false;
        }

        if (world.BlockAccessor.GetBlock(pos).Replaceable < 6000)
        {
            return false;
        }

        world.BlockAccessor.SetBlock(BlockId, pos);

        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityRockPile pile)
        {
            return false;
        }

        if (world.Side == EnumAppSide.Client)
        {
            pile.MarkClientsideFirstPlacement();
        }

        var held = player.InventoryManager.ActiveHotbarSlot;
        pile.SetLayoutMode(RockPileUtil.GetHeldLayoutMode(held.Itemstack), propagate: false);

        pile.SetOrientation(WallOrientationFor(world, pos, player));

        if (!pile.TryPut(player))
        {
            world.BlockAccessor.SetBlock(0, pos);
            return false;
        }

        pile.RecalcSegmentIndex();
        pile.RegenCollision();
        pile.MarkDirty(true);
        world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        return true;
    }

    /// <summary>
    /// Which way a wall laid here should run.
    ///
    /// An existing wall pile next door wins over the player's facing: you lay a wall by walking
    /// its line and dropping stones as you go, and over a few blocks your aim wanders. Inheriting
    /// from the neighbour keeps the run reading as one wall instead of a row of jinking courses.
    /// Only neighbours on the axis their own wall runs along count, so two walls meeting at a
    /// corner do not drag each other round.
    /// </summary>
    private static int WallOrientationFor(IWorldAccessor world, BlockPos pos, IPlayer player)
    {
        foreach (var facing in BlockFacing.HORIZONTALS)
        {
            if (world.BlockAccessor.GetBlockEntity(pos.AddCopy(facing)) is not BlockEntityRockPile neighbour
                || neighbour.LayoutMode != RockPileLayoutMode.Wall)
            {
                continue;
            }

            // The generated wall runs along X at orientation 0, so an even orientation runs
            // east-west and an odd one north-south.
            var neighbourRunsEastWest = neighbour.Orientation % 2 == 0;
            if (neighbourRunsEastWest == (facing.Axis == EnumAxis.X))
            {
                return neighbour.Orientation;
            }
        }

        return GameMath.Mod((int)Math.Round(player.Entity.Pos.Yaw / (Math.PI / 2)), 4);
    }
}
