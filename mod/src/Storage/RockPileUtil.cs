using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AcervusLapidum.Storage;

/// <summary>
/// Values are persisted in block entity and item attributes, so never renumber them.
/// </summary>
public enum RockPileLayoutMode
{
    Heap = 0,
    Neat = 1,
    Cairn = 2,
    Wall = 3,
    Scattered = 4
}

public static class RockPileUtil
{
    /// <summary>
    /// The most stones any single pile can hold, and so the inventory size.
    ///
    /// Thirty-two, because that is what one block holds at vanilla's own visual density: the top
    /// cube of <c>item/stone-pile</c> sits at 12.4px, so a 32nd stone lands just under the ceiling.
    /// Stone 33 has nowhere to go but the block above, which is exactly how a pile becomes a cairn.
    ///
    /// What a *particular* pile holds is the slot count of the layout it is wearing, which is
    /// smaller for the upper courses of a cairn — see <see cref="RockPileLayoutConfig.ForMode"/>.
    /// </summary>
    public const int Capacity = 32;

    /// <summary>One stone a click. Vanilla moves two, but vanilla draws one rock per two stones.</summary>
    public const int TransferQuantity = 1;
    public const int BulkTransferQuantity = 8;

    /// <summary>How many distinct cairn segment profiles the layout config ships.</summary>
    public const int CairnSegmentProfiles = 3;

    public const string LayoutAttr = "rockPileLayout";
    public static readonly AssetLocation BlockCode = new("acervuslapidum", "rockpile");
    public static readonly AssetLocation LayoutConfig = new("acervuslapidum", "config/rockpile-layout.json");

    /// <summary>The stone item cube, in block units: 5 x 2 x 4 pixels centred on the block.</summary>
    public const float StoneLength = 5f / 16f;
    public const float StoneHeight = 2f / 16f;
    public const float StoneDepth = 4f / 16f;

    /// <summary>
    /// What counts as a stone. Matching on the code prefix rather than a fixed list means the rock
    /// types added by Geology Addons and friends pile too, without a compatibility patch each.
    /// </summary>
    public static bool IsPileableStone(CollectibleObject? collectible)
    {
        if (collectible is null)
        {
            return false;
        }

        return collectible is ItemStone
               || collectible.Code?.Path.StartsWith("stone-", StringComparison.Ordinal) == true;
    }

    public static bool IsPileableStone(ItemStack? stack) => IsPileableStone(stack?.Collectible);

    /// <summary>Vanilla's four loose-stone samples, picked at random so repeated clicks vary.</summary>
    public static AssetLocation PlaceSound(IWorldAccessor world)
    {
        return new AssetLocation("game", $"sounds/block/loosestone{1 + world.Rand.Next(4)}");
    }

    /// <summary>Wraps round the enum, for the empty-handed hotkey that cycles instead of picking.</summary>
    public static RockPileLayoutMode NextLayoutMode(RockPileLayoutMode mode)
    {
        var modes = Enum.GetValues<RockPileLayoutMode>();
        var index = Array.IndexOf(modes, mode);
        return modes[(index + 1) % modes.Length];
    }

    /// <summary>Keeps stored or networked values inside the enum as modes come and go.</summary>
    public static RockPileLayoutMode ClampLayoutMode(int mode)
    {
        return Enum.IsDefined(typeof(RockPileLayoutMode), mode)
            ? (RockPileLayoutMode)mode
            : RockPileLayoutMode.Heap;
    }

    public static RockPileLayoutMode GetHeldLayoutMode(ItemStack? stack)
    {
        if (stack?.Attributes is null)
        {
            return RockPileLayoutMode.Heap;
        }

        return ClampLayoutMode(stack.Attributes.GetInt(LayoutAttr, (int)RockPileLayoutMode.Heap));
    }

    public static void SetHeldLayoutMode(ItemStack? stack, RockPileLayoutMode mode)
    {
        stack?.Attributes?.SetInt(LayoutAttr, (int)mode);
    }

    private static Matrixf SlotRotation(RockPileSlotTransform slot)
    {
        // Same order genTransformationMatrices applies, so the pose here matches what is drawn.
        return new Matrixf()
            .RotateYDeg(slot.YawDeg)
            .RotateXDeg(slot.PitchDeg)
            .RotateZDeg(slot.RollDeg);
    }

    /// <summary>
    /// How high the stone in this slot reaches. Exact for any pose, which matters once a layout
    /// tips stones on edge — assuming a flat stone would leave a tilted cairn's crown poking out
    /// of its own selection box, and you cannot click what you cannot hit.
    /// </summary>
    public static float SlotTopHeight(RockPileSlotTransform slot)
    {
        // Column-major 4x4: what each local axis contributes to world Y sits at 1, 5 and 9. Unlike
        // Liber Terra's books there is no baked-in ground transform to cancel here, because the
        // stone mesh reaches the pile exactly as its shape file draws it.
        var pose = SlotRotation(slot).Values;
        var reach = Math.Abs(pose[1]) * (StoneLength / 2f)
                    + Math.Abs(pose[5]) * StoneHeight
                    + Math.Abs(pose[9]) * (StoneDepth / 2f);

        return slot.Y + reach;
    }

    /// <summary>
    /// Hugs the tallest occupied slot, so a pile you have barely started is ankle high and a full
    /// one is knee high, rather than every pile claiming the same box.
    /// </summary>
    public static Cuboidf CollisionForCount(RockPileSlotTransform[] layout, int stoneCount)
    {
        var count = Math.Clamp(stoneCount, 1, Capacity);
        var top = StoneHeight;
        for (var i = 0; i < count && i < layout.Length; i++)
        {
            top = Math.Max(top, SlotTopHeight(layout[i]));
        }

        return new Cuboidf(0.05f, 0, 0.05f, 0.95f, Math.Clamp(top + 0.03f, 0.125f, 1f), 0.95f);
    }
}

/// <summary>
/// One stone pose inside a pile, in block-local space. Because the render chain pivots on the
/// stone's own bottom-centre, (X, Y, Z) is where that bottom-centre lands and the angles are the
/// stone's own — see tools/rockpile_geometry.py, which writes these.
/// </summary>
public sealed class RockPileSlotTransform
{
    [JsonProperty("x")]
    public float X { get; set; } = 0.5f;

    [JsonProperty("y")]
    public float Y { get; set; }

    [JsonProperty("z")]
    public float Z { get; set; } = 0.5f;

    [JsonProperty("yawDeg")]
    public float YawDeg { get; set; }

    [JsonProperty("pitchDeg")]
    public float PitchDeg { get; set; }

    [JsonProperty("rollDeg")]
    public float RollDeg { get; set; }
}

public sealed class RockPileLayoutConfig
{
    [JsonProperty("heap")]
    public RockPileSlotTransform[] Heap { get; set; } = [];

    [JsonProperty("neat")]
    public RockPileSlotTransform[] Neat { get; set; } = [];

    [JsonProperty("wall")]
    public RockPileSlotTransform[] Wall { get; set; } = [];

    [JsonProperty("scattered")]
    public RockPileSlotTransform[] Scattered { get; set; } = [];

    /// <summary>Widest cairn course, for the segment sitting on the ground.</summary>
    [JsonProperty("cairn0")]
    public RockPileSlotTransform[] Cairn0 { get; set; } = [];

    [JsonProperty("cairn1")]
    public RockPileSlotTransform[] Cairn1 { get; set; } = [];

    /// <summary>The spire. Every segment above the second reuses it, so tall cairns stay pointed.</summary>
    [JsonProperty("cairn2")]
    public RockPileSlotTransform[] Cairn2 { get; set; } = [];

    /// <summary>
    /// The slot poses for a pile in this mode at this height up a cairn column. Only Cairn cares
    /// about the segment; every other layout looks the same wherever it sits.
    /// </summary>
    public RockPileSlotTransform[] ForMode(RockPileLayoutMode mode, int segment)
    {
        var configured = mode switch
        {
            RockPileLayoutMode.Neat => Neat,
            RockPileLayoutMode.Wall => Wall,
            RockPileLayoutMode.Scattered => Scattered,
            RockPileLayoutMode.Cairn => Math.Clamp(segment, 0, RockPileUtil.CairnSegmentProfiles - 1) switch
            {
                0 => Cairn0,
                1 => Cairn1,
                _ => Cairn2
            },
            _ => Heap
        };

        return configured is { Length: > 0 } ? configured : CreateDefault();
    }

    /// <summary>
    /// A stand-in for when config/rockpile-layout.json fails to load, so a pile is still a pile
    /// rather than 32 stones in one spot. The shipped asset wins whenever it reads.
    /// </summary>
    public static RockPileSlotTransform[] CreateDefault()
    {
        var slots = new RockPileSlotTransform[RockPileUtil.Capacity];
        for (var i = 0; i < slots.Length; i++)
        {
            // Four to a layer on the block quarters, alternating course direction.
            var layer = i / 4;
            var quadrant = i % 4;
            slots[i] = new RockPileSlotTransform
            {
                X = quadrant % 2 == 0 ? 0.3f : 0.7f,
                Y = layer * RockPileUtil.StoneHeight,
                Z = quadrant < 2 ? 0.3f : 0.7f,
                YawDeg = layer % 2 == 0 ? 0f : 90f
            };
        }

        return slots;
    }
}
