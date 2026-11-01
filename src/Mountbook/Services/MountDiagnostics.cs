using System;

namespace Mountbook.Services;

/// <summary>
/// Builds the diagnostics block the settings window copies to the clipboard: exactly what a bug report needs,
/// nothing about the player's character.
/// </summary>
/// <remarks>
/// Kept Dalamud-free and pure so its shape can be tested (T1). The window only decides *when* to copy.
/// </remarks>
public static class MountDiagnostics
{
    /// <summary>
    /// One line per fact, in a stable order: plugin version, collection progress, catalog size, the
    /// obtain dataset it is reading, the snapshot's sheet cross-check when it recorded one, and the
    /// theme. Empty inputs are written as-is rather than replaced, so the result never claims a fact it
    /// does not have.
    /// </summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static string Build(string version, string progress, string catalog, string dataset, string theme, bool followsGameTheme, string crossCheck = "")
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(crossCheck);

        var newline = Environment.NewLine;

        // The cross-check line only appears when there is something to say: a diagnostics block that
        // claims a verification it did not make is worse than one that stays quiet.
        string crossCheckLine = crossCheck.Length == 0
            ? string.Empty
            : newline + "Sheet cross-check: " + crossCheck;

        return string.Concat(
            "Mountbook ",
            version,
            newline,
            "Collection: ",
            progress,
            newline,
            "Catalog: ",
            catalog,
            newline,
            "Obtain data: ",
            dataset,
            crossCheckLine,
            newline,
            "Theme: ",
            theme,
            followsGameTheme ? " (following the game theme)" : string.Empty);
    }
}
