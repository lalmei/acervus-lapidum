using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace AcervusLapidum.Storage;

/// <summary>
/// Migrates vanilla stone piles into rock piles as their block entity enters a loaded server
/// chunk — but only when the server has been told to.
///
/// This used to run unconditionally, and it should not have. Rewriting blocks in a world the
/// moment a mod is dropped into it is a change nobody asked for and, until the pile is reverted,
/// one they cannot easily undo: uninstalling the mod takes every rock pile block with it. Vanilla
/// piles work perfectly well alongside ours, so the default is now to leave them alone and let
/// <c>/rockpile convert</c> — or <c>convertVanillaPilesOnLoad</c> in the config — say otherwise.
/// </summary>
public sealed class BlockEntityBehaviorRockPileConverter(BlockEntity blockentity)
    : BlockEntityBehavior(blockentity)
{
    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        if (api.Side != EnumAppSide.Server
            || !AcervusLapidumModSystem.Config.ConvertVanillaPilesOnLoad
            || Blockentity is not BlockEntityGroundStorage)
        {
            return;
        }

        // A zero-delay callback lets the ground storage finish restoring its inventory (and lets
        // player placement finish assigning its stack) before we read it.
        Blockentity.RegisterDelayedCallback(_ => RockPileMigration.Convert(Api, Blockentity), 0);
    }
}
