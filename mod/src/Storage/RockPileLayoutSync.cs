using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace AcervusLapidum.Storage;

/// <summary>The layout a player picked, on its way to the server.</summary>
[ProtoContract]
public sealed class RockPileLayoutPreferencePacket
{
    [ProtoMember(1)]
    public int Mode;
}

/// <summary>
/// Carries a player's chosen layout to the server, so the next pile they start is laid that way
/// on both sides.
///
/// It used to travel for free. The layout was picked through vanilla's tool mode dialog, and
/// vanilla runs <c>SetToolMode</c> on the server as well as the client, which set the preference
/// in both places. The picker is ours now — one dialog, grouped rows, open with a stone in hand
/// or without one — so the choice only ever happens client-side, and
/// <see cref="BlockRockPile.CreatePile"/> reads the preference on the server. Without this packet
/// the server would lay every new pile as whatever the player picked last session, and its
/// authoritative state would snap the client's correctly-laid pile back to it a tick later.
/// </summary>
public sealed class RockPileLayoutSync : ModSystem
{
    public const string ChannelName = "acervuslapidum";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.Network.RegisterChannel(ChannelName).RegisterMessageType<RockPileLayoutPreferencePacket>();
    }

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        base.StartServerSide(sapi);
        sapi.Network
            .GetChannel(ChannelName)
            .SetMessageHandler<RockPileLayoutPreferencePacket>(OnPreference);
    }

    /// <summary>Nothing to validate beyond the mode itself: a player may prefer any layout.</summary>
    private static void OnPreference(IServerPlayer fromPlayer, RockPileLayoutPreferencePacket packet)
    {
        RockPileUtil.SetPreferredLayoutMode(
            fromPlayer.Entity,
            RockPileUtil.ClampLayoutMode(packet.Mode));
    }

    public static void SendPreference(ICoreClientAPI capi, RockPileLayoutMode mode)
    {
        capi.Network
            .GetChannel(ChannelName)
            ?.SendPacket(new RockPileLayoutPreferencePacket { Mode = (int)mode });
    }
}
