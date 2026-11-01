using System;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Mountbook.Services;

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
    /// <remarks>
    /// The rule is <see cref="TextFit.Fit"/>, not a copy: this file and <see cref="WantedBoardView"/> held two
    /// byte-identical versions of it, including the ASCII-dots rationale, and a third already lived in
    /// Services/ for exactly this reason. ASCII dots are passed in because the headless preview's atlas has no
    /// U+2026, so the golden would show a "?" where the game shows an ellipsis.
    /// </remarks>
    /// <param name="text">The string to fit.</param>
    /// <param name="width">The pixel width it has to fit in.</param>
    /// <param name="role">The role it draws in, which is also the role it is measured in.</param>
    private static string Fit(string text, float width, TextRole role)
    {
        Look look = Frame.Look;
        var scale = Frame.Scale;

        return TextFit.Fit(text, width, candidate => TextPaint.Measure(candidate, role, in look, scale).X, EllipsisDots);
    }

    /// <summary>What a trimmed line ends in here: ASCII, because the headless preview's atlas has no U+2026.</summary>
    private const string EllipsisDots = "...";

    /// <summary>How wide a footnote line draws, for <see cref="PathLabel"/>.</summary>
    private static float MeasurePath(string text)
    {
        var look = Frame.Look;
        return TextPaint.Measure(text, TextRole.Footnote, in look, Frame.Scale).X;
    }

    private static void DrawCollection(ConfigViewState state)
    {
        if (Gw.BeginSection(Loc.T("config.collection.title"), Loc.T("config.collection.subtitle")))
        {
            Gw.BeginRow(Loc.T("config.progress"));
            Gw.Text(state.ProgressText, TextRole.Body);
            Gw.ProgressBar(Math.Clamp(state.ProgressFraction, 0f, 1f), 0f, Frame.Look.Accent);
            Gw.EndRow();
        }

        Gw.EndSection();
    }

    private static void DrawAppearance(ConfigViewState state)
    {
        if (!Gw.BeginSection(Loc.T("config.appearance")))
        {
            Gw.EndSection();
            return;
        }

        Gw.BeginRow(Loc.T("config.theme"), Loc.T("config.theme.subtitle"));

        var index = state.ThemeIndex;

        if (state.ThemeLabels.Length > 0)
        {
            ImGui.SetNextItemWidth(210f * Frame.Scale);

            if (Gw.ComboBox(ref index, state.ThemeLabels, Loc.T("config.theme.label")))
            {
                state.ThemePickedIndex = index;
            }
        }
        else
        {
            Gw.Text(Loc.T("config.theme.none"), TextRole.Footnote);
        }

        Gw.EndRow();

        Gw.BeginRow(Loc.T("config.follow"), Loc.T("config.follow.subtitle"));
        var follow = state.FollowGameTheme;

        if (Gw.Toggle(ref follow, "##followgametheme"u8))
        {
            state.FollowGameThemeSet = follow;
        }

        Gw.EndRow();

        // Copying the JSON is what a user does right before editing a theme, and opening the folder is
        // where they edit it, so both live on the row that names the folder.
        Gw.BeginRow(Loc.T("config.folder"), Loc.T("config.folder.subtitle"));

        var spacing = Frame.Look.SpaceSmall * Frame.Scale;

        if (Gw.Button(Loc.T("config.copyJson"), ButtonStyle.Plain, ButtonSize.Small))
        {
            state.CopyThemeRequested = true;
        }

        ImGui.SameLine(0f, spacing);

        if (Gw.Button(Loc.T("config.openFolder"), ButtonStyle.Plain, ButtonSize.Small))
        {
            state.OpenThemesFolderRequested = true;
        }

        Gw.EndRow();

        // A single measured line, not Gw.TextWrapped: that helper ignores a pushed wrap position, so the
        // path overflowed the pane and forced a horizontal scroll (the config golden showed it clipped
        // and the trailing glyph as "?"). Fit instead: the path is information, not a paragraph.
        // Head-ellipsised rather than tail-truncated: the tail is the part that names the folder
        // ("...\Mountbook\themes"), and the full path is one hover away.
        var pathWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        Gw.Text(PathLabel.Shorten(state.ThemesDirectory, pathWidth, MeasurePath), TextRole.Footnote);
        Gw.TooltipFor(ImGui.IsItemHovered(), state.ThemesDirectory);

        Gw.EndSection();
    }

    private static void DrawBrowse(ConfigViewState state)
    {
        if (Gw.BeginSection(Loc.T("config.browse"), Loc.T("config.browse.subtitle")))
        {
            Gw.BeginRow(Loc.T("config.savedState"), Loc.T("config.savedState.subtitle"));

            if (Gw.Button(Loc.T("config.reset"), ButtonStyle.Default))
            {
                state.ResetBrowseRequested = true;
            }

            Gw.EndRow();

            // A wanted list is the one thing a player wants to hand to someone else (a static party's
            // hunting list, a friend's "what should I farm"). Copy and import sit together so the round
            // trip is discoverable from either half; the count is not repeated here because the toast
            // after a copy already says how many went to the clipboard.
            Gw.BeginRow(Loc.T("config.wanted"), Loc.T("config.wanted.subtitle"));

            var spacing = Frame.Look.SpaceSmall * Frame.Scale;

            if (Gw.Button(Loc.T("config.copyWanted"), ButtonStyle.Plain, ButtonSize.Small))
            {
                state.CopyWantedRequested = true;
            }

            ImGui.SameLine(0f, spacing);

            if (Gw.Button(Loc.T("config.importWanted"), ButtonStyle.Plain, ButtonSize.Small))
            {
                state.ImportWantedRequested = true;
            }

            Gw.EndRow();

            // Everything, not just the filters above: this is the one control in the window that can take the
            // wanted list, the grind records and the watches with it, so it asks first (ConfigReset decides
            // whether there is anything to ask about). The view raises the request; the window owns the
            // pending flag, because a confirmation has to survive the frame the click landed in.
            Gw.BeginRow(Loc.T("config.resetAll"), Loc.T("config.resetAll.subtitle"));

            if (!state.ResetConfirmOpen
                && Gw.Button(Loc.T("config.resetAll"), ButtonStyle.Default))
            {
                state.ResetAllRequested = true;
            }

            Gw.EndRow();

            if (state.ResetConfirmOpen)
            {
                // Two lines rather than one: the counts line is what the Fit() below would trim first, and a
                // confirmation whose "there is no undo" got cut is not a confirmation. Both are footnote
                // weight, which is what the section's own footer uses.
                Gw.Text(
                    Fit(
                        Loc.F(
                            "config.resetAll.confirm",
                            state.ResetImpact.Wanted,
                            state.ResetImpact.Grinds,
                            state.ResetImpact.Watches,
                            state.ResetImpact.Views),
                        ImGui.GetContentRegionAvail().X,
                        TextRole.Footnote),
                    TextRole.Footnote);

                Gw.Text(Loc.T("config.resetAll.noUndo"), TextRole.Footnote);

                var gap = Frame.Look.SpaceSmall * Frame.Scale;

                if (Gw.Button(Loc.T("config.resetAll.yes"), ButtonStyle.Plain, ButtonSize.Small))
                {
                    state.ResetConfirmRequested = true;
                }

                ImGui.SameLine(0f, gap);

                if (Gw.Button(Loc.T("config.resetAll.cancel"), ButtonStyle.Plain, ButtonSize.Small))
                {
                    state.ResetCancelRequested = true;
                }
            }
        }

        Gw.EndSection();
    }

    private static void DrawAbout(ConfigViewState state)
    {
        if (Gw.BeginSection(Loc.T("config.about")))
        {
            Gw.BeginRow(Loc.T("config.version"));
            Gw.Text(state.VersionText, TextRole.Body);
            Gw.EndRow();

            Gw.BeginRow(Loc.T("config.catalog"));
            Gw.Text(state.CatalogText, TextRole.Body);
            Gw.EndRow();

            // Which snapshot this build is actually reading, in the snapshot's own words: source,
            // date and schema version. It is the one place a player (or a bug report) can see that
            // the obtain data is old, offline-built, or newer than the plugin.
            Gw.BeginRow(Loc.T("config.dataset"));
            Gw.Text(state.DatasetText, TextRole.Body);
            Gw.EndRow();

            // Fitted, not wrapped: a long footnote in a section ran past the pane and forced a
            // horizontal scrollbar (the config golden showed it clipped mid-word).
            Gw.Text(
                Fit(
                    Loc.T("config.obtain.note"),
                    ImGui.GetContentRegionAvail().X,
                    TextRole.Footnote),
                TextRole.Footnote);

            // The credit, where the data is shown: the snapshot is a volunteer project's work, so the
            // settings window names the site the file came from instead of leaving the licence to a
            // file in the repository (docs/data-provenance.md has the reading behind it).
            if (state.DatasetSource.Length > 0)
            {
                Gw.Text(
                    Fit(Loc.F("config.obtain.credit", state.DatasetSource), ImGui.GetContentRegionAvail().X, TextRole.Footnote),
                    TextRole.Footnote);
            }

            // A bug report needs version, collection size and theme; one button puts all three on the
            // clipboard instead of asking the player to hunt through logs.
            if (Gw.Button(Loc.T("config.diagnostics"), ButtonStyle.Plain, ButtonSize.Small))
            {
                state.CopyDiagnosticsRequested = true;
            }
        }

        Gw.EndSection();
    }
}
