using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The saved-views row: four presets above the search box, the applied one drawn prominent, at the default
/// window size.
/// </summary>
/// <remarks>
/// <para>
/// This is the one golden that proves the row exists, and that it costs the filter block no line at all where the
/// column can afford it: every other scene draws with no presets, so they are byte-identical to the build before
/// the feature, and the difference between those images and this one is exactly what a player who saves a view
/// sees. The presets ride the ownership chips here - the applied one drawn prominent beside them, with the Manage
/// door after it - which is the form the row takes while the chips still draw every label whole. The line above
/// the search box is the fallback for a column too narrow for that, and it is drawn by the scenes below 340 px.
/// </para>
/// <para>
/// The list is the scene's frozen sample, not the result of these filters: the subject is the filter block, and
/// a preview never runs the query.
/// </para>
/// </remarks>
public sealed class SavedViewsPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public SavedViewsPreview()
    {
        state = MountbookPreview.CreateState();
        state.SavedViews = Presets();
    }

    /// <summary>Also the golden's file name: <c>goldens/views.png</c>.</summary>
    public override string Name => "views";

    /// <summary>The window size the plugin gives its main window, where the filter column pairs its combos.</summary>
    public override Vector2 WindowSize => new(1040f, 660f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);

    /// <summary>
    /// Four presets over the same untouched filter the other scenes draw with, so the first one is applied: the
    /// names are the questions the roadmap names, which is what the feature is for.
    /// </summary>
    internal static IReadOnlyList<SavedView> Presets() =>
    [
        SavedViewRules.Capture("Everything", string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None),
        SavedViewRules.Capture("Farmable now", "tradeable:y owned:n", MountOwnership.Unowned, MountExpansion.All, MountOrder.CommonestFirst, MountGrouping.Source),
        SavedViewRules.Capture("PvP only", "source:pvp", MountOwnership.Unowned, MountExpansion.All, MountOrder.RarestFirst, MountGrouping.None),
        SavedViewRules.Capture("Event mounts", "source:event", MountOwnership.Unowned, MountExpansion.All, MountOrder.OldestFirst, MountGrouping.Patch),
    ];
}

/// <summary>
/// The saved-views dialog: the name field and its save button, then one row per preset with apply, rename and
/// delete beside it.
/// </summary>
/// <remarks>
/// The four actions this feature has are all in this image, which is why it is its own golden rather than being
/// trusted to a click in the game: the row alone proves where a preset is *used*, and nothing about how one is
/// saved, renamed or deleted. The dialog is opened before the first frame, so the image is the settled card - the
/// script's last input only holds the run open long enough for Glasswork's spring to finish and to keep the
/// pointer off every control, so no hover or tooltip is baked in.
/// </remarks>
public sealed class SavedViewsDialogPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public SavedViewsDialogPreview()
    {
        state = MountbookPreview.CreateState();
        state.SavedViews = SavedViewsPreview.Presets();
        state.ViewsDialogOpen = true;
    }

    /// <summary>Also the golden's file name: <c>goldens/viewsdialog.png</c>.</summary>
    public override string Name => "viewsdialog";

    /// <summary>The default window, behind the scrim: the same size the row's golden uses.</summary>
    public override Vector2 WindowSize => new(1040f, 660f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    /// <summary>One move, away from every control, at a late frame: the run has to outlast the dialog's spring.</summary>
    private static readonly PreviewInput[] ScriptInputs =
    [
        new(20, PreviewInputKind.Move, new Vector2(12f, 640f)),
    ];

    public override ReadOnlySpan<PreviewInput> Script => ScriptInputs;

    protected override void DrawScene() => view.Draw(state);
}
