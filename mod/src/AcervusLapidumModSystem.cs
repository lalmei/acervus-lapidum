using AcervusLapidum.Items;
using AcervusLapidum.Storage;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AcervusLapidum;

public sealed class AcervusLapidumModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.Logger.Event(AcervusLapidumModMetadata.StartupLogMessage);
        api.RegisterCollectibleBehaviorClass("RockPileable", typeof(CollectibleBehaviorRockPileable));
        api.RegisterBlockClass("BlockRockPile", typeof(BlockRockPile));
        api.RegisterBlockEntityClass("RockPile", typeof(BlockEntityRockPile));
        api.RegisterBlockEntityBehaviorClass(
            "RockPileConverter",
            typeof(BlockEntityBehaviorRockPileConverter));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        PreferRockPileOverGroundStorage(api);
    }

    /// <summary>
    /// Stones ship with vanilla's GroundStorable (Stacking). Strip it so a sneak-place builds one
    /// of our piles instead, and attach RockPileable to any stone the JSON patch missed — which
    /// is most rock types added by other mods, since they define their own item files.
    ///
    /// Existing vanilla piles in the world stay valid: they are migrated by
    /// <see cref="BlockEntityBehaviorRockPileConverter"/> when their chunk loads, not by this.
    /// </summary>
    private static void PreferRockPileOverGroundStorage(ICoreAPI api)
    {
        var stripped = 0;
        var attached = 0;

        foreach (var item in api.World.Items)
        {
            if (!RockPileUtil.IsPileableStone(item))
            {
                continue;
            }

            if (item.CollectibleBehaviors is null)
            {
                item.CollectibleBehaviors = [];
            }

            var before = item.CollectibleBehaviors.Length;
            item.CollectibleBehaviors = item.CollectibleBehaviors
                .Where(behavior => behavior is not CollectibleBehaviorGroundStorable)
                .ToArray();

            if (item.CollectibleBehaviors.Length != before)
            {
                stripped++;
            }

            if (item.HasBehavior<CollectibleBehaviorRockPileable>())
            {
                continue;
            }

            var behavior = new CollectibleBehaviorRockPileable(item);
            behavior.Initialize(new Vintagestory.API.Datastructures.JsonObject(
                Newtonsoft.Json.Linq.JObject.Parse("{}")));
            item.CollectibleBehaviors = item.CollectibleBehaviors.Append(behavior).ToArray();
            attached++;
        }

        api.Logger.Event(
            "Acervus Lapidum rock piles: removed GroundStorable from {0} stone variant(s), attached RockPileable to {1}.",
            stripped,
            attached);
    }
}
