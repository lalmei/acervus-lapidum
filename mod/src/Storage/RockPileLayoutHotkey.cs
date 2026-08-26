using AcervusLapidum.Items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace AcervusLapidum.Storage;

/// <summary>
/// Makes F change a pile's layout even when the tool mode picker cannot open.
///
/// The picker is hard-wired to the held item: GuiDialogToolMode reads the active hotbar slot and
/// bails when that yields no tool modes, so with empty hands F does nothing. Chaining onto the
/// "toolmodeselect" handler does not fix it either — GuiDialogToolMode re-registers itself from
/// GuiDialog.OnBlockTexturesLoaded, which runs after every mod's StartClientSide and overwrites
/// whatever handler is installed there.
///
/// So we claim F with our own hotkey. HotkeyManager walks every hotkey bound to the pressed key
/// and only stops once a handler returns true, so vanilla keeps first refusal: holding a stone
/// (or a chisel) still opens the real picker and we never see the keypress.
/// </summary>
public sealed class RockPileLayoutHotkey : ModSystem
{
    /// <summary>Shown in Controls and referenced by the pile's interaction help.</summary>
    public const string HotkeyCode = "acervuslapidumrockpilelayout";

    private ICoreClientAPI? capi;

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

        api.Input.SetHotKeyHandler(HotkeyCode, _ => CycleLayout());
    }

    private bool CycleLayout()
    {
        var player = capi?.World?.Player;
        var selection = player?.CurrentBlockSelection;
        if (selection is null)
        {
            return false;
        }

        // Vanilla gets first refusal on F regardless of which hotkey the manager reaches first,
        // so a stone in hand still opens the picker instead of silently stepping the layout.
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

        // Every layout is reachable: one that holds fewer stones than the pile has simply drops
        // the extra ones at your feet, so there is nothing here to step over.
        var next = RockPileUtil.NextLayoutMode(pile.LayoutMode);

        // Apply locally so the pile redraws on the same frame; the server confirms or bounces it.
        pile.SetLayoutMode(next);
        capi.Network.SendBlockEntityPacket(
            pile.Pos,
            BlockEntityRockPile.PacketIdSetLayout,
            BitConverter.GetBytes((int)next));

        capi.ShowChatMessage(Lang.Get(
            "acervuslapidum:rockpile-layout-changed",
            Lang.Get("acervuslapidum:rockpile-layout-" + next.ToString().ToLowerInvariant())));

        return true;
    }
}
