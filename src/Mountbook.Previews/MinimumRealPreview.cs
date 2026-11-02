using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The window's 820x520 minimum in the state a player actually has: a full book, a saved view the filter
/// block has to make room for, and a filter narrow enough that the header says how much of the collection is
/// on screen.
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
/// The numbers this pair pins were re-measured twice: when the table's heading was frozen (`t_43679848`) and
/// again when the cards view's filter block was made to give a row back (`t_9a04c7e7`). Both lists now measure
/// 118 px. The table's rect is its 44 px heading, two whole rows of 32.19 and a 9 px sliver of a third; the
/// cards' is one whole card row - the 64 px tile, its 8 px caption gap, an 18.75 px caption line and the
/// 16 px row gap, 106.75 px - with 11 px to spare. Before that the cards view's block was one row taller than
/// the table's (the grouping combo keeps a row of its own there, and availability pairs with the sort only
/// where it does not), which left the list 70 px: shorter than a card row, so the first card drew as a bare
/// tile with its caption clipped and no card read at all.
/// </para>
/// <para>
/// The row comes back from no control - every combo in the block is one a player needs - but from the
/// saved-view line, the only row that exists merely while a preset does and the only one with a second door
/// (a right-click on the filter block opens the same dialog). It is drawn only while the list can still show
/// a whole card row under the block, measured against the block's height on a frame that drew the line
/// (`LayoutMeasure.FilterBlockCrowdsList`); at 820x520 it cannot, so `minimum-real` draws no preset chips
/// while `minimum-real-table`, a row shorter and its rows 32 px, keeps them. At the default 1040x660 the line
/// still rides the ownership chips and the block is unchanged.
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
/// image cannot pin both blocks - and it is also why this one still draws the preset chips that the cards
/// view has to give up.
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
