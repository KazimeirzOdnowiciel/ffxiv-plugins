using System;
using Dalamud.Configuration;

namespace Mountbook;

/// <summary>
/// The plugin-facing half of the settings: the shape Dalamud hands back from <c>GetPluginConfig()</c>, and
/// the one write the migration ladder asks for.
/// </summary>
/// <remarks>
/// The persisted members and the ladder itself live in <c>Configuration.Shape.cs</c>, which has no Dalamud
/// dependency - the tests project compiles that file by path (T1) and reads old fixtures with the same
/// serializer Dalamud uses, so "an older file still means what it meant" is a test rather than a review
/// opinion. Only this half may name a Dalamud type.
/// </remarks>
public sealed partial class Configuration : IPluginConfiguration
{
    /// <summary>Writes the whole configuration through Dalamud's own file storage.</summary>
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>
    /// Persists the file the ladder just upgraded, with the failure logged rather than thrown.
    /// </summary>
    /// <remarks>
    /// This runs from the plugin constructor, so it must not be able to stop the plugin from loading: a
    /// read-only or full config folder would turn a cosmetic version bump into "failed to load". Every
    /// other write in the plugin goes through <see cref="Plugin.SaveConfigSafe"/> for the same reason.
    /// </remarks>
    partial void OnMigrated()
    {
        try
        {
            Save();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Saving the migrated configuration failed.");
        }
    }
}
