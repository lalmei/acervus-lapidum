using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AcervusLapidum.Storage;

/// <summary>
/// <c>/rockpile convert</c> and <c>/rockpile revert</c>: the two halves of changing your mind
/// about this mod.
///
/// Conversion is a command rather than something that happens to you on load, and reversion exists
/// at all, because a rock pile is a block this mod owns. Uninstall with rock piles in the ground
/// and the game cannot resolve <c>acervuslapidum:rockpile</c> any more — the blocks and the stones
/// inside them go with it. Reverting first turns them back into vanilla piles that survive on
/// their own.
/// </summary>
public static class RockPileCommands
{
    /// <summary>Blocks either side of the caller when no radius is given.</summary>
    private const int DefaultRadius = 32;

    /// <summary>Chunks are 32 blocks; anything past this sweeps more of them than one tick wants.</summary>
    private const int MaxRadius = 512;

    public static void Register(ICoreServerAPI sapi)
    {
        var parsers = sapi.ChatCommands.Parsers;

        sapi.ChatCommands
            .Create("rockpile")
            .WithDescription(Lang.Get("acervuslapidum:command-rockpile-desc"))
            .RequiresPrivilege(Privilege.controlserver)
            .RequiresPlayer()
            .BeginSubCommand("convert")
                .WithDescription(Lang.Get("acervuslapidum:command-rockpile-convert-desc"))
                .WithArgs(parsers.OptionalWord("radius"))
                .HandleWith(args => Sweep(sapi, args, convert: true))
            .EndSubCommand()
            .BeginSubCommand("revert")
                .WithDescription(Lang.Get("acervuslapidum:command-rockpile-revert-desc"))
                .WithArgs(parsers.OptionalWord("radius"))
                .HandleWith(args => Sweep(sapi, args, convert: false))
            .EndSubCommand();
    }

    /// <summary>
    /// Walks the loaded chunks in range and migrates every pile it finds, one way or the other.
    ///
    /// Chunks rather than blocks: a 32 block radius is a quarter of a million positions to ask
    /// about individually, and every chunk already keeps a list of exactly the block entities we
    /// are looking for.
    /// </summary>
    private static TextCommandResult Sweep(ICoreServerAPI sapi, TextCommandCallingArgs args, bool convert)
    {
        var radiusArg = args[0] as string;
        var everywhere = string.Equals(radiusArg, "all", StringComparison.OrdinalIgnoreCase);

        var radius = DefaultRadius;
        if (!everywhere && !string.IsNullOrEmpty(radiusArg)
            && (!int.TryParse(radiusArg, out radius) || radius < 1 || radius > MaxRadius))
        {
            return TextCommandResult.Error(
                Lang.Get("acervuslapidum:command-rockpile-badradius", MaxRadius));
        }

        var origin = args.Caller.Pos?.AsBlockPos ?? new BlockPos(0, 0, 0);
        var chunks = everywhere ? AllLoadedChunks(sapi) : ChunksAround(sapi, origin, radius);

        var piles = 0;
        var stones = 0;
        var chunkCount = 0;

        foreach (var chunk in chunks)
        {
            chunkCount++;

            // Snapshot: migrating replaces block entities, which edits the dictionary underneath us.
            foreach (var blockEntity in chunk.BlockEntities.Values.ToArray())
            {
                var moved = convert
                    ? RockPileMigration.Convert(sapi, blockEntity)
                    : RockPileMigration.Revert(sapi, blockEntity);

                // Revert legitimately returns zero for an empty pile it still removed, so count the
                // pile by what it was rather than by what came out of it.
                var touched = convert
                    ? blockEntity is BlockEntityGroundStorage && moved > 0
                    : blockEntity is BlockEntityRockPile;

                if (touched)
                {
                    piles++;
                    stones += moved;
                }
            }
        }

        var key = convert
            ? "acervuslapidum:command-rockpile-converted"
            : "acervuslapidum:command-rockpile-reverted";

        return TextCommandResult.Success(Lang.Get(key, piles, stones, chunkCount));
    }

    private static IEnumerable<IWorldChunk> ChunksAround(ICoreServerAPI sapi, BlockPos origin, int radius)
    {
        var size = sapi.WorldManager.ChunkSize;
        var minX = (origin.X - radius) / size;
        var maxX = (origin.X + radius) / size;
        var minZ = (origin.Z - radius) / size;
        var maxZ = (origin.Z + radius) / size;

        // Every vertical layer: piles sit anywhere from a cellar floor to a mountain top, and the
        // caller asked about a horizontal area, not a box around their feet.
        var maxY = sapi.World.BlockAccessor.MapSizeY / size;

        for (var cx = minX; cx <= maxX; cx++)
        {
            for (var cz = minZ; cz <= maxZ; cz++)
            {
                for (var cy = 0; cy <= maxY; cy++)
                {
                    if (sapi.World.BlockAccessor.GetChunk(cx, cy, cz) is { } chunk)
                    {
                        yield return chunk;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Every chunk the server currently has in memory — which is not the whole world, and cannot
    /// be. Chunks nobody is near are on disk and untouchable from here.
    /// </summary>
    private static IEnumerable<IWorldChunk> AllLoadedChunks(ICoreServerAPI sapi)
    {
        var chunkMapSizeX = sapi.WorldManager.MapSizeX / sapi.WorldManager.ChunkSize;
        var chunkMapSizeZ = sapi.WorldManager.MapSizeZ / sapi.WorldManager.ChunkSize;

        foreach (var index in sapi.World.LoadedChunkIndices)
        {
            var cx = (int)(index % chunkMapSizeX);
            var cy = (int)(index / ((long)chunkMapSizeX * chunkMapSizeZ));
            var cz = (int)(index / chunkMapSizeX % chunkMapSizeZ);

            if (sapi.World.BlockAccessor.GetChunk(cx, cy, cz) is { } chunk)
            {
                yield return chunk;
            }
        }
    }
}
