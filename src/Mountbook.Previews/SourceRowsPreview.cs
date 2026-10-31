using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The detail pane with its "How to obtain" list in sight, which no other scene manages: at the usual
/// window sizes those rows sit below the fold, so the tappable-row treatment had no golden at all.
/// </summary>
/// <remarks>
/// The selected mount is given two source lines on purpose - one that names a record (a tappable row with
/// a chevron) and one that is prose (a plain row) - because those are the two branches of
/// <see cref="MountSourceLinks"/> and the difference is a single glyph on the right edge.
/// </remarks>
public sealed class SourceRowsPreview : IPreviewScreen
{
    /// <summary>The sample's Aithon, whose pane is the one this scene shows.</summary>
    private const uint SelectedId = 58u;

    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public SourceRowsPreview()
    {
        IReadOnlyList<MountShelfItem> sample = MountbookPreview.SampleMounts();
        var items = new List<MountShelfItem>(sample.Count);
        var owned = 0;

        foreach (MountShelfItem item in sample)
        {
            if (item.Owned)
            {
                owned++;
            }

            items.Add(item.Id == SelectedId
                ? item with
                {
                    Sources =
                    [
                        new MountSourceLine("Achievement", "A Legend for All Time", "Achievement", 50u),
                        new MountSourceLine("Quest", "At level 30 or above, speak with the Wandering Minstrel", string.Empty, 0u),
                    ],
                }
                : item);
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = SelectedId,
            // A token price, so the "how far off am I" line is in a golden: the amount and the count are
            // both fixed here, so the image cannot drift with a player's bags.
            CostText = "12,000 Seafarer's Cowries - you have 3,400, 8,600 to go",
            ItemName = "Aithon Whistle",
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/sources.png</c>.</summary>
    public string Name => "sources";

    /// <summary>Tall on purpose: the whole detail pane, source rows included, has to be above the fold.</summary>
    public Vector2 WindowSize => new(940f, 1000f);

    public Look Look { get; } = Look.Eorzea();

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}
