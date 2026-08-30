using Vintagestory.API.Common;

namespace AcervusLapidum;

/// <summary>
/// Server-side settings, stored in <c>ModConfig/acervuslapidum.json</c>.
///
/// There is exactly one knob, and it is off, because turning it on rewrites blocks in a world the
/// player already has. Installing a mod should not silently change what is already on the ground —
/// see <see cref="Storage.RockPileMigration"/> for the commands that do it when asked.
/// </summary>
public sealed class AcervusLapidumConfig
{
    public const string FileName = "acervuslapidum.json";

    /// <summary>
    /// Whether vanilla stone piles turn themselves into rock piles as their chunks load.
    ///
    /// Off by default. A world full of vanilla piles is a world that still works perfectly well
    /// with this mod installed, and a player who later removes the mod gets their piles back
    /// untouched. Turn it on, or run <c>/rockpile convert</c>, when you actually want the change.
    /// </summary>
    public bool ConvertVanillaPilesOnLoad { get; set; }

    public static AcervusLapidumConfig Load(ICoreAPI api)
    {
        try
        {
            var config = api.LoadModConfig<AcervusLapidumConfig>(FileName);
            if (config is not null)
            {
                return config;
            }
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "Acervus Lapidum could not read {0}, falling back to defaults: {1}",
                FileName,
                exception.Message);
        }

        var fresh = new AcervusLapidumConfig();
        try
        {
            api.StoreModConfig(fresh, FileName);
        }
        catch (Exception exception)
        {
            api.Logger.Warning("Acervus Lapidum could not write {0}: {1}", FileName, exception.Message);
        }

        return fresh;
    }
}
