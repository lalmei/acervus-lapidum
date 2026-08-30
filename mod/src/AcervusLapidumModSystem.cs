using AcervusLapidum.Items;
using AcervusLapidum.Storage;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AcervusLapidum;

public sealed class AcervusLapidumModSystem : ModSystem
{
    /// <summary>
    /// Read by the converter behavior, which has no other way to reach it. Server-authored; the
    /// client's copy is the defaults and is never consulted.
    /// </summary>
    public static AcervusLapidumConfig Config { get; private set; } = new();

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

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        base.StartServerSide(sapi);
        Config = AcervusLapidumConfig.Load(sapi);
        RockPileCommands.Register(sapi);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        PreferRockPileOverGroundStorage(api);
    }

    /// <summary>
    /// Puts RockPileable in front of vanilla's GroundStorable on every stone, so a sneak + Ctrl
    /// place builds one of our piles, and attaches it to any stone the JSON patch missed — which
    /// is most rock types added by other mods, since they define their own item files.
    ///
    /// Ordering, emphatically not removal. This used to strip GroundStorable off every stone, on
    /// the reasoning that nothing should be able to make a vanilla stone pile any more. It also
    /// broke every stone pile that already existed: <c>BlockEntityGroundStorage</c> does not
    /// persist its storage properties, it looks them up from the held stone's GroundStorable
    /// behavior each time it loads. With the behavior gone the lookup returned null, and a pile
    /// with no storage properties draws no mesh, refuses every interaction and cannot say what
    /// layout it is. Piles vanished where they stood and could only be broken to get the stone
    /// back.
    ///
    /// Leaving the behavior in place costs nothing: RockPileable runs first and returns
    /// PreventSubsequent whenever it handles the click, so vanilla never gets a look in at a new
    /// pile — while old piles keep rendering, keep giving stones back, and keep working for anyone
    /// who removes this mod later.
    /// </summary>
    private static void PreferRockPileOverGroundStorage(ICoreAPI api)
    {
        var attached = 0;
        var reordered = 0;

        foreach (var item in api.World.Items)
        {
            if (!RockPileUtil.IsPileableStone(item))
            {
                continue;
            }

            item.CollectibleBehaviors ??= [];

            if (!item.HasBehavior<CollectibleBehaviorRockPileable>())
            {
                var behavior = new CollectibleBehaviorRockPileable(item);
                behavior.Initialize(new Vintagestory.API.Datastructures.JsonObject(
                    Newtonsoft.Json.Linq.JObject.Parse("{}")));
                item.CollectibleBehaviors = item.CollectibleBehaviors.Append(behavior).ToArray();
                attached++;
            }

            // Whether it arrived from the JSON patch or was appended just now, it has to sit ahead
            // of GroundStorable: the first behavior to claim the click is the one that gets it.
            if (item.CollectibleBehaviors[0] is CollectibleBehaviorRockPileable)
            {
                continue;
            }

            item.CollectibleBehaviors = item.CollectibleBehaviors
                .OrderBy(behavior => behavior is CollectibleBehaviorRockPileable ? 0 : 1)
                .ToArray();
            reordered++;
        }

        api.Logger.Event(
            "Acervus Lapidum rock piles: attached RockPileable to {0} stone variant(s), reordered {1}. "
            + "Vanilla ground storage is left in place so existing stone piles keep working.",
            attached,
            reordered);
    }
}
