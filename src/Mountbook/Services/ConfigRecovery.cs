using System;
using System.Globalization;
using System.IO;

namespace Mountbook.Services;

/// <summary>
/// What happens to a settings file that cannot be read: it is moved aside under a name of its own, and the
/// plugin starts from defaults rather than refusing to load.
/// </summary>
/// <remarks>
/// <para>
/// Measured against the shipped loader (Dalamud 15.0.3.9, <c>PluginConfigurations.LoadForType&lt;T&gt;</c>):
/// a truncated or garbage file throws out of <c>JsonReaderException</c>/<c>JsonSerializationException</c>
/// and nothing in Dalamud catches it before the plugin's own constructor, so the plugin does not load at
/// all. Only an *empty* file (or the literal <c>null</c>) deserialises to null and reaches the
/// <c>?? new Configuration()</c> path. Staring at defaults is therefore a decision this plugin has to make
/// for itself, and the file is evidence: a player who lost a wanted list wants the bytes back.
/// </para>
/// <para>
/// Move, not delete, and not "rename in place": the next save writes a fresh file to the normal path, so the
/// broken one has to be out of the way or it will be read again on the next launch.
/// </para>
/// </remarks>
public static class ConfigRecovery
{
    /// <summary>The suffix a set-aside file is given, before its extension.</summary>
    public const string Suffix = ".corrupt-";

    /// <summary>
    /// The path a set-aside copy of <paramref name="configPath"/> is written to:
    /// <c>Mountbook.corrupt-20261101-183000.json</c> beside the original.
    /// </summary>
    /// <param name="configPath">The settings file that could not be read.</param>
    /// <param name="at">The moment it was found, for the name. Passed in so the name is testable.</param>
    /// <returns>A path in the same folder, never existing yet (a second crash in the same second gets -1).</returns>
    public static string BackupPath(string configPath, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        string folder = Path.GetDirectoryName(configPath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(configPath)
            + Suffix
            + at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        string candidate = Path.Combine(folder, name + ".json");

        // Two failures inside one second would otherwise lose the first backup, which is the one more likely
        // to hold the player's data.
        for (var copy = 1; File.Exists(candidate) && copy < 100; copy++)
        {
            candidate = Path.Combine(folder, name + "-" + copy.ToString(CultureInfo.InvariantCulture) + ".json");
        }

        return candidate;
    }

    /// <summary>
    /// Moves an unreadable settings file aside so the plugin can start from defaults without losing it.
    /// </summary>
    /// <param name="configPath">The settings file that could not be read.</param>
    /// <param name="at">The moment it was found, for the backup's name.</param>
    /// <returns>The file the copy landed in, or null when there was nothing to move or it could not be
    /// moved or copied. A null is reported with the original exception, never swallowed.</returns>
    public static string? TryMoveAside(string configPath, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return null;
        }

        string backup = BackupPath(configPath, at);

        try
        {
            File.Move(configPath, backup);

            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked (the loader may still hold it) or read-only: a copy keeps the evidence even when the
            // original cannot be moved out of the way. The next save overwrites the original path anyway.
            try
            {
                File.Copy(configPath, backup);

                return backup;
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
