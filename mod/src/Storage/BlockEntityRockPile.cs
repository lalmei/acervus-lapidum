using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AcervusLapidum.Storage;

/// <summary>
/// A pile of loose stones, one inventory slot per stone.
///
/// That one-slot-per-stone choice is the whole point: the pile renders the stone mesh once per
/// occupied slot, so what you see is exactly what you put in and exactly what you get back.
/// Vanilla's ground storage draws a fixed 32-cube model scaled to a 64-stone stack, which means
/// two stones share a rock and a half-empty pile looks full.
/// </summary>
public class BlockEntityRockPile : BlockEntityDisplay
{
    private readonly InventoryGeneric inventory;
    private RockPileLayoutConfig layoutConfig = new();
    private Cuboidf[] colBoxes = [RockPileUtil.CollisionForCount(RockPileLayoutConfig.CreateDefault(), 1)];
    private bool clientsideFirstPlacement;
    private RockPileLayoutMode layoutMode = RockPileLayoutMode.Heap;

    /// <summary>Which way a Wall layout runs. Ignored by every other mode.</summary>
    private int orientation;

    /// <summary>
    /// Cached so rendering does not walk the column every frame. Recomputed whenever a rockpile
    /// appears or disappears below us.
    /// </summary>
    private int segmentIndex;

    public BlockEntityRockPile()
    {
        inventory = new InventoryGeneric(RockPileUtil.Capacity, null, null, (_, inv) => new ItemSlot(inv));
        foreach (var slot in inventory)
        {
            slot.StorageType |= EnumItemStorageFlags.Backpack;
        }
    }

    public override InventoryBase Inventory => inventory;
    public override string InventoryClassName => "acervuslapidum-rockpile";

    /// <summary>
    /// The stone item has no ground storage transform of its own; we patch an identity one on so
    /// this resolves to a no-op and the mesh arrives exactly as its shape file draws it. The
    /// layout generator relies on that — see tools/rockpile_geometry.py.
    /// </summary>
    public override string AttributeTransformCode => "groundStorageTransform";

    public override string ClassCode => "acervuslapidumrockpile";

    public RockPileLayoutMode LayoutMode => layoutMode;
    public int Orientation => orientation;
    public int SegmentIndex => segmentIndex;

    public int StoneCount
    {
        get
        {
            var count = 0;
            foreach (var slot in inventory)
            {
                if (!slot.Empty)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// How many stones this pile can hold as it is currently laid. Not a constant: the upper
    /// courses of a cairn are narrower, and a narrow course genuinely holds fewer stones.
    /// </summary>
    public int SlotCount => Math.Min(CurrentLayout().Length, RockPileUtil.Capacity);

    public bool IsFull => StoneCount >= SlotCount;

    public Cuboidf[] GetCollisionBoxes() => colBoxes;

    public Cuboidf[] GetSelectionBoxes() => colBoxes;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        LoadLayout(api);
        RecalcSegmentIndex();
        RegenCollision();
    }

    private void LoadLayout(ICoreAPI api)
    {
        try
        {
            var asset = api.Assets.TryGet(RockPileUtil.LayoutConfig);
            if (asset is null)
            {
                return;
            }

            var config = asset.ToObject<RockPileLayoutConfig>();
            if (config is not null)
            {
                layoutConfig = config;
            }
        }
        catch (Exception exception)
        {
            api.Logger.Warning("Acervus Lapidum rock pile layout failed to load: {0}", exception.Message);
        }
    }

    private RockPileSlotTransform[] CurrentLayout() => layoutConfig.ForMode(layoutMode, segmentIndex);

    /// <summary>
    /// Restyles the pile, if the stones in it fit the new layout.
    /// </summary>
    /// <returns>False when they do not — a full heap has more stones than a cairn crown holds,
    /// and quietly hiding the surplus would break the one thing this pile promises.</returns>
    public bool SetLayoutMode(RockPileLayoutMode mode, bool propagate = true)
    {
        if (layoutMode == mode)
        {
            return true;
        }

        if (!CanHold(mode, StoneCount))
        {
            return false;
        }

        layoutMode = mode;
        RegenCollision();
        MarkMeshesDirty();
        MarkDirty(true);
        Api?.World.BlockAccessor.MarkBlockDirty(Pos);

        // A cairn is one object even though it is several blocks, so restyling any segment
        // restyles the column. Without this you can end up with a neat course wearing a cairn hat.
        if (propagate)
        {
            foreach (var segment in ColumnFrom(Pos))
            {
                segment.SetLayoutMode(mode, propagate: false);
            }
        }

        return true;
    }

    /// <summary>Whether this pile, where it sits, could wear that layout with this many stones.</summary>
    public bool CanHold(RockPileLayoutMode mode, int stoneCount)
    {
        return stoneCount <= layoutConfig.ForMode(mode, segmentIndex).Length;
    }

    public void SetOrientation(int value)
    {
        orientation = ((value % 4) + 4) % 4;
    }

    /// <summary>Every other rockpile in this vertical run, walking both ways from a position.</summary>
    private IEnumerable<BlockEntityRockPile> ColumnFrom(BlockPos origin)
    {
        if (Api is null)
        {
            yield break;
        }

        foreach (var step in new[] { -1, 1 })
        {
            var probe = origin.Copy();
            while (true)
            {
                probe = probe.AddCopy(0, step, 0);
                if (probe.Y < 0 || probe.Y >= Api.World.BlockAccessor.MapSizeY)
                {
                    break;
                }

                if (Api.World.BlockAccessor.GetBlockEntity(probe) is not BlockEntityRockPile pile)
                {
                    break;
                }

                yield return pile;
            }
        }
    }

    /// <summary>
    /// How far up a cairn this pile sits. Drives which segment profile it draws, so a column
    /// tapers instead of repeating one shape.
    /// </summary>
    public void RecalcSegmentIndex()
    {
        if (Api is null)
        {
            return;
        }

        var index = 0;
        var probe = Pos.DownCopy();
        while (probe.Y >= 0
               && Api.World.BlockAccessor.GetBlockEntity(probe) is BlockEntityRockPile
               && index < RockPileUtil.CairnSegmentProfiles)
        {
            index++;
            probe = probe.DownCopy();
        }

        if (index == segmentIndex)
        {
            return;
        }

        segmentIndex = index;
        RegenCollision();
        MarkMeshesDirty();
        Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }

    /// <summary>Empty-handed layout change, sent by <see cref="RockPileLayoutHotkey"/>.</summary>
    public const int PacketIdSetLayout = 5101;

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (packetid != PacketIdSetLayout)
        {
            base.OnReceivedClientPacket(fromPlayer, packetid, data);
            return;
        }

        if (data is not { Length: >= 4 }
            || !Api.World.Claims.TryAccess(fromPlayer, Pos, EnumBlockAccessFlags.BuildOrBreak))
        {
            // Bounce our real state back, so the sender's optimistic change does not stick.
            MarkDirty(true, fromPlayer);
            return;
        }

        SetLayoutMode(RockPileUtil.ClampLayoutMode(BitConverter.ToInt32(data, 0)));
    }

    public void MarkClientsideFirstPlacement()
    {
        clientsideFirstPlacement = true;
    }

    public bool OnPlayerInteract(IPlayer byPlayer, BlockSelection blockSel)
    {
        var hotbar = byPlayer.InventoryManager.ActiveHotbarSlot;
        // ShiftKey is the mouse modifier; Sneak is the crouch motion. Ground storage reads the
        // same flag here, and reading the other one desyncs put/take from the aim the click starts.
        var sneaking = byPlayer.Entity.Controls.ShiftKey;

        bool ok;
        if (sneaking && !hotbar.Empty && RockPileUtil.IsPileableStone(hotbar.Itemstack))
        {
            ok = TryPut(byPlayer);
        }
        else if (!sneaking)
        {
            ok = TryTake(byPlayer);
        }
        else
        {
            ok = false;
        }

        if (ok)
        {
            RegenCollision();
            MarkDirty(true);
            if (inventory.Empty && !clientsideFirstPlacement)
            {
                Api.World.BlockAccessor.SetBlock(0, Pos);
                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
            }
        }

        return ok;
    }

    public bool TryPut(IPlayer byPlayer)
    {
        var hotbar = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (hotbar.Empty || IsFull)
        {
            return false;
        }

        var bulk = byPlayer.Entity.Controls.CtrlKey;
        var maxTake = bulk ? RockPileUtil.BulkTransferQuantity : RockPileUtil.TransferQuantity;
        maxTake = Math.Min(maxTake, SlotCount - StoneCount);

        // One stone at a time, debiting the hand only once its slot has taken the stone, so a
        // stone can never leave a hand and find nowhere to land.
        var placed = 0;
        while (placed < maxTake)
        {
            var empty = FirstEmptySlot();
            if (empty is null || hotbar.Empty)
            {
                break;
            }

            var taken = hotbar.TakeOut(1);
            if (taken is null)
            {
                break;
            }

            empty.Itemstack = taken;
            empty.MarkDirty();
            placed++;
        }

        if (placed == 0)
        {
            return false;
        }

        hotbar.MarkDirty();
        PlayStoneSound(byPlayer);

        (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        Api.World.Logger.Audit(
            "{0} Put {1} stone(s) into Acervus Lapidum rock pile at {2}.",
            byPlayer.PlayerName,
            placed,
            Pos);

        return true;
    }

    public bool TryTake(IPlayer byPlayer)
    {
        if (StoneCount == 0)
        {
            return false;
        }

        var bulk = byPlayer.Entity.Controls.CtrlKey;
        var takeCount = bulk ? RockPileUtil.BulkTransferQuantity : RockPileUtil.TransferQuantity;
        takeCount = Math.Min(takeCount, StoneCount);

        var taken = new List<ItemStack>(takeCount);
        for (var i = 0; i < takeCount; i++)
        {
            var slot = LastFilledSlot();
            if (slot is null)
            {
                break;
            }

            taken.Add(slot.TakeOut(1)!);
            slot.MarkDirty();
        }

        if (taken.Count == 0)
        {
            return false;
        }

        foreach (var stone in taken)
        {
            if (!byPlayer.InventoryManager.TryGiveItemstack(stone, true))
            {
                Api.World.SpawnItemEntity(stone, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }

        PlayStoneSound(byPlayer);

        (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        Api.World.Logger.Audit(
            "{0} Took {1} stone(s) from Acervus Lapidum rock pile at {2}.",
            byPlayer.PlayerName,
            taken.Count,
            Pos);

        return true;
    }

    private void PlayStoneSound(IPlayer byPlayer)
    {
        Api.World.PlaySoundAt(
            RockPileUtil.PlaceSound(Api.World),
            Pos.X + 0.5,
            Pos.InternalY,
            Pos.Z + 0.5,
            byPlayer,
            0.9f + (float)Api.World.Rand.NextDouble() * 0.2f,
            16);
    }

    private ItemSlot? FirstEmptySlot()
    {
        foreach (var slot in inventory)
        {
            if (slot.Empty)
            {
                return slot;
            }
        }

        return null;
    }

    private ItemSlot? LastFilledSlot()
    {
        for (var i = inventory.Count - 1; i >= 0; i--)
        {
            if (!inventory[i].Empty)
            {
                return inventory[i];
            }
        }

        return null;
    }

    public ItemStack[] GetContentStacks()
    {
        var stacks = new List<ItemStack>(StoneCount);
        foreach (var slot in inventory)
        {
            if (!slot.Empty)
            {
                stacks.Add(slot.Itemstack!.Clone());
            }
        }

        return stacks.ToArray();
    }

    /// <summary>
    /// Fills a freshly placed pile without going through a player's hand — used when a vanilla
    /// ground storage pile migrates into one of ours.
    /// </summary>
    public void PopulateFrom(IReadOnlyList<ItemStack> stones, RockPileLayoutMode mode)
    {
        for (var i = 0; i < inventory.Count; i++)
        {
            inventory[i].Itemstack = i < stones.Count ? stones[i].Clone() : null;
            inventory[i].MarkDirty();
        }

        layoutMode = mode;
        RecalcSegmentIndex();
        RegenCollision();
        MarkMeshesDirty();
        MarkDirty(true);
        Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }

    public void RegenCollision()
    {
        colBoxes = [RockPileUtil.CollisionForCount(CurrentLayout(), Math.Max(1, StoneCount))];
    }

    protected override float[][] genTransformationMatrices()
    {
        var layout = CurrentLayout();
        // A wall runs whichever way it was laid; every other layout is the same from any side.
        var yaw = layoutMode == RockPileLayoutMode.Wall ? orientation * 90f : 0f;

        var matrices = new float[RockPileUtil.Capacity][];
        for (var i = 0; i < RockPileUtil.Capacity; i++)
        {
            var pose = i < layout.Length
                ? layout[i]
                : new RockPileSlotTransform { X = 0.5f, Y = i * RockPileUtil.StoneHeight, Z = 0.5f };

            matrices[i] = new Matrixf()
                .Translate(0.5f, 0f, 0.5f)
                .RotateYDeg(yaw)
                .Translate(-0.5f, 0f, -0.5f)
                .Translate(pose.X, pose.Y, pose.Z)
                .RotateYDeg(pose.YawDeg)
                .RotateXDeg(pose.PitchDeg)
                .RotateZDeg(pose.RollDeg)
                // Puts the stone's own bottom-centre on the origin, so the pose above is read as
                // "where this stone sits" rather than "where the block corner sits".
                .Translate(-0.5f, 0f, -0.5f)
                .Values;
        }

        return matrices;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("layoutMode", (int)layoutMode);
        tree.SetInt("orientation", orientation);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        clientsideFirstPlacement = false;
        layoutMode = RockPileUtil.ClampLayoutMode(tree.GetInt("layoutMode", (int)RockPileLayoutMode.Heap));
        orientation = tree.GetInt("orientation");
        RecalcSegmentIndex();
        RegenCollision();
        RedrawAfterReceivingTreeAttributes(worldForResolving);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        var count = StoneCount;
        dsc.AppendLine(Lang.Get("acervuslapidum:blockinfo-rockpile-count", count, SlotCount));
        dsc.AppendLine(Lang.Get(
            "acervuslapidum:blockinfo-rockpile-layout",
            Lang.Get("acervuslapidum:rockpile-layout-" + layoutMode.ToString().ToLowerInvariant())));

        // Worth saying out loud: it is the cue that you can start the next course of a cairn.
        if (IsFull)
        {
            dsc.AppendLine(Lang.Get("acervuslapidum:blockinfo-rockpile-full"));
        }
    }
}
