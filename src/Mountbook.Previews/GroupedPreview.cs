using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The card grid with its sections drawn: one heading per expansion, each with the count, in release
/// order. The sample spans five expansions on purpose, so the headings, their order and the row they take
/// above their cards are all in one image.
/// </summary>
public sealed class GroupedPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public GroupedPreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        foreach (MountShelfItem item in items)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            RarestMissing = MountRarityInsight.Find(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = 420u,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            GroupBy = MountGrouping.Expansion,
            SourceOptions = MountbookPreview.SourceOptions(items),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(items),
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/grouped.png</c>.</summary>
    public override string Name => "grouped";

    /// <summary>
    /// Taller than the table scene on purpose: one heading is not evidence that the headings are ordered,
    /// so the image has to hold at least two of them with their cards.
    /// </summary>
    public override Vector2 WindowSize => new(1040f, 900f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>
/// The card grid grouped by obtain type: one heading per type the sample carries, in the taxonomy's own order -
/// largest group first, not alphabetical - each with the number of mounts under it.
/// </summary>
/// <remarks>
/// Split from <c>grouped</c> rather than swapping that scene over, because that image is the evidence for the
/// expansion order and one frame cannot be evidence for two different orders. No other scene groups by source,
/// so the two rules this adds had no golden at all: a mount carrying more than one type is filed under its first
/// (a section per type would draw the same card twice), and an unknown type would sort after every known one.
/// </remarks>
public sealed class TaxonomyPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public TaxonomyPreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        foreach (MountShelfItem item in items)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            RarestMissing = MountRarityInsight.Find(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = 420u,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            GroupBy = MountGrouping.Source,
            SourceOptions = MountbookPreview.SourceOptions(items),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(items),
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/taxonomy.png</c>.</summary>
    public override string Name => "taxonomy";

    /// <summary>
    /// As tall as the grouped scene: the sample spans seven types, and the point of the image is that at least
    /// two headings and their cards are in it at once with their order readable.
    /// </summary>
    public override Vector2 WindowSize => new(1040f, 900f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}
