using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace AcervusLapidum.Storage;

/// <summary>
/// The layout picker, and the only one — F opens it whether or not you are holding a stone.
///
/// Vanilla's tool mode dialog used to take the stone-in-hand case, and it could never take the
/// other one: GuiDialogToolMode reads the active hotbar slot and bails when that yields no tool
/// modes, so with nothing in hand there was nothing to show. Two pickers over one list meant the
/// same choice looked different depending on your hands, and the vanilla one could only draw a
/// flat grid — eleven layouts in a row, told apart by squinting at eleven grey icons.
///
/// So this one shows the rows <see cref="RockPileLayoutModes.LayoutGroups"/> defines: loose stone,
/// waymarks, masonry, ornament, and the turn on its own. A row is a kind of pile, which is what a
/// player is picking between before they pick a shape.
///
/// Opened and closed by <see cref="RockPileLayoutHotkey"/> rather than by a key combination of
/// its own, since F is already claimed by that hotkey.
/// </summary>
public sealed class GuiDialogRockPileLayout : GuiDialog
{
    private const string NameKey = "layoutname";
    private const string CountKey = "layoutcount";

    /// <summary>Vertical room between a row's heading and the row above it.</summary>
    private const int RowSpacing = 10;

    /// <summary>
    /// The pile this picker was opened on, or null when it was opened over bare ground with a
    /// stone in hand — in which case there is nothing to restyle and the choice is only
    /// remembered for the pile that stone starts. It does not follow the cursor afterwards.
    /// </summary>
    private readonly BlockPos? pos;

    private readonly SkillItem[] modes;
    private readonly RockPileLayoutGroup[] groups;

    public GuiDialogRockPileLayout(ICoreClientAPI capi, BlockPos? pos) : base(capi)
    {
        this.pos = pos?.Copy();
        modes = RockPileLayoutModes.GetOrCreate(capi);
        groups = RockPileLayoutModes.GroupsFor(pos is not null);
        Compose();
    }

    public override string? ToggleKeyCombinationCode => null;

    /// <summary>Icons are clicked, so the mouse has to come back from the camera.</summary>
    public override bool PrefersUngrabbedMouse => true;

    private BlockEntityRockPile? Pile =>
        pos is null ? null : capi.World?.BlockAccessor.GetBlockEntity(pos) as BlockEntityRockPile;

    private static string GridKey(RockPileLayoutGroup group) => $"layouts-{group.Code}";

    private void Compose()
    {
        // Every row is as wide as the widest one, so the headings line up and the dialog does not
        // step in and out down its right edge.
        var columns = groups.Max(group => group.PickerIndices.Length);
        var rowWidth = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, columns, 1).fixedWidth;

        var children = new List<ElementBounds>();

        // A zero-height anchor under the title bar, so the first heading stacks the same way as
        // every heading after it.
        ElementBounds previous = ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, rowWidth, 0);

        var headings = new ElementBounds[groups.Length];
        var grids = new ElementBounds[groups.Length];

        for (var i = 0; i < groups.Length; i++)
        {
            headings[i] = ElementBounds.Fixed(0, 0, rowWidth, 20).FixedUnder(previous, RowSpacing);
            grids[i] = ElementStdBounds
                .SlotGrid(EnumDialogArea.None, 0, 0, groups[i].PickerIndices.Length, 1)
                .FixedUnder(headings[i], 2);

            children.Add(headings[i]);
            children.Add(grids[i]);
            previous = grids[i];
        }

        var nameBounds = ElementBounds.Fixed(0, 0, rowWidth, 25).FixedUnder(previous, RowSpacing);
        var countBounds = ElementBounds.Fixed(0, 0, rowWidth, 22).FixedUnder(nameBounds, 2);
        children.Add(nameBounds);
        children.Add(countBounds);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(children.ToArray());

        var composer = capi.Gui
            .CreateCompo("acervuslapidumrockpilelayout", ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get("acervuslapidum:rockpile-layout-title"), () => TryClose())
            .BeginChildElements(bgBounds);

        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];

            composer.AddStaticText(
                group.Title,
                CairoFont.WhiteDetailText().WithColor(GuiStyle.DialogDefaultTextColor),
                headings[i]);

            composer.AddSkillItemGrid(
                group.PickerIndices.Select(index => modes[index]).ToList(),
                group.PickerIndices.Length,
                1,
                // The grid hands back a slot within its own row; the picker speaks in slots of the
                // one shared list, which is what turns a click back into a layout.
                slot => OnSlotClick(group.PickerIndices[slot]),
                grids[i],
                GridKey(group));
        }

        SingleComposer = composer
            .AddDynamicText(
                "",
                CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center),
                nameBounds,
                NameKey)
            .AddDynamicText(
                "",
                CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center),
                countBounds,
                CountKey)
            .EndChildElements()
            .Compose();

        // The grids take their click handler through the composer but not their hover handler, and
        // the names are the whole reason a picker beats cycling blind.
        foreach (var group in groups)
        {
            SingleComposer.GetSkillItemGrid(GridKey(group)).OnSlotOver =
                slot => Describe(group.PickerIndices[slot]);
        }

        ShowSelected();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        ShowSelected();
    }

    /// <summary>
    /// Marks the layout that is in force, and describes it while nothing is hovered.
    ///
    /// On a pile that is the layout it is wearing; over bare ground it is the one this player last
    /// picked, which is how the next pile they start will come out. Only the row the layout lives
    /// in carries the mark — every other grid is told nothing is selected.
    /// </summary>
    private void ShowSelected()
    {
        var mode = Pile?.LayoutMode ?? RockPileUtil.GetPreferredLayoutMode(capi.World?.Player?.Entity);
        var selected = RockPileLayoutModes.IndexForMode(mode);

        foreach (var group in groups)
        {
            SingleComposer.GetSkillItemGrid(GridKey(group)).selectedIndex =
                Array.IndexOf(group.PickerIndices, selected);
        }

        Describe(selected);
    }

    /// <summary>
    /// Names the entry under the cursor, and says how many stones it holds.
    ///
    /// The count is what this pile would hold laid that way, not a figure from a table: a cairn
    /// narrows as it climbs, so the same choice is 19 stones on the ground and fewer three
    /// courses up. Worth reading before you pick, because a layout that holds fewer stones than
    /// the pile has hands the extra ones straight back to you.
    /// </summary>
    private void Describe(int index)
    {
        if (index < 0 || index >= modes.Length)
        {
            return;
        }

        SingleComposer.GetDynamicText(NameKey).SetNewText(modes[index].Name);

        // The turn is not a layout and holds nothing; it leaves the pile exactly as many stones
        // as it had. Nor is there a pile to measure when the picker is standing over bare ground.
        var capacity = index == RockPileLayoutModes.RotateIndex
            ? null
            : Pile?.SlotCountFor(RockPileLayoutModes.ModeForIndex(index));

        SingleComposer.GetDynamicText(CountKey).SetNewText(
            capacity is { } stones ? Lang.Get("acervuslapidum:rockpile-layout-capacity", stones) : "");
    }

    private void OnSlotClick(int index)
    {
        var pile = Pile;
        if (pile is null && pos is not null)
        {
            // The pile was taken apart while the picker was open.
            TryClose();
            return;
        }

        RockPileLayoutModes.Apply(capi, pile, index);

        // Turning stays open, because turning is something you do a step at a time until the pile
        // faces the way you want. Choosing a layout is a single decision, so it closes.
        if (index == RockPileLayoutModes.RotateIndex)
        {
            return;
        }

        ShowSelected();
        TryClose();
    }
}
