using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace AcervusLapidum.Storage;

/// <summary>
/// The layout picker for a pile you are looking at empty-handed.
///
/// Vanilla's tool mode dialog cannot do this job: GuiDialogToolMode reads the active hotbar slot
/// and bails when that yields no tool modes, so with nothing in hand there is nothing to show. It
/// holds the same entries as the tool mode picker — every layout, then the turn — because both
/// read <see cref="RockPileLayoutModes"/>, so the pile is restyled and turned the same way with
/// or without a stone in hand.
///
/// Opened and closed by <see cref="RockPileLayoutHotkey"/> rather than by a key combination of
/// its own, since F is already claimed by that hotkey with vanilla's picker behind it.
/// </summary>
public sealed class GuiDialogRockPileLayout : GuiDialog
{
    private const string GridKey = "layouts";
    private const string NameKey = "layoutname";
    private const int Columns = 6;

    /// <summary>The pile this picker was opened on. It does not follow the cursor afterwards.</summary>
    private readonly BlockPos pos;

    private readonly SkillItem[] modes;

    public GuiDialogRockPileLayout(ICoreClientAPI capi, BlockPos pos) : base(capi)
    {
        this.pos = pos.Copy();
        modes = RockPileLayoutModes.GetOrCreate(capi);
        Compose();
    }

    public override string? ToggleKeyCombinationCode => null;

    /// <summary>Icons are clicked, so the mouse has to come back from the camera.</summary>
    public override bool PrefersUngrabbedMouse => true;

    private BlockEntityRockPile? Pile => capi.World?.BlockAccessor.GetBlockEntity(pos) as BlockEntityRockPile;

    private void Compose()
    {
        var rows = (int)Math.Ceiling(modes.Length / (double)Columns);

        var gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, GuiStyle.TitleBarHeight, Columns, rows);
        var nameBounds = ElementBounds.Fixed(0, 0, gridBounds.fixedWidth, 25).FixedUnder(gridBounds, 6);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(gridBounds, nameBounds);

        SingleComposer = capi.Gui
            .CreateCompo("acervuslapidumrockpilelayout", ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get("acervuslapidum:rockpile-layout-title"), () => TryClose())
            .BeginChildElements(bgBounds)
                .AddSkillItemGrid(modes.ToList(), Columns, rows, OnSlotClick, gridBounds, GridKey)
                .AddDynamicText(
                    "",
                    CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center),
                    nameBounds,
                    NameKey)
            .EndChildElements()
            .Compose();

        // The grid takes its click handler through the composer but not its hover handler, and the
        // names are the whole reason a picker beats cycling blind.
        SingleComposer.GetSkillItemGrid(GridKey).OnSlotOver = OnSlotOver;

        ShowSelected();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        ShowSelected();
    }

    /// <summary>Marks the layout the pile is wearing, and names it while nothing is hovered.</summary>
    private void ShowSelected()
    {
        var selected = (int)(Pile?.LayoutMode ?? RockPileLayoutMode.Heap);

        SingleComposer.GetSkillItemGrid(GridKey).selectedIndex = selected;
        SingleComposer.GetDynamicText(NameKey).SetNewText(modes[selected].Name);
    }

    private void OnSlotOver(int index)
    {
        if (index >= 0 && index < modes.Length)
        {
            SingleComposer.GetDynamicText(NameKey).SetNewText(modes[index].Name);
        }
    }

    private void OnSlotClick(int index)
    {
        if (Pile is not { } pile)
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
