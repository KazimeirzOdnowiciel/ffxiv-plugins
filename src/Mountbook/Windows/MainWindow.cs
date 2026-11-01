using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Glasswork.Backend;
using Mountbook.Game;
using Mountbook.Services;
using Mountbook.UI;
using Mountbook.Views;

namespace Mountbook.Windows;

public sealed class MainWindow : GlassWindow, IDisposable
{
    /// <summary>The rounding of the glass pane; the pushed style var uses the same value.</summary>
    protected override float Radius => 22f;

    /// <summary>The design layout's window size in logical units (SPEC).</summary>
    private const float DefaultWidth = 1040f;

    private const float DefaultHeight = 660f;

    /// <summary>
    /// The narrowest body (in logical units) that still fits the three chrome buttons on one line.
    /// Below it the row is skipped rather than wrapped into a second line that steals grid height.
    /// </summary>
    private readonly Plugin plugin;

    /// <summary>Universalis reads, off the draw thread; the view only ever sees a formatted line.</summary>
    private readonly MarketPrices market;

    /// <summary>The token names and counts behind a "12,000 Seafarer's Cowries" source line.</summary>
    private readonly CurrencyStock stock;

    /// <summary>The selection the token line was last computed for, so the bags are not counted every frame.</summary>
    private uint costForId;

    private string costText = string.Empty;

    /// <summary>The price of the selection, kept beside the line it was formatted into: the tracker's default target.</summary>
    private MountCost? costOfSelection;

    /// <summary>The selection the tracker was last loaded for, so typing in the note is not overwritten per frame.</summary>
    private uint grindForId;

    /// <summary>The selection the watch control was last loaded for, for the same reason as <see cref="grindForId"/>.</summary>
    private uint watchForId;

    /// <summary>
    /// The record the watch line was last formatted from. Seeded past every value the data can hold, so the
    /// first frame formats rather than trusting a match nobody wrote.
    /// </summary>
    private uint watchLineForId = uint.MaxValue;

    private int watchLineThreshold = int.MinValue;

    private long watchLineCheckedAt = long.MinValue;

    private int watchLineLastSeen = int.MinValue;

    /// <summary>The item id whose price request failed, so the pane can say so and offer a retry.</summary>
    private uint marketFailedItem;

    private readonly MountbookView view = new();

    private readonly MountViewState state = new();

    private string lastSearch = string.Empty;

    private MountOwnership lastOwnership;

    private MountExpansion lastExpansion;

    private MountOrder lastOrder;

    /// <summary>The market price version <see cref="state"/> was last built from.</summary>
    private int priceVersionBuilt = -1;

    /// <summary>The order the price read was last offered for, so switching to it asks and switching away re-arms.</summary>
    private MountOrder pricesOrder;

    /// <summary>
    /// The seed <see cref="MountOrder.Random"/> shuffles with. Re-picked whenever the player chooses that
    /// order, then held: a seed that changed per frame would reshuffle the grid under the pointer.
    /// </summary>
    private int shuffleSeed;

    private int lastRevision = -1;

    /// <summary>Bumped when the user toggles a mount in the wanted list, so the query re-runs.</summary>
    private int wantedRevision;

    private int lastWantedRevision;

    private uint? lastTextureSelection;

    private bool itemsBuilt;

    /// <summary>The source combo's cached entries and the revision they were built at.</summary>
    private MountSourceCount[] sourceOptions = [new MountSourceCount(MountSourceKinds.Any, 0)];

    private int sourceOptionsRevision = int.MinValue;

    /// <summary>The source filter the current list was built with, so a change rebuilds it.</summary>
    private MountSourceKind lastSource = MountSourceKinds.Any;

    /// <summary>The cached recently-collected set the view reads, and the revision it was built at.</summary>
    private HashSet<uint> recentIds = [];

    // int.MinValue, not -1: the revision is also -1 before the catalog has been built, and the old
    // "|| recentIds.Count == 0" guard in its place rebuilt this set every frame for any player with
    // no recent mounts - the steady state two weeks after a session.
    private int recentIdsRevision = int.MinValue;

    /// <summary>The cached per-expansion progress, and the catalog revision it was built at.</summary>
    private IReadOnlyList<ExpansionProgress> expansions = [];

    private int expansionsRevision = int.MinValue;

    /// <summary>
    /// The rarest mount the player is missing, and the catalog revision it was found at: the header's one
    /// insight line, cached because finding it walks every mount and the per-frame path is lookups only.
    /// </summary>
    private RarestMissing? rarestMissing;

    private int rarestRevision = int.MinValue;

    /// <summary>Set by <see cref="OnOpen"/> so the first draw after opening re-reads the collection.</summary>
    private bool refreshOnOpen;

    /// <summary>
    /// Whether this session's window shows the first-run line, decided on the first draw and then held.
    /// </summary>
    /// <remarks>
    /// The flag it reads is persisted on the very frame the line is drawn, so re-reading the config each
    /// frame showed the line for one frame and then retired it before anyone could read it. Null, not false,
    /// because "not decided yet" and "decided against" are different states here.
    /// </remarks>
    private bool? firstRunHint;

    /// <summary>Set once a draw has failed, so the failure is logged on the transition (N4).</summary>
    private bool loggedDrawFailure;

    /// <summary>
    /// <c>NoBackground</c> stops ImGui painting its own fill: the glass is the background.
    /// The <c>###</c> suffix is the stable ImGui id, so renaming the visible title never
    /// resets a user's window position.
    /// </summary>
    public MainWindow(Plugin plugin)
        : base(plugin, Loc.T("window.main") + "###MountbookMain")
    {
        // FirstUseEver, not Always: a user who resizes the window keeps that size across sessions.
        Size = new Vector2(DefaultWidth, DefaultHeight);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
        market = new MarketPrices(Plugin.Log, Plugin.DataCenterName);
        stock = new CurrencyStock(Plugin.Inventory, Plugin.Data, Plugin.Log);
        IsOpen = plugin.Configuration.MainWindowOpen;
    }

    public void Dispose()
    {
        market.Dispose();
    }

    /// <summary>
    /// The priced source line of a mount, or null when it costs no tokens. Only the first priced line is
    /// read: a mount that costs two different tokens is rare, and two lines of arithmetic under one
    /// heading is not worth the space.
    /// </summary>
    /// <param name="item">The mount in the pane, or null.</param>
    private static MountCost? CostOf(MountShelfItem? item) =>
        item is null ? null : MountCosts.FirstOf(item.Sources);

    /// <summary>
    /// The price with the player's own count, e.g. <c>12,000 Seafarer's Cowries - you have 3,400, 8,600
    /// to go</c>, or empty when the mount costs no tokens.
    /// </summary>
    /// <param name="cost">The parsed price, or null.</param>
    private string DescribeCost(MountCost? cost)
    {
        if (cost is null)
        {
            return string.Empty;
        }

        uint itemId = stock.ItemIdOf(cost.Currency);

        return MountCosts.Describe(cost, itemId == 0 ? null : stock.Count(itemId));
    }

    public override void OnOpen()
    {
        plugin.Configuration.MainWindowOpen = true;
        Plugin.SaveConfigSafe(plugin.Configuration);
        refreshOnOpen = true;
        base.OnOpen();
    }

    public override void OnClose()
    {
        plugin.Configuration.MainWindowOpen = false;
        Plugin.SaveConfigSafe(plugin.Configuration);
        base.OnClose();
    }

    /// <summary>
    /// Draw is called every frame from Dalamud's UI thread. An exception that escapes it can
    /// take the whole plugin UI down, so the body is isolated and the failure logged.
    /// </summary>
    public override void Draw()
    {
        try
        {
            DrawBody();

            if (loggedDrawFailure)
            {
                Plugin.Log.Debug("MainWindow.Draw recovered.");
                loggedDrawFailure = false;
            }
        }
        catch (Exception ex)
        {
            // While a failure persists this runs every frame, and the catch also swallows the
            // exception, so Plugin.OnDraw's transition guard never sees it. The flag has to live
            // here or this is a 60 Hz Error stream (N4).
            if (!loggedDrawFailure)
            {
                Plugin.Log.Error(ex, "MainWindow.Draw failed.");
                loggedDrawFailure = true;
            }
        }
    }

    /// <summary>
    /// Gathers the browser state into <see cref="MountViewState"/>, draws the view, then applies
    /// what the view reported. The view never reads the config or a service, which is what makes it
    /// previewable.
    /// </summary>
    /// <remarks>
    /// The catalog refreshes its owned flags at most every two seconds, not every frame; the query
    /// is re-applied only when a field, the selection or the catalog changed. Everything game-facing
    /// is therefore on a timer or a user action, and the per-frame path is dictionary lookups only.
    /// </remarks>
    private void DrawBody()
    {
        var config = plugin.Configuration;

        // Records one glass callback that refracts whatever is behind the window - the game
        // and other plugins' windows included. Falls back to a flat fill when the device is
        // not ready or the GPU budget is spent, and never throws.
        DrawGlass();


        // The view writes these fields back; the config is the source of truth at the top of a frame.
        state.Search = config.SearchText ?? string.Empty;
        state.Ownership = config.Ownership;
        state.Expansion = config.Expansion;
        state.Order = config.Order;
        state.ViewMode = config.ViewMode;
        state.GroupBy = config.GroupBy;
        // A saved filter is the type's own spelling, so an older config's "Trial" reads as the Trial type. Only a
        // type the taxonomy knows is adopted: a hand-edited or newer value reads as "any" (and is rewritten as
        // empty after the draw) rather than filtering the whole shelf away for a frame.
        MountSourceKind savedSource = MountSourceKinds.Parse(config.SavedSource);
        state.SourceFilter = savedSource.IsKnown ? savedSource : MountSourceKinds.Any;

        // The presets, by reference: the row and the dialog only read them, and a delete applied after the draw
        // has to be visible on the next frame without a copy to keep in step.
        state.SavedViews = config.SavedViews;

        state.SelectedId = config.SelectedMountId == 0 ? null : config.SelectedMountId;
        // The breakdown is of the whole collection, not of the filtered list: it is a progress strip,
        // and a filter must not make it look like the player owns more of an expansion. Cached by the
        // catalog revision - it walks every mount and allocates per call, which the per-frame contract
        // above ("dictionary lookups only") does not allow.
        if (expansionsRevision != plugin.Catalog.Revision)
        {
            expansions = MountBreakdown.ByExpansion(plugin.Catalog.Items);
            expansionsRevision = plugin.Catalog.Revision;
        }

        state.Expansions = expansions;

        // The rarest mount still missing: same shape of cache, same reason. Null once everything is owned,
        // which is what makes the header's insight line disappear rather than claim a mount that is not there.
        if (rarestRevision != plugin.Catalog.Revision)
        {
            rarestMissing = MountRarityInsight.Find(plugin.Catalog.Items);
            rarestRevision = plugin.Catalog.Revision;
        }

        state.RarestMissing = rarestMissing;

        // The view caches text measured with the Look's type face; a theme change invalidates all of it.
        state.MeasurementGeneration = plugin.LookRevision;



        // The source combo's counts describe the whole shelf, so they are rebuilt with the catalogue rather than
        // with the filtered list - and not per frame: the tally walks every mount and allocates per type.
        if (sourceOptionsRevision != plugin.Catalog.Revision)
        {
            sourceOptions = BuildSourceOptions();
            sourceOptionsRevision = plugin.Catalog.Revision;
        }

        state.SourceOptions = sourceOptions;

        state.WantedIds = config.WantedMounts;

        var refreshNow = refreshOnOpen;
        refreshOnOpen = false;
        var refreshed = plugin.Catalog.RefreshIfDue(Environment.TickCount64, refreshNow);
        EnsurePrices();
        var itemsChanged = RefreshItems();

        if (refreshed)
        {
            TrackRecentlyCollected(config);
        }

        // The set the view reads. Rebuilt only when the reconciliation ran or the collection changed, so the
        // draw path stays allocation-free.
        if (recentIdsRevision != lastRevision)
        {
            state.RecentIds = recentIds = BuildRecentIds(config);
            recentIdsRevision = lastRevision;
        }
        else
        {
            state.RecentIds = recentIds;
        }

        if (refreshed || itemsChanged || state.SelectedId != lastTextureSelection)
        {
            plugin.Textures.Request(state.Items, state.SelectedId);
            lastTextureSelection = state.SelectedId;
        }

        // Progressive fill, a few icons per frame: creating every icon in one draw pass was a
        // 125 ms UiBuilder hitch on the first open. Unresolved icons draw the placeholder.
        if (plugin.Textures.HasPending)
        {
            plugin.Textures.Pump();
        }

        // The wrap behind a game icon is frame-scoped, so the handles are re-read every frame from
        // the shared textures. Caching the bare handle produced dangling ids: blank or garbled tiles.
        plugin.Textures.RefreshHandles(state.Items, state.SelectedId);

        state.Textures = plugin.Textures.Handles;
        state.TextureSizes = plugin.Textures.Sizes;
        state.Owned = plugin.Catalog.Owned;
        state.Total = plugin.Catalog.Total;
        state.DatasetSource = plugin.Catalog.DatasetSource;

        var selected = FindSelected(state.Items, state.SelectedId);
        uint whistleItemId = 0;
        if (selected is not null && plugin.Catalog.TryGetItemId(selected.Id, out var sourceItemId))
        {
            whistleItemId = sourceItemId;
        }

        state.ItemName = selected is null ? null : plugin.Catalog.ItemNameFor(selected.Id);
        state.WikiName = selected?.Name;
        state.RideDisabledReason = plugin.Actions.DescribeRide(selected);
        state.GuideDisabledReason = plugin.Actions.DescribeMountGuide();
        state.LinkDisabledReason = plugin.Actions.DescribeLink(whistleItemId);

        // The market asks a network service, so it is only ever shown for a mount the dataset records
        // as tradeable and only from an answer we already have: nothing is fetched on a selection.
        var marketItemId = selected is not null && selected.Community?.Tradeable == true ? whistleItemId : 0;

        while (market.TryTakeFinished(out uint pricedId, out MarketQuote? pricedQuote, out bool priceFailed))
        {
            // One line per click, never per frame: a price the player asked for is worth a log entry, and
            // it is the only way to check the number without looking at the screen.
            if (priceFailed)
            {
                if (pricedId == marketItemId)
                {
                    marketFailedItem = pricedId;
                }

                continue;
            }

            // The same words the pane shows, so the log line and the screen cannot drift apart (and the
            // plural of "1 listing" is decided in one place).
            Plugin.Log.Information(
                "Market: item {ItemId} {Quote}",
                pricedId,
                MountMarket.Describe(pricedQuote, Plugin.DataCenterName()));
        }

        state.MarketCanCheck = marketItemId != 0;
        state.MarketCheckInFlight = marketItemId != 0 && market.IsInFlight(marketItemId);
        // A token price in a source line, with the player's own count when the token can be found: this is
        // the "12,000 Seafarer's Cowries" line answering "how far off am I". Counted once per selection -
        // walking ten containers every frame to print a number that only changes when the bag does is the
        // kind of cost that shows up as a hitch nobody can attribute.
        if ((state.SelectedId ?? 0) != costForId)
        {
            costForId = state.SelectedId ?? 0;
            costOfSelection = CostOf(selected);
            costText = DescribeCost(costOfSelection);
        }

        state.CostText = costText;

        // The tracker is loaded once per selection for the same reason, and for one more: the note is edited
        // in place on the state, so re-reading the config every frame would eat the letter being typed.
        if ((state.SelectedId ?? 0) != grindForId)
        {
            grindForId = state.SelectedId ?? 0;
            _ = config.Grinds.TryGetValue(grindForId, out MountGrindRecord? grind);

            state.GrindHave = MountGrind.Have(grind);
            state.GrindWant = MountGrind.Target(grind, costOfSelection);
            state.GrindNote = grind?.Note ?? string.Empty;
            state.GrindVisible = costOfSelection is not null || MountGrind.IsTracked(grind);
        }

        // Three ranks of answer: a lookup the player asked for (with its world), a price the batch read
        // already carries (no world, so it says "From"), and nothing.
        // The id-0 guard is not decoration: a mount whose whistle the sheet lookup misses has item id 0,
        // and comparing that against an unset failed id drew "Could not read the market." for a mount
        // nobody had asked about.
        state.MarketPriceText = marketItemId != 0 && marketItemId == marketFailedItem
            ? Loc.T("market.unreadable")
            : marketItemId != 0 && market.TryGetFresh(marketItemId, out MarketQuote? quote)
                ? MountMarket.Describe(quote, Plugin.DataCenterName())
                : marketItemId != 0 && market.TryGetPrice(marketItemId, out int batchPrice)
                    ? MountMarket.DescribeFrom(batchPrice)
                    : string.Empty;

        // The watch control is loaded once per selection, like the tracker: the threshold is edited in place
        // on the state, so re-reading it every frame would eat a stepper click.
        if ((state.SelectedId ?? 0) != watchForId)
        {
            watchForId = state.SelectedId ?? 0;
            _ = config.Watches.TryGetValue(watchForId, out MountWatchRecord? watch);

            state.Watching = MountWatch.IsWatching(watch);
            state.WatchThreshold = state.Watching ? watch!.Threshold : 0;
        }

        // "Watching 3" in the header. A walk over the watched records - a handful at most - and no allocation.
        state.WatchingCount = MountWatch.Count(config.Watches);

        // The line is formatted only when the record behind it moved (a poll landed, a stepper moved), because
        // it goes through the string table and the draw path may not allocate per frame.
        RefreshWatchLine(config);

        // Decided once per open and then held: the flag below is cleared on the frame it is drawn, so a
        // per-frame read would retire the line immediately (see the field).
        firstRunHint ??= !config.FirstRunHintSeen;
        state.FirstRunHint = firstRunHint.Value;

        view.Draw(state);

        // A wanted toggle is a config write like any other: apply it before the browse-state diff
        // so both changes go through one SaveConfigSafe call after the draw.
        var wantedChanged = ApplyWantedToggle(config);

        // The tracker's edits are the same kind of write, applied in the same place so a note typed on a
        // frame that also changed a filter is still one save.
        var grindChanged = ApplyGrind(config);

        // The watch toggle and its threshold are config writes like the two above.
        var watchChanged = ApplyWatch(config);

        // A preset's five axes are a browse-state write, so applying one is a state change like a click on a
        // filter; saving, renaming and deleting are config writes, and all of it lands in the one save below.
        var viewsChanged = ApplySavedViews(config);

        // The first-run line is one-shot: recording it as seen on the frame it was drawn is what stops it
        // coming back next session. The held field is what keeps it on screen for the rest of this one.
        var firstRunChanged = state.FirstRunHint && !config.FirstRunHintSeen;

        if (firstRunChanged)
        {
            config.FirstRunHintSeen = true;
        }

        // The header's wanted badge is the board's only door (the task asked for no new header button).
        if (state.BoardRequested)
        {
            plugin.OpenBoard();
        }

        // The empty state's button asks for a reset; do it on the state so the query cache sees the
        // change and ApplyBrowseState persists it in the same frame.
        if (state.ClearFiltersRequested)
        {
            state.Search = string.Empty;
            state.Ownership = MountOwnership.All;
            state.Expansion = MountExpansion.All;

            // The source filter is a filter like the other three: without this line the empty state's
            // only button could not clear the very filter that emptied the list, and the reset was
            // then persisted straight back into the config.
            state.SourceFilter = MountSourceKinds.Any;
        }

        // The header's chrome flags are read after the draw, like every other view request.
        if (state.SettingsRequested)
        {
            plugin.ToggleConfigUi();
        }

        if (state.RefreshRequested)
        {
            RefreshMounts();
        }

        if (state.CopyMissingRequested)
        {
            CopyMissingMounts();
        }

        var browseChanged = ApplyBrowseState(config);

        if (wantedChanged || browseChanged || grindChanged || watchChanged || viewsChanged || firstRunChanged)
        {
            Plugin.SaveConfigSafe(config);
        }

        // The view can change the selection and set a request in the same frame, so the action is
        // resolved from the post-draw state: acting on the pre-draw selection could link the old
        // mount's whistle after a click moved the detail pane.
        var actionItem = FindSelected(state.Items, state.SelectedId);
        uint actionItemId = 0;
        if (actionItem is not null && plugin.Catalog.TryGetItemId(actionItem.Id, out var actionSourceItemId))
        {
            actionItemId = actionSourceItemId;
        }

        if (state.RideRequested && actionItem is not null)
        {
            if (!plugin.Actions.Ride(actionItem.Id))
            {
                Gw.Toast(Loc.T("toast.rideFailed"), ToastKind.Error);
            }
        }

        if (state.WikiRequested && !string.IsNullOrEmpty(state.WikiName))
        {
            OpenWiki(state.WikiName);
        }

        // A "How to obtain" row the player tapped: the view hands over the page it stands for, already
        // built by the pure service.
        if (state.SourceLinkRequested is { } sourceUrl)
        {
            // One line per click: the readback for "the row I tapped opened the page it stands for".
            Plugin.Log.Information("Source link: {Url}", sourceUrl);
            OpenUrl(sourceUrl);
        }

        // One request per click, for the mount the pane is showing now (not the one it showed before a
        // click moved the selection). A failed price can be asked again.
        if (state.MarketCheckRequested
            && actionItem is not null
            && actionItem.Community?.Tradeable == true
            && actionItemId != 0)
        {
            marketFailedItem = 0;
            market.Request(actionItemId);
        }

        if (state.GuideRequested)
        {
            if (!plugin.Actions.OpenMountGuide())
            {
                Gw.Toast(Loc.T("toast.guideFailed"), ToastKind.Error);
            }
        }

        if (state.LinkRequested && actionItemId != 0)
        {
            if (!plugin.Actions.LinkWhistle(actionItemId))
            {
                Gw.Toast(Loc.T("toast.linkFailed"), ToastKind.Error);
            }
        }
    }

    /// <summary>
    /// Re-reads the player's collection now instead of at the catalog's next two-second tick, then
    /// drops the built query so the next frame re-applies the search, filters and order to it.
    /// </summary>
    private void RefreshMounts()
    {
        plugin.Catalog.RefreshIfDue(Environment.TickCount64, force: true);

        // Report the result. The two-second tick already keeps the counters right, so without a toast
        // this action had no visible effect at all and read as inert: the one thing it can add is a
        // number the player can check against the game.
        if (!plugin.Catalog.IsBuilt)
        {
            Gw.Toast(Loc.T("toast.mountsUnreadable"), ToastKind.Error);
        }
        else
        {
            Gw.Toast(
                MountProgress.RefreshToast(plugin.Catalog.Owned, plugin.Catalog.Total),
                ToastKind.Info);
        }

        // RefreshItems only re-runs when one of its inputs changed; clearing the built flag is what
        // forces the re-query without touching the user's search, filters or selection.
        itemsBuilt = false;
        lastRevision = -1;
    }

    /// <summary>
    /// Re-applies the query only when one of its inputs changed. The view draws
    /// <see cref="MountViewState.Items"/> in order, so the window owns both filtering and sorting.
    /// </summary>
    private bool RefreshItems()
    {
        var revision = plugin.Catalog.Revision;

        if (itemsBuilt
            && revision == lastRevision
            && wantedRevision == lastWantedRevision
            && priceVersionBuilt == market.PriceVersion
            && lastOwnership == state.Ownership
            && lastExpansion == state.Expansion
            && lastOrder == state.Order
            && string.Equals(lastSearch, state.Search, StringComparison.Ordinal)
            && lastSource == state.SourceFilter)
        {
            return false;
        }

        if (state.Order == MountOrder.Random && lastOrder != MountOrder.Random)
        {
            shuffleSeed = Environment.TickCount;
        }

        state.OwnershipCounts = new MountQuery(state.Search, MountOwnership.All, state.Expansion, state.Order)
        {
            Source = state.SourceFilter,
        }
            .OwnershipCounts(plugin.Catalog.Items, plugin.Configuration.WantedMounts);

        // Re-keyed from whistle item id to mount id, and rebuilt only when a read finished: the query
        // and the table both order rows by mount, and the market only speaks item ids.
        state.Prices = BuildPrices();

        state.Items = new MountQuery(state.Search, state.Ownership, state.Expansion, state.Order)
        {
            Source = state.SourceFilter,
        }
            .Apply(plugin.Catalog.Items, state.WantedIds, state.Prices, shuffleSeed);

        // The chips and the list are two runs of the same query; with the "All" chip they must agree.
        // When they do not, the pane says "no mounts match" while the chips claim there are some, which
        // is the one state a player cannot reason about - so say it out loud rather than leave it.
        if (state.Ownership == MountOwnership.All
            && state.OwnershipCounts.Count > 0
            && state.OwnershipCounts[0] != state.Items.Count)
        {
            Plugin.Log.Warning(
                "Mountbook: search '{Search}' counted {Counted} but listed {Listed} (order {Order}).",
                state.Search,
                state.OwnershipCounts[0],
                state.Items.Count,
                state.Order);
        }

        lastSearch = state.Search;
        lastSource = state.SourceFilter;
        lastOwnership = state.Ownership;
        lastExpansion = state.Expansion;
        lastOrder = state.Order;
        lastRevision = revision;
        lastWantedRevision = wantedRevision;
        priceVersionBuilt = market.PriceVersion;
        itemsBuilt = true;

        return true;
    }

    /// <summary>
    /// Chooses the moment to read prices: switching to the cheapest-first order is the request, because
    /// that is the click that makes the answer useful. Switching away and back asks again, so a failed
    /// read is never a dead end, and nothing is fetched while the list is ordered any other way.
    /// </summary>
    private void EnsurePrices()
    {
        if (state.Order != MountOrder.Cheapest)
        {
            pricesOrder = state.Order;
            return;
        }

        if (pricesOrder == MountOrder.Cheapest)
        {
            return;
        }

        pricesOrder = MountOrder.Cheapest;

        if (market.HasFreshPrices() || market.BatchInFlight)
        {
            return;
        }

        market.RequestBatch(TradeableItemIds());
    }

    /// <summary>The whistle item ids of every mount the dataset records as tradeable, for one batch read.</summary>
    private List<uint> TradeableItemIds()
    {
        var ids = new List<uint>();

        foreach (MountShelfItem item in plugin.Catalog.Items)
        {
            if (item.Community?.Tradeable == true
                && plugin.Catalog.TryGetItemId(item.Id, out uint itemId)
                && itemId != 0)
            {
                ids.Add(itemId);
            }
        }

        return ids;
    }

    /// <summary>
    /// The batch prices, keyed by mount id. A mount with no known price is simply absent: the query
    /// parks it at the end and the table draws an empty cell.
    /// </summary>
    private IReadOnlyDictionary<uint, int> BuildPrices()
    {
        var byMount = new Dictionary<uint, int>();

        foreach (MountShelfItem item in plugin.Catalog.Items)
        {
            if (plugin.Catalog.TryGetItemId(item.Id, out uint itemId)
                && itemId != 0
                && market.TryGetPrice(itemId, out int price))
            {
                byMount[item.Id] = price;
            }
        }

        return byMount;
    }

    /// <summary>
    /// Applies the view's one-frame wanted pulse to the persisted hunting list. Returns whether the
    /// set changed, so the caller can save once after the draw.
    /// </summary>
    private bool ApplyWantedToggle(Configuration config)
    {
        if (state.ToggleWantedId is not { } mountId)
        {
            return false;
        }

        // Set.Remove is the toggle: its return value is exactly "it was there before".
        if (!config.WantedMounts.Remove(mountId))
        {
            config.WantedMounts.Add(mountId);
        }

        state.ToggleWantedId = null;
        state.WantedIds = config.WantedMounts;
        wantedRevision++;
        plugin.BoardRevision++;
        return true;
    }

    /// <summary>
    /// Applies the tracker's edits to the persisted record, and returns whether the config changed so the
    /// caller saves once after the draw.
    /// </summary>
    /// <remarks>
    /// An empty record is removed rather than saved: dragging a stepper back to zero, or clearing a note,
    /// must leave the config exactly as it was before the mount was ever touched. The timestamp is stamped
    /// here, once, so the board's "updated" line and the file agree.
    /// </remarks>
    private bool ApplyGrind(Configuration config)
    {
        if (!state.GrindChanged)
        {
            return false;
        }

        state.GrindChanged = false;

        uint mountId = state.SelectedId ?? 0;

        if (mountId == 0)
        {
            return false;
        }

        var record = new MountGrindRecord
        {
            Have = Math.Max(state.GrindHave, 0),
            Want = Math.Max(state.GrindWant, 0),
            Note = state.GrindNote?.Trim() ?? string.Empty,
        };

        if (MountGrind.Kept(record) is null)
        {
            if (!config.Grinds.Remove(mountId))
            {
                return false;
            }
        }
        else
        {
            config.Grinds[mountId] = MountGrind.Touch(record, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        plugin.BoardRevision++;

        return true;
    }

    /// <summary>
    /// Applies the watch control's edits to the persisted records, and returns whether the config changed so
    /// the caller saves once after the draw.
    /// </summary>
    /// <remarks>
    /// Switching a watch off removes its record instead of zeroing its threshold: the poll is driven by these
    /// records, so an entry that is off would still have to be filtered out of every frame's count, and a file
    /// full of zeroes is what "watching nothing" would look like by accident.
    /// <para>
    /// A new watch starts latched when the price it was set against is already at or under the threshold: the
    /// player is looking at that price, so announcing it would be telling them what they just read. The latch
    /// clears the moment a read puts the price back above, so the next real crossing is announced.
    /// </para>
    /// </remarks>
    private bool ApplyWatch(Configuration config)
    {
        uint mountId = state.SelectedId ?? 0;

        // The toggle belongs to the window, not the view: the view only reports that the control moved, and
        // the starting threshold needs the market the view cannot see.
        if (state.ToggleWatchRequested && mountId != 0)
        {
            state.ToggleWatchRequested = false;
            state.Watching = !state.Watching;
            state.WatchChanged = true;

            if (state.Watching)
            {
                state.WatchThreshold = StartThresholdFor(mountId);
            }
        }

        if (!state.WatchChanged || mountId == 0)
        {
            return false;
        }

        state.WatchChanged = false;

        if (!state.Watching)
        {
            return config.Watches.Remove(mountId);
        }

        config.Watches.TryGetValue(mountId, out MountWatchRecord? saved);

        MountWatchRecord record = saved ?? new MountWatchRecord();
        record.Threshold = Math.Clamp(state.WatchThreshold, MountWatch.ThresholdStep, MountWatch.MaxThreshold);

        if (saved is null)
        {
            SeedSeen(mountId, record);
        }
        else
        {
            // A moved threshold re-decides the latch against the last read: raised above the price it arms the
            // watch, lowered to or below it says the crossing already happened, which is what the player saw.
            record.Alerted = MountWatch.Crossed(record.LastSeen, record.Threshold);
        }

        config.Watches[mountId] = record;
        state.WatchThreshold = record.Threshold;

        return true;
    }

    /// <summary>
    /// Gives a new watch the price this session already holds, so the pane answers "last seen" without a
    /// request. A batch price carries no world, which does not matter here: the player is setting their own
    /// number against it, not buying from it.
    /// </summary>
    /// <param name="mountId">The mount being watched.</param>
    /// <param name="record">The record to seed.</param>
    private void SeedSeen(uint mountId, MountWatchRecord record)
    {
        if (!plugin.Catalog.TryGetItemId(mountId, out uint itemId) || itemId == 0)
        {
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (market.TryGetFresh(itemId, out MarketQuote? quote) && quote is not null)
        {
            MountWatch.Observe(record, quote.MinPrice, now);
            return;
        }

        if (market.TryGetPrice(itemId, out int batchPrice))
        {
            MountWatch.Observe(record, batchPrice, now);
        }
    }

    /// <summary>
    /// Applies what the saved-views row and dialog reported: a preset to put back, one to delete, or the name
    /// field's save. Returns whether the config changed, so the caller saves once after the draw.
    /// </summary>
    /// <remarks>
    /// Applying a preset copies its five axes into the view state and nothing else: the browse-state write at
    /// the end of the frame persists whatever the state holds, so a preset and a hand-picked filter take exactly
    /// one path to disk. A name is only ever written by an explicit save or rename, which is what makes "the
    /// applied chip" honest - it is the preset those five fields *are*, not the last one clicked.
    /// </remarks>
    /// <param name="config">The configuration the presets live in.</param>
    private bool ApplySavedViews(Configuration config)
    {
        var changed = false;

        if (state.ApplyViewIndex is { } applied && applied >= 0 && applied < config.SavedViews.Count)
        {
            SavedView view = config.SavedViews[applied];

            state.Search = view.Query ?? string.Empty;
            state.Ownership = view.Ownership;
            state.Expansion = view.Expansion;
            state.Order = view.Order;
            state.GroupBy = view.GroupBy;

            // Choosing a preset is the answer the dialog was asking for, so it closes: a modal left over the
            // list means the player dismisses it before seeing what they chose.
            state.ViewsDialogOpen = false;

            Plugin.Log.Information(
                "Applied view {Name}: query \"{Query}\", ownership {Ownership}, expansion {Expansion}, order {Order}, grouping {GroupBy}.",
                view.Name,
                state.Search,
                state.Ownership,
                state.Expansion,
                state.Order,
                state.GroupBy);
        }

        if (state.DeleteViewIndex is { } doomed && SavedViewRules.Delete(config.SavedViews, doomed))
        {
            changed = true;

            // The aim was an index; deleting the entry it names leaves it past the end of the list, where a
            // rename would be refused by index and read as a broken button.
            if (state.ViewRenameIndex is { } aim && aim >= config.SavedViews.Count)
            {
                state.ViewRenameIndex = null;
            }

            Plugin.Log.Information("Deleted view at {Index}; {Count} left.", doomed, config.SavedViews.Count);
        }

        if (state.SaveViewRequested)
        {
            changed |= SaveSavedView(config);
        }

        return changed;
    }

    /// <summary>
    /// Commits the dialog's name field: renames the aimed-at preset, or stores the current filter under that
    /// name. Returns whether the config changed.
    /// </summary>
    /// <remarks>
    /// A refused save says why and keeps the dialog exactly as it was - the typed name stays in the field - so
    /// the answer to "that name is taken" is a different name rather than a closed dialog and a guess about what
    /// happened. A profile that already holds <see cref="SavedViewRules.MaxViews"/> views is the other refusal,
    /// and it is a toast for the same reason: a button that silently does nothing is a bug report.
    /// </remarks>
    /// <param name="config">The configuration to write the presets into.</param>
    private bool SaveSavedView(Configuration config)
    {
        string name = SavedViewRules.Clean(state.ViewNameDraft);

        // The dialog disables its button for a blank name; this is the other half of that guard, because the
        // request is read after the draw and the builder may have closed the dialog in between.
        if (name.Length == 0)
        {
            return false;
        }

        if (state.ViewRenameIndex is { } aim)
        {
            SavedViewRules.RenameResult result = SavedViewRules.Rename(config.SavedViews, aim, name);

            if (result == SavedViewRules.RenameResult.NameTaken)
            {
                Gw.Toast(Loc.T("toast.viewNameTaken"), ToastKind.Error);
                return false;
            }

            if (result == SavedViewRules.RenameResult.Refused)
            {
                state.ViewRenameIndex = null;
                state.ViewNameDraft = string.Empty;
                return false;
            }

            Plugin.Log.Information("Renamed view {Index} to \"{Name}\".", aim, name);
            Gw.Toast(Loc.F("toast.viewRenamed", name), ToastKind.Success);

            state.ViewRenameIndex = null;
            state.ViewNameDraft = string.Empty;
            return true;
        }

        SavedView captured = SavedViewRules.Capture(
            name,
            state.Search,
            state.Ownership,
            state.Expansion,
            state.Order,
            state.GroupBy);

        if (!SavedViewRules.TrySave(config.SavedViews, captured, out int index))
        {
            Gw.Toast(Loc.F("toast.viewsFull", SavedViewRules.MaxViews), ToastKind.Error);
            return false;
        }

        Plugin.Log.Information(
            "Saved view {Name} at {Index}: query \"{Query}\", ownership {Ownership}, expansion {Expansion}, order {Order}, grouping {GroupBy}.",
            name,
            index,
            captured.Query,
            captured.Ownership,
            captured.Expansion,
            captured.Order,
            captured.GroupBy);

        Gw.Toast(Loc.F("toast.viewSaved", name), ToastKind.Success);
        state.ViewNameDraft = string.Empty;

        return true;
    }

    /// <summary>
    /// The threshold a watch starts at when it is switched on: the price already known for the mount, or the
    /// default when this session has none yet.
    /// </summary>
    /// <param name="mountId">The mount being watched.</param>
    private int StartThresholdFor(uint mountId)
    {
        var known = CachedPrice(mountId);

        if (known == 0 && plugin.Catalog.TryGetItemId(mountId, out uint itemId))
        {
            _ = market.TryGetFresh(itemId, out MarketQuote? quote);
            known = quote?.MinPrice ?? 0;
        }

        return MountWatch.StartThreshold(known);
    }

    /// <summary>
    /// Formats the watch line from the persisted record, and only when that record moved.
    /// </summary>
    /// <remarks>
    /// The line goes through the string table, so building it every frame would allocate in a path that is
    /// not allowed to (see the contract on <see cref="DrawBody"/>). The four values it is made of are compared
    /// instead, which is why the cached id starts past every value the data can hold rather than at zero.
    /// </remarks>
    /// <param name="config">The configuration the records live in.</param>
    private void RefreshWatchLine(Configuration config)
    {
        uint mountId = state.SelectedId ?? 0;
        _ = config.Watches.TryGetValue(mountId, out MountWatchRecord? record);

        var threshold = MountWatch.IsWatching(record) ? record!.Threshold : 0;
        long checkedAt = record?.CheckedAt ?? 0;
        int lastSeen = record?.LastSeen ?? 0;

        if (mountId == watchLineForId
            && threshold == watchLineThreshold
            && checkedAt == watchLineCheckedAt
            && lastSeen == watchLineLastSeen)
        {
            return;
        }

        watchLineForId = mountId;
        watchLineThreshold = threshold;
        watchLineCheckedAt = checkedAt;
        watchLineLastSeen = lastSeen;

        state.WatchText = MountWatch.Line(record, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// The cheapest price already known for a mount, in gil, or zero. Never asks the market (N2): the wanted
    /// board draws what the batch read has, and an unpriced mount sorts last rather than costing a request.
    /// </summary>
    /// <param name="mountId">The mount id.</param>
    internal int CachedPrice(uint mountId)
    {
        if (!plugin.Catalog.TryGetItemId(mountId, out uint itemId))
        {
            return 0;
        }

        return market.TryGetPrice(itemId, out int price) ? price : 0;
    }

    /// <summary>Bumped by the market whenever a read lands, so the board can drop rows priced on old data.</summary>
    internal int PriceVersion => market.PriceVersion;


    /// <summary>
    /// The source combo's entries: "any" with how many mounts the shelf carries, then one entry per type with
    /// its own count, in taxonomy order. Built from the catalogue, never from the filtered list: a count that
    /// moved with the filter would be counting itself.
    /// </summary>
    private MountSourceCount[] BuildSourceOptions()
    {
        IReadOnlyList<MountShelfItem> items = plugin.Catalog.Items;

        return [new MountSourceCount(MountSourceKinds.Any, items.Count), .. MountSourceKinds.Tally(items)];
    }

    /// <summary>
    /// What the config stores for a source filter: the type's own spelling, or empty for "any". The spelling
    /// rather than the folded key, because the file is something a human may read, and the value an older build
    /// wrote for the same type (<c>Trial</c>, <c>PvP</c>) is exactly this string.
    /// </summary>
    /// <param name="kind">The type the combo holds.</param>
    private static string SourceFilterValue(MountSourceKind kind) => kind.IsAny ? string.Empty : kind.Name;

    /// <summary>The recently collected ids that are still owned, for the view's New markers.</summary>
    private HashSet<uint> BuildRecentIds(Configuration config)
    {
        var ids = new HashSet<uint>();

        foreach (MountShelfItem item in plugin.Catalog.Items)
        {
            if (item.Owned && config.RecentCollected.ContainsKey(item.Id))
            {
                ids.Add(item.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Reconciles the stored snapshot with the collection after a catalog refresh: newly collected mounts
    /// are marked (first-seen time, kept for two weeks, capped), and the snapshot is updated. Saves only
    /// when something actually moved.
    /// </summary>
    /// <summary>
    /// Re-reads the collection and updates the recently-collected record now. The window does this on every
    /// draw; the <c>recent</c> command calls it too, so the answer is current even when the book has never
    /// been opened this session - otherwise the digest is only as fresh as the last time the window drew.
    /// </summary>
    internal void TrackRecentlyCollectedNow() => TrackRecentlyCollected(plugin.Configuration);

    private void TrackRecentlyCollected(Configuration config)
    {
        // This is a diff against the previous snapshot, so it may only run when the snapshot is
        // trustworthy. Logged out - or with a catalog that failed to build - every mount reads as
        // unowned, and reconciling that against a real collection erases it: both the saved ids and
        // every "New" marker go, and the whole collection becomes the baseline again on recovery.
        if (!Plugin.ClientState.IsLoggedIn || !plugin.Catalog.IsBuilt)
        {
            return;
        }

        try
        {
            var ownedNow = new HashSet<uint>();

            foreach (MountShelfItem item in plugin.Catalog.Items)
            {
                if (item.Owned)
                {
                    ownedNow.Add(item.Id);
                }
            }

            var recent = MountRecent.Reconcile(
                config.KnownOwnedIds,
                ownedNow,
                config.RecentCollected,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                out var knownChanged,
                out var recentChanged);

            if (!knownChanged && !recentChanged)
            {
                return;
            }

            if (knownChanged)
            {
                config.KnownOwnedIds = ownedNow;
            }

            // recentChanged is now the answer to this question: the service compares the trimmed map it
            // returns against the stored one, so the extra count check that stood in for it is gone.
            if (recentChanged)
            {
                config.RecentCollected = recent;
            }

            recentIdsRevision = -1;
            Plugin.SaveConfigSafe(config);
        }
        catch (Exception ex)
        {
            // Bookkeeping must never take the window down (N4).
            Plugin.Log.Error(ex, "Could not update the recently collected mounts.");
        }
    }

    /// <summary>
    /// Opens a mount's wiki page with the default browser. A failure is logged and toasted; it must never look
    /// like a dead button (N4).
    /// </summary>
    private static void OpenWiki(string displayName) => OpenUrl(MountWiki.Url(displayName));

    /// <summary>
    /// Opens one URL with the default browser. The only place this plugin starts a process, so the guard
    /// and the failure toast exist once: a source row and the wiki link fail the same way and say so.
    /// </summary>
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not open {Url} in a browser.", url);
            Gw.Toast(Loc.T("toast.browserFailed"), ToastKind.Error);
        }
    }

    /// <summary>
    /// Writes the browse state back after the view ran, so a search, filter or card click survives
    /// a reload. Returns whether anything changed; the caller saves through
    /// <see cref="Plugin.SaveConfigSafe"/> once, after both the browse state and the wanted toggle.
    /// </summary>
    /// <summary>
    /// Puts the uncollected names of the current list on the clipboard, one per line. The text is
    /// built by <see cref="MountExport"/>; this only touches the clipboard and reports the result.
    /// </summary>
    private void CopyMissingMounts()
    {
        try
        {
            ImGui.SetClipboardText(MountExport.MissingNames(state.Items));
            Gw.Toast(MountExport.MissingSummary(state.Items), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not copy the missing mounts to the clipboard.");
            Gw.Toast(Loc.T("toast.copyMissingFailed"), ToastKind.Error);
        }
    }

    private bool ApplyBrowseState(Configuration config)
    {
        var changed = false;


        var search = state.Search ?? string.Empty;
        if (!string.Equals(config.SearchText, search, StringComparison.Ordinal))
        {
            config.SearchText = search;
            changed = true;
        }

        if (config.Ownership != state.Ownership)
        {
            config.Ownership = state.Ownership;
            changed = true;
        }

        if (config.Expansion != state.Expansion)
        {
            config.Expansion = state.Expansion;
            changed = true;
        }

        if (config.Order != state.Order)
        {
            config.Order = state.Order;
            changed = true;
        }

        if (config.GroupBy != state.GroupBy)
        {
            config.GroupBy = state.GroupBy;
            changed = true;
        }

        if (config.ViewMode != state.ViewMode)
        {
            config.ViewMode = state.ViewMode;
            changed = true;
        }

        string sourceFilter = SourceFilterValue(state.SourceFilter);
        if (!string.Equals(config.SavedSource, sourceFilter, StringComparison.Ordinal))
        {
            config.SavedSource = sourceFilter;
            changed = true;
        }

        var selected = state.SelectedId ?? 0;
        if (config.SelectedMountId != selected)
        {
            config.SelectedMountId = selected;
            changed = true;
        }

        return changed;
    }

    private static MountShelfItem? FindSelected(IReadOnlyList<MountShelfItem> items, uint? selectedId)
    {
        if (selectedId is not { } id)
        {
            return null;
        }

        foreach (MountShelfItem item in items)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }
}
