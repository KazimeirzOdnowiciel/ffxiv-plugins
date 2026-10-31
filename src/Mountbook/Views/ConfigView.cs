using System;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;

namespace Mountbook.Views;

/// <summary>
/// The settings body, drawn from <see cref="ConfigViewState"/> and nothing else.
/// </summary>
/// <remarks>
/// Split out of <see cref="Windows.ConfigWindow"/> for the same reason <see cref="MountbookView"/> was
/// split from the main window: the headless preview can then draw it against a golden (T6), so a
/// padding or wording regression in the settings stops being invisible. The view never touches the
/// plugin, the config file or the clipboard - it raises requests and the window applies them.
/// </remarks>
public sealed class ConfigView
{
    public void Draw(ConfigViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.ClearRequests();

        DrawCollection(state);
        DrawAppearance(state);
        DrawBrowse(state);
        DrawAbout(state);
    }

    /// <summary>
    /// Trims <paramref name="text"/> with an ellipsis so it fits <paramref name="width"/>, measured with
    /// Glasswork's own text paint (ImGui's measurement is narrower and cut text just before the glyph).
    /// </summary>
    private static string Fit(string text, float width, TextRole role)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;

        if (TextPaint.Measure(text, role, in look, scale).X <= width)
        {
            return text;
        }

        // ASCII dots on purpose: the headless preview's atlas has no U+2026, so the golden would show a
        // "?" where the game shows an ellipsis. The path is a diagnostic line; three dots read fine.
        const string Ellipsis = "...";
        var length = text.Length;

        while (length > 1
            && TextPaint.Measure(string.Concat(text.AsSpan(0, length), Ellipsis), role, in look, scale).X > width)
        {
            length--;
        }

        return string.Concat(text.AsSpan(0, length).TrimEnd(), Ellipsis);
    }

    private static void DrawCollection(ConfigViewState state)
    {
        if (Gw.BeginSection("Collection"u8, "Names, icons, flavour text and obtain data come from the game's sheets."u8))
        {
            Gw.BeginRow("Progress"u8);
            Gw.Text(state.ProgressText, TextRole.Body);
            Gw.ProgressBar(Math.Clamp(state.ProgressFraction, 0f, 1f), 0f, Frame.Look.Accent);
            Gw.EndRow();
        }

        Gw.EndSection();
    }

    private static void DrawAppearance(ConfigViewState state)
    {
        if (!Gw.BeginSection("Appearance"u8))
        {
            Gw.EndSection();
            return;
        }

        Gw.BeginRow("Theme"u8, "A Glasswork Look, or a theme JSON"u8);

        var index = state.ThemeIndex;

        if (state.ThemeLabels.Length > 0)
        {
            ImGui.SetNextItemWidth(210f * Frame.Scale);

            if (Gw.ComboBox(ref index, state.ThemeLabels, "theme"u8))
            {
                state.ThemePickedIndex = index;
            }
        }
        else
        {
            Gw.Text("No themes found."u8, TextRole.Footnote);
        }

        Gw.EndRow();

        Gw.BeginRow("Follow game theme"u8, "Match the game's own window theme instead"u8);
        var follow = state.FollowGameTheme;

        if (Gw.Toggle(ref follow, "##followgametheme"u8))
        {
            state.FollowGameThemeSet = follow;
        }

        Gw.EndRow();

        // Copying the JSON is what a user does right before editing a theme, and opening the folder is
        // where they edit it, so both live on the row that names the folder.
        Gw.BeginRow("Themes folder"u8, "Drop a JSON here to add it"u8);

        var spacing = Frame.Look.SpaceSmall * Frame.Scale;

        if (Gw.Button("Copy JSON"u8, ButtonStyle.Plain, ButtonSize.Small))
        {
            state.CopyThemeRequested = true;
        }

        ImGui.SameLine(0f, spacing);

        if (Gw.Button("Open themes folder"u8, ButtonStyle.Plain, ButtonSize.Small))
        {
            state.OpenThemesFolderRequested = true;
        }

        Gw.EndRow();

        // A single measured line, not Gw.TextWrapped: that helper ignores a pushed wrap position, so the
        // path overflowed the pane and forced a horizontal scroll (the config golden showed it clipped
        // and the trailing glyph as "?"). Fit instead: the path is information, not a paragraph.
        Gw.Text(Fit(state.ThemesDirectory, ImGui.GetContentRegionAvail().X, TextRole.Footnote), TextRole.Footnote);

        Gw.EndSection();
    }

    private static void DrawBrowse(ConfigViewState state)
    {
        if (Gw.BeginSection("Browse"u8, "Search, filters, selection and the wanted list are saved automatically."u8))
        {
            Gw.BeginRow("Saved state"u8, "Clears the search and filters, and forgets the selected mount."u8);

            if (Gw.Button("Reset"u8, ButtonStyle.Default))
            {
                state.ResetBrowseRequested = true;
            }

            Gw.EndRow();

            // A wanted list is the one thing a player wants to hand to someone else (a static party's
            // hunting list, a friend's "what should I farm"). Copy and import sit together so the round
            // trip is discoverable from either half; the count is not repeated here because the toast
            // after a copy already says how many went to the clipboard.
            Gw.BeginRow("Wanted list"u8, "Share or import as JSON"u8);

            var spacing = Frame.Look.SpaceSmall * Frame.Scale;

            if (Gw.Button("Copy wanted"u8, ButtonStyle.Plain, ButtonSize.Small))
            {
                state.CopyWantedRequested = true;
            }

            ImGui.SameLine(0f, spacing);

            if (Gw.Button("Import from clipboard"u8, ButtonStyle.Plain, ButtonSize.Small))
            {
                state.ImportWantedRequested = true;
            }

            Gw.EndRow();
        }

        Gw.EndSection();
    }

    private static void DrawAbout(ConfigViewState state)
    {
        if (Gw.BeginSection("About"u8))
        {
            Gw.BeginRow("Version"u8);
            Gw.Text(state.VersionText, TextRole.Body);
            Gw.EndRow();

            Gw.BeginRow("Catalog"u8);
            Gw.Text(state.CatalogText, TextRole.Body);
            Gw.EndRow();

            // Fitted, not wrapped: a long footnote in a section ran past the pane and forced a
            // horizontal scrollbar (the config golden showed it clipped mid-word).
            Gw.Text(
                Fit(
                    "Obtain data ships with the plugin; nothing is downloaded.",
                    ImGui.GetContentRegionAvail().X,
                    TextRole.Footnote),
                TextRole.Footnote);

            // A bug report needs version, collection size and theme; one button puts all three on the
            // clipboard instead of asking the player to hunt through logs.
            if (Gw.Button("Copy diagnostics"u8, ButtonStyle.Plain, ButtonSize.Small))
            {
                state.CopyDiagnosticsRequested = true;
            }
        }

        Gw.EndSection();
    }
}
