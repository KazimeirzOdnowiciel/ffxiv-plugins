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
    /// One line per fact, in a stable order: plugin version, collection progress, catalog size and theme.
    /// Empty inputs are written as-is rather than replaced, so the result never claims a fact it does not have.
    /// </summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static string Build(string version, string progress, string catalog, string theme, bool followsGameTheme)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(theme);

        var newline = Environment.NewLine;

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
            "Theme: ",
            theme,
            followsGameTheme ? " (following the game theme)" : string.Empty);
    }
}
