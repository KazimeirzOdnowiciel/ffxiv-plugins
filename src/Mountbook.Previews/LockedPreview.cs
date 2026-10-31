using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// A mount the player has not collected. This is the state that shows the disabled <c>Ride</c> button and its
/// reason line, the `Not collected` badge, and a mount with no whistle item - none of which any other golden
/// draws (every other screen selects an owned mount).
/// </summary>
public sealed class LockedPreview : IPreviewScreen
{
    /// <summary>The sample's unowned mount: the two-seat Garlond machine.</summary>
    private const uint LockedId = 470;

    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public LockedPreview()
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
            SelectedId = LockedId,
            SourceLabels = ["Any", "Achievement", "Event", "PvP", "Quest", "Trial"],
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            RideDisabledReason = "Not collected.",
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/locked.png</c>.</summary>
    public string Name => "locked";

    public Look Look { get; } = Look.Eorzea();

    public ReadOnlySpan<PreviewInput> Script => [];

    public Vector2 WindowSize => new(1040f, 660f);

    public void Draw() => view.Draw(state);
}
