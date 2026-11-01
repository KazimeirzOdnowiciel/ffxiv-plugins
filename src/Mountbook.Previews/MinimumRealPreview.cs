using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The window's 820x520 minimum in the state a player actually has: a full book, a saved view in the row,
/// and a filter narrow enough that the header says how much of the collection is on screen.
/// </summary>
/// <remarks>
/// <para>
/// <c>minimum.png</c> draws the smallest window with no presets and no filter, which is the easiest state the
/// view can be in - and two rows have landed above the list since the filter block was measured: the saved-view
/// row (<c>DrawSavedViewRow</c>) and the header's "n of m shown collected" line
/// (<c>RefreshFilterCounts</c>). Together they are about a card row, so the in-game sweep
/// at the 0.2.0 boundary found the list 65 px tall with the real book: no card and no table row fully visible,
/// while this scene's simpler sibling stayed green. The two states differ by exactly the two rows that were
/// invisible to the gate, so both are committed: <c>minimum</c> keeps the empty-filter case covered and this
/// pair draws the one that has to fit a card.
/// </para>
/// <para>
/// The book is the twelve-mount sample narrowed by a real query, exactly as <see cref="ChipCounts"/> draws a
/// full book's counts over the same sample: a preview never reads the game's catalogue, and a scene whose
/// counters contradicted its filters would be a picture of a state that cannot exist. The header counts the
/// book the 0.2.0 sweep measured (369 mounts, 136 collected), and the list is what the applied preset selects
/// from it.
/// </para>
/// <para>
/// The numbers this pair pins, measured in the render: the list is 118 px in the cards view and 166 px in the
/// table, where the same state measured 23 and 71 before the fix (65 in the client), against a card of 90.75 px
/// and a table row of 32.19. So one whole card draws at the minimum, and the table's 44 px heading plus three
/// rows. At the default 1040x660 the list is 231 px - 2.2 card rows of the goldens' 106.8 px pitch - which is
/// the client's own 157 px plus the 48 the preset row used to take there: the ~2.5 rows `t_c3b76c01` recorded.
/// </para>
/// </remarks>
public sealed class MinimumRealPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public MinimumRealPreview()
    {
        state = MinimumRealState.Build(MountViewMode.Grid);
    }

    /// <summary>Also the golden's file name: <c>goldens/minimum-real.png</c>.</summary>
    public override string Name => "minimum-real";

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    /// <summary>Exactly the window's enforced minimum.</summary>
    public override Vector2 WindowSize => new(820f, 520f);

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>
/// The same state in the table: the view that failed the sweep's minimum case with a column header and one
/// clipped row. Its filter block is a row shorter than the grid's (the grouping combo is a grid feature, so
/// there the availability combo pairs with the sort), which is exactly why the two are separate goldens: one
/// image cannot pin both blocks.
/// </summary>
public sealed class MinimumRealTablePreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public MinimumRealTablePreview()
    {
        state = MinimumRealState.Build(MountViewMode.Table);
    }

    /// <summary>Also the golden's file name: <c>goldens/minimum-real-table.png</c>.</summary>
    public override string Name => "minimum-real-table";

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    public override Vector2 WindowSize => new(820f, 520f);

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>The state both minimum-with-a-book scenes draw, so the two images differ only in the view mode.</summary>
internal static class MinimumRealState
{
    /// <summary>The book the 0.2.0 sweep measured in the client, and how much of it was collected.</summary>
    internal const int BookSize = 369;

    internal const int Owned = 136;

    /// <summary>The preset the scene has applied: the ownership chip and nothing else, so the row shows an
    /// applied chip and the header draws its filtered-count line.</summary>
    private static readonly SavedView Applied =
        SavedViewRules.Capture("Unowned", string.Empty, MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None);

    /// <summary>The row's presets: the applied one, the state everything else draws, and one with a query in it
    /// so the row is a row of chips rather than a repeat of the chip above it.</summary>
    internal static IReadOnlyList<SavedView> Presets() =>
    [
        SavedViewRules.Capture("Everything", string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None),
        Applied,
        SavedViewRules.Capture("Tradeable", "tradeable:y", MountOwnership.All, MountExpansion.All, MountOrder.CommonestFirst, MountGrouping.None),
    ];

    /// <summary>Builds the state the scenes draw: the sample narrowed by the applied preset, over the book's counts.</summary>
    /// <param name="mode">Which view the scene draws.</param>
    internal static MountViewState Build(MountViewMode mode)
    {
        IReadOnlyList<MountShelfItem> all = MountbookPreview.SampleMounts();
        var query = new MountQuery(Applied.Query, Applied.Ownership, Applied.Expansion, Applied.Order);
        IReadOnlyList<MountShelfItem> shown = query.Apply(all);

        return new MountViewState
        {
            Items = shown,
            Expansions = MountBreakdown.ByExpansion(shown),
            RarestMissing = MountRarityInsight.Find(shown),
            OwnershipCounts = [BookSize, Owned, BookSize - Owned, 0],
            Owned = Owned,
            Total = BookSize,
            SelectedId = shown.Count > 0 ? shown[0].Id : null,
            SourceOptions = MountbookPreview.SourceOptions(all),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(all),
            SavedViews = Presets(),
            Ownership = Applied.Ownership,
            Expansion = Applied.Expansion,
            Order = Applied.Order,
            GroupBy = Applied.GroupBy,
            ViewMode = mode,
        };
    }
}
