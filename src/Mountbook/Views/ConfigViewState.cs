namespace Mountbook.Views;

/// <summary>
/// Everything the settings body draws, plus the one-frame requests it raises. The window fills it from
/// the plugin and applies the requests afterwards; the headless preview fills it with fixed values, so
/// the settings window can be reviewed (and gated) like the main window instead of being the only
/// surface with no image.
/// </summary>
public sealed class ConfigViewState
{
    /// <summary>The plugin version shown in the About row, e.g. <c>v0.1.0.0</c>.</summary>
    public string VersionText { get; set; } = string.Empty;

    /// <summary>How much of the collection is owned, 0 to 1, for the progress bar.</summary>
    public float ProgressFraction { get; set; }

    /// <summary>One line of catalog facts for the About row, e.g. <c>369 mounts, 356 with obtain data</c>.</summary>
    public string CatalogText { get; set; } = string.Empty;

    /// <summary>True when the player asked to copy the diagnostics to the clipboard.</summary>
    public bool CopyDiagnosticsRequested { get; set; }

    /// <summary>The collection line, e.g. <c>136 of 369 mounts collected.</c>.</summary>
    public string ProgressText { get; set; } = string.Empty;

    /// <summary>The theme picker's labels, in the order <see cref="ThemeNames"/> lists them.</summary>
    public string[] ThemeLabels { get; set; } = [];

    /// <summary>The theme names the labels stand for; the window maps a picked index back through it.</summary>
    public string[] ThemeNames { get; set; } = [];

    /// <summary>The currently applied theme index.</summary>
    public int ThemeIndex { get; set; }

    /// <summary>Whether the Look follows the game's own window theme.</summary>
    public bool FollowGameTheme { get; set; }

    /// <summary>The folder the theme JSONs are read from.</summary>
    public string ThemesDirectory { get; set; } = string.Empty;

    /// <summary>The theme the player picked this frame, or null.</summary>
    public int? ThemePickedIndex { get; set; }

    /// <summary>The follow-game-theme value the player set this frame, or null.</summary>
    public bool? FollowGameThemeSet { get; set; }

    /// <summary>True when the player asked to copy the current theme JSON.</summary>
    public bool CopyThemeRequested { get; set; }

    /// <summary>True when the player asked to open the themes folder.</summary>
    public bool OpenThemesFolderRequested { get; set; }

    /// <summary>True when the player asked to reset the saved browse state.</summary>
    public bool ResetBrowseRequested { get; set; }

    /// <summary>True when the player asked to put the wanted list on the clipboard as JSON.</summary>
    public bool CopyWantedRequested { get; set; }

    /// <summary>True when the player asked to read a wanted list off the clipboard.</summary>
    public bool ImportWantedRequested { get; set; }

    /// <summary>Clears every request. The window calls it before drawing, so a request lives one frame.</summary>
    public void ClearRequests()
    {
        // Every request, including the clipboard one: it was the one member of this set that no test
        // covered, so it survived the move to a per-frame state object and would have re-copied the
        // diagnostics every frame the moment the window cached its state.
        CopyDiagnosticsRequested = false;
        ThemePickedIndex = null;
        FollowGameThemeSet = null;
        CopyThemeRequested = false;
        OpenThemesFolderRequested = false;
        ResetBrowseRequested = false;
        CopyWantedRequested = false;
        ImportWantedRequested = false;
    }
}
