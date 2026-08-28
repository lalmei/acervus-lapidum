using AcervusLapidum.Items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace AcervusLapidum.Storage;

/// <summary>
/// Makes F open a pile's layout picker even when the tool mode picker cannot.
///
/// The vanilla picker is hard-wired to the held item: GuiDialogToolMode reads the active hotbar
/// slot and bails when that yields no tool modes, so with empty hands F does nothing. Chaining
/// onto the "toolmodeselect" handler does not fix it either — GuiDialogToolMode re-registers
/// itself from GuiDialog.OnBlockTexturesLoaded, which runs after every mod's StartClientSide and
/// overwrites whatever handler is installed there.
///
/// So we claim F with our own hotkey and open <see cref="GuiDialogRockPileLayout"/>, which offers
/// the same entries the tool mode picker does. HotkeyManager walks every hotkey bound to the
/// pressed key and only stops once a handler returns true, so vanilla keeps first refusal:
/// holding a stone (or a chisel) still opens the real picker and we never see the keypress.
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

        // Vanilla gets first refusal on F regardless of which hotkey the manager reaches first,
        // so a stone in hand still opens the tool mode picker rather than this one.
        var held = player!.InventoryManager?.ActiveHotbarSlot;
        if (held?.Itemstack?.Collectible.GetToolModes(held, player, selection) is not null)
        {
            return false;
        }

        var pile = CollectibleBehaviorRockPileable.FindTargetPile(capi!.World, selection);
        if (pile is null)
        {
            return false;
        }

        dialog?.Dispose();
        dialog = new GuiDialogRockPileLayout(capi, pile.Pos);

        return dialog.TryOpen();
    }
}
