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
public sealed class GroupedPreview : IPreviewScreen
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
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = 420u,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            GroupBy = MountGrouping.Expansion,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/grouped.png</c>.</summary>
    public string Name => "grouped";

    /// <summary>
    /// Taller than the table scene on purpose: one heading is not evidence that the headings are ordered,
    /// so the image has to hold at least two of them with their cards.
    /// </summary>
    public Vector2 WindowSize => new(1040f, 900f);

    public Look Look { get; } = Look.Eorzea();

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}
