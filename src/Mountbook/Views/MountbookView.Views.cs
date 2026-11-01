using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>
/// Part of <see cref="MountbookView"/>: the row of saved filter presets above the search box, and the dialog
/// that saves, renames and deletes them.
/// </summary>
/// <remarks>
/// <para>
/// The row is drawn only when at least one preset exists, which is the whole space budget of this feature: the
/// filter block was taken from seven rows to four in the layout pass before this one, and a permanently empty
/// "no saved views" line would spend a fifth of that back. With no preset there is nothing here, so every golden
/// that does not deliberately save one stays byte-identical.
/// </para>
/// <para>
/// It has two forms, and which one is drawn is measured, never chosen by window size. On a column wide enough for
/// the applied chip and the Manage door to sit beside the ownership chips, the presets ride that row
/// (<see cref="DrawSavedViewsInline"/>): the row costs the list nothing, which at the window's 820x520 minimum is
/// the difference between one whole card and none. On a column too narrow for that, the presets keep the line
/// above the search box this type draws, because a chip that has to be clipped to fit is worse than a line.
/// </para>
/// <para>
/// The dialog is the one surface that works from nothing, and it is opened by a right-click on the filter
/// block as well as by the row's button: with no presets the row is absent, so a row-only door would be a
/// feature nobody can start using. A right-click costs no pixels in any scene (a closed popup draws nothing),
/// which is what keeps the two rules - "nothing is drawn when there are no presets" and "there is always a way
/// to save one" - from contradicting each other.
/// </para>
/// </remarks>
public sealed partial class MountbookView
{
    /// <summary>
    /// Each chip's measured width, reused every frame. The draw path may not allocate (see the contract on
    /// <see cref="MountbookView"/>), and a chip's width only changes with the Look's type face, which is already
    /// what <see cref="MountbookView.TextWidth"/> keys its own memo on.
    /// </summary>
    private readonly float[] chipWidths = new float[SavedViewRules.MaxViews];

    /// <summary>
    /// The search field's tooltip, built once: the operators the grammar accepts and then the one affordance
    /// that has no visible control - right-clicking the filters to save them as a view. Two lines rather than
    /// two tooltips, because only one tooltip per item draws, and the operator list is the whole discoverability
    /// of the grammar, so it cannot be the half that loses.
    /// </summary>
    private static string? searchTooltip;

    /// <summary>The search field's tooltip text, with the saved-views line under the operators.</summary>
    private static string SearchTooltip =>
        searchTooltip ??= Loc.F("filter.search.tooltip", SearchHint) + '\n' + Loc.T("views.hint.rightClick");

    /// <summary>
    /// The row of presets: one chip per view, the applied one drawn prominent, and the button that opens the
    /// dialog. Draws nothing at all when there is no preset to show.
    /// </summary>
    /// <param name="state">The state whose presets and current filter decide what is drawn.</param>
    private void DrawSavedViewRow(MountViewState state)
    {
        IReadOnlyList<SavedView> views = state.SavedViews;

        if (views.Count == 0)
        {
            return;
        }

        var scale = Frame.Scale;
        var look = Frame.Look;
        var gap = look.SpaceSmall * scale;
        var width = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        var manage = Loc.T("views.manage");

        // The button keeps its corner whatever the chips do: it is the only door into the dialog, and a row of
        // chips with no way to edit them reads as read-only.
        var manageWidth = SmallButtonWidth(manage, look, scale);

        // Bounded by the buffer, not by the list: a config a newer build wrote is left alone by the ladder
        // (forward compatibility), so this frame cannot assume the list is inside the cap.
        var counted = Math.Min(views.Count, chipWidths.Length);

        for (var index = 0; index < counted; index++)
        {
            chipWidths[index] = SmallButtonWidth(views[index].Name, look, scale);
        }

        int fit = LayoutMeasure.ViewsThatFit(width, manageWidth + gap, gap, chipWidths.AsSpan(0, counted));
        int applied = AppliedView(state);

        for (var index = 0; index < fit; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine(0f, gap);
            }

            // The applied one is drawn prominent rather than with a marker: the row's job is "which filter am I
            // looking at", and a selected style answers it without costing the chip any width.
            if (Gw.Button(views[index].Name, index == applied ? ButtonStyle.Prominent : ButtonStyle.Default, ButtonSize.Small))
            {
                state.ApplyViewIndex = index;
            }

            Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("views.chip.tooltip"));
        }

        if (fit > 0)
        {
            ImGui.SameLine(0f, gap);
        }

        if (Gw.Button(manage, ButtonStyle.Plain, ButtonSize.Small))
        {
            state.ViewsDialogOpen = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("views.manage.tooltip"));
    }

    /// <summary>
    /// Where the preset row goes this frame, and how much of the ownership row it takes when it rides there.
    /// </summary>
    /// <param name="Inline">True when the applied chip and the Manage door share the ownership row.</param>
    /// <param name="Reserve">What the inline form takes from that row's width, in pixels; zero when it does not.</param>
    private readonly record struct SavedViewRow(bool Inline, float Reserve);

    /// <summary>
    /// Decides whether the presets ride the ownership row: they do when the chip row still draws every label
    /// whole beside the applied chip and its Manage door, and take a line of their own when it would not.
    /// </summary>
    /// <remarks>
    /// Measured rather than chosen by window size, like every other width decision in this block: the answer
    /// depends on how wide the counts in the chip labels are, so it has to be taken against the same numbers
    /// <c>Gw.Segmented</c> divides into cells. With no presets there is nothing to place and nothing is reserved,
    /// which is what keeps every scene without a view byte-identical.
    /// </remarks>
    /// <param name="state">The state whose presets and current filter decide the form.</param>
    /// <param name="width">The filter column's width, in pixels.</param>
    /// <param name="cells">How many ownership chips are drawn.</param>
    /// <param name="widestChipLabel">The widest chip label's advance width, in pixels.</param>
    /// <param name="chipInset">The chip row's inset from a cell's edge to its ink, in pixels.</param>
    /// <param name="chipSlack">Slack for rounding and hinting, in pixels.</param>
    /// <param name="gap">The gutter between two controls, in pixels.</param>
    private static SavedViewRow PlanSavedViews(
        MountViewState state,
        float width,
        int cells,
        float widestChipLabel,
        float chipInset,
        float chipSlack,
        float gap)
    {
        if (state.SavedViews.Count == 0)
        {
            return new SavedViewRow(false, 0f);
        }

        var look = Frame.Look;
        var scale = Frame.Scale;
        var reserve = SmallButtonWidth(Loc.T("views.manage"), look, scale) + gap;
        int applied = AppliedView(state);

        if (applied >= 0 && applied < state.SavedViews.Count)
        {
            reserve += SmallButtonWidth(state.SavedViews[applied].Name, look, scale) + gap;
        }

        var inline = LayoutMeasure.ViewsInline(width, cells, widestChipLabel, chipInset, chipSlack, reserve);
        return new SavedViewRow(inline, inline ? reserve : 0f);
    }

    /// <summary>
    /// The inline form of the preset row: the applied preset's chip and the button that opens the dialog, on
    /// the ownership row. Drawn only when <see cref="PlanSavedViews"/> measured that the chips still fit.
    /// </summary>
    /// <remarks>
    /// One chip rather than the row's other presets: a row half filled with chips that the fit test then had to
    /// drop would hide the one chip that answers "which filter am I looking at" - and that chip is the carried
    /// meaning of the row, not its length. The rest stay in the dialog, which Manage is the door to.
    /// </remarks>
    /// <param name="state">The state whose presets to read and write back into.</param>
    private static void DrawSavedViewsInline(MountViewState state)
    {
        var gap = Frame.Look.SpaceSmall * Frame.Scale;
        int applied = AppliedView(state);

        if (applied >= 0 && applied < state.SavedViews.Count)
        {
            ImGui.SameLine(0f, gap);

            if (Gw.Button(state.SavedViews[applied].Name, ButtonStyle.Prominent, ButtonSize.Small))
            {
                state.ApplyViewIndex = applied;
            }

            Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("views.chip.tooltip"));
        }

        ImGui.SameLine(0f, gap);

        if (Gw.Button(Loc.T("views.manage"), ButtonStyle.Plain, ButtonSize.Small))
        {
            state.ViewsDialogOpen = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("views.manage.tooltip"));
    }

    /// <summary>
    /// The index of the preset the current filter is exactly, or -1. Compared on every axis, so a chip stops
    /// claiming to be applied the moment the player changes the search, a filter, the order or the grouping.
    /// </summary>
    /// <param name="state">The state to test.</param>
    private static int AppliedView(MountViewState state) =>
        SavedViewRules.MatchIndex(
            state.SavedViews,
            state.Search,
            state.Ownership,
            state.Expansion,
            state.Order,
            state.GroupBy);

    /// <summary>
    /// The saved-views dialog: the name field and its save button, then one row per preset with apply, rename
    /// and delete beside it.
    /// </summary>
    /// <param name="state">The state to read and report into.</param>
    private void DrawSavedViewsDialog(MountViewState state)
    {
        var open = state.ViewsDialogOpen;

        if (!Gw.Dialog("##savedviews"u8, ref open, Loc.T("views.title"), new Vector2(430f, 0f)))
        {
            // Not open, or waiting behind another dialog: the flag stays as it was, so a dialog that is queued
            // opens on the frame the other closes instead of being forgotten.
            state.ViewsDialogOpen = open;
            return;
        }

        try
        {
            DrawSavedViewsDialogBody(state, ref open);
        }
        finally
        {
            // Like BeginPopupModal: a throw inside the content must not leave Glasswork's dialog depth one
            // short for the rest of the session.
            Gw.EndDialog();
            state.ViewsDialogOpen = open;
        }
    }

    /// <summary>The dialog's content: what it edits, and the four actions a preset has.</summary>
    /// <param name="state">The state to read and report into.</param>
    /// <param name="open">The dialog's own open flag; clearing it closes the dialog this frame.</param>
    private static void DrawSavedViewsDialogBody(MountViewState state, ref bool open)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;
        var gap = look.SpaceSmall * scale;
        var renaming = state.ViewRenameIndex is { } aim && aim >= 0 && aim < state.SavedViews.Count;

        Gw.Text(Loc.T("views.hint"), TextRole.Footnote);

        // The field and its button share one row, so the whole dialog is a field, the list and a Close - short
        // enough that a player reads it instead of scrolling it.
        var save = Loc.T(renaming ? "views.rename" : "views.save");
        var saveWidth = SmallButtonWidth(save, look, scale);

        ImGui.SetNextItemWidth(MathF.Max(ImGui.GetContentRegionAvail().X - saveWidth - gap, 1f));

        var name = state.ViewNameDraft;
        _ = Gw.TextField(ref name, hint: Loc.T("views.name.hint"), maxLength: SavedViewRules.MaxNameLength);
        state.ViewNameDraft = name;

        ImGui.SameLine(0f, gap);

        // Disabled rather than refusing on click: a name is the one thing a preset cannot do without, and a
        // button that does nothing is the shape of a bug report rather than an explanation.
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(name));

        if (Gw.Button(save, ButtonStyle.Default, ButtonSize.Small))
        {
            state.SaveViewRequested = true;
        }

        ImGui.EndDisabled();

        if (renaming)
        {
            ImGui.SameLine(0f, gap);

            if (Gw.Button(Loc.T("views.cancel"), ButtonStyle.Plain, ButtonSize.Small))
            {
                RenameCancelled(state);
            }
        }

        Gw.Divider();

        if (state.SavedViews.Count == 0)
        {
            Gw.Text(Loc.T("views.empty"), TextRole.Footnote);
        }

        int applied = AppliedView(state);

        for (var index = 0; index < state.SavedViews.Count; index++)
        {
            SavedView view = state.SavedViews[index];

            // The name is the apply button, exactly as it is in the row: two shapes for one action would be two
            // things to learn, and the dialog is where the row's overflow ends up.
            if (Gw.Button(view.Name, index == applied ? ButtonStyle.Prominent : ButtonStyle.Default, ButtonSize.Small))
            {
                state.ApplyViewIndex = index;
            }

            ImGui.SameLine(0f, gap);

            if (Gw.Button(Loc.T("views.rename"), ButtonStyle.Plain, ButtonSize.Small))
            {
                state.ViewRenameIndex = index;
                state.ViewNameDraft = view.Name;
            }

            ImGui.SameLine(0f, gap);

            if (Gw.Button(Loc.T("views.delete"), ButtonStyle.Plain, ButtonSize.Small))
            {
                state.DeleteViewIndex = index;
            }
        }

        Gw.Divider();

        if (Gw.Button(Loc.T("views.close"), ButtonStyle.Default, ButtonSize.Small))
        {
            open = false;
        }
    }

    /// <summary>
    /// Leaves rename mode: the aim is dropped and the field is emptied, because the next save names a new
    /// preset rather than renaming the one that was aimed at.
    /// </summary>
    /// <param name="state">The state to clear.</param>
    private static void RenameCancelled(MountViewState state)
    {
        state.ViewRenameIndex = null;
        state.ViewNameDraft = string.Empty;
    }
}
