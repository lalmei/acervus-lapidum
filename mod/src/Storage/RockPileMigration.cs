using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AcervusLapidum.Storage;

/// <summary>
/// Moves stone piles between vanilla's ground storage and ours, in both directions.
///
/// Both directions matter. Converting is what makes an existing world's piles usable as cairns and
/// walls; reverting is what makes this mod safe to uninstall, because a world full of
/// <c>acervuslapidum:rockpile</c> blocks loses every one of them — stones included — the moment the
/// block code stops resolving. Run <c>/rockpile revert all</c> before removing the mod and the
/// world goes back to plain vanilla piles with every stone still in them.
/// </summary>
public static class RockPileMigration
{
    /// <summary>Vanilla's own code for the ground storage block a reverted pile becomes.</summary>
    public static readonly AssetLocation GroundStorageCode = new("game", "groundstorage");

    /// <summary>
    /// Whether this block entity is a vanilla pile made only of stones, and if so what is in it —
    /// one entry per stone, because that is the unit our piles count in.
    ///
    /// Deliberately does not require <c>StorageProps</c> to be resolved: it is rebuilt at runtime
    /// from the stone's own GroundStorable behavior, so any mod that disturbs that behavior leaves
    /// it null and a pile we could read perfectly well would be skipped as unrecognised.
    /// </summary>
    public static bool TryReadVanillaStonePile(BlockEntity? blockEntity, out List<ItemStack> stones)
    {
        stones = [];

        if (blockEntity is not BlockEntityGroundStorage storage)
        {
            return false;
        }

        // Quadrant and single-item layouts hold everything else the game drops on the floor. Only
        // Stacking is a stone pile — but an unresolved layout is not evidence of anything, so the
        // contents get the final say.
        if (storage.StorageProps is { Layout: not EnumGroundStorageLayout.Stacking })
        {
            return false;
        }

        foreach (var slot in storage.Inventory)
        {
            if (slot.Empty)
            {
                continue;
            }

            if (!RockPileUtil.IsPileableStone(slot.Itemstack))
            {
                stones.Clear();
                return false;
            }

            for (var i = 0; i < slot.Itemstack!.StackSize; i++)
            {
                var single = slot.Itemstack.Clone();
                single.StackSize = 1;
                stones.Add(single);
            }
        }

        return stones.Count > 0;
    }

    /// <summary>
    /// Turns one vanilla stone pile into as many rock pile segments as its stones need, and
    /// returns how many stones ended up in blocks.
    ///
    /// A vanilla pile holds up to 64 stones where a heap holds 32, so a full one becomes a
    /// two-segment cairn — which is the honest result: it always contained two courses' worth of
    /// stone, it just drew them as one. Anything that will not fit is handed back as items rather
    /// than deleted.
    /// </summary>
    public static int Convert(ICoreAPI api, BlockEntity? blockEntity)
    {
        if (api.Side != EnumAppSide.Server || !TryReadVanillaStonePile(blockEntity, out var stones))
        {
            return 0;
        }

        var pos = blockEntity!.Pos.Copy();

        try
        {
            if (api.World.GetBlock(RockPileUtil.BlockCode) is not BlockRockPile pileBlock)
            {
                api.Logger.Warning(
                    "Acervus Lapidum left the stone pile at {0} unchanged because {1} is missing.",
                    pos,
                    RockPileUtil.BlockCode);
                return 0;
            }

            var total = stones.Count;
            var placed = 0;
            var segment = 0;

            while (placed < total)
            {
                var target = pos.UpCopy(segment);
                if (target.Y >= api.World.BlockAccessor.MapSizeY)
                {
                    break;
                }

                // Only the first segment replaces the ground storage; the ones above it need empty
                // space to move into, and stones we cannot place stay in the world as items.
                if (segment > 0 && api.World.BlockAccessor.GetBlock(target).Replaceable < 6000)
                {
                    break;
                }

                api.World.BlockAccessor.SetBlock(pileBlock.Id, target);
                if (api.World.BlockAccessor.GetBlockEntity(target) is not BlockEntityRockPile pile)
                {
                    api.Logger.Error(
                        "Acervus Lapidum replaced the stone pile at {0}, but no rock pile entity was created.",
                        target);
                    break;
                }

                // Ask the pile itself rather than assuming 32: it knows how far up the column it
                // sits, and an upper course holds fewer stones.
                var course = stones.GetRange(placed, Math.Min(pile.SlotCount, total - placed));
                pile.PopulateFrom(course, RockPileLayoutMode.Heap);
                api.World.BlockAccessor.TriggerNeighbourBlockUpdate(target);

                placed += course.Count;
                segment++;
            }

            SpawnRemainder(api, pos, stones, placed);
            return placed;
        }
        catch (Exception exception)
        {
            api.Logger.Error(
                "Acervus Lapidum failed to convert the stone pile at {0}: {1}",
                pos,
                exception);
            return 0;
        }
    }

    /// <summary>
    /// Turns one rock pile back into a vanilla stone pile, and returns how many stones ended up in
    /// the block. This is the uninstall path, so it errs towards the world: whatever the vanilla
    /// pile cannot hold — a masonry course is ninety-six stones against vanilla's sixty-four, and
    /// a vanilla pile is a single stack so mixed rock cannot all stay — drops as items rather than
    /// being quietly rounded away.
    /// </summary>
    public static int Revert(ICoreAPI api, BlockEntity? blockEntity)
    {
        if (api.Side != EnumAppSide.Server || blockEntity is not BlockEntityRockPile pile)
        {
            return 0;
        }

        var pos = pile.Pos.Copy();
        var stones = pile.GetContentStacks().ToList();

        try
        {
            if (stones.Count == 0)
            {
                api.World.BlockAccessor.SetBlock(0, pos);
                api.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
                return 0;
            }

            // Vanilla's pile is one stack, so the rock type most of the pile is made of stays and
            // the odd stones in it come back to the player. Picking the majority keeps the biggest
            // pile intact and the fewest items on the floor.
            var keeper = stones
                .GroupBy(stone => stone.Collectible.Code.ToString())
                .OrderByDescending(group => group.Count())
                .First()
                .ToList();

            var capacity = keeper[0].Collectible
                .GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps?.StackingCapacity ?? 64;
            var kept = Math.Min(keeper.Count, Math.Max(1, capacity));

            if (api.World.GetBlock(GroundStorageCode) is not BlockGroundStorage storageBlock)
            {
                api.Logger.Warning(
                    "Acervus Lapidum left the rock pile at {0} in place because {1} is missing.",
                    pos,
                    GroundStorageCode);
                return 0;
            }

            api.World.BlockAccessor.SetBlock(storageBlock.Id, pos);
            if (api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage storage)
            {
                api.Logger.Error(
                    "Acervus Lapidum replaced the rock pile at {0}, but no ground storage entity was created.",
                    pos);
                return 0;
            }

            var stack = keeper[0].Clone();
            stack.StackSize = kept;
            storage.Inventory[0].Itemstack = stack;
            storage.Inventory[0].MarkDirty();

            // The block entity resolved its properties while it was empty, so tell it to look
            // again now that there is a stone in it to look at.
            storage.DetermineStorageProperties(null);
            storage.MarkDirty(true);
            api.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);

            // Everything the single vanilla stack could not carry.
            var leftovers = keeper.Skip(kept).Concat(stones.Where(stone => !keeper.Contains(stone))).ToList();
            SpawnRemainder(api, pos, leftovers, 0);

            return kept;
        }
        catch (Exception exception)
        {
            api.Logger.Error("Acervus Lapidum failed to revert the rock pile at {0}: {1}", pos, exception);
            return 0;
        }
    }

    /// <summary>
    /// Hands back every stone from <paramref name="fromIndex"/> on. Losing a player's stone to a
    /// migration they asked for is still losing their stone.
    /// </summary>
    private static void SpawnRemainder(ICoreAPI api, BlockPos pos, List<ItemStack> stones, int fromIndex)
    {
        for (var i = fromIndex; i < stones.Count; i++)
        {
            api.World.SpawnItemEntity(stones[i], pos.ToVec3d().Add(0.5, 1.0, 0.5));
        }
    }
}
