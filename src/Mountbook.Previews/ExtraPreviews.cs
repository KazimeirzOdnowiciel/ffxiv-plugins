using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The empty state: no mounts match, so the view's "no results" branch is covered by a golden.
/// </summary>
/// <remarks>
/// The main golden always has twelve mounts on screen; without this screen a regression in the empty
/// state - a missing hint, a stray grid row, a header that divides by zero - would ship unseen.
/// </remarks>
public sealed class EmptyStatePreview : IPreviewScreen
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public EmptyStatePreview() => state = CreateState(MountOwnership.Wanted, string.Empty);

    /// <summary>
    /// The filtered-empty state: an empty list with the whole sample still counted behind it, because a
    /// real window showing an empty Wanted list still reads "10 of 12".
    /// </summary>
    /// <param name="ownership">Which filter produced the empty list - it picks the branch's wording.</param>
    /// <param name="search">The search text, so the no-match branch can show a query that matches nothing.</param>
    internal static MountViewState CreateState(MountOwnership ownership, string search)
    {
        IReadOnlyList<MountShelfItem> all = MountbookPreview.SampleMounts();
        var owned = 0;

        foreach (MountShelfItem item in all)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        return new MountViewState
        {
            Items = [],
            Expansions = MountBreakdown.ByExpansion(all),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(all, null),
            Owned = owned,
            Total = all.Count,
            SourceLabels = ["Any", "Achievement", "Event", "PvP", "Quest", "Trial"],
            Ownership = ownership,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            Search = search,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/empty.png</c>.</summary>
    public string Name => "empty";

    /// <summary>
    /// Tall enough to contain the block it is testing. At 560 the filter column left the list about
    /// forty pixels, so only the heading was in the image and the hint and its button were clipped - the
    /// golden proved less than it claimed.
    /// </summary>
    public Vector2 WindowSize => new(1040f, 760f);

    public Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}

/// <summary>
/// The other empty state: a search that matches nothing.
/// </summary>
/// <remarks>
/// Split from <c>empty</c> rather than swapping that scene over, because the two branches differ in
/// three strings - the title, the hint and the operator list the hint carries - and that list is the
/// only place the search grammar is advertised where a player is already stuck.
/// </remarks>
public sealed class NoMatchPreview : IPreviewScreen
{
    private readonly MountbookView view = new();

    private readonly MountViewState state = EmptyStatePreview.CreateState(MountOwnership.All, "zzz");

    /// <summary>Also the golden's file name: <c>goldens/nomatch.png</c>.</summary>
    public string Name => "nomatch";

    /// <summary>
    /// Tall enough to contain the block it is testing. At 560 the filter column left the list about
    /// forty pixels, so only the heading was in the image and the hint and its button were clipped - the
    /// golden proved less than it claimed.
    /// </summary>
    public Vector2 WindowSize => new(1040f, 760f);

    public Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}

/// <summary>
/// The wanted ("hunting") list: three mounts marked, the filter showing that view, and one of them
/// selected so the detail pane draws <c>Unwant</c> instead of <c>Want</c>.
/// </summary>
/// <remarks>
/// Without this screen nothing covers the filled marker or the Unwant label: every other golden has
/// an empty wanted set, so both would only ever be seen with the muted marker.
/// </remarks>
public sealed class WantedPreview : IPreviewScreen
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public WantedPreview()
    {
        IReadOnlyList<MountShelfItem> all = MountbookPreview.SampleMounts();
        var wanted = new List<MountShelfItem>(3);
        var ids = new HashSet<uint>();

        for (var index = 0; index < all.Count && wanted.Count < 3; index++)
        {
            wanted.Add(all[index]);
            ids.Add(all[index].Id);
        }

        var ownedAll = 0;

        foreach (var item in all)
        {
            if (item.Owned)
            {
                ownedAll++;
            }
        }

        state = new MountViewState
        {
            // The list is the wanted subset, but the statistics describe the whole collection - that
            // is what the real window shows, and the earlier version's strip read "ARR 100% HW 100%".
            Items = wanted,
            Expansions = MountBreakdown.ByExpansion(all),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name).OwnershipCounts(all, ids),
            Owned = ownedAll,
            Total = all.Count,
            // The recent mount is the selected one, so the golden covers the New badge as well as the
            // New square on its card.
            SelectedId = wanted[1].Id,
            ItemName = "Aithon Whistle",
            Ownership = MountOwnership.Wanted,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            WantedIds = ids,

            // One of the wanted mounts is also "collected this session", so the golden covers the New
            // square and the New badge next to the round wanted marker.
            RecentIds = new HashSet<uint> { wanted[1].Id },
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/wanted.png</c>.</summary>
    public string Name => "wanted";

    public Vector2 WindowSize => new(1040f, 660f);

    public Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}

/// <summary>
/// The same book in a narrow window, where the two columns squeeze and the card grid drops to fewer
/// per row. The main golden is 1040 wide and never exercises that.
/// </summary>
public sealed class NarrowPreview : IPreviewScreen
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public NarrowPreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name).OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            // An unowned mount on purpose: at this width the action row stacks, so the narrow golden covers the
            // stacked footer *and* the disabled button with its reason line. Genie of the Lamp is also the
            // one sample recorded as tradeable, so this scene is where the market button and its price line
            // are in a golden (the preview has no network: the text is what the window would draw).
            SelectedId = 412u,
            MarketCanCheck = true,
            MarketPriceText = "2,999,999 gil | 12 listings | Shiva (Light)",
            ItemName = null,
            RideDisabledReason = "Not collected.",
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/narrow.png</c>.</summary>
    public string Name => "narrow";

    /// <summary>
    /// 620 wide is the point of the scene; tall enough that the market row, the wiki link, the price line and
    /// the watch toggle are all drawn in full. At 620 tall the toggle landed exactly on the body clip edge,
    /// and a half-drawn control is not evidence of a working one: this mount stacks five badges at this width,
    /// which is what pushed the price block down.
    /// </summary>
    public Vector2 WindowSize => new(620f, 820f);

    public Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}

/// <summary>
/// The market path at the flagship size. <c>narrow</c> is the only other scene with a price, and at 620 px the
/// pane is too narrow for the price button and the wiki link to share a line - so without this scene the
/// one-row layout would never appear in a golden and a clipping bug there would ship unseen.
/// </summary>
public sealed class PricePreview : IPreviewScreen
{
    private readonly MountbookView view = new();
    private readonly MountViewState state;

    public PricePreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name).OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            // A price and a listing count, frozen: the preview has no network, and a golden must not depend on
            // one. Genie of the Lamp is the sample the fixtures record as tradeable.
            SelectedId = 412u,
            MarketCanCheck = true,
            MarketPriceText = "2,999,999 gil | 12 listings | Shiva (Light)",
            ItemName = "Genie of the Lamp",

            // Watched, on purpose: the "Unwatch" label, the threshold stepper, the "last seen" line and the
            // header's count all exist only while a watch does, so this scene is where they are drawn
            // (`narrow` has the same mount unwatched, which is the other branch). The line is built by the same
            // rule the window uses, against fixed numbers, so the wording cannot drift away from the code.
            Watching = true,
            WatchThreshold = 3_000_000,
            WatchText = MountWatch.Line(
                new MountWatchRecord { Threshold = 3_000_000, CheckedAt = 1_000_000, LastSeen = 2_999_999 },
                1_000_720),
            WatchingCount = 1,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/price.png</c>.</summary>
    public string Name => "price";

    public Vector2 WindowSize => new(1040f, 660f);

    public Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}

/// <summary>
/// The dense table: the same sample mounts as rows, with the wanted list tinted, one row selected and
/// one mount carrying no obtain data so the blank cells are in the golden too.
/// </summary>
/// <remarks>
/// Without this the table's header, its zebra rows and its truncation would only ever be checked by
/// opening the client, which is the state the settings window was in before its own golden existed.
/// </remarks>
public sealed class TablePreview : IPreviewScreen
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public TablePreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var wanted = new HashSet<uint> { items[0].Id, items[9].Id };
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
                .OwnershipCounts(items, wanted),
            Owned = owned,
            Total = items.Count,
            // A collected mount near the top of the list, so the row highlight is inside the part of
            // the table a 660 px preview shows; the first row is already wanted, and a tinted name in
            // the accent would hide the selection bar.
            SelectedId = items[1].Id,
            ViewMode = MountViewMode.Table,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            WantedIds = wanted,
            // Two rows carry a market price and the rest are unasked, which is the state the table is
            // actually in until the batch lands: priced rows show a number, the others stay blank rather
            // than claiming 0 gil.
            Prices = new Dictionary<uint, int>
            {
                [items[1].Id] = 2401000,
                [items[4].Id] = 48999,
            },
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/table.png</c>.</summary>
    public string Name => "table";

    public Vector2 WindowSize => new(1040f, 660f);

    public Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);
}
