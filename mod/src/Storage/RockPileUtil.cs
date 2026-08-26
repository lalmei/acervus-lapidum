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
    Scattered = 4,
    Masonry = 5,
    Ring = 6,
    Spiral = 7,
    Steps = 8,
    Balanced = 9,
    TwinColumns = 10
}

public static class RockPileUtil
{
    /// <summary>
    /// The most stones any single pile can hold, and so the inventory size. Masonry earns it:
    /// tiling a whole cube takes twelve stones a course and eight courses.
    ///
    /// What a *particular* pile holds is the slot count of the layout it is wearing — see
    /// <see cref="RockPileLayoutConfig.ForMode"/>. A heap holds 32, a cairn crown 19, a balanced
    /// stack 7.
    /// </summary>
    public const int MaxSlots = 96;

    /// <summary>
    /// Vanilla's own loose-pile density: the top cube of <c>item/stone-pile</c> sits at 12.4px, so
    /// 32 stones fill a block the way the game already fills one. Heap, neat and scattered hold
    /// exactly this, so tipping stone on the ground behaves as it always did.
    /// </summary>
    public const int HeapCapacity = 32;

    /// <summary>Rotation steps a pile can be turned through, at 45 degrees each.</summary>
    public const int OrientationSteps = 8;

    /// <summary>
    /// Whether a pile laid this way, in this position, fills its block solidly enough to stand on
    /// and build against.
    ///
    /// Masonry always. Steps only once something is stacked on it: a flight of stairs taller than
    /// one block is carried by solid stone underneath, so a steps pile with a load above stops
    /// being a stair and becomes the footing for the one above it.
    /// </summary>
    public static bool IsSolidLayout(RockPileLayoutMode mode, bool loadAbove = false)
    {
        return mode == RockPileLayoutMode.Masonry
               || (mode == RockPileLayoutMode.Steps && loadAbove);
    }

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
    /// A box around the stones that are actually there.
    ///
    /// Measured rather than assumed, in all three axes: a pile you have barely started is ankle
    /// high, a balanced stack is a narrow post you can walk around, and a finished masonry course
    /// comes out a full cube — which is what lets it behave as a solid block without a special
    /// case here.
    /// </summary>
    public static Cuboidf CollisionForCount(RockPileSlotTransform[] layout, int stoneCount, float yawDeg = 0f)
    {
        var count = Math.Clamp(stoneCount, 1, layout.Length);
        if (count == 0)
        {
            return new Cuboidf(0.05f, 0, 0.05f, 0.95f, 0.125f, 0.95f);
        }

        float minX = 1f, minZ = 1f, maxX = 0f, maxZ = 0f, top = StoneHeight;
        var spin = new Matrixf().RotateYDeg(yawDeg).Values;

        for (var i = 0; i < count; i++)
        {
            var pose = layout[i];
            top = Math.Max(top, SlotTopHeight(pose));

            // Half-extents of the stone once its own pose is applied, then the whole set is spun
            // by the pile's orientation about the block centre.
            var m = SlotRotation(pose).Values;
            var halfX = Math.Abs(m[0]) * (StoneLength / 2f)
                        + Math.Abs(m[4]) * StoneHeight
                        + Math.Abs(m[8]) * (StoneDepth / 2f);
            var halfZ = Math.Abs(m[2]) * (StoneLength / 2f)
                        + Math.Abs(m[6]) * StoneHeight
                        + Math.Abs(m[10]) * (StoneDepth / 2f);

            var dx = pose.X - 0.5f;
            var dz = pose.Z - 0.5f;
            var cx = 0.5f + spin[0] * dx + spin[8] * dz;
            var cz = 0.5f + spin[2] * dx + spin[10] * dz;
            var reach = Math.Max(halfX, halfZ);

            minX = Math.Min(minX, cx - reach);
            maxX = Math.Max(maxX, cx + reach);
            minZ = Math.Min(minZ, cz - reach);
            maxZ = Math.Max(maxZ, cz + reach);
        }

        return new Cuboidf(
            Math.Clamp(minX, 0f, 0.45f),
            0,
            Math.Clamp(minZ, 0f, 0.45f),
            Math.Clamp(maxX, 0.55f, 1f),
            Math.Clamp(top + 0.03f, 0.125f, 1f),
            Math.Clamp(maxZ, 0.55f, 1f));
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

    /// <summary>A whole cube of coursed stone, and the only layout that yields a solid block.</summary>
    [JsonProperty("masonry")]
    public RockPileSlotTransform[] Masonry { get; set; } = [];

    [JsonProperty("ring")]
    public RockPileSlotTransform[] Ring { get; set; } = [];

    [JsonProperty("spiral")]
    public RockPileSlotTransform[] Spiral { get; set; } = [];

    [JsonProperty("steps")]
    public RockPileSlotTransform[] Steps { get; set; } = [];

    [JsonProperty("balanced")]
    public RockPileSlotTransform[] Balanced { get; set; } = [];

    [JsonProperty("twincolumns")]
    public RockPileSlotTransform[] TwinColumns { get; set; } = [];

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
    /// The slot poses for a pile in this mode, at this height up a column, carrying or not
    /// carrying something above it.
    ///
    /// Cairn is the one that reads the segment, narrowing as it climbs. Steps is the one that
    /// reads the load: put a pile on a flight of stairs and the flight becomes the solid footing
    /// for it, so the stair carries on up rather than starting again at the bottom of every block.
    /// </summary>
    public RockPileSlotTransform[] ForMode(RockPileLayoutMode mode, int segment, bool loadAbove = false)
    {
        if (mode == RockPileLayoutMode.Steps && loadAbove && Masonry is { Length: > 0 })
        {
            return Masonry;
        }

        var configured = mode switch
        {
            RockPileLayoutMode.Neat => Neat,
            RockPileLayoutMode.Wall => Wall,
            RockPileLayoutMode.Scattered => Scattered,
            RockPileLayoutMode.Masonry => Masonry,
            RockPileLayoutMode.Ring => Ring,
            RockPileLayoutMode.Spiral => Spiral,
            RockPileLayoutMode.Steps => Steps,
            RockPileLayoutMode.Balanced => Balanced,
            RockPileLayoutMode.TwinColumns => TwinColumns,
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
        var slots = new RockPileSlotTransform[RockPileUtil.HeapCapacity];
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
