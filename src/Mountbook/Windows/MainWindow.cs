using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    /// <summary>The source combo's cached labels and the revision they were built at.</summary>
    private string[] sourceLabels = ["Any"];

    private int sourceLabelsRevision = -1;

    /// <summary>The source filter the current list was built with, so a change rebuilds it.</summary>
    private string lastSource = string.Empty;

    /// <summary>The cached recently-collected set the view reads, and the revision it was built at.</summary>
    private HashSet<uint> recentIds = [];

    // int.MinValue, not -1: the revision is also -1 before the catalog has been built, and the old
    // "|| recentIds.Count == 0" guard in its place rebuilt this set every frame for any player with
    // no recent mounts - the steady state two weeks after a session.
    private int recentIdsRevision = int.MinValue;

    /// <summary>The cached per-expansion progress, and the catalog revision it was built at.</summary>
    private IReadOnlyList<ExpansionProgress> expansions = [];

    private int expansionsRevision = int.MinValue;

    /// <summary>Set by <see cref="OnOpen"/> so the first draw after opening re-reads the collection.</summary>
    private bool refreshOnOpen;

    /// <summary>Set once a draw has failed, so the failure is logged on the transition (N4).</summary>
    private bool loggedDrawFailure;

    /// <summary>
    /// <c>NoBackground</c> stops ImGui painting its own fill: the glass is the background.
    /// The <c>###</c> suffix is the stable ImGui id, so renaming the visible title never
    /// resets a user's window position.
    /// </summary>
    public MainWindow(Plugin plugin)
        : base(plugin, "Mountbook###MountbookMain")
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
        market = new MarketPrices(Plugin.Log, DataCenterName);
        stock = new CurrencyStock(Plugin.Inventory, Plugin.Data, Plugin.Log);
        IsOpen = plugin.Configuration.MainWindowOpen;
    }

    public void Dispose()
    {
        market.Dispose();
    }

    /// <summary>
    /// The priced source line of a mount, with progress, or empty when the mount costs no tokens. Only the
    /// first priced line is read: a mount that costs two different tokens is rare, and two lines of
    /// arithmetic under one heading is not worth the space.
    /// </summary>
    /// <param name="item">The mount in the pane, or null.</param>
    private string DescribeCost(MountShelfItem? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        foreach (MountSourceLine line in item.Sources)
        {
            if (MountCosts.Parse(line.Detail) is not { } cost)
            {
                continue;
            }

            uint itemId = stock.ItemIdOf(cost.Currency);

            return MountCosts.Describe(cost, itemId == 0 ? null : stock.Count(itemId));
        }

        return string.Empty;
    }

    /// <summary>
    /// The player's data center, or empty when the client has not loaded the character. Prices are read
    /// across the whole DC: a world-only answer hides every cheaper listing a one-hop trip away. A RowRef
    /// is only valid after the player state loads, and a price is not worth a throw on the draw path.
    /// </summary>
    private static string DataCenterName()
    {
        try
        {
            return Plugin.PlayerState.HomeWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText()
                ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
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
        state.SourceFilter = config.SavedSource ?? string.Empty;
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

        // The view caches text measured with the Look's type face; a theme change invalidates all of it.
        state.MeasurementGeneration = plugin.LookRevision;



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
            state.SourceLabels = BuildSourceLabels();
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
                MountMarket.Describe(pricedQuote, DataCenterName()));
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
            costText = DescribeCost(selected);
        }

        state.CostText = costText;

        // Three ranks of answer: a lookup the player asked for (with its world), a price the batch read
        // already carries (no world, so it says "From"), and nothing.
        // The id-0 guard is not decoration: a mount whose whistle the sheet lookup misses has item id 0,
        // and comparing that against an unset failed id drew "Could not read the market." for a mount
        // nobody had asked about.
        state.MarketPriceText = marketItemId != 0 && marketItemId == marketFailedItem
            ? "Could not read the market."
            : marketItemId != 0 && market.TryGetFresh(marketItemId, out MarketQuote? quote)
                ? MountMarket.Describe(quote, DataCenterName())
                : marketItemId != 0 && market.TryGetPrice(marketItemId, out int batchPrice)
                    ? MountMarket.DescribeFrom(batchPrice)
                    : string.Empty;

        view.Draw(state);

        // A wanted toggle is a config write like any other: apply it before the browse-state diff
        // so both changes go through one SaveConfigSafe call after the draw.
        var wantedChanged = ApplyWantedToggle(config);
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
            state.SourceFilter = string.Empty;
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

        if (state.ThemesRequested)
        {
            OpenThemesFolder();
        }

        if (state.CopyMissingRequested)
        {
            CopyMissingMounts();
        }

        var browseChanged = ApplyBrowseState(config);

        if (wantedChanged || browseChanged)
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
                Gw.Toast("Could not ride that mount. See the log."u8, ToastKind.Error);
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
                Gw.Toast("Could not open the Mount Guide. See the log."u8, ToastKind.Error);
            }
        }

        if (state.LinkRequested && actionItemId != 0)
        {
            if (!plugin.Actions.LinkWhistle(actionItemId))
            {
                Gw.Toast("Could not link that whistle. See the log."u8, ToastKind.Error);
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

        // RefreshItems only re-runs when one of its inputs changed; clearing the built flag is what
        // forces the re-query without touching the user's search, filters or selection.
        itemsBuilt = false;
        lastRevision = -1;
    }

    /// <summary>
    /// Opens the themes folder in the desktop's file manager, creating it first so a fresh install
    /// has one to open. A failure is logged and toasted, never left to the draw guard (N4).
    /// </summary>
    private void OpenThemesFolder()
    {
        var folder = plugin.ThemesDirectory;

        try
        {
            Directory.CreateDirectory(folder);

            // UseShellExecute hands the folder to the desktop; without it Process.Start would try
            // to execute a directory. The returned handle is not needed after the spawn.
            using Process? process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not open the themes folder '{Folder}'.", folder);
            Gw.Toast("Could not open the folder."u8, ToastKind.Error);
        }
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
            && string.Equals(lastSource, state.SourceFilter, StringComparison.Ordinal))
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
        return true;
    }


    /// <summary>The source combo's labels: "Any" plus every category on the shelf, cached by revision.</summary>
    private string[] BuildSourceLabels()
    {
        if (sourceLabelsRevision == lastRevision && sourceLabels.Length > 0)
        {
            return sourceLabels;
        }

        string[] types = MountQuery.SourceTypes(plugin.Catalog.Items);
        sourceLabels = ["Any", .. types];
        sourceLabelsRevision = lastRevision;
        return sourceLabels;
    }

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
            Gw.Toast("Could not open the browser. See the log."u8, ToastKind.Error);
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
            Gw.Toast("Could not copy the list."u8, ToastKind.Error);
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

            // The table is re-created on the next draw, and with it the sort spec ImGui kept for its id:
            // the first frame it draws must not be read as a header click.
            state.TableSortReady = false;
            changed = true;
        }

        var sourceFilter = state.SourceFilter ?? string.Empty;
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
