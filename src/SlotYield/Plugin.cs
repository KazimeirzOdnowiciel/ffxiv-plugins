using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using SlotYield.Core;
using SlotYield.Game;
using SlotYield.Market;
using SlotYield.UI;

namespace SlotYield;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/slotyield";
    public const string AliasName = "/sy";

    private readonly WindowSystem windowSystem = new("SlotYield");
    private readonly MainWindow mainWindow;
    private readonly WorthWindow worthWindow;
    private readonly TickerWindow tickerWindow;
    private readonly HistoryWindow historyWindow;
    private readonly HelpWindow helpWindow;
    private readonly CompareWindow compareWindow;
    private readonly MarketCapture capture;
    private IReadOnlyList<string>? worlds;
    private volatile bool disposed;
    private volatile bool listingsDirty;
    private DateTime nextListingSaveUtc = DateTime.MinValue;
    private DateTime nextSellListPollUtc = DateTime.MinValue;
    private DateTime nextListingPriceUtc = DateTime.MinValue;
    private DateTime nextAlertPollUtc = DateTime.MinValue;
    private bool alertInFlight;
    private readonly Dictionary<(uint ItemId, Quality Quality), decimal> pinnedProfit = new();
    private bool pinnedProfitPrimed;
    private readonly Dictionary<(uint ItemId, Quality Quality), MarketQuote> lastAlertQuotes = new();
    private readonly Dictionary<(uint ItemId, Quality Quality), MarketQuote> listingQuotes = new();
    private readonly HashSet<uint> listingQuotesInFlight = new();
    private readonly object listingQuoteLock = new();
    private IDtrBarEntry? dtrEntry;
    private bool frameworkFaultLogged;
    private int exceptionLogCount;
    private bool exceptionLogging;

    /// <summary>Set while SlotYield runs its own try/catch-guarded lookups, so the tracer ignores them.</summary>
    [System.ThreadStatic]
    public static bool SuppressTracer;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Normalize();

        var repairedListings = ListingBook.RepairEncodedIds(Configuration.MyListings);
        if (repairedListings > 0)
        {
            Log.Information("[SlotYield] Repaired {Count} listing(s) stored with HQ-encoded item ids.", repairedListings);
            PluginInterface.SavePluginConfig(Configuration);
        }

        Cache = new MarketCache(
            PluginInterface.GetPluginConfigDirectory(),
            message => Log.Debug($"[SlotYield] {message}"));

        var trimmed = Cache.Trim();
        if (trimmed > 0)
            Log.Debug($"[SlotYield] trimmed {trimmed} stale cache file(s).");

        Client = new UniversalisClient(maxConcurrency: Configuration.MaxConcurrency);
        this.arbitrageService = new ArbitrageService(this.Client);
        
        Catalog = new ItemCatalog(DataManager);
        State = new ScanState();

        this.capture = new MarketCapture(this);
        this.capture.Attach();
        Framework.Update += this.OnFrameworkUpdate;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", this.OnSellDialogOpened);

        this.mainWindow = new MainWindow(this);
        this.worthWindow = new WorthWindow(this);
        this.tickerWindow = new TickerWindow(this);
        this.historyWindow = new HistoryWindow(this);
        this.helpWindow = new HelpWindow();
        this.compareWindow = new CompareWindow(this, this.mainWindow);
        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.worthWindow);
        this.windowSystem.AddWindow(this.tickerWindow);
        this.windowSystem.AddWindow(this.historyWindow);
        this.windowSystem.AddWindow(this.helpWindow);
        this.windowSystem.AddWindow(this.compareWindow);
        this.worthWindow.IsOpen = Configuration.WorthOpen;

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenMain;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Slot Yield. Options: scan, world <name>, pin <itemId> [hq], unpin <itemId>, craft, alerts, price, reprice, checkall, undercutall, worth, next, stop, big, dumpretainer, probe <slot>, setprice <gil>, confirmprice, diag, reset, clear.",
            ShowInHelp = true,
        });

        try
        {
            CommandManager.AddHandler(AliasName, new CommandInfo(this.OnCommand)
            {
                HelpMessage = "Alias for /slotyield.",
                ShowInHelp = false,
            });
        }
        catch
        {
            // Another plugin owns /sy; the long command still works.
        }

        if (Configuration.WindowOpen)
            this.mainWindow.IsOpen = true;

        Log.Information("[SlotYield] Loaded. World: {World}", this.ResolveWorld());

        Theme.Apply(Configuration.Theme);

        this.dtrEntry = DtrBar.Get("Slot Yield");
        this.dtrEntry.Tooltip = "Best gil/day - click for Slot Yield";
        this.dtrEntry.OnClick = _ => this.OpenMain();
        this.dtrEntry.Text = "no scan";
        this.dtrEntry.Shown = false;

        if (Configuration.ExceptionLog)
            AppDomain.CurrentDomain.FirstChanceException += this.OnFirstChanceException;
    }

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IDtrBar DtrBar { get; private set; } = null!;

    [PluginService]
    internal static IMarketBoard MarketBoard { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static INotificationManager Notifications { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }

    public MarketCache Cache { get; }

    public UniversalisClient Client { get; }

    private readonly ArbitrageService? arbitrageService;
    private ArbitrageReport? arbitrageReport;
    private volatile bool arbitrageRunning;
    private CancellationTokenSource? arbitrageCancellation;
    private int arbitrageDone;
    private int arbitrageTotal;
    private readonly List<ArbitragePulse> arbitrageHistory = new();
    private string? pendingWorldWalk;
    private DateTime pendingWorldWalkDeadline;
    private string? pendingCityThenWorld;
    private DateTime pendingCityDeadline;

    public ArbitrageReport? Arbitrage => this.arbitrageReport;

    public bool ArbitrageRunning => this.arbitrageRunning;

    public int ArbitrageDone => this.arbitrageDone;

    public int ArbitrageTotal => this.arbitrageTotal;

    public IReadOnlyList<ArbitragePulse> ArbitrageHistory => this.arbitrageHistory;

    public void CancelArbitrage() => this.arbitrageCancellation?.Cancel();

    /// <summary>Ask Lifestream (via IPC) to change worlds with the chosen gateway, then walk to the market board.</summary>
    public bool TravelToWorld(string world)
    {
        if (string.IsNullOrWhiteSpace(world))
            return false;

        // Lifestream only honours a gateway that exists in your current zone, so get to the
        // gateway city first and change worlds from there.
        if (!this.IsInGatewayCity())
        {
            if (!this.TeleportToGatewayCity())
            {
                Log.Warning("[SlotYield] Could not teleport to the gateway city.");
                return false;
            }

            this.pendingCityThenWorld = world;
            this.pendingCityDeadline = DateTime.UtcNow.AddMinutes(3);
            Log.Information("[SlotYield] Travelling to the gateway city before changing to {World}.", world);
            return true;
        }

        return this.ChangeWorldNow(world);
    }

    private bool ChangeWorldNow(string world)
    {
        var crossDc = false;

        try
        {
            var canCross = PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitCrossDC");
            crossDc = canCross.InvokeFunc(world);
        }
        catch
        {
            // Lifestream without the cross-DC helper: assume same-DC
        }

        try
        {
            var changeWorld = PluginInterface.GetIpcSubscriber<string, bool, string, bool, int?, bool?, bool?, object>("Lifestream.TPAndChangeWorld");
            changeWorld.InvokeAction(world, crossDc, null!, false, Math.Clamp(this.Configuration.TravelGateway, 2, 9), null, null);
            Log.Information(
                "[SlotYield] Lifestream changing world to {World} via gateway {Gateway}.",
                world,
                this.Configuration.TravelGateway);

            if (this.Configuration.TravelWalk)
            {
                this.pendingWorldWalk = world;
                this.pendingWorldWalkDeadline = DateTime.UtcNow.AddSeconds(180);
            }

            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[SlotYield] Lifestream IPC is not available.");
            return false;
        }
    }

    private static ushort GatewayTerritory(int gateway) => gateway switch
    {
        8 => 129,  // Limsa Lominsa Lower Decks
        2 => 132,  // Gridania
        9 => 130,  // Ul'dah
        _ => 129,
    };

    private bool IsInGatewayCity() => ClientState.TerritoryType == GatewayTerritory(this.Configuration.TravelGateway);

    private bool TeleportToGatewayCity()
    {
        try
        {
            var teleport = PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
            return teleport.InvokeFunc((uint)Math.Clamp(this.Configuration.TravelGateway, 2, 9), 0);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[SlotYield] Lifestream teleport is not available.");
            return false;
        }
    }

    private void UpdatePendingCityTravel()
    {
        if (this.pendingCityThenWorld is not { } world)
            return;

        if (DateTime.UtcNow > this.pendingCityDeadline)
        {
            this.pendingCityThenWorld = null;
            return;
        }

        if (!PlayerState.IsLoaded || !this.IsInGatewayCity())
            return;

        this.pendingCityThenWorld = null;
        this.ChangeWorldNow(world);
    }

    /// <summary>Path to the nearest market board on the current map using vnavmesh.</summary>
    public bool WalkToMarketBoard()
    {
        var board = this.FindMarketBoard();
        if (board is null)
        {
            Log.Warning("[SlotYield] No market board on this map.");
            return false;
        }

        try
        {
            var subscriber = PluginInterface.GetIpcSubscriber<Vector3, bool, object>("vnavmesh.PathfindAndMoveTo");
            subscriber.InvokeAction(board.Position, false);
            Log.Information("[SlotYield] Walking to the market board at {Board}.", board.Position);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[SlotYield] vnavmesh is not available.");
            return false;
        }
    }

    // ---------------------------------------------------------------- auto buy ----

    private sealed record BuyRequest(string World, uint ItemId, string Name, bool Hq, int Quantity, decimal MaxUnitPrice);

    private BuyRequest? pendingBuy;
    private DateTime buyRequestUtc;
    private string buyStatus = string.Empty;

    public string BuyStatus => this.buyStatus;

    /// <summary>
    /// Start a guarded purchase: the board must be open, then the live listings are checked
    /// against the limit before anything is sent.
    /// </summary>
    public bool StartBuy(string world, uint itemId, string name, bool hq, int quantity, decimal maxUnitPrice)
    {
        if (this.pendingBuy is not null)
            return false;

        if (GameGui.GetAddonByName("MarketBoard", 1) == nint.Zero)
        {
            this.buyStatus = $"Open the market board on {world} first.";
            Notifications.AddNotification(new Notification
            {
                Title = "Slot Yield",
                Content = this.buyStatus,
                Type = NotificationType.Warning,
            });
            return false;
        }

        if (!MarketBoardBuyer.RequestItem(itemId))
        {
            this.buyStatus = "The market board search proxy is not ready.";
            return false;
        }

        this.pendingBuy = new BuyRequest(world, itemId, name, hq, Math.Clamp(quantity, 1, 999), maxUnitPrice);
        this.buyRequestUtc = DateTime.UtcNow;
        this.buyStatus = $"checking {name}...";
        Log.Information("[SlotYield] Buy check: {Quantity}x {Name} at <= {Max}", quantity, name, maxUnitPrice);
        return true;
    }

    private void StepBuy()
    {
        if (this.pendingBuy is not { } request)
            return;

        if (DateTime.UtcNow - this.buyRequestUtc > TimeSpan.FromSeconds(20))
        {
            this.pendingBuy = null;
            this.buyStatus = "price check timed out";
            return;
        }

        if (MarketBoardBuyer.WaitingForListings())
            return;

        var listings = MarketBoardBuyer.ReadListings(request.ItemId)
            .Where(listing => listing.Hq == request.Hq && listing.UnitPrice <= request.MaxUnitPrice)
            .OrderBy(listing => listing.UnitPrice)
            .ToList();

        if (listings.Count == 0)
        {
            this.pendingBuy = null;
            this.buyStatus = $"no {request.Name} at or below {Money.Gil(request.MaxUnitPrice)}";
            Notifications.AddNotification(new Notification
            {
                Title = "Slot Yield",
                Content = this.buyStatus,
                Type = NotificationType.Warning,
            });
            return;
        }

        var best = listings[0];
        var take = Math.Min(request.Quantity, (int)best.Quantity);

        if (!MarketBoardBuyer.Buy(best, take))
        {
            this.pendingBuy = null;
            this.buyStatus = "the purchase request was rejected";
            return;
        }

        this.pendingBuy = null;
        this.buyStatus = $"bought {take} x {request.Name} at {Money.Gil(best.UnitPrice)}";
        Log.Information("[SlotYield] {Status}", this.buyStatus);
        Notifications.AddNotification(new Notification
        {
            Title = "Slot Yield",
            Content = this.buyStatus,
            Type = NotificationType.Success,
        });
    }

    // ---------------------------------------------------------------- buy run ----

    private enum BuyRunPhase
    {
        Travel,
        Arrive,
        Walk,
        Approach,
        Board,
        Buy,
        Wait,
    }

    private List<FlipCartEntry>? buyRun;
    private int buyRunIndex;
    private BuyRunPhase buyRunPhase;
    private DateTime buyRunPhaseUtc;
    private bool buyRunActive;

    public bool BuyRunActive => this.buyRunActive;

    public string BuyRunStatus { get; private set; } = string.Empty;

    /// <summary>Walks the whole cart: travel to each buy world, walk to the board, buy each entry.</summary>
    public void StartBuyRun()
    {
        if (this.buyRunActive)
            return;

        var entries = this.Configuration.FlipCart.Where(entry => entry.Quantity > 0).ToList();
        if (entries.Count == 0)
        {
            this.BuyRunStatus = "the cart is empty";
            return;
        }

        this.buyRun = entries;
        this.buyRunIndex = 0;
        this.buyRunPhase = BuyRunPhase.Travel;
        this.buyRunPhaseUtc = DateTime.UtcNow;
        this.buyRunActive = true;
        this.BuyRunStatus = "starting";
        Log.Information("[SlotYield] Buy run started for {Count} item(s).", entries.Count);
    }

    public void CancelBuyRun()
    {
        this.buyRunActive = false;
        this.buyRun = null;
        this.buyStatus = string.Empty;
        this.BuyRunStatus = "cancelled";
    }

    private void StepBuyRun()
    {
        if (!this.buyRunActive || this.buyRun is null)
            return;

        if (this.buyRunIndex >= this.buyRun.Count)
        {
            this.buyRunActive = false;
            this.buyRun = null;
            this.BuyRunStatus = "done";
            Notifications.AddNotification(new Notification
            {
                Title = "Slot Yield",
                Content = "Buy run finished.",
                Type = NotificationType.Success,
            });
            return;
        }

        var entry = this.buyRun[this.buyRunIndex];
        var world = entry.BuyWorld;
        var onWorld = this.CurrentWorldName().Equals(world, StringComparison.OrdinalIgnoreCase);

        switch (this.buyRunPhase)
        {
            case BuyRunPhase.Travel:
                if (onWorld)
                {
                    this.buyRunPhase = BuyRunPhase.Walk;
                    return;
                }

                this.BuyRunStatus = $"travelling to {world}";
                this.TravelToWorld(world);
                this.buyRunPhase = BuyRunPhase.Arrive;
                this.buyRunPhaseUtc = DateTime.UtcNow;
                return;

            case BuyRunPhase.Arrive:
                if (onWorld)
                {
                    this.buyRunPhase = BuyRunPhase.Walk;
                    return;
                }

                if (DateTime.UtcNow - this.buyRunPhaseUtc > TimeSpan.FromMinutes(3))
                {
                    this.BuyRunStatus = $"could not reach {world}";
                    this.buyRunActive = false;
                    this.buyRun = null;
                }

                return;

            case BuyRunPhase.Walk:
                this.BuyRunStatus = "walking to the market board";
                this.WalkToMarketBoard();
                this.buyRunPhase = BuyRunPhase.Approach;
                this.buyRunPhaseUtc = DateTime.UtcNow;
                return;

            case BuyRunPhase.Approach:
                {
                    var board = this.FindMarketBoard();
                    var player = ObjectTable.LocalPlayer;
                    if (board is null || player is null)
                    {
                        this.buyRunPhase = BuyRunPhase.Board;
                        this.buyRunPhaseUtc = DateTime.UtcNow;
                        return;
                    }

                    if (Vector3.Distance(board.Position, player.Position) > 4.5f)
                    {
                        if (DateTime.UtcNow - this.buyRunPhaseUtc > TimeSpan.FromSeconds(75))
                        {
                            this.buyRunPhase = BuyRunPhase.Board;
                            this.buyRunPhaseUtc = DateTime.UtcNow;
                        }

                        return;
                    }

                    this.InteractWithMarketBoard();
                    this.buyRunPhase = BuyRunPhase.Board;
                    this.buyRunPhaseUtc = DateTime.UtcNow;
                    return;
                }

            case BuyRunPhase.Board:
            case BuyRunPhase.Wait:
                if (GameGui.GetAddonByName("MarketBoard", 1) != nint.Zero)
                {
                    var name = this.Catalog.Name(entry.ItemId);
                    this.BuyRunStatus = $"buying {name}";
                    this.StartBuy(world, entry.ItemId, name, entry.Hq, entry.Quantity, entry.BuyPrice);
                    this.buyRunPhase = BuyRunPhase.Buy;
                    this.buyRunPhaseUtc = DateTime.UtcNow;
                    return;
                }

                if (this.buyRunPhase == BuyRunPhase.Board)
                {
                    if (DateTime.UtcNow - this.buyRunPhaseUtc > TimeSpan.FromSeconds(45))
                        this.buyRunPhase = BuyRunPhase.Wait;

                    return;
                }

                this.BuyRunStatus = "open the market board to continue";
                return;

            case BuyRunPhase.Buy:
                if (this.pendingBuy is not null)
                {
                    if (DateTime.UtcNow - this.buyRunPhaseUtc > TimeSpan.FromSeconds(40))
                    {
                        this.pendingBuy = null;
                        this.buyRunIndex++;
                        this.buyRunPhase = BuyRunPhase.Travel;
                    }

                    return;
                }

                this.buyRunIndex++;
                this.buyRunPhase = BuyRunPhase.Travel;
                return;
        }
    }

    /// <summary>Open the market board by interacting with the nearest board object.</summary>
    public unsafe bool InteractWithMarketBoard()
    {
        var board = this.FindMarketBoard();
        var player = ObjectTable.LocalPlayer;
        if (board is null || player is null)
            return false;

        if (Vector3.Distance(board.Position, player.Position) > 5f)
            return false;

        try
        {
            TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)board.Address, false);
            Log.Information("[SlotYield] Interacted with the market board.");
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[SlotYield] Market board interaction failed.");
            return false;
        }
    }

    private IGameObject? FindMarketBoard()
    {
        IGameObject? best = null;
        var bestDistance = float.MaxValue;
        var origin = ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;

        foreach (var obj in ObjectTable)
        {
            if (obj.ObjectKind is not (ObjectKind.EventObj or ObjectKind.EventNpc))
                continue;

            var name = obj.Name.ToString();
            if (!name.Contains("Market Board", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Summoning Bell", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var distance = Vector3.Distance(obj.Position, origin);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = obj;
        }

        return best;
    }

    private void UpdatePendingWorldWalk()
    {
        if (this.pendingWorldWalk is not { } target)
            return;

        if (DateTime.UtcNow > this.pendingWorldWalkDeadline)
        {
            this.pendingWorldWalk = null;
            return;
        }

        if (!PlayerState.IsLoaded)
            return;

        if (!this.CurrentWorldName().Equals(target, StringComparison.OrdinalIgnoreCase))
            return;

        this.pendingWorldWalk = null;

        if (!this.WalkToMarketBoard())
            return;

        Notifications.AddNotification(new Notification
        {
            Title = "Slot Yield",
            Content = $"Arrived on {target} - walking to the market board.",
            Type = NotificationType.Info,
        });
    }

    public ItemCatalog Catalog { get; }

    private RecipeCatalog? recipes;

    public RecipeCatalog Recipes => this.recipes ??= RecipeCatalog.Build(DataManager, message => Log.Debug($"[SlotYield] {message}"));

    private IReadOnlyDictionary<uint, decimal>? vendorPrices;

    public IReadOnlyDictionary<uint, decimal> VendorPrices => this.vendorPrices ??= ItemCatalog.BuildVendorPriceMap(DataManager);

    public ScanState State { get; }

    public MainWindow MainWindow => this.mainWindow;

    public IReadOnlyList<string> Worlds => this.worlds ??= WorldDirectory.PublicWorlds(DataManager);

    public void Dispose()
    {
        this.disposed = true;
        this.CancelScan();

        Framework.Update -= this.OnFrameworkUpdate;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSell", this.OnSellDialogOpened);
        this.capture.Detach();

        CommandManager.RemoveHandler(CommandName);
        if (CommandManager.Commands.ContainsKey(AliasName))
            CommandManager.RemoveHandler(AliasName);

        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenMain;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        this.windowSystem.RemoveAllWindows();

        this.dtrEntry?.Remove();
        this.dtrEntry = null;

        AppDomain.CurrentDomain.FirstChanceException -= this.OnFirstChanceException;

        this.Client.Dispose();
        this.SaveConfig();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(Configuration);

    private void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
    {
        if (SuppressTracer || this.exceptionLogging || this.exceptionLogCount >= 2000)
            return;

        this.exceptionLogging = true;
        try
        {
            var exception = args.Exception;
            var stack = exception.StackTrace ?? string.Empty;
            if (stack.Contains("SlotYield", StringComparison.Ordinal))
                return;

            this.exceptionLogCount++;
            var trace = new System.Diagnostics.StackTrace(1, true).ToString();
            Log.Warning(
                "[SlotYield][firstchance] {Type}: {Message}\n{Trace}",
                exception.GetType().FullName,
                exception.Message,
                trace);
        }
        catch
        {
            // the tracer must never throw
        }
        finally
        {
            this.exceptionLogging = false;
        }
    }

    /// <summary>Re-apply the dtr bar preference (and the current best pick) immediately.</summary>
    public void RefreshDtr()    {
        if (this.dtrEntry is not { } dtr)
            return;

        dtr.Shown = Configuration.DtrBar;

        if (State.Result is { } result)
            ApplyDtr(dtr, result);
    }

    private static void ApplyDtr(IDtrBarEntry dtr, ScanResult result)
    {
        var best = result.Candidates
            .Where(c => !c.Score.IsDead && !c.Score.IsSuspect)
            .Select(c => c.Score.ProfitPerDay)
            .DefaultIfEmpty(0m)
            .Max();

        dtr.Text = best > 0m ? $"+{Money.Gil(best)}/d" : "no picks";
    }

    /// <summary>Encode the pinned portfolio as a portable base64 string.</summary>
    public string BuildShareCode()
    {
        var parts = Configuration.Pinned.Select(slot => $"{slot.ItemId}:{(slot.Hq ? 1 : 0)}:{slot.StackSize}");
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Join(",", parts)));
    }

    /// <summary>Merge a share code into the pinned portfolio; returns how many slots were added.</summary>
    public int ImportShareCode(string code)
    {
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(code.Trim()));
            var added = 0;

            foreach (var part in decoded.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = part.Split(':');
                if (fields.Length < 3 || !uint.TryParse(fields[0], out var itemId) || !int.TryParse(fields[2], out var stack))
                    continue;

                var hq = fields[1] == "1";
                if (Configuration.Pinned.Any(slot => slot.ItemId == itemId && slot.Hq == hq))
                    continue;

                Configuration.Pinned.Add(new PinnedSlot
                {
                    ItemId = itemId,
                    Hq = hq,
                    StackSize = Math.Clamp(stack, 1, 9999),
                });
                added++;
            }

            if (added > 0)
            {
                Configuration.Normalize();
                this.SaveConfig();
            }

            return added;
        }
        catch
        {
            return 0;
        }
    }

    public void Print(string message) => ChatGui.Print($"[Slot Yield] {message}");

    public IReadOnlyList<CraftOpportunity> CraftPlan { get; private set; } = Array.Empty<CraftOpportunity>();

    public int CraftOwnedItemCount { get; private set; }

    public int CraftBagStacks { get; private set; }

    public int CraftSaddlebagStacks { get; private set; }

    public int CraftRetainerStacks { get; private set; }

    public string CraftRetainerName { get; private set; } = string.Empty;

    public DateTime? CraftPlanUtc { get; private set; }

    private Dictionary<uint, int>? craftStock;
    private int probeSlot = -1;
    private DateTime probeDumpAt;
    private int pendingPrice;
    private DateTime pendingPriceAt;
    private bool craftQuotesInFlight;

    /// <summary>
    /// Reads the bags, ranks what could be crafted, and prices anything the last scan did not cover -
    /// the craft list should not depend on what happened to make the scan shortlist.
    /// </summary>
    public void RefreshCraftPlan()
    {
        var stock = PlayerInventory.Read();

        this.craftStock = stock.Items;
        this.CraftOwnedItemCount = stock.Items.Count;
        this.CraftBagStacks = stock.BagStacks;
        this.CraftSaddlebagStacks = stock.SaddlebagStacks;
        this.CraftRetainerStacks = stock.RetainerStacks;
        this.CraftRetainerName = stock.RetainerName;

        this.ComputeCraftPlan();

        if (this.craftStock.Count == 0)
            return;

        var candidates = CraftPlanner.ReachableResults(this.Recipes.All, this.craftStock);
        var missing = new List<uint>();

        foreach (var itemId in candidates)
        {
            if (this.FindCraftQuote(itemId, Quality.Nq) is null)
                missing.Add(itemId);
        }

        if (missing.Count > 0)
            this.RequestCraftQuotes(missing);

        Log.Information(
            "[SlotYield] Craft plan: {Owned} item(s) from {Bags} bag / {Saddle} saddlebag / {Retainer} retainer stack(s), {Ideas} idea(s), {Missing} unpriced candidate(s).",
            this.CraftOwnedItemCount,
            this.CraftBagStacks,
            this.CraftSaddlebagStacks,
            this.CraftRetainerStacks,
            this.CraftPlan.Count,
            missing.Count);
    }

    public bool IsPricingCraft => this.craftQuotesInFlight;


    /// <summary>Plans a reprice pass over your listings; never applies anything by itself.</summary>
    public RepricePlan BuildRepricePlan()
    {
        var parameters = new SlotParameters
        {
            Strategy = Configuration.Strategy,
            TaxRate = Configuration.TaxRate,
            StackSize = Configuration.DefaultStackSize,
            DeadAfterDays = Configuration.DeadAfterDays,
            MinimumVelocity = Configuration.MinimumVelocity,
        };

        return RepricePlanner.Build(
            Configuration.MyListings,
            this.FindCraftQuote,
            parameters,
            Configuration.RepriceMinMoveGil,
            Configuration.RepriceFloorPercent);
    }

    private IReadOnlyList<RepriceChange> repriceQueue = Array.Empty<RepriceChange>();
    private int repriceIndex;
    private bool repriceAssistActive;

    public bool RepriceAssistActive => this.repriceAssistActive;

    /// <summary>
    /// Guided repricing: opens the adjust dialog for each listing and puts the target price on the
    /// clipboard, so the human step is one paste and one confirm.
    /// </summary>
    public void StartRepriceAssist()
    {
        var plan = this.BuildRepricePlan();

        if (plan.IsEmpty)
        {
            var blockedHint = plan.SkippedForFloor > 0
                ? $" {plan.SkippedForFloor} blocked by the 50% floor; /slotyield big allows them."
                : string.Empty;

            this.Print($"nothing to reprice ({plan.SkippedForSmallMove} under threshold, {plan.SkippedForFloor} blocked).{blockedHint}");
            return;
        }

        this.repriceQueue = plan.Changes;
        this.repriceIndex = 0;
        this.repriceAssistActive = true;
        this.AdvanceRepriceAssist();
    }

    public void AdvanceRepriceAssist()
    {
        if (!this.repriceAssistActive || this.repriceIndex >= this.repriceQueue.Count)
        {
            this.repriceAssistActive = false;
            this.Print("reprice assist finished.");
            return;
        }

        var change = this.repriceQueue[this.repriceIndex];
        var slot = RetainerSellSlots.FindSlot(change.ItemId, change.Hq);

        if (slot < 0)
        {
            this.Print($"skipped {this.Catalog.Name(change.ItemId)}: not in the open retainer list.");
            this.repriceIndex++;
            this.AdvanceRepriceAssist();
            return;
        }

        var opened = AddonDump.TryOpenSellDialog(slot);
        ImGui.SetClipboardText(change.TargetPrice.ToString("0", System.Globalization.CultureInfo.InvariantCulture));

        var hq = change.Hq ? " (HQ)" : string.Empty;
        this.Print(
            $"{this.repriceIndex + 1}/{this.repriceQueue.Count} {this.Catalog.Name(change.ItemId)}{hq}: " +
            $"dialog open ({opened}), target {change.TargetPrice:N0} is on the clipboard. " +
            "Paste, confirm, then /slotyield next.");
        this.repriceIndex++;
    }

    public void StopRepriceAssist()
    {
        this.repriceAssistActive = false;
        this.repriceQueue = Array.Empty<RepriceChange>();
        this.Print("reprice assist stopped.");
    }

    private DateTime sellDialogPriceAt;

    /// <summary>Called when a market board page loads, so an open sell dialog can price itself.</summary>
    public void NotifyBoardPrices()
    {
        if (this.sweepRunning)
            return;

        if (!this.Configuration.AutoPriceSellDialog || !AddonInteraction.IsOpen())
            return;

        this.sellDialogPriceAt = DateTime.UtcNow.AddMilliseconds(350);
    }

    private void OnSellDialogOpened(AddonEvent type, AddonArgs args)
    {
        // The sweep reads and writes this dialog itself; auto-fill would overwrite its baseline.
        if (this.sweepRunning)
            return;

        if (!this.Configuration.AutoPriceSellDialog)
            return;

        // The dialog fills its own fields just after setup, so write the price a moment later.
        this.sellDialogPriceAt = DateTime.UtcNow.AddMilliseconds(400);
    }

    /// <summary>Fills the open sell dialog's asking price from the known market price.</summary>
    public void ApplySellDialogPrice()
    {
        if (this.sweepRunning)
            return;

        if (!SellDialog.TryGetItem(out var itemId, out var hq))
        {
            this.Print("could not read the item in the sell dialog.");
            return;
        }

        var quality = hq ? Quality.Hq : Quality.Nq;
        var quote = this.FindCraftQuote(itemId, quality);

        if (quote is null || quote.MinListing <= 0)
        {
            this.Print($"no market price known for {this.Catalog.Name(itemId)}; scan or open its board page first.");
            return;
        }

        var parameters = new SlotParameters
        {
            Strategy = Configuration.Strategy,
            TaxRate = Configuration.TaxRate,
            StackSize = Configuration.DefaultStackSize,
            DeadAfterDays = Configuration.DeadAfterDays,
            MinimumVelocity = Configuration.MinimumVelocity,
        };

        var target = (int)Math.Max(1m, Math.Round(SlotModel.ChoosePrice(quote, parameters.Strategy)));
        var result = AddonInteraction.SetPrice(target);

        Log.Information(
            "[SlotYield] Sell dialog {Item} asking price {Price}: {Result}",
            this.Catalog.Name(itemId),
            target,
            result);

        this.Print($"{this.Catalog.Name(itemId)}: asking price set to {target:N0} ({result}).");
    }

    private readonly Queue<(InventoryType Type, int Slot, uint ItemId, bool Hq, int Price)> listQueue = new();
    private bool listRunning;
    private int listStage;
    private int listApplied;
    private int listFailed;
    private DateTime listNext;
    private DateTime listSwitchNext;
    private bool listAwaitingMenu;
    private bool listWarned;
    private ulong listRetainerId;
    private DateTime listDeadline;

    public bool ListRunning => this.listRunning;

    /// <summary>Bag items that are not already on the market and have a known market price.</summary>
    public IReadOnlyList<(InventoryType Type, int Slot, uint ItemId, bool Hq, int Quantity, int Price)>
        GetListingCandidates(int limit = 12)
    {
        // Withdrawn listings linger in the book until their retainer is opened; age them out here so
        // an item taken off the market can be listed again straight away.
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var alreadyListed = new HashSet<(uint ItemId, bool Hq)>(
            this.Configuration.MyListings
                .Where(listing => listing.SeenUtc >= cutoff)
                .Select(listing => (listing.ItemId, listing.Hq)));

        var result = new List<(InventoryType, int, uint, bool, int, int)>();

        foreach (var slot in PlayerInventory.Slots())
        {
            if (alreadyListed.Contains((slot.ItemId, slot.Hq)))
                continue;

            var quote = this.FindCraftQuote(slot.ItemId, slot.Hq ? Quality.Hq : Quality.Nq);

            if (quote is null || quote.MinListing <= 0)
                continue;

            var price = (int)Math.Max(1m, Math.Round(SlotModel.ChoosePrice(quote, Configuration.Strategy)));
            result.Add((slot.Type, slot.Slot, slot.ItemId, slot.Hq, slot.Quantity, price));
        }

        return result
            .OrderByDescending(candidate => candidate.Item6 * candidate.Item5)
            .Take(limit)
            .ToList();
    }

    /// <summary>Lists one bag item: open its sell dialog, write the price, confirm.</summary>
    public void ListOne(InventoryType container, int slot, uint itemId, bool hq, int price)
    {
        if (this.listRunning)
        {
            this.Print("a listing is already in progress; /slotyield stop cancels it.");
            return;
        }

        var retainer = RetainerFlow.FirstWithSpace();

        if (retainer == 0)
        {
            this.Print("every retainer is full; free a market slot first.");
            return;
        }

        this.listRetainerId = retainer;
        this.listAwaitingMenu = false;
        this.listWarned = false;
        this.listSwitchNext = DateTime.UtcNow;

        this.listQueue.Clear();
        this.listQueue.Enqueue((container, slot, itemId, hq, price));
        this.listRunning = true;
        this.listStage = 0;
        this.listApplied = 0;
        this.listFailed = 0;
        this.listNext = DateTime.UtcNow;

        this.Print($"listing {this.Catalog.Name(itemId)} at {price:N0}...");
    }

    public void StopListing()
    {
        this.listRunning = false;
        this.listQueue.Clear();
    }

    /// <summary>Switches to the chosen retainer and waits for its sell list to be open.</summary>
    private bool EnsureListingRetainer(DateTime now)
    {
        if (now < this.listSwitchNext)
            return false;

        this.listSwitchNext = now.AddMilliseconds(450);

        var sellListOpen = RetainerFlow.IsOpen("RetainerSellList");

        if (sellListOpen && RetainerFlow.ActiveRetainerId() == this.listRetainerId)
        {
            this.listAwaitingMenu = false;
            return true;
        }

        if (sellListOpen)
        {
            RetainerFlow.Close("RetainerSellList");
            this.listAwaitingMenu = false;
            return false;
        }

        if (RetainerFlow.IsOpen("SelectString"))
        {
            if (this.listAwaitingMenu)
                RetainerFlow.SelectSellItems(RetainerFlow.SellItemsEntry);
            else
                RetainerFlow.Close("SelectString");

            return false;
        }

        if (RetainerFlow.IsOpen("RetainerList"))
        {
            var index = RetainerFlow.IndexById(this.listRetainerId);

            if (index < 0)
            {
                this.Print("the chosen retainer is not in the open list; stopping.");
                this.listRunning = false;
                return false;
            }

            RetainerFlow.ClickRetainer(index);
            this.listAwaitingMenu = true;
            return false;
        }

        if (!this.listWarned)
        {
            this.listWarned = true;
            this.Print("open the retainer list at a summoning bell so a retainer can be picked.");
        }

        return false;
    }

    private void StepListing(DateTime now)
    {
        if (now < this.listNext)
            return;

        if (this.listQueue.Count == 0)
        {
            this.listRunning = false;
            this.Print($"listing finished: {this.listApplied} listed, {this.listFailed} failed.");
            return;
        }

        var current = this.listQueue.Peek();
        var name = this.Catalog.Name(current.ItemId);
        var open = AddonInteraction.IsOpen();

        switch (this.listStage)
        {
            case 0:
                if (!this.EnsureListingRetainer(now))
                    return;

                AddonDump.TryOpenSellDialog(current.Type, current.Slot);
                this.listDeadline = now.AddSeconds(3);
                this.listStage = 1;
                this.listNext = now.AddMilliseconds(500);
                return;

            case 1:
                if (open)
                {
                    this.listStage = 2;
                    this.listNext = now.AddMilliseconds(250);
                    return;
                }

                if (now >= this.listDeadline)
                {
                    this.Print($"skip {name}: the sell dialog did not open.");
                    this.listFailed++;
                    this.listQueue.Dequeue();
                    this.listStage = 0;
                    this.listNext = now.AddMilliseconds(400);
                }

                return;

            case 2:
                if (!AddonInteraction.TrySetPrice(current.Price, out var actual, out var message))
                {
                    this.Print($"skip {name}: {message}.");
                    this.listFailed++;
                    this.listQueue.Dequeue();
                    this.listStage = 0;
                    this.listNext = now.AddMilliseconds(400);
                    return;
                }

                Log.Information(
                    "[SlotYield] Listing {Item}{Hq} at {Price} (read {Actual}).",
                    name,
                    current.Hq ? " (HQ)" : string.Empty,
                    current.Price,
                    actual);

                this.listDeadline = now.AddSeconds(3);
                this.listStage = 3;
                this.listNext = now.AddMilliseconds(350);
                return;

            case 3:
                AddonInteraction.Confirm();
                Log.Information("[SlotYield] Listing {Item} confirmed at {Price}.", name, current.Price);
                this.listApplied++;
                this.listStage = 4;
                this.listNext = now.AddMilliseconds(700);
                return;

            case 4:
                if (!open)
                {
                    this.listQueue.Dequeue();
                    this.listStage = 0;
                    this.listNext = now.AddMilliseconds(400);
                    return;
                }

                if (now >= this.listDeadline)
                {
                    this.Print($"stopped: the dialog for {name} stayed open.");
                    this.listRunning = false;
                }

                return;
        }
    }

    private bool sweepRunning;
    private int sweepIndex;
    private bool sweepAwaitingMenu;
    private DateTime sweepNext;
    private DateTime sweepProgressAt;
    private DateTime sweepItemDeadline;
    private int sweepItemIndex;
    private int sweepStage;
    private int sweepApplied;
    private bool sweepRepricing;
    private int sweepExpectedPrice;
    private uint sweepItemId;
    private IReadOnlyList<ulong> sweepTargets = Array.Empty<ulong>();
    private readonly HashSet<uint> sweepItemIds = new();

    public bool SweepRunning => this.sweepRunning;

    private bool sweepUndercutAll;

    /// <summary>
    /// Visits every retainer that has listings, reads its sell list, then refreshes the market price
    /// for everything found. Needs the retainer list open at a bell.
    /// </summary>
    public void CheckAllListings(bool undercutAll = false)
    {
        this.sweepUndercutAll = undercutAll;
        if (this.sweepRunning)
        {
            this.Print("a price check is already running.");
            return;
        }

        if (!RetainerFlow.IsOpen("RetainerList"))
        {
            this.Print("open the retainer list at a summoning bell first.");
            return;
        }

        this.sweepTargets = RetainerFlow.RetainersWithListings();

        if (this.sweepTargets.Count == 0)
        {
            this.Print("no retainer has anything listed.");
            return;
        }

        this.sweepItemIds.Clear();
        this.sweepIndex = 0;
        this.sweepItemIndex = 0;
        this.sweepStage = 0;
        this.sweepAwaitingMenu = false;
        this.sweepRunning = true;
        this.sweepNext = DateTime.UtcNow;
        this.sweepProgressAt = DateTime.UtcNow;

        this.Print($"checking {this.sweepTargets.Count} retainer(s) for current prices...");
    }

    public void StopSweep()
    {
        this.sweepRunning = false;
        this.Print($"price check stopped after {this.sweepIndex} retainer(s), {this.sweepItemIds.Count} item(s).");
    }

    private void StepSweep(DateTime now)
    {
        if (now < this.sweepNext)
            return;

        this.sweepNext = now.AddMilliseconds(450);

        if (this.sweepIndex >= this.sweepTargets.Count)
        {
            RetainerFlow.Close("RetainerSellList");

            var ids = this.sweepItemIds.ToList();
            this.sweepRunning = false;

            if (ids.Count == 0)
            {
                this.Print("no listings found.");
                return;
            }

            this.Print($"{ids.Count} listing(s) found; fetching current prices...");
            this.RequestListingQuotes(ids);
            return;
        }

        var target = this.sweepTargets[this.sweepIndex];
        var sellListOpen = RetainerFlow.IsOpen("RetainerSellList");

        if (sellListOpen && RetainerFlow.ActiveRetainerId() == target)
        {
            foreach (var item in RetainerFlow.SellListItems())
                this.sweepItemIds.Add(item.ItemId);

            this.sweepAwaitingMenu = false;
            RetainerFlow.Close("RetainerSellList");
            this.sweepIndex++;
            this.sweepProgressAt = now;
            return;
        }

        if (sellListOpen)
        {
            RetainerFlow.Close("RetainerSellList");
            this.sweepAwaitingMenu = false;
            return;
        }

        if (RetainerFlow.IsOpen("SelectString"))
        {
            if (this.sweepAwaitingMenu)
            {
                RetainerFlow.SelectSellItems(RetainerFlow.SellItemsEntry);
                this.sweepProgressAt = now;
            }
            else
            {
                RetainerFlow.Close("SelectString");
            }

            return;
        }

        if (RetainerFlow.IsOpen("RetainerList"))
        {
            var index = RetainerFlow.IndexById(target);

            if (index < 0)
            {
                this.sweepIndex++;
                return;
            }

            RetainerFlow.ClickRetainer(index);
            this.sweepAwaitingMenu = true;
            this.sweepProgressAt = now;

            Log.Information(
                "[SlotYield] Check: opened retainer row {Index} ({Done}/{Total}).",
                index,
                this.sweepIndex + 1,
                this.sweepTargets.Count);

            return;
        }

        // Addon transitions have a frame or two with nothing open; only give up after a real gap.
        if (now - this.sweepProgressAt < TimeSpan.FromSeconds(4))
            return;

        this.sweepRunning = false;
        this.Print("the retainer list closed; price check stopped.");
    }

    private void StepSweepCompare(DateTime now)
    {
        if (now < this.sweepNext)
            return;

        this.sweepNext = now.AddMilliseconds(400);

        if (this.sweepIndex >= this.sweepTargets.Count)
        {
            RetainerFlow.Close("RetainerSellList");

            var ids = this.sweepItemIds.ToList();
            this.sweepRunning = false;

            if (ids.Count == 0)
            {
                this.Print("no listings found.");
                return;
            }

            this.Print($"{ids.Count} listing(s) checked, {this.sweepApplied} repriced.");
            this.RequestListingQuotes(ids);
            return;
        }

        var target = this.sweepTargets[this.sweepIndex];
        var sellListOpen = RetainerFlow.IsOpen("RetainerSellList");

        if (!sellListOpen || RetainerFlow.ActiveRetainerId() != target)
        {
            if (sellListOpen)
            {
                RetainerFlow.Close("RetainerSellList");
                this.sweepItemIndex = 0;
                this.sweepStage = 0;
                return;
            }

            if (RetainerFlow.IsOpen("SelectString"))
            {
                if (this.sweepAwaitingMenu)
                    RetainerFlow.SelectSellItems(RetainerFlow.SellItemsEntry);
                else
                    RetainerFlow.Close("SelectString");

                return;
            }

            if (RetainerFlow.IsOpen("RetainerList"))
            {
                var index = RetainerFlow.IndexById(target);

                if (index < 0)
                {
                    this.sweepIndex++;
                    return;
                }

                RetainerFlow.ClickRetainer(index);
                this.sweepAwaitingMenu = true;
                this.sweepProgressAt = now;
                this.sweepItemIndex = 0;
                this.sweepStage = 0;
                return;
            }

            if (now - this.sweepProgressAt < TimeSpan.FromSeconds(4))
                return;

            this.sweepRunning = false;
            this.Print("the retainer list closed; price check stopped.");
            return;
        }

        this.sweepAwaitingMenu = false;
        var rows = RetainerFlow.SellListCount();

        if (this.sweepItemIndex >= rows)
        {
            foreach (var item in RetainerFlow.SellListItems())
                this.sweepItemIds.Add(item.ItemId);

            RetainerFlow.Close("RetainerSellList");
            this.sweepIndex++;
            this.sweepItemIndex = 0;
            this.sweepStage = 0;
            this.sweepProgressAt = now;
            return;
        }

        switch (this.sweepStage)
        {
            case 0:
                RetainerFlow.ClickSellRow(this.sweepItemIndex);
                this.sweepStage = 1;
                this.sweepItemDeadline = now.AddSeconds(2);
                this.sweepProgressAt = now;
                return;

            case 1:
                if (RetainerFlow.IsOpen("ContextMenu"))
                {
                    RetainerFlow.ChooseAdjustPrice();
                    this.sweepStage = 2;
                    this.sweepItemDeadline = now.AddSeconds(2);
                    this.sweepProgressAt = now;
                    return;
                }

                if (now >= this.sweepItemDeadline)
                {
                    this.sweepItemIndex++;
                    this.sweepStage = 0;
                }

                return;

            case 2:
                if (AddonInteraction.IsOpen())
                {
                    RetainerFlow.ClickComparePrices();
                    this.sweepStage = 3;
                    this.sweepItemDeadline = now.AddMilliseconds(1200);
                    this.sweepProgressAt = now;
                    return;
                }

                if (now >= this.sweepItemDeadline)
                {
                    this.sweepItemIndex++;
                    this.sweepStage = 0;
                }

                return;

            case 3:
                // Let the board answer before closing it; the prices are captured from its packet.
                if (now < this.sweepItemDeadline)
                    return;

                if (RetainerFlow.IsOpen("ItemSearchResult"))
                    RetainerFlow.Close("ItemSearchResult");

                this.sweepStage = 4;
                this.sweepItemDeadline = now.AddMilliseconds(200);
                this.sweepProgressAt = now;
                return;

            case 4:
            {
                // Fresh board price is in; decide whether this listing should move.
                var rowsNow = RetainerFlow.SellListItems();
                this.sweepRepricing = false;

                if (this.sweepItemIndex >= rowsNow.Count)
                {
                    this.sweepStage = 5;
                    this.sweepItemDeadline = now.AddMilliseconds(350);
                    this.sweepProgressAt = now;
                    return;
                }

                var item = rowsNow[this.sweepItemIndex];
                var name = this.Catalog.Name(item.ItemId);
                var quote = this.FindCraftQuote(item.ItemId, item.Hq ? Quality.Hq : Quality.Nq);
                var current = AddonInteraction.ReadPrice();

                string verdict;

                if (quote is null || quote.MinListing <= 0)
                {
                    verdict = "no market price for it";
                }
                else if (current <= 0)
                {
                    verdict = "could not read its current price";
                }
                else
                {
                    var newPrice = this.sweepUndercutAll
                        ? (int)Math.Max(1m, quote.MinListing - 1m)
                        : (int)Math.Max(1m, Math.Round(SlotModel.ChoosePrice(quote, Configuration.Strategy)));

                    var move = current - newPrice;

                    if (move <= 0)
                    {
                        verdict = $"already at or below the market ({current:N0} vs {newPrice:N0})";
                    }
                    else if (!this.sweepUndercutAll && move < Configuration.RepriceMinMoveGil)
                    {
                        verdict = $"move of {move:N0} gil is under the threshold";
                    }
                    else if (!this.sweepUndercutAll && Configuration.RepriceFloorPercent > 0 && newPrice < current * Configuration.RepriceFloorPercent / 100m)
                    {
                        verdict = $"blocked by the 50% floor ({current:N0} -> {newPrice:N0})";
                    }
                    else if (!AddonInteraction.TrySetPrice(newPrice, out var actual, out var message))
                    {
                        verdict = $"price write failed: {message}";
                    }
                    else
                    {
                        this.sweepRepricing = true;
                        this.sweepExpectedPrice = newPrice;
                        this.sweepItemId = item.ItemId;
                        this.sweepApplied++;
                        verdict = $"repricing {current:N0} -> {newPrice:N0} (read {actual:N0})";
                    }
                }

                Log.Information("[SlotYield] Check {Item}{Hq}: {Verdict}.", name, item.Hq ? " (HQ)" : string.Empty, verdict);

                this.sweepStage = 5;
                this.sweepItemDeadline = now.AddMilliseconds(350);
                this.sweepProgressAt = now;
                return;
            }

            case 5:
                if (!this.sweepRepricing)
                {
                    RetainerFlow.Close("RetainerSell");
                    this.sweepItemIndex++;
                    this.sweepStage = 0;
                    this.sweepProgressAt = now;
                    return;
                }

                AddonInteraction.Confirm();
                this.sweepStage = 6;
                this.sweepItemDeadline = now.AddSeconds(2);
                this.sweepProgressAt = now;
                return;

            case 6:
                if (!AddonInteraction.IsOpen())
                {
                    // The dialog is gone; check the sell list agrees with what we asked for.
                    if (this.sweepRepricing)
                    {
                        if (RetainerFlow.TryReadListingPrice(this.sweepItemId, out var listed))
                        {
                            Log.Information(
                                "[SlotYield] Reprice verify {Item}: wanted {Wanted}, sell list shows {Listed}.",
                                this.Catalog.Name(this.sweepItemId),
                                this.sweepExpectedPrice,
                                listed);

                            if (listed != this.sweepExpectedPrice)
                            {
                                this.Print(
                                    $"{this.Catalog.Name(this.sweepItemId)}: the game shows {listed:N0}, " +
                                    $"not {this.sweepExpectedPrice:N0} - the change did not stick.");
                            }
                        }
                    }

                    this.sweepRepricing = false;
                    this.sweepItemIndex++;
                    this.sweepStage = 0;
                    this.sweepProgressAt = now;
                    return;
                }

                if (now >= this.sweepItemDeadline)
                {
                    RetainerFlow.Close("RetainerSell");
                    this.sweepRepricing = false;
                    this.sweepItemIndex++;
                    this.sweepStage = 0;
                }

                return;
        }
    }

    private bool autoRepriceRunning;
    private IReadOnlyList<RepriceChange> autoRepriceQueue = Array.Empty<RepriceChange>();
    private int autoRepriceIndex;
    private int autoRepriceApplied;
    private int autoRepriceFailed;
    private int autoRepriceStage;
    private DateTime autoRepriceNext;
    private DateTime autoRepriceDeadline;
    private DateTime autoRepriceSwitchNext;
    private bool autoRepriceNoListWarned;
    private bool autoRepriceAwaitingSellMenu;

    /// <summary>
    /// Gets the sell list onto the right retainer before a change is applied: close the previous
    /// retainer's windows, click the target in RetainerList, then pick "Sell items".
    /// </summary>
    private void StepRetainerSwitch(DateTime now, RepriceChange change)
    {
        if (now < this.autoRepriceSwitchNext)
            return;

        this.autoRepriceSwitchNext = now.AddMilliseconds(450);

        if (RetainerFlow.IsOpen("RetainerSellList"))
        {
            RetainerFlow.Close("RetainerSellList");
            this.autoRepriceAwaitingSellMenu = false;
            return;
        }

        if (RetainerFlow.IsOpen("SelectString"))
        {
            // Only take over a menu we just opened by clicking this retainer.
            if (this.autoRepriceAwaitingSellMenu)
            {
                Log.Information("[SlotYield] Reprice: choosing Sell items from the retainer menu.");
                RetainerFlow.SelectSellItems(SellEntryIndex());
            }
            else
            {
                RetainerFlow.Close("SelectString");
            }

            return;
        }

        if (RetainerFlow.IsOpen("RetainerList"))
        {
            var index = this.RetainerIndex(change.RetainerId);

            if (index < 0)
            {
                this.Print($"skip: retainer for {this.Catalog.Name(change.ItemId)} is not in the list.");
                this.NextRepriceStep(now);
                return;
            }

            Log.Information("[SlotYield] Reprice: clicking retainer row {Index} for {Retainer}.", index, change.RetainerId);
            RetainerFlow.ClickRetainer(index);
            this.autoRepriceAwaitingSellMenu = true;
            return;
        }

        if (!this.autoRepriceNoListWarned)
        {
            this.autoRepriceNoListWarned = true;
            this.Print("open the retainer list at a summoning bell; the reprice will take it from there.");
        }
    }

    private int RetainerIndex(ulong retainerId) => RetainerFlow.IndexById(retainerId);

    private static int SellEntryIndex() => RetainerFlow.SellItemsEntry;


    public bool AutoRepriceRunning => this.autoRepriceRunning;

    /// <summary>
    /// Applies the reprice plan one listing at a time: open the dialog, set the verified price,
    /// confirm, wait for the window to close, next. Aborts on any mismatch instead of guessing.
    /// </summary>
    public void StartAutomaticReprice()
    {
        if (this.autoRepriceRunning)
        {
            this.Print("reprice already running; /slotyield stop cancels it.");
            return;
        }

        var plan = this.BuildRepricePlan();

        if (plan.IsEmpty)
        {
            var blockedHint = plan.SkippedForFloor > 0
                ? $" {plan.SkippedForFloor} blocked by the 50% floor; /slotyield big allows them."
                : string.Empty;

            this.Print($"nothing to reprice ({plan.SkippedForSmallMove} under threshold, {plan.SkippedForFloor} blocked).{blockedHint}");
            return;
        }

        this.autoRepriceQueue = plan.Changes;
        this.autoRepriceIndex = 0;
        this.autoRepriceApplied = 0;
        this.autoRepriceFailed = 0;
        this.autoRepriceStage = 0;
        this.autoRepriceRunning = true;
        this.autoRepriceNext = DateTime.UtcNow;
        this.autoRepriceDeadline = DateTime.UtcNow.AddSeconds(5);

        this.Print(
            $"{plan.Changes.Count} listing(s) queued. Keep the retainer sell list open; /slotyield stop cancels.");
    }

    public void StopAutomaticReprice()
    {
        if (!this.autoRepriceRunning)
        {
            this.Print("no reprice running.");
            return;
        }

        this.autoRepriceRunning = false;
        this.autoRepriceQueue = Array.Empty<RepriceChange>();
        this.Print($"reprice stopped: {this.autoRepriceApplied} applied, {this.autoRepriceFailed} failed.");
    }

    private void StepAutomaticReprice(DateTime now)
    {
        if (now < this.autoRepriceNext)
            return;

        if (this.autoRepriceIndex >= this.autoRepriceQueue.Count)
        {
            this.autoRepriceRunning = false;
            this.Print($"reprice finished: {this.autoRepriceApplied} applied, {this.autoRepriceFailed} failed.");
            return;
        }

        var change = this.autoRepriceQueue[this.autoRepriceIndex];

        // When the sell list for the target retainer finally opens, the row lookup gets a fresh
        // window; the switch itself can take longer than the lookup timeout.
        if (RetainerFlow.IsOpen("RetainerSellList") && this.autoRepriceAwaitingSellMenu &&
            RetainerFlow.ActiveRetainerId() == change.RetainerId)
        {
            this.autoRepriceAwaitingSellMenu = false;
            this.autoRepriceDeadline = now.AddSeconds(6);
            Log.Information(
                "[SlotYield] Reprice: sell list open for retainer {Retainer} ({Rows} rows).",
                change.RetainerId,
                RetainerFlow.SellListCount());
        }

        // The menu opens before the sell list does, and only the sell list holds the rows. Wait for
        // the list itself, on the right retainer, before looking anything up.
        var sellListOpen = RetainerFlow.IsOpen("RetainerSellList");
        var activeRetainer = RetainerFlow.ActiveRetainerId();

        if (change.RetainerId != 0 && (!sellListOpen || activeRetainer != change.RetainerId))
        {
            this.StepRetainerSwitch(now, change);
            return;
        }

        var hq = change.Hq ? " (HQ)" : string.Empty;
        var name = this.Catalog.Name(change.ItemId);
        var dialogOpen = AddonInteraction.IsOpen();

        switch (this.autoRepriceStage)
        {
            case 0:
            {
                var slot = RetainerSellSlots.FindSlot(change.ItemId, change.Hq);

                if (slot < 0)
                {
                    // A just-opened sell list needs a moment before its rows are readable.
                    if (now < this.autoRepriceDeadline)
                    {
                        this.autoRepriceNext = now.AddMilliseconds(200);
                        return;
                    }

                    Log.Information("[SlotYield] Reprice: {Item}{Hq} not in the open list ({Rows} rows).", name, hq, RetainerFlow.SellListCount());
                    this.Print($"skip {name}{hq}: not in the open retainer list ({RetainerFlow.SellListCount()} rows).");
                    this.autoRepriceFailed++;
                    this.NextRepriceStep(now);
                    return;
                }

                AddonDump.TryOpenSellDialog(slot);
                this.autoRepriceDeadline = now.AddSeconds(3);
                this.autoRepriceStage = 1;
                this.autoRepriceNext = now.AddMilliseconds(500);
                return;
            }

            case 1:
            {
                if (dialogOpen)
                {
                    this.autoRepriceStage = 2;
                    this.autoRepriceNext = now.AddMilliseconds(250);
                    return;
                }

                if (now >= this.autoRepriceDeadline)
                {
                    this.Print($"skip {name}{hq}: the sell dialog did not open.");
                    this.autoRepriceFailed++;
                    this.NextRepriceStep(now);
                }

                return;
            }

            case 2:
            {
                if (!AddonInteraction.TrySetPrice((int)change.TargetPrice, out var actual, out var message))
                {
                    this.Print($"skip {name}{hq}: {message}.");
                    this.autoRepriceFailed++;
                    this.NextRepriceStep(now);
                    return;
                }

                Log.Information(
                    "[SlotYield] Reprice {Item}{Hq}: price set to {Price} (component read {Actual}).",
                    name,
                    hq,
                    change.TargetPrice,
                    actual);

                this.autoRepriceDeadline = now.AddSeconds(3);
                this.autoRepriceStage = 3;
                this.autoRepriceNext = now.AddMilliseconds(350);
                return;
            }

            case 3:
            {
                var result = AddonInteraction.Confirm();
                Log.Information(
                    "[SlotYield] Reprice {Item}{Hq} to {Price} confirmed: {Result}.",
                    name,
                    hq,
                    change.TargetPrice,
                    result);

                var listing = this.Configuration.MyListings
                    .FirstOrDefault(item => item.ItemId == change.ItemId && item.Hq == change.Hq);

                if (listing is not null)
                {
                    ListingBook.Upsert(
                        this.Configuration.MyListings,
                        listing with { PricePerUnit = (uint)change.TargetPrice, SeenUtc = now, ListingId = 0 });

                    this.SaveConfig();
                }

                this.autoRepriceApplied++;
                this.autoRepriceStage = 4;
                this.autoRepriceNext = now.AddMilliseconds(600);
                return;
            }

            case 4:
            {
                if (!dialogOpen)
                {
                    this.NextRepriceStep(now);
                    return;
                }

                if (now >= this.autoRepriceDeadline)
                {
                    this.Print($"stopped: the dialog for {name}{hq} stayed open. Close it and run /slotyield reprice again.");
                    this.autoRepriceRunning = false;
                }

                return;
            }
        }
    }

    private void NextRepriceStep(DateTime now)
    {
        this.autoRepriceIndex++;
        this.autoRepriceStage = 0;
        this.autoRepriceNext = now.AddMilliseconds(400);
        this.autoRepriceDeadline = now.AddSeconds(5);
    }


    private MarketQuote? FindCraftQuote(uint itemId, Quality quality)
    {
        if (State.Result is { } result)
        {
            foreach (var candidate in result.Candidates)
            {
                if (candidate.ItemId == itemId && candidate.Quality == quality)
                    return candidate.Quote;
            }
        }

        return this.FindListingQuote(itemId, quality);
    }

    private void ComputeCraftPlan()
    {
        if (this.craftStock is not { Count: > 0 } stock)
        {
            this.CraftPlan = Array.Empty<CraftOpportunity>();
            return;
        }

        var vendor = this.VendorPrices;

        MarketQuote? QuoteFor(uint itemId, Quality quality) => this.FindCraftQuote(itemId, quality);

        decimal? MaterialPrice(uint itemId)
        {
            var quote = this.FindCraftQuote(itemId, Quality.Nq);
            if (quote is { MinListing: > 0 })
                return quote.MinListing;

            return vendor.TryGetValue(itemId, out var price) ? price : null;
        }

        var parameters = new SlotParameters
        {
            Strategy = Configuration.Strategy,
            TaxRate = Configuration.TaxRate,
            StackSize = Configuration.DefaultStackSize,
            DeadAfterDays = Configuration.DeadAfterDays,
            MinimumVelocity = Configuration.MinimumVelocity,
        };

        this.CraftPlan = CraftPlanner.Plan(this.Recipes.All, stock, QuoteFor, MaterialPrice, parameters);
        this.CraftPlanUtc = DateTime.UtcNow;
    }

    private void RequestCraftQuotes(IReadOnlyList<uint> itemIds)
    {
        if (this.craftQuotesInFlight)
            return;

        var world = this.ResolveWorld();
        if (string.IsNullOrWhiteSpace(world))
            return;

        var wanted = itemIds.Distinct().Take(60).ToList();
        if (wanted.Count == 0)
            return;

        this.craftQuotesInFlight = true;

        _ = Task.Run(async () =>
        {
            try
            {
                for (var offset = 0; offset < wanted.Count; offset += 20)
                {
                    var batch = wanted.Skip(offset).Take(20).ToList();

                    var response = await this.Client
                        .GetQuotesAsync(world, batch, 60, 120, CancellationToken.None)
                        .ConfigureAwait(false);

                    if (response?.Items is null)
                        continue;

                    var quotes = new List<MarketQuote>();

                    foreach (var entry in response.Items.Values)
                    {
                        var nq = QuoteFactory.Build(entry, Quality.Nq, DateTime.UtcNow);
                        if (nq is not null)
                            quotes.Add(nq);

                        var hq = QuoteFactory.Build(entry, Quality.Hq, DateTime.UtcNow);
                        if (hq is not null)
                            quotes.Add(hq);
                    }

                    this.MergeListingQuotes(quotes);
                }

                if (this.disposed)
                    return;

                await Framework.RunOnFrameworkThread(this.ComputeCraftPlan);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[SlotYield] Craft price fetch failed.");
            }
            finally
            {
                this.craftQuotesInFlight = false;
            }
        });
    }

    /// <summary>Records a listing that vanished between checks, as a probable sale.</summary>
    public void RecordSale(CapturedListing listing)
    {
        this.Configuration.SoldLog.Add(new SoldEntry(
            listing.ItemId,
            listing.Hq,
            listing.PricePerUnit,
            (uint)Math.Max(1, listing.Quantity),
            listing.RetainerId,
            DateTime.UtcNow));

        this.Configuration.SoldLog.RemoveAll(entry => entry.At < DateTime.UtcNow.AddDays(-14));
    }

    public (int Count, decimal Total) SoldSince(DateTime since)
    {
        var count = 0;
        var total = 0m;

        foreach (var entry in this.Configuration.SoldLog)
        {
            if (entry.At < since)
                continue;

            count++;
            total += entry.Total;
        }

        return (count, total);
    }

    public WorthSnapshot? WorthSnapshot { get; private set; }

    public DateTime WorthUpdatedAt { get; private set; }

    public bool WorthRefreshing { get; private set; }

    /// <summary>Shows or hides the net worth window, measuring once if it has never run.</summary>
    public void ToggleTicker()
    {
        this.tickerWindow.IsOpen = !this.tickerWindow.IsOpen;
    }

    public void OpenHistory(uint itemId, Quality quality) => this.historyWindow.Show(itemId, quality);

    public void ToggleHelp()
    {
        this.helpWindow.IsOpen = !this.helpWindow.IsOpen;
    }

    public void ToggleCompare()
    {
        this.compareWindow.IsOpen = !this.compareWindow.IsOpen;
    }

    public void ToggleWorth()
    {
        this.worthWindow.Toggle();

        if (this.worthWindow.IsOpen && this.WorthSnapshot is null)
            this.RefreshWorth();
    }

    private DateTime nextWorthRefreshUtc;

    /// <summary>Re-reads the bags and prices them, then updates the daily history point.</summary>
    public void RefreshWorth()
    {
        if (this.WorthRefreshing)
            return;

        var world = this.ResolveWorld();
        if (string.IsNullOrWhiteSpace(world))
            return;

        this.WorthRefreshing = true;

        var mode = this.Configuration.WorthMode;
        var stock = PlayerInventory.Read();
        var gil = PlayerInventory.Gil();
        var items = stock.Items.Select(pair => (pair.Key, pair.Value)).ToList();

        _ = Task.Run(async () =>
        {
            var prices = new Dictionary<uint, decimal>();

            try
            {
                for (var offset = 0; offset < items.Count; offset += 100)
                {
                    var batch = items.Skip(offset).Take(100).Select(item => item.Key).ToList();
                    var response = await this.Client
                        .GetAggregatedAsync(world, batch, CancellationToken.None)
                        .ConfigureAwait(false);

                    if (response?.Results is null)
                        continue;

                    foreach (var entry in response.Results)
                    {
                        var price = mode == WorthValuation.AverageSale
                            ? entry.Nq?.AverageSalePrice?.World?.Price
                              ?? entry.Nq?.AverageSalePrice?.Dc?.Price
                              ?? entry.Nq?.MinListing?.World?.Price
                            : entry.Nq?.MinListing?.World?.Price
                              ?? entry.Nq?.MinListing?.Dc?.Price
                              ?? entry.Nq?.AverageSalePrice?.World?.Price;

                        if (price is > 0)
                            prices[entry.ItemId] = price.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[SlotYield] Net worth price fetch failed.");
            }

            if (this.disposed)
                return;

            await Framework.RunOnFrameworkThread(() =>
            {
                var snapshot = NetWorth.Compute(
                    items,
                    gil,
                    itemId => prices.TryGetValue(itemId, out var price) ? price : null,
                    itemId => this.Catalog.Get(itemId).Category,
                    DateTime.UtcNow,
                    mode);

                this.WorthSnapshot = snapshot;
                this.WorthUpdatedAt = DateTime.UtcNow;
                this.WorthRefreshing = false;
                this.nextWorthRefreshUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, this.Configuration.WorthRefreshMinutes));

                var today = DateTime.UtcNow.Date;
                var index = this.Configuration.WorthHistory.FindIndex(point => point.Day == today);
                var newPoint = new WorthPoint(today, snapshot.Total);

                if (index >= 0)
                    this.Configuration.WorthHistory[index] = newPoint;
                else
                    this.Configuration.WorthHistory.Add(newPoint);

                if (this.Configuration.WorthHistory.Count > 120)
                    this.Configuration.WorthHistory.RemoveRange(0, this.Configuration.WorthHistory.Count - 120);

                this.SaveConfig();

                Log.Information(
                    "[SlotYield] Net worth {Total} ({Stacks} stacks, {Unpriced} unpriced, gil {Gil}).",
                    snapshot.Total,
                    snapshot.Stacks,
                    snapshot.UnpricedItems,
                    snapshot.Gil);
            });
        });
    }

    public bool IsPricingListings
    {
        get
        {
            lock (this.listingQuoteLock)
                return this.listingQuotesInFlight.Count > 0;
        }
    }

    public MarketQuote? FindListingQuote(uint itemId, Quality quality)
    {
        lock (this.listingQuoteLock)
            return this.listingQuotes.GetValueOrDefault((itemId, quality));
    }

    public void MergeListingQuotes(IEnumerable<MarketQuote> quotes)
    {
        lock (this.listingQuoteLock)
        {
            foreach (var quote in quotes)
                this.listingQuotes[(quote.ItemId, quote.Quality)] = quote;
        }
    }

    /// <summary>
    /// Prices your captured listings straight away instead of waiting for a full scan: one quote
    /// request per twenty items, skipped for anything already priced or in flight.
    /// </summary>
    public void RequestListingQuotes(IEnumerable<uint> itemIds)
    {
        var world = this.ResolveWorld();
        if (string.IsNullOrWhiteSpace(world))
            return;

        if (DateTime.UtcNow < this.nextListingPriceUtc)
            return;

        var wanted = new List<uint>();

        lock (this.listingQuoteLock)
        {
            foreach (var id in itemIds)
            {
                if (this.listingQuotes.ContainsKey((id, Quality.Nq)) ||
                    this.listingQuotes.ContainsKey((id, Quality.Hq)))
                {
                    continue;
                }

                if (!this.listingQuotesInFlight.Add(id))
                    continue;

                wanted.Add(id);

                if (wanted.Count >= 40)
                    break;
            }
        }

        if (wanted.Count == 0)
            return;

        this.nextListingPriceUtc = DateTime.UtcNow.AddSeconds(10);

        _ = Task.Run(async () =>
        {
            try
            {
                var response = await this.Client
                    .GetQuotesAsync(world, wanted, 60, 120, CancellationToken.None)
                    .ConfigureAwait(false);

                var quotes = new List<MarketQuote>();

                if (response?.Items is not null)
                {
                    var fetchedAt = DateTime.UtcNow;

                    foreach (var entry in response.Items.Values)
                    {
                        var nq = QuoteFactory.Build(entry, Quality.Nq, fetchedAt);
                        if (nq is not null)
                            quotes.Add(nq);

                        var hq = QuoteFactory.Build(entry, Quality.Hq, fetchedAt);
                        if (hq is not null)
                            quotes.Add(hq);
                    }
                }

                this.MergeListingQuotes(quotes);

                if (quotes.Count > 0)
                    Log.Information("[SlotYield] Priced your listings: {Quotes} quote(s) for {Items} item(s).", quotes.Count, wanted.Count);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[SlotYield] Listing price fetch failed.");
            }
            finally
            {
                lock (this.listingQuoteLock)
                {
                    foreach (var id in wanted)
                        this.listingQuotesInFlight.Remove(id);
                }
            }
        });
    }

    /// <summary>
    /// Polls quotes for your pinned items and your listings, then says something in chat when a
    /// listing gets undercut or a price falls hard. Cooldowns stop one collapsing market from
    /// filling the chat log.
    /// </summary>
    public void RunAlertPoll(bool manual = false)
    {
        if (this.disposed || this.alertInFlight || (!this.Configuration.AlertsEnabled && !manual))
            return;

        if (!manual && DateTime.UtcNow < this.nextAlertPollUtc)
            return;

        var interval = TimeSpan.FromMinutes(Math.Max(1, this.Configuration.AlertIntervalMinutes));

        var world = this.ResolveWorld();
        if (string.IsNullOrWhiteSpace(world))
        {
            // Not logged in yet: try again in a minute rather than sleeping a whole interval.
            this.nextAlertPollUtc = DateTime.UtcNow.AddMinutes(1);
            return;
        }

        this.nextAlertPollUtc = DateTime.UtcNow + interval;

        var ids = new List<uint>();

        foreach (var pin in this.Configuration.Pinned)
            ids.Add(pin.ItemId);

        foreach (var listing in this.Configuration.MyListings)
            ids.Add(listing.ItemId);

        var distinct = ids.Distinct().Take(60).ToList();

        if (distinct.Count == 0)
        {
            if (manual)
                this.Print("alerts: nothing to watch yet - pin an item or open a retainer listing.");

            return;
        }

        // Snapshot on the framework thread: capture may add rows while the poll runs.
        var listings = this.Configuration.MyListings.ToList();
        var undercutPercent = this.Configuration.AlertUndercutPercent;
        var dropPercent = this.Configuration.AlertPriceDropPercent;
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, this.Configuration.AlertCooldownMinutes));

        this.alertInFlight = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var response = await this.Client
                    .GetQuotesAsync(world, distinct, 60, 120, CancellationToken.None)
                    .ConfigureAwait(false);

                var now = DateTime.UtcNow;
                var fresh = new Dictionary<(uint ItemId, Quality Quality), MarketQuote>();

                if (response?.Items is not null)
                {
                    foreach (var entry in response.Items.Values)
                    {
                        var nq = QuoteFactory.Build(entry, Quality.Nq, now);
                        if (nq is not null)
                            fresh[(nq.ItemId, Quality.Nq)] = nq;

                        var hq = QuoteFactory.Build(entry, Quality.Hq, now);
                        if (hq is not null)
                            fresh[(hq.ItemId, Quality.Hq)] = hq;
                    }
                }

                var alerts = new List<MarketAlert>();

                foreach (var listing in listings)
                {
                    var quality = listing.Hq ? Quality.Hq : Quality.Nq;

                    if (!fresh.TryGetValue((listing.ItemId, quality), out var quote))
                        continue;

                    if (AlertRules.Undercut(listing, quote, undercutPercent, now) is { } alert)
                        alerts.Add(alert);
                }

                foreach (var pair in fresh)
                {
                    if (!this.lastAlertQuotes.TryGetValue(pair.Key, out var previous))
                        continue;

                    if (AlertRules.PriceDrop(pair.Key.ItemId, pair.Key.Quality, previous, pair.Value, dropPercent, now) is { } alert)
                        alerts.Add(alert);
                }

                this.lastAlertQuotes.Clear();
                foreach (var pair in fresh)
                    this.lastAlertQuotes[pair.Key] = pair.Value;

                var found = alerts.Count;

                if (this.disposed)
                    return;

                // Cooldowns live in the config, so they survive a plugin reload. All of that state is
                // touched on the framework thread only.
                await Framework.RunOnFrameworkThread(() =>
                {
                    var shown = new List<MarketAlert>();

                    foreach (var alert in alerts)
                    {
                        var last = this.FindAlertCooldown(alert.ItemId, alert.Quality, alert.Kind);
                        if (!AlertRules.CooldownElapsed(last, now, cooldown))
                            continue;

                        this.RecordAlertCooldown(alert.ItemId, alert.Quality, alert.Kind, now);

                        var hq = alert.Quality == Quality.Hq ? " (HQ)" : string.Empty;
                        ChatGui.Print($"[Slot Yield] {this.Catalog.Name(alert.ItemId)}{hq} {alert.Message}");
                        shown.Add(alert);
                    }

                    if (shown.Count > 0)
                        this.SaveConfig();

                    if (this.Configuration.AlertToasts && shown.Count > 0)
                    {
                        var body = new System.Text.StringBuilder();

                        foreach (var alert in shown.Take(2))
                        {
                            var hq = alert.Quality == Quality.Hq ? " (HQ)" : string.Empty;
                            body.AppendLine($"{this.Catalog.Name(alert.ItemId)}{hq} {alert.Message}");
                        }

                        if (shown.Count > 2)
                            body.Append($"and {shown.Count - 2} more");

                        Notifications.AddNotification(new Notification
                        {
                            Title = "Slot Yield",
                            Content = body.ToString().TrimEnd(),
                            Type = NotificationType.Warning,
                            MinimizedText = $"{shown.Count} market alert(s)",
                        });
                    }

                    if (manual && shown.Count == 0)
                    {
                        ChatGui.Print(
                            $"[Slot Yield] No new alerts: {fresh.Count} quote(s) checked, {found} suppressed by cooldown.");
                    }

                    Log.Information(
                        "[SlotYield] Alert poll: {Quotes} quotes, {Alerts} alert(s), {OnCooldown} on cooldown.",
                        fresh.Count,
                        shown.Count,
                        found - shown.Count);
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[SlotYield] Alert poll failed.");
            }
            finally
            {
                this.alertInFlight = false;
            }
        });
    }

    /// <summary>
    /// After a scan, says something when a pinned item has climbed above your profit floor. The
    /// first scan after a reload only records the baseline, so a healthy pin does not shout.
    /// </summary>
    private void CheckProfitFloor(ScanResult result)
    {
        var floor = this.Configuration.AlertMinProfitPerDay;
        var now = DateTime.UtcNow;
        var current = new Dictionary<(uint ItemId, Quality Quality), decimal>();

        foreach (var pin in this.Configuration.Pinned)
        {
            var quality = pin.Hq ? Quality.Hq : Quality.Nq;

            foreach (var candidate in result.Candidates)
            {
                if (candidate.ItemId != pin.ItemId || candidate.Quality != quality)
                    continue;

                current[(pin.ItemId, quality)] = candidate.Score.ProfitPerDay;
                break;
            }
        }

        if (current.Count == 0)
            return;

        if (!this.pinnedProfitPrimed)
        {
            this.pinnedProfitPrimed = true;
            foreach (var pair in current)
                this.pinnedProfit[pair.Key] = pair.Value;

            return;
        }

        var cooldown = TimeSpan.FromMinutes(Math.Max(1, this.Configuration.AlertCooldownMinutes));
        var shown = 0;
        var toast = new System.Text.StringBuilder();

        foreach (var pair in current)
        {
            var previous = this.pinnedProfit.TryGetValue(pair.Key, out var earlier) ? earlier : (decimal?)null;
            var alert = AlertRules.ProfitFloor(pair.Key.ItemId, pair.Key.Quality, pair.Value, previous, floor, now);

            if (alert is null)
                continue;

            var last = this.FindAlertCooldown(alert.ItemId, alert.Quality, alert.Kind);
            if (!AlertRules.CooldownElapsed(last, now, cooldown))
                continue;

            this.RecordAlertCooldown(alert.ItemId, alert.Quality, alert.Kind, now);

            var hq = alert.Quality == Quality.Hq ? " (HQ)" : string.Empty;
            var line = $"{this.Catalog.Name(alert.ItemId)}{hq} {alert.Message}";

            ChatGui.Print($"[Slot Yield] {line}");

            if (toast.Length < 300)
                toast.AppendLine(line);

            shown++;
        }

        this.pinnedProfit.Clear();
        foreach (var pair in current)
            this.pinnedProfit[pair.Key] = pair.Value;

        if (shown == 0)
            return;

        this.SaveConfig();

        if (this.Configuration.AlertToasts)
        {
            Notifications.AddNotification(new Notification
            {
                Title = "Slot Yield",
                Content = toast.ToString().TrimEnd(),
                Type = NotificationType.Info,
                MinimizedText = $"{shown} pinned item(s) above your floor",
            });
        }
    }

    private DateTime? FindAlertCooldown(uint itemId, Quality quality, AlertKind kind)
    {
        foreach (var entry in this.Configuration.AlertCooldowns)
        {
            if (entry.ItemId == itemId && entry.Quality == quality && entry.Kind == kind)
                return entry.RaisedUtc;
        }

        return null;
    }

    private void RecordAlertCooldown(uint itemId, Quality quality, AlertKind kind, DateTime now)
    {
        var entries = this.Configuration.AlertCooldowns;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.ItemId != itemId || entry.Quality != quality || entry.Kind != kind)
                continue;

            entries[i] = entry with { RaisedUtc = now };
            return;
        }

        entries.Add(new AlertCooldownEntry(itemId, quality, kind, now));
    }

    /// <summary>Marks captured listings dirty; the actual write is debounced to one per two seconds.</summary>
    public void MarkListingsDirty()
    {
        this.listingsDirty = true;
        this.nextListingSaveUtc = DateTime.UtcNow.AddSeconds(2);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            this.OnFrameworkUpdateCore(framework);
        }
        catch (Exception exception)
        {
            if (!this.frameworkFaultLogged)
            {
                this.frameworkFaultLogged = true;
                Log.Error(exception, "[SlotYield] Framework update failed.");
            }
        }
    }

    private void OnFrameworkUpdateCore(IFramework framework)
    {
        var now = DateTime.UtcNow;

        this.UpdatePendingWorldWalk();

        this.UpdatePendingCityTravel();

        this.StepBuy();

        this.StepBuyRun();

        if (this.Configuration.AutoScanHours > 0 && !this.State.IsScanning && PlayerState.IsLoaded)
        {
            var lastScan = this.Configuration.LastScanUtc ?? DateTime.MinValue;
            if (now - lastScan >= TimeSpan.FromHours(this.Configuration.AutoScanHours))
                this.StartScan();
        }

        if (now >= this.nextSellListPollUtc)
        {
            this.nextSellListPollUtc = now.AddMilliseconds(700);
            this.capture.PollRetainerSellList();
        }

        if (this.autoRepriceRunning)
            this.StepAutomaticReprice(now);

        if (this.listRunning)
            this.StepListing(now);

        if (this.sweepRunning)
            this.StepSweepCompare(now);

        if (this.worthWindow.IsOpen && !this.WorthRefreshing && now >= this.nextWorthRefreshUtc)
        {
            this.nextWorthRefreshUtc = now.AddMinutes(Math.Max(1, this.Configuration.WorthRefreshMinutes));
            this.RefreshWorth();
        }

        if (this.sellDialogPriceAt != default && now >= this.sellDialogPriceAt)
        {
            this.sellDialogPriceAt = default;
            this.ApplySellDialogPrice();
        }

        if (this.pendingPrice > 0 && now >= this.pendingPriceAt)
        {
            this.Print(AddonInteraction.SetPrice(this.pendingPrice));
            this.pendingPrice = 0;
        }

        if (this.probeSlot >= 0 && now >= this.probeDumpAt)
        {
            var probePath = AddonDump.Write(PluginInterface.GetPluginConfigDirectory());
            Log.Information("[SlotYield] Probe dump written to {Path}", probePath);
            this.Print($"probe dump written to {probePath}");
            this.probeSlot = -1;
        }

        if (this.Configuration.AlertsEnabled && now >= this.nextAlertPollUtc)
            this.RunAlertPoll();

        if (!this.listingsDirty || DateTime.UtcNow < this.nextListingSaveUtc)
            return;

        this.listingsDirty = false;
        this.SaveConfig();
    }

    public void OpenMain()
    {
        this.mainWindow.IsOpen = true;
        this.mainWindow.BringToFront();
    }

    public string CurrentWorldName() => WorldDirectory.CurrentWorld(PlayerState);

    public string ResolveWorld()
    {
        var overrideWorld = Configuration.WorldOverride?.Trim();
        if (!string.IsNullOrEmpty(overrideWorld))
            return overrideWorld;

        return this.CurrentWorldName();
    }

    /// <summary>Every world in the player's data center, current world first.</summary>
    public IReadOnlyList<string> DataCenterWorlds()
    {
        var current = this.ResolveWorld();
        var worlds = new List<string> { current };

        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
            var currentRow = sheet.FirstOrDefault(world => world.Name.ToString().Equals(current, StringComparison.OrdinalIgnoreCase));
            if (currentRow.RowId != 0)
            {
                var dataCenter = currentRow.DataCenter.RowId;
                foreach (var world in sheet)
                {
                    if (world.DataCenter.RowId != dataCenter)
                        continue;

                    var name = world.Name.ToString();
                    if (!string.IsNullOrEmpty(name) && !name.Equals(current, StringComparison.OrdinalIgnoreCase))
                        worlds.Add(name);
                }
            }
        }
        catch
        {
            // fall back to the current world only
        }

        return worlds;
    }

    /// <summary>The worlds arbitrage compares: an explicit list, or the data center up to the configured count.</summary>
    public IReadOnlyList<string> ResolveArbitrageWorlds()
    {
        if (this.Configuration.ArbitrageWorlds.Count > 0)
            return this.Configuration.ArbitrageWorlds;

        var count = Math.Clamp(this.Configuration.ArbitrageWorldCount, 2, 16);
        return this.DataCenterWorlds().Take(count).ToList();
    }

    public void StartArbitrage()
    {
        if (this.disposed || this.arbitrageRunning || this.arbitrageService is null)
            return;

        if (State.Result is not { } result)
        {
            State.Error = "Scan first - arbitrage compares the scan's shortlist across worlds.";
            this.OpenMain();
            return;
        }

        var worlds = this.ResolveArbitrageWorlds();
        if (worlds.Count < 2)
        {
            State.Error = "Arbitrage needs at least two worlds.";
            this.OpenMain();
            return;
        }

        this.arbitrageRunning = true;
        this.arbitrageDone = 0;
        this.arbitrageTotal = worlds.Count;
        var cancellation = new CancellationTokenSource();
        this.arbitrageCancellation = cancellation;
        var token = cancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var report = await this.arbitrageService.ComputeAsync(
                    result,
                    worlds,
                    this.Configuration.ArbitrageTopItems,
                    this.Configuration.TaxRate,
                    Math.Clamp(this.Configuration.ArbitrageMinVelocity, 0d, 20d),
                    this.Configuration.ArbitrageSellBasis == 1,
                    this.Configuration.ArbitrageSellHomeOnly,
                    (done, total) =>
                    {
                        this.arbitrageDone = done;
                        this.arbitrageTotal = total;
                    },
                    token).ConfigureAwait(false);

                this.arbitrageReport = report;
                this.arbitrageHistory.Add(new ArbitragePulse(
                    report.ComputedUtc,
                    report.Rows.Count,
                    (long)report.Rows.Sum(row => row.NetPerUnit * this.Configuration.DefaultStackSize)));

                if (this.arbitrageHistory.Count > 30)
                    this.arbitrageHistory.RemoveAt(0);

                foreach (var watched in this.Configuration.Watchlist)
                {
                    var row = report.Rows.FirstOrDefault(candidate =>
                        candidate.ItemId == watched.ItemId &&
                        candidate.Quality == (watched.Hq ? Quality.Hq : Quality.Nq));

                    if (row is null)
                        continue;

                    Notifications.AddNotification(new Notification
                    {
                        Title = "Slot Yield - arbitrage",
                        Content = $"{this.Catalog.Name(row.ItemId)}: {row.BuyWorld} {Money.Gil(row.BuyPrice)} -> {row.SellWorld} {Money.Gil(row.SellPrice)} (+{Money.Gil(row.NetPerUnit)})",
                        Type = NotificationType.Info,
                    });
                }
                Log.Information(
                    "[SlotYield] Arbitrage: {Rows} rows across {Worlds} worlds ({List}), {Requests} requests total",
                    report.Rows.Count,
                    report.Worlds.Count,
                    string.Join(", ", report.Worlds),
                    this.Client.RequestCount);

                if (report.Warnings.Count > 0)
                    Log.Warning("[SlotYield] Arbitrage warnings: {Warnings}", string.Join("; ", report.Warnings));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Error(exception, "[SlotYield] Arbitrage failed.");
                State.Error = $"Arbitrage failed: {exception.Message}";
            }
            finally
            {
                this.arbitrageRunning = false;
                if (ReferenceEquals(this.arbitrageCancellation, cancellation))
                    this.arbitrageCancellation = null;

                cancellation.Dispose();
            }
        });
    }

    public void StartScan()
    {
        if (this.disposed || State.IsScanning)
            return;

        var world = this.ResolveWorld();
        if (string.IsNullOrWhiteSpace(world))
        {
            State.Error = "Pick a world on the Scan tab first.";
            this.OpenMain();
            return;
        }

        var model = new SlotParameters
        {
            Strategy = Configuration.Strategy,
            TaxRate = Configuration.TaxRate,
            StackSize = Configuration.DefaultStackSize,
            DeadAfterDays = Configuration.DeadAfterDays,
            MinimumVelocity = Configuration.MinimumVelocity,
        };

        var options = new ScanOptions
        {
            World = world,
            Model = model,
            ShortlistSize = Configuration.ShortlistSize,
            BatchSize = Configuration.BatchSize,
            MaxConcurrency = Configuration.MaxConcurrency,
            CacheMaxAge = TimeSpan.FromHours(Configuration.CacheHours),
            UseCache = true,
            ExtraItemIds = Configuration.Pinned.Select(p => p.ItemId)
                .Concat(Configuration.MyListings.Select(l => l.ItemId))
                .Distinct()
                .ToList(),
            CostMode = Configuration.CostMode,
        };

        var cancellation = new CancellationTokenSource();
        State.Cancellation = cancellation;
        State.IsScanning = true;
        State.Error = string.Empty;
        State.Phase = "starting";
        State.Done = 0;
        State.Total = 1;
        State.Detail = world;

        Log.Information("[SlotYield] Scanning {World}...", world);

        var progress = new Progress<ScanProgress>(p =>
        {
            State.Phase = p.Phase;
            State.Done = p.Done;
            State.Total = p.Total;
            State.Detail = p.Detail;
        });

        // Game sheets are read here, on the framework thread; the scan itself runs off-thread.
        // This is the one place that can stall the game for a moment, so it is timed and logged.
        var sheetTimer = System.Diagnostics.Stopwatch.StartNew();
        var recipes = this.Recipes;
        var vendorPrices = this.VendorPrices;
        sheetTimer.Stop();

        if (sheetTimer.ElapsedMilliseconds >= 15)
        {
            Log.Information(
                "[SlotYield] Built game tables in {Ms} ms ({Recipes} recipes, {Vendors} vendor prices)",
                sheetTimer.ElapsedMilliseconds,
                recipes.Count,
                vendorPrices.Count);
        }

        Task.Run(async () =>
        {
            try
            {
                var scanner = new MarketScanner(
                    Client,
                    Cache,
                    recipes,
                    id => vendorPrices.TryGetValue(id, out var price) ? price : null);
                var result = await scanner.ScanAsync(options, progress, cancellation.Token).ConfigureAwait(false);

                var liveCount = result.Candidates.Count(c => !c.Score.IsDead && !c.Score.IsSuspect);
                var profitableCount = result.Candidates.Count(c => !c.Score.IsDead && !c.Score.IsSuspect && c.Score.ProfitPerDay > 0m);

                Log.Information(
                    "[SlotYield] Scan {World}: {Priced} priced, {Live} live, {Profitable} profitable, " +
                    "cost {Costed}/{Priced} (craft {Craft}, market {Market}, {Partial} partial), {Requests} requests, {Seconds:0.0}s, " +
                    "aggregates {AggCache}, quotes {QuoteCache}",
                    result.World,
                    result.Stats.PricedItems,
                    liveCount,
                    profitableCount,
                    result.Stats.CostedItems,
                    result.Stats.PricedItems,
                    result.Stats.CraftCostItems,
                    result.Stats.MarketCostItems,
                    result.Stats.PartialCostItems,
                    result.Stats.Requests,
                    result.Stats.Duration.TotalSeconds,
                    result.Stats.AggregatesFromCache ? "cached" : "fresh",
                    result.Stats.QuotesFromCache ? "cached" : "fresh");

                foreach (var warning in result.Warnings)
                    Log.Warning("[SlotYield] {Warning}", warning);

                this.UpdateSnapshot(result);
                State.Result = result;
                this.MergeListingQuotes(result.Candidates.Select(candidate => candidate.Quote));

                // Chat and config are game-side; hand them back to the framework thread.
                if (this.disposed)
                    return;

                await Framework.RunOnFrameworkThread(() =>
                {
                    Configuration.LastScanUtc = result.ScannedUtc;
                    Configuration.LastWorld = world;
                    Configuration.ScanHistory.Add(new ScanPulse
                    {
                        Utc = result.ScannedUtc,
                        GilPerDay = (long)result.Candidates.Where(c => !c.Score.IsDead && !c.Score.IsSuspect).Sum(c => c.Score.ProfitPerDay),
                    });
                    if (Configuration.ScanHistory.Count > 30)
                        Configuration.ScanHistory.RemoveAt(0);
                    this.SaveConfig();

                    ChatGui.Print(
                        $"[Slot Yield] {liveCount} live / {profitableCount} profitable on {world}, " +
                        $"{result.Stats.Requests} requests in {result.Stats.Duration.TotalSeconds:0}s.");

                    if (Configuration.CostMode != CostMode.Revenue && this.Recipes.Count == 0)
                        ChatGui.PrintError("[Slot Yield] No recipe data was loaded; costs fall back to market prices.");

                    this.CheckProfitFloor(result);

                    if (this.dtrEntry is { } dtr)
                    {
                        ApplyDtr(dtr, result);
                        dtr.Shown = Configuration.DtrBar;
                    }

                    if (Configuration.ToastThreshold > 0)
                    {
                        var bestToast = result.Candidates
                            .Where(c => !c.Score.IsDead && !c.Score.IsSuspect)
                            .Select(c => c.Score.ProfitPerDay)
                            .DefaultIfEmpty(0m)
                            .Max();

                        if (bestToast >= Configuration.ToastThreshold)
                            Notifications.AddNotification(new Notification
                            {
                                Title = "Slot Yield",
                                Content = $"Best pick now {Money.Gil(bestToast)}/day",
                                Type = NotificationType.Success,
                            });
                    }

                    if (Configuration.ArbitrageEnabled)
                        this.StartArbitrage();

                    foreach (var watched in Configuration.Watchlist)
                    {
                        var watchedCandidate = result.Candidates.FirstOrDefault(c => c.ItemId == watched.ItemId && c.Quality == (watched.Hq ? Quality.Hq : Quality.Nq));
                        if (watchedCandidate is null || watchedCandidate.Score.ProfitPerDay < Math.Max(1m, Configuration.ToastThreshold))
                            continue;

                        Notifications.AddNotification(new Notification
                        {
                            Title = "Slot Yield - watched",
                            Content = $"{this.Catalog.Name(watched.ItemId)}: {Money.Gil(watchedCandidate.Score.ProfitPerDay)}/day",
                            Type = NotificationType.Info,
                        });
                    }
                });
            }
            catch (OperationCanceledException)
            {
                if (!this.disposed)
                    await Framework.RunOnFrameworkThread(() => ChatGui.Print("[Slot Yield] Scan cancelled."));
            }
            catch (Exception ex)
            {
                State.Error = ex.Message;
                Log.Error(ex, "[SlotYield] Scan failed.");

                if (!this.disposed)
                    await Framework.RunOnFrameworkThread(() => ChatGui.PrintError($"[Slot Yield] Scan failed: {ex.Message}"));
            }
            finally
            {
                State.IsScanning = false;
                cancellation.Dispose();
            }
        });
    }

    /// <summary>
    /// Saves a compact picture of this scan and diffs it against the previous one for the same world.
    /// Runs on the scan thread: file IO only, no game state.
    /// </summary>
    private void UpdateSnapshot(ScanResult result)
    {
        try
        {
            var ranked = result.Candidates
                .Where(c => !c.Score.IsDead && !c.Score.IsSuspect && c.Score.ProfitPerDay > 0m)
                .GroupBy(c => (c.ItemId, c.Quality))
                .Select(g => g.First())
                .Select(c => new SnapshotEntry(
                    c.ItemId,
                    c.Quality,
                    c.Score.ListPrice,
                    c.Score.ProfitPerDay,
                    c.Score.DaysToClear))
                .ToList();

            // Pinned slots are always tracked, even when they are outside the top 150 that day.
            var pinned = Configuration.Pinned
                .Select(p => (p.ItemId, p.Hq ? Quality.Hq : Quality.Nq))
                .ToHashSet();

            var entries = SnapshotBuilder.Select(ranked, pinned);

            if (entries.Count == 0)
            {
                State.Diff = null;
                return;
            }

            var snapshot = new ScanSnapshot(result.ScannedUtc, result.World, entries) { Mode = Configuration.CostMode };
            var key = $"snapshot-{result.World}-v2";

            if (Cache.TryLoad<ScanSnapshot>(key, TimeSpan.FromDays(60), out var previous)
                && previous is not null
                && previous.Mode == Configuration.CostMode)
                State.Diff = SnapshotDiff.Compare(previous, snapshot);
            else
                State.Diff = null;

            Cache.Save(key, snapshot);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[SlotYield] Could not update the scan snapshot.");
        }
    }

    public void CancelScan()
    {
        try
        {
            State.Cancellation?.Cancel();
        }
        catch
        {
            // Cancellation is best effort.
        }

        State.IsScanning = false;
    }

    /// <summary>
    /// Writes a self-contained report next to the plugin config. Useful when the log has rotated or
    /// when someone needs to send the state of a scan without screenshots.
    /// </summary>
    public void WriteDiagnostics()
    {
        try
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Slot Yield diagnostics");
            builder.AppendLine($"when: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            builder.AppendLine($"version: {typeof(Plugin).Assembly.GetName().Version}");
            builder.AppendLine($"world: {this.ResolveWorld()} (override: {(string.IsNullOrWhiteSpace(Configuration.WorldOverride) ? "none" : Configuration.WorldOverride)})");
            builder.AppendLine($"player loaded: {PlayerState.IsLoaded}");
            builder.AppendLine();

            builder.AppendLine("model");
            builder.AppendLine($"  strategy: {Configuration.Strategy}");
            builder.AppendLine($"  cost mode: {Configuration.CostMode}");
            builder.AppendLine($"  tax: {Configuration.TaxPercent:0.0}%");
            builder.AppendLine($"  stack: {Configuration.DefaultStackSize}");
            builder.AppendLine($"  dead after: {Configuration.DeadAfterDays}d, min velocity {Configuration.MinimumVelocity:0.0}/day");
            builder.AppendLine($"  shortlist: {Configuration.ShortlistSize}, cache {Configuration.CacheHours}h");
            builder.AppendLine($"  portfolio: {Configuration.PortfolioSlots} slots, {Money.Exact((decimal)Configuration.PortfolioBudget)} gil");
            builder.AppendLine($"  pinned: {Configuration.Pinned.Count}");
            builder.AppendLine($"  captured listings: {Configuration.MyListings.Count} ({Configuration.MyListings.Select(l => l.RetainerId).Distinct().Count()} retainer(s))");
            builder.AppendLine($"  recipes loaded: {this.Recipes.Count}");
            builder.AppendLine();

            builder.AppendLine("scan state");
            builder.AppendLine($"  running: {State.IsScanning}, phase {State.Phase} {State.Done}/{State.Total}");
            builder.AppendLine($"  error: {(string.IsNullOrEmpty(State.Error) ? "none" : State.Error)}");

            if (State.Result is { } result)
            {
                var live = result.Candidates.Count(c => !c.Score.IsDead && !c.Score.IsSuspect);
                var stale = result.Candidates.Count(c => c.Score.IsStale);

                builder.AppendLine($"  last scan: {result.ScannedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} on {result.World}");
                builder.AppendLine(
                    $"  stats: {result.Stats.MarketableItems} marketable, {result.Stats.AggregatedItems} seen, " +
                    $"{result.Stats.PricedItems} priced, {result.Stats.Requests} requests in {result.Stats.Duration.TotalSeconds:0.0}s");
                builder.AppendLine(
                    $"  cache: aggregates {(result.Stats.AggregatesFromCache ? "hit" : "miss")}, " +
                    $"quotes {(result.Stats.QuotesFromCache ? "hit" : "miss")}, failed batches {result.Stats.FailedBatches}");
                builder.AppendLine($"  candidates: {result.Candidates.Count} total, {live} live, {stale} stale");
                builder.AppendLine(
                    $"  cost: {result.Stats.CostedItems}/{result.Stats.PricedItems} priced, craft {result.Stats.CraftCostItems}, market {result.Stats.MarketCostItems}, {result.Stats.PartialCostItems} partial");

                foreach (var warning in result.Warnings)
                    builder.AppendLine($"  warning: {warning}");
            }
            else
            {
                builder.AppendLine("  last scan: none");
            }

            builder.AppendLine();
            builder.AppendLine(
                State.Diff is { } diff
                    ? $"diff: {diff.Drops.Count} drops, {diff.Rises.Count} rises, {diff.NewEntries.Count} new, {diff.Gone.Count} gone"
                    : "diff: none");

            builder.AppendLine();
            builder.AppendLine($"cache dir: {Cache.DirectoryPath}");

            try
            {
                foreach (var file in new DirectoryInfo(Cache.DirectoryPath)
                             .GetFiles("cache-*.json")
                             .OrderByDescending(f => f.LastWriteTimeUtc)
                             .Take(12))
                {
                    builder.AppendLine($"  {file.Name}  {file.Length:N0} bytes  {file.LastWriteTimeUtc:yyyy-MM-dd HH:mm}Z");
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine($"  (could not list cache: {ex.Message})");
            }

            var path = Path.Combine(
                PluginInterface.GetPluginConfigDirectory(),
                $"diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            File.WriteAllText(path, builder.ToString());

            Log.Information("[SlotYield] Wrote diagnostics to {Path}", path);
            ChatGui.Print($"[Slot Yield] Diagnostics written to {path}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SlotYield] Could not write diagnostics.");
            ChatGui.PrintError($"[Slot Yield] Diagnostics failed: {ex.Message}");
        }
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = (args ?? string.Empty).Trim();
        var split = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = split.Length > 0 ? split[0].ToLowerInvariant() : string.Empty;

        switch (verb)
        {
            case "":
                this.mainWindow.Toggle();
                break;

            case "scan":
                this.StartScan();
                this.OpenMain();
                break;

            case "world":
                if (split.Length < 2)
                {
                    ChatGui.Print($"[Slot Yield] World: {this.ResolveWorld()}");
                    break;
                }

                var wanted = split[1];
                var match = this.Worlds.FirstOrDefault(w => string.Equals(w, wanted, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    ChatGui.PrintError($"[Slot Yield] Unknown world '{wanted}'.");
                    break;
                }

                Configuration.WorldOverride = match;
                this.SaveConfig();
                ChatGui.Print($"[Slot Yield] World set to {match}.");
                break;

            case "pin":
            case "unpin":
                this.HandlePin(verb == "unpin", split);
                break;

            case "setprice":
                var priceTarget = split.Length > 1 && int.TryParse(split[1], out var parsedPrice) ? parsedPrice : 0;
                if (priceTarget <= 0)
                {
                    this.Print("usage: /slotyield setprice <gil>  (with the sell dialog open)");
                    break;
                }

                this.pendingPrice = priceTarget;
                this.pendingPriceAt = DateTime.UtcNow.AddMilliseconds(300);
                break;

            case "confirmprice":
                this.Print(AddonInteraction.Confirm());
                break;

            case "probe":
                var probeTarget = split.Length > 1 && int.TryParse(split[1], out var parsedSlot) ? parsedSlot : 0;
                this.Print(AddonDump.TryOpenSellDialog(probeTarget));
                this.probeSlot = probeTarget;
                this.probeDumpAt = DateTime.UtcNow.AddMilliseconds(800);
                break;

            case "dumpretainer":
                var dumpPath = AddonDump.Write(PluginInterface.GetPluginConfigDirectory());
                this.Print($"retainer UI dump written to {dumpPath}");
                break;

            case "next":
                this.AdvanceRepriceAssist();
                break;

            case "undercutall":
                this.CheckAllListings(true);
                break;

            case "worth":
                this.ToggleWorth();
                break;

            case "checkall":
                this.CheckAllListings();
                break;

            case "price":
                this.ApplySellDialogPrice();
                break;

            case "big":
                Configuration.RepriceFloorPercent = Configuration.RepriceFloorPercent == 0 ? 50 : 0;
                this.SaveConfig();
                this.Print(Configuration.RepriceFloorPercent == 0
                    ? "floor off: every above-market listing can be matched."
                    : "floor back at 50%.");
                break;

            case "floor":
                if (split.Length > 1 && int.TryParse(split[1], out var floorValue))
                {
                    Configuration.RepriceFloorPercent = Math.Clamp(floorValue, 0, 100);
                    this.SaveConfig();
                    this.Print($"floor set to {Configuration.RepriceFloorPercent}% (0 turns it off).");
                    break;
                }

                this.Print($"floor is {Configuration.RepriceFloorPercent}%; use /slotyield floor <0-100>.");
                break;

            case "stop":
                this.StopAutomaticReprice();
                this.StopListing();
                this.StopSweep();
                this.StopRepriceAssist();
                break;

            case "reprice":
                this.CheckAllListings();
                break;

            case "craft":
                this.RefreshCraftPlan();
                this.Print($"read {this.CraftOwnedItemCount} item stack(s); {this.CraftPlan.Count} recipe(s) match your bags.");
                foreach (var idea in this.CraftPlan.Take(3))
                    this.Print(
                        $"  {this.Catalog.Name(idea.ItemId)} [{this.Catalog.JobAbbreviation(idea.CraftJob)} {idea.RequiredLevel}]: " +
                        $"{idea.Crafts} craft(s), {Money.Gil(idea.Profit)} profit");
                break;

            case "alerts":
                this.RunAlertPoll(true);
                break;

            case "diag":
                this.WriteDiagnostics();
                break;

            case "reset":
                this.mainWindow.ResetWindow();
                this.mainWindow.IsOpen = true;
                ChatGui.Print("[Slot Yield] Window reset.");
                break;

            case "clear":
                this.Cache.Clear();
                State.Result = null;
                State.Diff = null;
                this.SaveConfig();
                ChatGui.Print("[Slot Yield] Cache cleared.");
                break;

            default:
                ChatGui.Print("[Slot Yield] Options: scan, world <name>, pin <itemId> [hq], unpin <itemId>, craft, alerts, price, reprice, checkall, undercutall, worth, next, stop, big, dumpretainer, probe <slot>, setprice <gil>, confirmprice, diag, reset, clear.");
                break;
        }
    }

    private void HandlePin(bool remove, string[] parts)
    {
        if (parts.Length < 2 || !uint.TryParse(parts[1], out var itemId))
        {
            ChatGui.PrintError("[Slot Yield] Usage: /slotyield pin <itemId> [hq]");
            return;
        }

        var hq = parts.Length > 2 && parts[2].Equals("hq", StringComparison.OrdinalIgnoreCase);
        var existing = Configuration.Pinned.FirstOrDefault(p => p.ItemId == itemId && p.Hq == hq);

        if (remove)
        {
            if (existing is not null)
                Configuration.Pinned.Remove(existing);
        }
        else if (existing is null)
        {
            Configuration.Pinned.Add(new PinnedSlot
            {
                ItemId = itemId,
                Hq = hq,
                StackSize = Configuration.DefaultStackSize,
            });
        }

        this.SaveConfig();
        ChatGui.Print($"[Slot Yield] {(remove ? "Unpinned" : "Pinned")} {itemId}{(hq ? " (HQ)" : string.Empty)}.");
    }
}
