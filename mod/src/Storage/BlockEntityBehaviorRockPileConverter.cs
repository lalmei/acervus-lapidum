using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AcervusLapidum.Storage;

/// <summary>
/// Migrates vanilla stone piles into rock piles as their block entity enters a loaded server
/// chunk. That catches existing worlds, worldgen and player-placed piles without scanning every
/// block, and ground storage holding anything other than stones is left completely alone.
///
/// A vanilla pile holds up to 64 stones where ours holds 32, so a full one becomes a two-segment
/// cairn — which is the honest result: it always contained two courses' worth of stone, it just
/// drew them as one.
/// </summary>
public sealed class BlockEntityBehaviorRockPileConverter(BlockEntity blockentity)
    : BlockEntityBehavior(blockentity)
{
    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (api.Side == EnumAppSide.Server)
        {
            // A zero-delay callback lets the ground storage finish restoring its inventory (and
            // lets player placement finish assigning its stack) before we read it.
            Blockentity.RegisterDelayedCallback(_ => TryConvert(), 0);
        }
    }

    private void TryConvert()
    {
        if (Blockentity is not BlockEntityGroundStorage storage)
        {
            return;
        }

        // Only the Stacking layout is a stone pile. Quadrants and single-item layouts hold
        // everything else the game drops on the floor and must stay vanilla.
        if (storage.StorageProps?.Layout != EnumGroundStorageLayout.Stacking)
        {
            return;
        }

        var stones = CollectStones(storage);
        if (stones is null || stones.Count == 0)
        {
            return;
        }

        try
        {
            if (Api.World.GetBlock(RockPileUtil.BlockCode) is not BlockRockPile pileBlock)
            {
                Api.Logger.Warning(
                    "Acervus Lapidum left the stone pile at {0} unchanged because {1} is missing.",
                    Pos,
                    RockPileUtil.BlockCode);
                return;
            }

            // Snapshot before the SetBlock below tears this block entity down.
            var pos = Pos.Copy();
            var total = stones.Count;

            var placed = 0;
            var segment = 0;
            while (placed < total)
            {
                var target = pos.UpCopy(segment);
                if (target.Y >= Api.World.BlockAccessor.MapSizeY)
                {
                    break;
                }

                // Only the first segment replaces the ground storage; the ones above it need
                // empty space to move into, and stones we cannot place stay in the world as items.
                if (segment > 0 && Api.World.BlockAccessor.GetBlock(target).Replaceable < 6000)
                {
                    break;
                }

                Api.World.BlockAccessor.SetBlock(pileBlock.Id, target);
                if (Api.World.BlockAccessor.GetBlockEntity(target) is not BlockEntityRockPile pile)
                {
                    Api.Logger.Error(
                        "Acervus Lapidum replaced the stone pile at {0}, but no rock pile entity was created.",
                        target);
                    return;
                }

                // Ask the pile itself rather than assuming 32: it knows how far up the column it
                // sits, and an upper course holds fewer stones.
                var course = stones.GetRange(placed, Math.Min(pile.SlotCount, total - placed));
                pile.PopulateFrom(course, RockPileLayoutMode.Heap);
                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(target);

                placed += course.Count;
                segment++;
            }

            // Anything that did not fit — a ceiling in the way, say — is handed back rather than
            // deleted. Losing a player's stone to a migration they never asked for is not on.
            for (var i = placed; i < total; i++)
            {
                Api.World.SpawnItemEntity(stones[i], pos.ToVec3d().Add(0.5, 1.0, 0.5));
            }

            Api.Logger.Debug(
                "Acervus Lapidum converted the stone pile at {0} into {1} rock pile segment(s), {2} stone(s).",
                pos,
                segment,
                placed);
        }
        catch (Exception exception)
        {
            Api.Logger.Error(
                "Acervus Lapidum failed to convert the stone pile at {0}: {1}",
                Pos,
                exception);
        }
    }

    /// <summary>
    /// The pile's stones, one stack entry per stone. Returns null the moment anything that is not
    /// a stone turns up, so a mixed or unfamiliar Stacking pile is left for vanilla to keep.
    /// </summary>
    private static List<ItemStack>? CollectStones(BlockEntityGroundStorage storage)
    {
        var stones = new List<ItemStack>();
        foreach (var slot in storage.Inventory)
        {
            if (slot.Empty)
            {
                continue;
            }

            if (!RockPileUtil.IsPileableStone(slot.Itemstack))
            {
                return null;
            }

            for (var i = 0; i < slot.Itemstack!.StackSize; i++)
            {
                var single = slot.Itemstack.Clone();
                single.StackSize = 1;
                stones.Add(single);
            }
        }

        return stones;
    }
}
