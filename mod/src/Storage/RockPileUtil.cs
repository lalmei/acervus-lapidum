using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AcervusLapidum.Storage;

/// <summary>
/// Values are persisted in block entity and item attributes, so never renumber them. A layout
/// that is withdrawn leaves its number behind as a gap — <see cref="RockPileUtil.ClampLayoutMode"/>
/// turns a saved pile wearing it back into a heap, and the pickers walk
/// <see cref="RockPileLayoutModes.PickerModes"/> rather than counting from zero, so the gap costs
/// nothing but keeps every other layout where existing worlds left it.
/// </summary>
public enum RockPileLayoutMode
{
    Heap = 0,
    Neat = 1,
    Cairn = 2,
    Wall = 3,
    // 4 was Scattered: a thin spread the heap already covered. Withdrawn, and the number
    // with it, so every layout below keeps the value existing saves wrote.
    Masonry = 5,
    Ring = 6,
    Spiral = 7,
    Steps = 8,
    Balanced = 9,
    TwinColumns = 10,
    Arrow = 11,
    // 12 was Arch: three voussoirs and a keystone, which at sixteen pixels read as a lump of
    // stone with a notch in it rather than as an arch. Withdrawn, and the number with it.
    NicheCairn = 13
}

public static class RockPileUtil
{
    /// <summary>
    /// The inventory size, and so the ceiling on any layout.
    ///
    /// It sits above the largest layout rather than on it. Masonry reaches highest at 72 — nine
    /// jointed stones a course, eight courses — but it held 96 back when its courses tiled the
    /// cube face to face, and piles saved then still have to load before
    /// <see cref="BlockEntityRockPile"/> can hand the surplus back.
    ///
    /// What a *particular* pile holds is the slot count of the layout it is wearing — see
    /// <see cref="RockPileLayoutConfig.ForMode"/>. A heap holds 32, a cairn crown 19, a balanced
    /// stack 7.
    /// </summary>
    public const int MaxSlots = 96;

    /// <summary>
    /// The niche's slot, past every stone slot.
    ///
    /// Reserved rather than borrowed from the stones: what the niche holds is not a stone and must
    /// not be counted as one, shed as surplus when the layout narrows, or handed to the vanilla
    /// pile the revert command builds. Every stone-side walk stops at <see cref="MaxSlots"/>, so
    /// the one thing that can reach this slot is the niche itself.
    /// </summary>
    public const int NicheSlotIndex = MaxSlots;

    /// <summary>Stone slots plus the niche.</summary>
    public const int InventorySize = MaxSlots + 1;

    /// <summary>Whether a pile laid this way has a niche to put anything in.</summary>
    public static bool HasNiche(RockPileLayoutMode mode) => mode == RockPileLayoutMode.NicheCairn;

    /// <summary>
    /// Vanilla's own loose-pile density: the top cube of <c>item/stone-pile</c> sits at 12.4px, so
    /// 32 stones fill a block the way the game already fills one. Heap and neat hold exactly
    /// this, so tipping stone on the ground behaves as it always did.
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

    /// <summary>Keeps stored or networked values inside the enum as modes come and go.</summary>
    public static RockPileLayoutMode ClampLayoutMode(int mode)
    {
        return Enum.IsDefined(typeof(RockPileLayoutMode), mode)
            ? (RockPileLayoutMode)mode
            : RockPileLayoutMode.Heap;
    }

    /// <summary>
    /// The layout this player last picked, for the next pile they start.
    ///
    /// Kept on the player, not on the stone. A stone carrying the choice as a stack attribute is
    /// no longer equal to a plain stone: it stops merging in a hotbar slot, on the ground and in
    /// a chest, so picking a layout quietly split every rock you were holding away from every
    /// other rock in the world. A loose rock is a loose rock — the preference is the player's.
    /// </summary>
    public static RockPileLayoutMode GetPreferredLayoutMode(Entity? entity)
    {
        if (entity?.WatchedAttributes is null)
        {
            return RockPileLayoutMode.Heap;
        }

        return ClampLayoutMode(entity.WatchedAttributes.GetInt(LayoutAttr, (int)RockPileLayoutMode.Heap));
    }

    public static void SetPreferredLayoutMode(Entity? entity, RockPileLayoutMode mode)
    {
        entity?.WatchedAttributes?.SetInt(LayoutAttr, (int)mode);
    }

    /// <summary>
    /// Takes the layout marker off a stone left over from when the choice rode on the stack.
    ///
    /// Nothing writes it any more, but stones in existing worlds still carry it, and while they
    /// do they will not stack with plain ones. Every stone that passes through a pile or a hand
    /// is scrubbed on the way, so old saves heal themselves as they are played.
    /// </summary>
    public static ItemStack? ClearHeldLayoutMode(ItemStack? stack)
    {
        stack?.Attributes?.RemoveAttribute(LayoutAttr);
        return stack;
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

    /// <summary>
    /// Set on a stone in a <em>bond course</em>: one that lays a stone across the joint with the
    /// pile next door, the way a through stone ties a wall together. Without them every block
    /// boundary shows an unbroken vertical joint on every course, because each pile is otherwise
    /// a self-contained brick.
    ///
    /// Four positions, indexed by what the course has to tie into — <c>[none, ahead, behind,
    /// both]</c>. Both ends matter: reasoning about the pile behind alone left the far end of a
    /// run notched open while the near end sat flush, so the same wall looked different from each
    /// end and swapped over when you turned it round.
    ///
    /// Note this moves stones rather than adding them: bonding never changes how many stones a
    /// pile holds, so neighbours coming and going cannot strand or demand any.
    /// </summary>
    [JsonProperty("xBond")]
    public float[]? XBond { get; set; }

    /// <summary>Where this stone sits, given what the pile has to tie into on each side.</summary>
    public float XFor(bool behind, bool ahead)
    {
        if (XBond is not { Length: 4 })
        {
            return X;
        }

        return XBond[(behind ? 2 : 0) + (ahead ? 1 : 0)];
    }
}

public sealed class RockPileLayoutConfig
{
    [JsonProperty("heap")]
    public RockPileSlotTransform[] Heap { get; set; } = [];

    [JsonProperty("neat")]
    public RockPileSlotTransform[] Neat { get; set; } = [];

    [JsonProperty("wall")]
    public RockPileSlotTransform[] Wall { get; set; } = [];

    /// <summary>
    /// A whole cube of coursed stone, and the only layout that yields a solid block. Nine stones
    /// to a course, laid with a joint between them rather than face to face: stones that touch
    /// read as one milled slab, so the course takes the most stones that still leave daylight.
    ///
    /// Courses start from four different corners of the lattice in turn, so every gap has a stone
    /// above and below it — a pocket to pack, not a hole through a block that claims to be solid.
    /// Two alternating courses cannot manage that: the joints of one run across the joints of the
    /// other. See docs/masonry-bond.svg, which the generator draws from these very slots.
    /// </summary>
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

    /// <summary>A waypoint marker, half a block tall, pointing whichever way the pile is turned.</summary>
    [JsonProperty("arrow")]
    public RockPileSlotTransform[] Arrow { get; set; } = [];

    /// <summary>The cairn footing with a pocket cut into one face, for something to sit in.</summary>
    [JsonProperty("nichecairn")]
    public RockPileSlotTransform[] NicheCairn { get; set; } = [];

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
            RockPileLayoutMode.Masonry => Masonry,
            RockPileLayoutMode.Ring => Ring,
            RockPileLayoutMode.Spiral => Spiral,
            RockPileLayoutMode.Steps => Steps,
            RockPileLayoutMode.Balanced => Balanced,
            RockPileLayoutMode.TwinColumns => TwinColumns,
            RockPileLayoutMode.Arrow => Arrow,

            // One profile, not the cairn's three: a pocket belongs at the foot where you can reach
            // into it, and a segment stacked above this one is an ordinary cairn course.
            RockPileLayoutMode.NicheCairn => NicheCairn,
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
