using AcervusLapidum.Items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace AcervusLapidum.Storage;

/// <summary>
/// Makes F open the layout picker, with a stone in hand or without one.
///
/// The vanilla picker is hard-wired to the held item: GuiDialogToolMode reads the active hotbar
/// slot and bails when that yields no tool modes, so with empty hands F did nothing. Chaining
/// onto the "toolmodeselect" handler does not fix it either — GuiDialogToolMode re-registers
/// itself from GuiDialog.OnBlockTexturesLoaded, which runs after every mod's StartClientSide and
/// overwrites whatever handler is installed there.
///
/// So we claim F with our own hotkey and open <see cref="GuiDialogRockPileLayout"/>. Stones no
/// longer report tool modes at all — see <see cref="CollectibleBehaviorRockPileable"/> — so
/// vanilla's handler declines the keypress and it reaches us either way, and there is one picker
/// for one choice. HotkeyManager walks every hotkey bound to the pressed key and only stops once a
/// handler returns true, which is what leaves vanilla first refusal for everything else: a chisel
/// in hand still opens the real tool mode picker and we never see the press.
/// </summary>
public sealed class RockPileLayoutHotkey : ModSystem
{
    /// <summary>Shown in Controls and referenced by the pile's interaction help.</summary>
    public const string HotkeyCode = "acervuslapidumrockpilelayout";

    private ICoreClientAPI? capi;
    private GuiDialogRockPileLayout? dialog;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        api.Input.RegisterHotKey(
            HotkeyCode,
            Lang.Get("acervuslapidum:hotkey-rockpile-layout"),
            GlKeys.F,
            HotkeyType.CharacterControls);

        api.Input.SetHotKeyHandler(HotkeyCode, _ => TogglePicker());
    }

    public override void Dispose()
    {
        base.Dispose();

        dialog?.Dispose();
        dialog = null;
    }

    private bool TogglePicker()
    {
        // Second press closes the one already up, the way the tool mode picker toggles. The
        // mouse is ungrabbed while it is open, so there is no block selection to re-find here.
        if (dialog?.IsOpened() == true)
        {
            dialog.TryClose();
            return true;
        }

        var player = capi?.World?.Player;
        var selection = player?.CurrentBlockSelection;
        if (selection is null)
        {
            return false;
        }

        // Anything that does have tool modes keeps vanilla's picker — a chisel is not ours to
        // answer for. Stones report none, so they fall through to the picker below.
        var held = player!.InventoryManager?.ActiveHotbarSlot;
        if (held?.Itemstack?.Collectible.GetToolModes(held, player, selection) is not null)
        {
            return false;
        }

        var pile = CollectibleBehaviorRockPileable.FindTargetPile(capi!.World, selection);

        // With a stone in hand, the picker also opens over ground where a pile could go: the
        // choice is then remembered rather than applied, and the pile that stone starts comes out
        // laid that way. Without a pile or a stone there is nothing for it to say.
        if (pile is null
            && !(RockPileUtil.IsPileableStone(held?.Itemstack) && selection.Face == BlockFacing.UP))
        {
            return false;
        }

        dialog?.Dispose();
        dialog = new GuiDialogRockPileLayout(capi, pile?.Pos);

        return dialog.TryOpen();
    }
}
