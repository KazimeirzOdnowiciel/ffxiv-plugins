using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using Glasswork;
using Glasswork.Backend;
using SlotYield.Core;
using SlotYield.Game;
using SlotYield.Market;

namespace SlotYield.UI;

public sealed class MainWindow : GlassWindow
{
    /// <summary>Every retainer can hold twenty market listings.</summary>
    private const int MarketSlotsPerRetainer = 20;

    private IReadOnlyList<(InventoryType Type, int Slot, uint ItemId, bool Hq, int Quantity, int Price)> listingCandidates =
        Array.Empty<(InventoryType, int, uint, bool, int, int)>();

    private readonly Plugin plugin;

    private string search = string.Empty;
    private int sortColumn = 5;
    private bool sortAscending;
    private int opportunityTableFrame = -100;
    private int opportunityVisibleMask;
    private bool showDead;
    private int qualityFilter;
    private decimal maxProfitMagnitude = 1m;
    private int activeTab;
    private float animatedBestProfit;
    private float animatedLive;
    private float animatedProfitable;
    private float animatedBestPickProfit;
    private float animatedPortfolioProfit;
    private float cardFade = 1f;
    private float sparkProgress = 1f;
    private bool focusSearch;
    private int lastPinnedCount;
    private float stripPulse;
    private uint detailItemId;
    private Quality detailQuality;
    private bool openDetail;
    private int selectedRow = -1;
    private float tabUnderlineX;
    private bool tabUnderlineInit;
    private readonly HashSet<(uint ItemId, Quality Quality)> multiSelected = new();
    private string presetName = string.Empty;
    private string shareInput = string.Empty;
    private string categoryFilter = string.Empty;
    private readonly Dictionary<uint, string> categoryNames = new();
    private readonly Dictionary<uint, int> requiredLevels = new();
    private int levelCap;
    private int minStackProfit;
    private int arbitrageSortColumn = 5;
    private bool arbitrageSortAscending = true;
    private int arbitrageTableFrame = -100;
    private uint flipItemId;
    private Quality flipQuality;
    private int flipQty = 99;
    private bool flipOpen;
    private bool buyOpen;
    private uint buyItemId;
    private bool buyHq;
    private string buyName = string.Empty;
    private string buyWorld = string.Empty;
    private int buyQty = 99;
    private decimal buyMaxPrice;
    private PinnedSlot? undoSlot;
    private DateTime undoUtc;
    private bool watchedOnly;

    /// <summary>Pending destructive confirmations, so nothing irreversible fires on one click.</summary>
    private bool confirmUndercutAll;
    private bool confirmForgetListings;
    private bool confirmClearCart;
    private bool confirmClearCache;

    /// <summary>Typed max price for the buy dialog, parsed back into <see cref="buyMaxPrice"/>.</summary>
    private string buyMaxPriceText = string.Empty;

    /// <summary>World names for the picker, rebuilt only when the world list changes.</summary>
    private string[] worldChoices = Array.Empty<string>();

    /// <summary>Category filter items, reused so the combo does not allocate per frame.</summary>
    private readonly List<string> categoryItems = new();

    public IReadOnlyCollection<(uint ItemId, Quality Quality)> MultiSelection => this.multiSelected;

    private static readonly string[] TabLabels =
    {
        "Opportunities", "Changes", "Portfolio", "Craft", "My listings", "Arbitrage", "Scan",
    };

    private static readonly string[] ColumnLabels =
    {
        "Item", "Q", "Price", "Cost", "Revenue/d", "Profit/d", "Sales/d", "Sellers", "Share", "Capital", "Clear", "30d", "Stack", "",
    };

    /// <summary>Data-column index of the trailing per-row pin action, which never sorted on its own.</summary>
    private const int PinColumn = 13;

    /// <summary>Fixed widths, or a stretch weight for the name, in data order.</summary>
    private static readonly float[] ColumnWidths = { 2.4f, 26f, 70f, 70f, 78f, 78f, 58f, 52f, 48f, 72f, 52f, 112f, 84f, 40f };

    private static readonly bool[] ColumnStretch = { true, false, false, false, false, false, false, false, false, false, false, false, false, false };

    /// <summary>Columns whose cells are numbers, so the header and text right-align.</summary>
    private static readonly bool[] ColumnNumeric = { false, false, true, true, true, true, true, true, false, true, true, false, true, false };

    private static readonly string[] QualityFilterLabels = { "NQ+HQ", "NQ", "HQ" };

    /// <summary>One-line meanings of the opportunities columns, for the toolbar's "?" popover.</summary>
    private static readonly (string Label, string Meaning)[] ColumnMeanings =
    {
        ("Item", "The item and its icon; a ! marks quotes older than the freshness window."),
        ("Q", "NQ or HQ - which version of the item this row prices."),
        ("Price", "Your listing price: the price this slot would be posted at after undercutting."),
        ("Cost", "Estimated acquisition cost per unit; a ? means some materials had no price."),
        ("Revenue/d", "Gil the slot would take per day at your expected share, after tax but before cost."),
        ("Profit/d", "Net gil per day after tax and acquisition cost."),
        ("Sales/d", "Units of this item sold per day on your world."),
        ("Sellers", "Distinct sellers competing with you at your price."),
        ("Share", "Your expected share of those daily sales."),
        ("Capital", "Gil tied up in the slot - the listing price times the stack you would hold."),
        ("Clear", "Days to sell a whole stack at your expected share."),
        ("30d", "Price history: quantity-weighted daily prices over the last 30 days."),
        ("Stack", "Whole-stack profit: profit per day times the days to clear."),
        ("pin", "Pins or unpins the slot, so it counts toward your portfolio."),
    };

    private static readonly PricingStrategy[] StrategyValues = Enum.GetValues<PricingStrategy>();

    private static readonly string[] StrategyLabels = StrategyValues.Select(StrategyLabel).ToArray();

    private static readonly CostMode[] CostValues = Enum.GetValues<CostMode>();

    private static readonly string[] CostLabels = CostValues.Select(CostLabel).ToArray();

    private readonly bool[] columnVisible =
    {
        true, true, true, true, true, true, true, true, true, true, true, true, true, true,
    };
    private int minGilPerDay;
    private bool pinnedOnly;
    private bool affordableOnly;
    private bool hideStale;
    private bool drawFailureReported;
    private int listingSortColumn = -1;
    private bool listingSortAscending;
    private int listingTableFrame = -100;
    private int portfolioSortColumn;
    private bool portfolioSortAscending = true;
    private int portfolioTableFrame = -100;
    private int craftSortColumn = -1;
    private bool craftSortAscending;
    private int craftTableFrame = -100;

    /// <summary>Rows of the portfolio and craft tables, reused so sorting them allocates nothing per frame.</summary>
    private readonly List<PortfolioCandidate> portfolioRows = new();
    private readonly List<CraftOpportunity> craftRows = new();

    /// <summary>Comparators per column, built once and picked by the current sort column.</summary>
    private Comparison<PortfolioCandidate>[]? portfolioComparisons;
    private Comparison<CraftOpportunity>[]? craftComparisons;

    /// <summary>Quotes for the portfolio frame being sorted, so the sales/day and sellers comparators can read them.</summary>
    private Dictionary<(uint ItemId, Quality Quality), MarketQuote>? portfolioSortQuotes;

    /// <summary>The filtered+sorted opportunities rows, rebuilt only when the scan or a filter input changes.</summary>
    private ScanResult? rowsCacheSource;
    private List<SlotCandidate>? rowsCache;
    private bool rowsCacheValid;
    private string rowsCacheSearch = "\0";
    private int rowsCacheMinGilPerDay;
    private int rowsCacheQuality;
    private string rowsCacheCategory = "\0";
    private int rowsCacheLevelCap;
    private int rowsCacheMinStack;
    private bool rowsCachePinnedOnly;
    private bool rowsCacheShowDead;
    private bool rowsCacheAffordableOnly;
    private bool rowsCacheHideStale;
    private bool rowsCacheWatchedOnly;
    private int rowsCacheSortColumn;
    private bool rowsCacheSortAscending;
    private int rowsCachePinnedCount;
    private int rowsCacheWatchedCount;
    private long rowsCacheBudget;

    /// <summary>Category names for the current scan, rebuilt only when the scan changes.</summary>
    private ScanResult? categoryChoicesSource;
    private readonly List<string> categoryChoices = new();

    /// <summary>Reused scratch array for the header pulse, resized only when the history grows.</summary>
    private decimal[] pulseSeries = Array.Empty<decimal>();

    public MainWindow(Plugin plugin)
        : base(plugin, "Slot Yield###SlotYieldMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(1120, 780);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(960, 460),
            MaximumSize = new Vector2(2400, 1600),
        };
        this.BgAlpha = 1f;

        this.sortColumn = plugin.Configuration.SortColumn;
        this.sortAscending = plugin.Configuration.SortAscending;
        if (plugin.Configuration.ColumnVisible.Count == this.columnVisible.Length)
        {
            for (var index = 0; index < this.columnVisible.Length; index++)
                this.columnVisible[index] = plugin.Configuration.ColumnVisible[index];
        }
    }

    private Configuration Config => this.plugin.Configuration;

    /// <summary>Opens the window on a tab, used by the command palette.</summary>
    public void OpenTab(int index)
    {
        if (index < 0 || index >= TabLabels.Length)
            return;

        this.activeTab = index;
        this.IsOpen = true;
    }

    /// <summary>
    /// Puts the window back somewhere visible at a sensible size. Used by the error screen and by
    /// /slotyield reset, because a window that opened off-screen is otherwise unrecoverable.
    /// </summary>
    public void ResetWindow()
    {
        this.Size = new Vector2(1120, 780);
        this.SizeCondition = ImGuiCond.Once;
        this.Position = new Vector2(140f, 90f);
        this.PositionCondition = ImGuiCond.Once;
        this.drawFailureReported = false;
    }

    public override void OnOpen()
    {
        this.Config.WindowOpen = true;
        this.plugin.SaveConfig();
    }

    public override void OnClose()
    {
        this.Config.WindowOpen = false;
        this.plugin.SaveConfig();
    }

    public override void Draw()
    {
        // A UI fault must not take the window system down with it. Show a recovery panel instead.
        try
        {
            this.DrawGlass();

            this.DrawCore();
        }
        catch (Exception ex)
        {
            this.DrawFailure(ex);
        }
    }

    private void DrawFailure(Exception exception)
    {
        if (!this.drawFailureReported)
        {
            Plugin.Log.Error(exception, "[SlotYield] UI draw failed.");
            this.drawFailureReported = true;
        }

        ImGui.TextWrapped("Slot Yield hit an error while drawing its window. That is a plugin bug, not a problem with your game.");
        ImGui.Spacing();
        ImGui.TextWrapped($"{exception.GetType().Name}: {exception.Message}");
        ImGui.Spacing();

        if (ImGui.Button("Reset window"))
            this.ResetWindow();

        ImGui.SameLine();
        if (ImGui.Button("Disable the plugin"))
        {
            this.Config.Enabled = false;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        if (ImGui.Button("Write diagnostics"))
            this.plugin.WriteDiagnostics();
    }

    /// <summary>Table height that fills the window instead of stranding empty space below.</summary>
    private static float TableHeight() => MathF.Max(220f, ImGui.GetContentRegionAvail().Y - 4f);

    /// <summary>
    /// A table cell's text with a tooltip over the whole cell. <see cref="Gw.TableText"/> reserves only the
    /// text's own width, so a right-aligned or shortened cell would otherwise hang its tooltip off to one side.
    /// </summary>
    private static void TableCellText(string text, TextRole role, Vector4 color, Func<string>? tooltip = null)
    {
        var left = ImGui.GetCursorScreenPos().X;
        var width = ImGui.GetContentRegionAvail().X;

        Gw.TableText(text, role, Theme.ToColor(color));

        if (tooltip is null)
            return;

        var top = ImGui.GetItemRectMin().Y;
        var bottom = ImGui.GetItemRectMax().Y;
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.None) &&
            ImGui.IsMouseHoveringRect(new Vector2(left, top), new Vector2(left + width, bottom)))
            ImGui.SetTooltip(tooltip());
    }

    /// <summary>Approximate width of a <c>Gw</c> small button, for right-aligning a toolbar.</summary>
    private static float SmallButtonWidth(string label) => ImGui.CalcTextSize(label).X + 24f;

    private void DrawCore()
    {
        if (!this.Config.Enabled)
        {
            Theme.Dimmed("Slot Yield is disabled.");
            if (ImGui.Button("Enable"))
            {
                this.Config.Enabled = true;
                this.plugin.SaveConfig();
            }

            return;
        }

        this.DrawContent();
    }

    private void DrawContent()
    {
        var deltaTime = ImGui.GetIO().DeltaTime;
        if (Theme.Look.ReduceMotion)
        {
            this.cardFade = 1f;
            this.sparkProgress = 1f;
        }
        else
        {
            this.cardFade += (1f - this.cardFade) * (1f - MathF.Exp(-10f * deltaTime));
            this.sparkProgress += (1f - this.sparkProgress) * (1f - MathF.Exp(-8f * deltaTime));
        }

        this.DrawHeader();
        this.DrawTabBar();

        switch (this.activeTab)
        {
            case 0:
                this.DrawOpportunities();
                break;
            case 1:
                this.DrawChanges();
                break;
            case 2:
                this.DrawPortfolio();
                break;
            case 3:
                this.DrawCraft();
                break;
            case 4:
                this.DrawMyListings();
                break;
            case 5:
                this.DrawArbitrageTab();
                break;
            default:
                this.DrawScanTab();
                break;
        }

        this.DrawDetailPopup();
    }

    /// <summary>Open a row's full breakdown: ladder, history, explain and actions.</summary>
    private void DrawDetailPopup()
    {
        if (!this.openDetail)
            return;

        // A real modal: the scrim blocks the windows behind, the keyboard stays inside, and
        // Escape or a scrim click closes it (Gw.Dialog clears openDetail itself).
        if (!Gw.Dialog("itemdetail"u8, ref this.openDetail, "Item detail"u8, new Vector2(460f, 420f)))
            return;

        var result = this.plugin.State.Result;
        var candidate = result?.Candidates.FirstOrDefault(c => c.ItemId == this.detailItemId && c.Quality == this.detailQuality);
        if (candidate is null)
        {
            Theme.Dimmed("Not in the current scan.");
            if (Gw.Button("close"u8, ButtonStyle.Plain, ButtonSize.Small))
                this.openDetail = false;

            Gw.EndDialog();
            return;
        }

        var score = candidate.Score;
        var info = this.plugin.Catalog.Get(candidate.ItemId);

        UiKit.ItemIcon(UiKit.IconIdForItem(candidate.ItemId), candidate.Quality == Quality.Hq, 32f);
        ImGui.SameLine(0f, 8f);
        Theme.Colored(Theme.Text, info.Name);
        if (candidate.Quality == Quality.Hq)
        {
            ImGui.SameLine(0f, 6f);
            Theme.Colored(Theme.Hq, "HQ");
        }

        ImGui.Spacing();
        var (verdictLabel, verdictColor) = VerdictChip(score.Verdict);
        UiKit.Chip(verdictLabel, verdictColor);
        ImGui.SameLine(0f, 6f);
        UiKit.ConfidenceDots(score.Confidence);

        ImGui.Spacing();
        Theme.Faint($"price {Money.Gil(score.ListPrice)}   revenue {Money.Gil(score.GilPerDay)}/day   profit {Money.Gil(score.ProfitPerDay)}/day");
        Theme.Faint($"{score.Quote.DailyVelocity:0.0} sales/day   {score.Quote.DistinctSellers} sellers   share {score.ExpectedShare:P0}   clears in {Money.Days(score.DaysToClear)}");
        Theme.Faint($"capital {Money.Gil(score.CapitalPerSlot)}   trend {score.TrendPercent:+0.0%;-0.0%;0%}");

        ImGui.Spacing();
        Theme.Dimmed(score.Explain);

        if (candidate.Quote.PriceHistory.Length >= 2)
        {
            ImGui.Spacing();
            var chartPos = ImGui.GetCursorScreenPos();
            var chartSize = new Vector2(ImGui.GetContentRegionAvail().X, 64f);
            ImGui.Dummy(chartSize);
            DrawSparkline(ImGui.GetWindowDrawList(), chartPos, chartSize, candidate.Quote.PriceHistory, Theme.U32(Theme.Accent));
        }

        ImGui.Spacing();
        if (Gw.Button(this.IsSlotPinned(candidate.ItemId, candidate.Quality) ? "unpin"u8 : "pin to slot"u8))
            this.TogglePin(candidate.ItemId, candidate.Quality);

        ImGui.SameLine();
        if (Gw.Button("universalis"u8))
            OpenUrl($"https://universalis.app/market/{candidate.ItemId}");

        ImGui.SameLine();
        if (Gw.Button("history"u8))
            this.plugin.OpenHistory(candidate.ItemId, candidate.Quality);

        ImGui.SameLine();
        if (Gw.Button("close"u8, ButtonStyle.Plain, ButtonSize.Small))
            this.openDetail = false;

        Gw.EndDialog();
    }

    /// <summary>
    /// Brass-underline tabs drawn by hand. The stock ImGui tabs are the last "tool window"
    /// tell in this UI; these read as a ledger's index instead.
    /// </summary>
    private void DrawTabBar()
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var pos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetTextLineHeight() + 10f;
        var x = pos.X;
        var activeMin = pos.X;
        var activeWidth = 0f;

        for (var index = 0; index < TabLabels.Length; index++)
        {
            var label = TabLabels[index];
            var width = ImGui.CalcTextSize(label).X + 16f;
            var min = new Vector2(x, pos.Y);
            var max = min + new Vector2(width, height);
            var hovered = ImGui.IsMouseHoveringRect(min, max);
            var active = index == this.activeTab;

            if (active)
            {
                activeMin = min.X;
                activeWidth = width;
            }

            if (hovered && !active)
                drawList.AddRectFilled(min, max, Theme.WithAlpha(Theme.PanelHover, 0.7f), 3f);

            var textColor = active || hovered ? Theme.Text : Theme.TextDim;
            drawList.AddText(font, ImGui.GetFontSize(), min + new Vector2(8f, 5f), Theme.U32(textColor), label);

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && this.activeTab != index)
            {
                this.activeTab = index;
                this.cardFade = 0f;
                this.sparkProgress = 0f;
            }

            x += width;
        }

        // One underline that glides between tabs instead of switching instantly.
        if (!this.tabUnderlineInit)
        {
            this.tabUnderlineX = activeMin;
            this.tabUnderlineInit = true;
        }

        if (Theme.Look.ReduceMotion)
        {
            this.tabUnderlineX = activeMin;
        }
        else
        {
            this.tabUnderlineX += (activeMin - this.tabUnderlineX) * (1f - MathF.Exp(-14f * ImGui.GetIO().DeltaTime));
        }

        drawList.AddRectFilled(
            new Vector2(this.tabUnderlineX + 6f, pos.Y + height - 2f),
            new Vector2(this.tabUnderlineX + activeWidth - 6f, pos.Y + height),
            Theme.U32(Theme.Accent),
            1f);

        ImGui.Dummy(new Vector2(0f, height));
        UiKit.Rule(0.05f);
    }

    // ------------------------------------------------------------------ header ----

    private void DrawHeader()
    {
        var state = this.plugin.State;
        var world = this.plugin.ResolveWorld();
        var scanned = this.Config.LastScanUtc is { } lastScan ? RelativeTime(lastScan) : "never";

        Theme.Colored(Theme.Text, world);

        var subtitle = $"   last scan {scanned}";

        if (state.Result is { } result)
            subtitle += $"   -   {result.Stats.Requests} requests in {result.Stats.Duration.TotalSeconds:0}s";

        ImGui.SameLine();
        Theme.Colored(Theme.TextFaint, subtitle);

        // Actions sit on the right edge so the left side stays a status line.
        ImGui.SameLine();
        var style = ImGui.GetStyle();
        var actionsWidth = ImGui.CalcTextSize("Net worth").X + ImGui.CalcTextSize("Ticker").X + ImGui.CalcTextSize("Rescan").X + ImGui.CalcTextSize("?").X +
                           (style.FramePadding.X * 8f) + (style.ItemSpacing.X * 4f) + 6f;
        var rightEdge = ImGui.GetWindowContentRegionMax().X - actionsWidth;
        if (ImGui.GetCursorPosX() < rightEdge)
            ImGui.SetCursorPosX(rightEdge);

        if (ImGui.SmallButton("Net worth"))
            this.plugin.ToggleWorth();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Opens the net worth window (/slotyield worth). It remembers being open.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Ticker"))
            this.plugin.ToggleTicker();

        ImGui.SameLine();
        if (ImGui.SmallButton("?"))
            this.plugin.ToggleHelp();

        if (state.IsScanning)
        {
            ImGui.SameLine();
            Theme.Colored(Theme.Gold, $"{state.Phase} {state.Done}/{state.Total}");
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Rescan"))
                this.plugin.StartScan();
        }

        if (state.Result is { } bandResult)
        {
            ImGui.NewLine();
            this.DrawStatBand(bandResult);
        }
        else
        {
            ImGui.NewLine();
        }

        // Retainer slot strip: the portfolio's shape at a glance, one cell per slot.
        var stripOrigin = ImGui.GetCursorScreenPos() + new Vector2(0f, 9f);
        var stripWidth = ImGui.GetContentRegionAvail().X;
        var slots = Math.Clamp(this.Config.PortfolioSlots, 1, 40);
        var filled = Math.Clamp(this.Config.Pinned.Count, 0, slots);

        if (filled != this.lastPinnedCount)
        {
            this.lastPinnedCount = filled;
            this.stripPulse = 1f;
        }

        this.stripPulse = Theme.Look.ReduceMotion
            ? 0f
            : MathF.Max(0f, this.stripPulse - (ImGui.GetIO().DeltaTime * 2f));

        var pinnedIcons = new List<(uint IconId, bool Hq)>(filled);
        for (var index = 0; index < this.Config.Pinned.Count; index++)
        {
            var slot = this.Config.Pinned[index];
            pinnedIcons.Add((UiKit.IconIdForItem(slot.ItemId), slot.Hq));
        }

        UiKit.SlotStrip(ImGui.GetWindowDrawList(), stripOrigin, stripWidth, 26f, slots, filled, out var hovered, pinnedIcons, this.stripPulse);
        ImGui.Dummy(new Vector2(stripWidth, 38f));

        if (hovered >= 0)
            ImGui.SetTooltip(hovered < filled ? $"slot {hovered + 1}   pinned" : $"slot {hovered + 1}   free");

        var budget = (decimal)this.Config.PortfolioBudget;
        var capital = 0m;
        if (state.Result is { } ringResult)
        {
            foreach (var slot in this.Config.Pinned)
            {
                var ringCandidate = ringResult.Candidates.FirstOrDefault(c => c.ItemId == slot.ItemId && c.Quality == (slot.Hq ? Quality.Hq : Quality.Nq));
                if (ringCandidate is not null)
                    capital += ringCandidate.Score.CapitalPerSlot;
            }
        }

        var ringFraction = budget > 0m ? Math.Clamp((float)(capital / budget), 0f, 1f) : 0f;
        Gw.Gauge(ringFraction, 22f, default, Theme.ToColor(capital > budget ? Theme.Warn : Theme.Accent));
        ImGui.SameLine(0f, 4f);
        Theme.Faint($"{filled} pinned   -   {slots} slots   -   budget {Money.Gil(budget)}   ({Money.Gil(capital)} in)");

        var history = this.Config.ScanHistory;
        if (history.Count >= 2)
        {
            var pulseWidth = 120f;
            var pulseHeight = 16f;
            var pulseX = MathF.Max(ImGui.GetCursorPosX() + 12f, ImGui.GetWindowContentRegionMax().X - pulseWidth);
            ImGui.SameLine(pulseX);
            var pulsePos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(pulseWidth, pulseHeight));
            if (this.pulseSeries.Length != history.Count)
                this.pulseSeries = new decimal[history.Count];

            for (var index = 0; index < history.Count; index++)
                this.pulseSeries[index] = history[index].GilPerDay;
            DrawSparkline(ImGui.GetWindowDrawList(), pulsePos, new Vector2(pulseWidth, pulseHeight), this.pulseSeries, Theme.U32(Theme.Accent));
        }
        if (state.IsScanning)
        {
            var progressOrigin = ImGui.GetCursorScreenPos();
            UiKit.ProgressLine(ImGui.GetWindowDrawList(), progressOrigin, stripWidth, 3f, state.Fraction);
            ImGui.Dummy(new Vector2(stripWidth, 3f));
        }

        if (state.Error.Length > 0)
            Theme.Colored(Theme.Danger, state.Error);

        UiKit.Rule();
    }

    private static string RelativeTime(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalMinutes < 1)
            return "just now";
        if (elapsed.TotalHours < 1)
            return $"{elapsed.TotalMinutes:0}m ago";
        if (elapsed.TotalDays < 1)
            return $"{elapsed.TotalHours:0}h ago";
        if (elapsed.TotalDays < 7)
            return $"{elapsed.TotalDays:0}d ago";
        return utc.ToLocalTime().ToString("MMM d HH:mm");
    }

    /// <summary>Three large figures where the status line used to be: a dashboard head, not a sentence.</summary>
    private void DrawStatBand(ScanResult result)
    {
        var live = 0;
        var profitable = 0;
        var bestProfit = 0m;

        for (var index = 0; index < result.Candidates.Count; index++)
        {
            var candidate = result.Candidates[index];
            if (candidate.Score.IsDead || candidate.Score.IsSuspect)
                continue;

            live++;
            if (candidate.Score.ProfitPerDay > 0m)
                profitable++;
            if (candidate.Score.ProfitPerDay > bestProfit)
                bestProfit = candidate.Score.ProfitPerDay;
        }

        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float bandHeight = 46f;
        var cell = width / 3f;

        // Frame-rate independent easing: the figures climb to their new value instead of snapping,
        // so a finished scan reads as "filling in" rather than flickering.
        var ease = 1f - MathF.Exp(-7f * ImGui.GetIO().DeltaTime);
        this.animatedBestProfit += ((float)bestProfit - this.animatedBestProfit) * ease;
        this.animatedLive += (live - this.animatedLive) * ease;
        this.animatedProfitable += (profitable - this.animatedProfitable) * ease;

        drawList.AddRectFilled(origin, origin + new Vector2(width, bandHeight), Theme.WithAlpha(Theme.Panel, 0.55f), 4f);
        drawList.AddRect(origin, origin + new Vector2(width, bandHeight), Theme.U32(Theme.Hairline), 4f);

        DrawStatCell(drawList, font, origin + new Vector2(12f, 6f), "BEST / DAY", "+" + Money.Gil((decimal)this.animatedBestProfit), Theme.Profit);
        DrawStatCell(drawList, font, origin + new Vector2(cell + 12f, 6f), "LIVE ITEMS", ((int)MathF.Round(this.animatedLive)).ToString(), Theme.Text);
        DrawStatCell(drawList, font, origin + new Vector2((cell * 2f) + 12f, 6f), "PROFITABLE", ((int)MathF.Round(this.animatedProfitable)).ToString(), Theme.Accent);

        ImGui.Dummy(new Vector2(width, bandHeight + 6f));
    }

    private static void DrawStatCell(ImDrawListPtr drawList, ImFontPtr font, Vector2 position, string label, string value, Vector4 color)
    {
        drawList.AddText(font, 10f, position, Theme.U32(Theme.TextFaint), label);
        drawList.AddText(font, 20f, position + new Vector2(0f, 13f), Theme.U32(color), value);
    }

    private void DrawOpportunities()
    {
        var result = this.plugin.State.Result;
        if (result is null || result.Candidates.Count == 0)
        {
            ImGui.Spacing();

            if (this.plugin.State.IsScanning)
            {
                Gw.Text("Scanning - the table fills in as results arrive."u8, TextRole.Subheadline);
                Gw.SkeletonText(1, 0.72f, TextRole.Body);
                Gw.SkeletonText(1, 0.55f, TextRole.Body);
                Gw.SkeletonText(1, 0.81f, TextRole.Body);
                Gw.SkeletonText(1, 0.64f, TextRole.Body);
                return;
            }

            var startScan = UiKit.EmptyState(
                "No scan yet",
                "A scan walks every marketable item on your world, ranks them by how much gil a slot could realistically earn, and prices the shortlist exactly. It takes one to three minutes and stays cached for a few hours.",
                "Scan now");
            if (startScan)
            {
                this.plugin.StartScan();
                Gw.Toast("Scan started"u8, ToastKind.Info);
            }

            return;
        }

        var best = this.BestCandidate(result.Candidates);
        if (best is not null)
            this.DrawBestPick(best);

        UiKit.SectionHeader("All opportunities");

        // Filters.
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.F))
            this.focusSearch = true;

        // The search field flexes: it takes whatever the fixed controls leave, so a wide window
        // fills with the row instead of leaving the right half empty.
        var rowFixed =
            (110f + ImGui.CalcTextSize("min/day").X) +
            90f +
            (110f + ImGui.CalcTextSize("category").X) +
            (80f + ImGui.CalcTextSize("level").X) +
            SmallButtonWidth("my level") +
            (90f + ImGui.CalcTextSize("stack").X) +
            (ImGui.GetStyle().ItemSpacing.X * 7f);

        var searchWidth = Math.Clamp(ImGui.GetContentRegionAvail().X - rowFixed, 200f, 900f);
        ImGui.SetNextItemWidth(searchWidth);
        if (this.focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            this.focusSearch = false;
        }

        Gw.TextField(ref this.search, "filter by item name..."u8, maxLength: 64);
        if (ImGui.IsItemActive() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            this.search = string.Empty;
        ImGui.SameLine(0f, 8f);
        // 0 means "no minimum" and is deliberate: unprofitable rows stay visible so a losing
        // slot can be spotted. Say that instead of printing "min 0", which read as ">= 0".
        ImGui.SetNextItemWidth(110f);
        var minFormat = this.minGilPerDay <= 0 ? "any" : "%d";
        Gw.Slider(ref this.minGilPerDay, 0, 200_000, "min/day"u8, 1, minFormat);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("0 shows every row, including ones that would lose gil. Raise it to hide them.");
        ImGui.SameLine();

        var quality = this.qualityFilter;
        if (Gw.Segmented(ref quality, QualityFilterLabels))
            this.qualityFilter = quality;

        ImGui.SameLine();
        var categoryChoices = this.CategoryChoices(result);
        this.categoryItems.Clear();
        this.categoryItems.Add("all");
        this.categoryItems.AddRange(categoryChoices);

        var categoryIndex = this.categoryFilter.Length == 0 ? 0 : categoryChoices.IndexOf(this.categoryFilter) + 1;
        if (categoryIndex < 0)
            categoryIndex = 0;

        ImGui.SetNextItemWidth(110f);
        if (Gw.ComboBox(ref categoryIndex, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.categoryItems), "category"u8))
            this.categoryFilter = categoryIndex <= 0 ? string.Empty : categoryChoices[categoryIndex - 1];

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        var levelCap = Math.Clamp(this.levelCap, 0, 100);
        if (Gw.Slider(ref levelCap, 0, 100, "level"u8, 1, levelCap <= 0 ? "any" : "%d"))
            this.levelCap = levelCap;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide items whose craft or equip level is above this. 0 shows everything.");

        ImGui.SameLine();
        if (Gw.Button("my level"u8, ButtonStyle.Plain, ButtonSize.Small))
            this.levelCap = Plugin.PlayerState.Level;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        var minStack = Math.Clamp(this.minStackProfit, 0, 5_000_000);
        if (Gw.Slider(ref minStack, 0, 5_000_000, "stack"u8, 1, minStack <= 0 ? "any" : "%d+"))
            this.minStackProfit = minStack;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide rows whose whole stack would earn less than this before it clears.");

        // One toolbar strip behind the chips and actions, so a wide window reads as a bar instead
        // of two clusters floating in empty space. Only worth drawing when the window is wide.
        ImGui.NewLine();
        var toolbarBarMin = ImGui.GetCursorScreenPos();
        var toolbarBarWidth = ImGui.GetContentRegionAvail().X;
        var toolbarBarHeight = ImGui.GetFrameHeight() + 10f;
        var toolbarLook = this.plugin.Host.Look;
        var toolbarHasBar = toolbarBarWidth >= 900f;

        if (toolbarHasBar)
        {
            Paint.Material(
                ImGui.GetWindowDrawList(),
                Rect.FromSize(toolbarBarMin, new Vector2(toolbarBarWidth, toolbarBarHeight)),
                Corners.All(6f * Frame.Scale),
                toolbarLook.CornerStyle,
                in toolbarLook.BarMaterial,
                in toolbarLook,
                Frame.Glass,
                Frame.Scale,
                0.55f);

            ImGui.SetCursorScreenPos(toolbarBarMin + new Vector2(6f, 5f));
        }

        UiKit.ToggleChip("pinned", ref this.pinnedOnly);
        ImGui.SameLine(0f, 4f);
        UiKit.ToggleChip("dead", ref this.showDead);
        ImGui.SameLine(0f, 4f);
        UiKit.ToggleChip("affordable", ref this.affordableOnly);
        ImGui.SameLine(0f, 4f);
        UiKit.ToggleChip("fresh only", ref this.hideStale);
        ImGui.SameLine(0f, 4f);
        UiKit.ToggleChip("watched", ref this.watchedOnly);

        var rows = this.FilteredRows(result);

        var toolbarWidth =
            ImGui.CalcTextSize($"{rows.Count} shown").X +
            SmallButtonWidth("copy CSV") + SmallButtonWidth("reset") + SmallButtonWidth("save filter") +
            SmallButtonWidth("presets") + SmallButtonWidth("columns") + SmallButtonWidth("?") +
            (ImGui.GetStyle().ItemSpacing.X * 7f);

        var toolbarX = ImGui.GetWindowContentRegionMax().X - toolbarWidth - (toolbarHasBar ? 6f : 0f);
        if (toolbarX > ImGui.GetCursorPosX() + 24f)
        {
            ImGui.SameLine(toolbarX);
        }
        else
        {
            ImGui.SameLine(0f, 20f);
        }

        ImGui.AlignTextToFramePadding();
        Theme.Faint($"{rows.Count} shown");

        ImGui.SameLine();
        if (Gw.Button("copy CSV"u8, ButtonStyle.Plain, ButtonSize.Small))
        {
            ImGui.SetClipboardText(this.BuildCsv(rows));
            this.plugin.Print($"Copied {rows.Count} rows as CSV.");
        }

        ImGui.SameLine();
        if (Gw.Button("reset"u8, ButtonStyle.Plain, ButtonSize.Small))
        {
            this.search = string.Empty;
            this.minGilPerDay = 0;
            this.qualityFilter = 0;
            this.pinnedOnly = false;
            this.showDead = true;
            this.affordableOnly = false;
            this.hideStale = false;
        }

        ImGui.SameLine();
        if (Gw.Button("save filter"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.OpenPopup("##savefilter");

        if (ImGui.BeginPopup("##savefilter"))
        {
            ImGui.InputTextWithHint("##presetname", "preset name", ref this.presetName, 32);
            if (ImGui.Button("save") && this.presetName.Length > 0)
            {
                this.Config.Presets.RemoveAll(preset => preset.Name == this.presetName);
                this.Config.Presets.Add(this.CurrentPreset(this.presetName));
                this.plugin.SaveConfig();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (Gw.Button("presets"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.OpenPopup("##presets");

        if (ImGui.BeginPopup("##presets"))
        {
            if (this.Config.Presets.Count == 0)
                Theme.Faint("no saved filters");

            foreach (var preset in this.Config.Presets)
            {
                if (ImGui.Selectable(preset.Name))
                    this.ApplyPreset(preset);
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (Gw.Button("columns"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.OpenPopup("##columns");

        if (ImGui.BeginPopup("##columns"))
        {
            for (var index = 0; index < ColumnLabels.Length; index++)
            {
                var visible = this.columnVisible[index];
                if (ImGui.Checkbox(ColumnLabels[index] + "##col" + index, ref visible))
                {
                    this.columnVisible[index] = visible;
                    this.Config.ColumnVisible = this.columnVisible.ToList();
                    this.plugin.SaveConfig();
                }
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (Gw.Button("?"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.OpenPopup("##colshelp");
        Gw.Tooltip("What each column means"u8);

        if (ImGui.BeginPopup("##colshelp"))
        {
            Theme.Colored(Theme.Text, "Column meanings");
            Theme.Faint("Hover a row for that row's numbers.");
            ImGui.Separator();

            if (ImGui.BeginChild("##colshelpbody", new Vector2(430f, 380f)))
            {
                foreach (var (label, meaning) in ColumnMeanings)
                {
                    ImGui.AlignTextToFramePadding();
                    Theme.Colored(Theme.Accent, label);
                    ImGui.SameLine(74f);
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 310f);
                    ImGui.TextWrapped(meaning);
                    ImGui.PopTextWrapPos();
                    ImGui.Spacing();
                }
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }

        if (toolbarHasBar)
            ImGui.SetCursorScreenPos(new Vector2(toolbarBarMin.X, toolbarBarMin.Y + toolbarBarHeight + 6f));

        // Secondary line only when there is a selection, an undo or a diff to report.
        var undoAvailable = this.undoSlot is not null && (DateTime.UtcNow - this.undoUtc).TotalSeconds < 10;
        var diff = this.plugin.State.Diff is { HasAnything: true } changes ? changes : null;

        if (this.multiSelected.Count > 0 || undoAvailable || diff is not null)
        {
            ImGui.NewLine();
            ImGui.AlignTextToFramePadding();
            var first = true;

            if (this.multiSelected.Count > 0)
            {
                if (Gw.Button($"pin {this.multiSelected.Count}", ButtonStyle.Plain, ButtonSize.Small))
                {
                    foreach (var (itemId, pinQuality) in this.multiSelected)
                    {
                        if (!this.IsSlotPinned(itemId, pinQuality))
                            this.TogglePin(itemId, pinQuality);
                    }

                    this.multiSelected.Clear();
                }

                ImGui.SameLine();
                if (Gw.Button("compare"u8, ButtonStyle.Plain, ButtonSize.Small))
                    this.plugin.ToggleCompare();

                ImGui.SameLine();
                if (Gw.Button("x"u8, ButtonStyle.Plain, ButtonSize.Small))
                    this.multiSelected.Clear();

                first = false;
            }

            if (undoAvailable)
            {
                if (!first)
                    ImGui.SameLine();

                if (Gw.Button("undo unpin"u8, ButtonStyle.Plain, ButtonSize.Small))
                {
                    var restored = this.undoSlot;
                    if (restored is not null)
                        this.Config.Pinned.Add(restored);

                    this.Config.Normalize();
                    this.plugin.SaveConfig();
                    this.undoSlot = null;
                }

                first = false;
            }

            if (diff is not null)
            {
                if (!first)
                    ImGui.SameLine();

                Theme.Faint(
                    $"since last scan: {diff.Drops.Count} down, {diff.Rises.Count} up, {diff.NewEntries.Count} new");
            }
        }

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.IsAnyItemActive() && rows.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
                this.selectedRow = Math.Min(rows.Count - 1, this.selectedRow + 1);
            else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
                this.selectedRow = Math.Max(0, this.selectedRow - 1);

            if (this.selectedRow >= 0 && this.selectedRow < rows.Count && ImGui.IsKeyPressed(ImGuiKey.Enter))
                this.TogglePin(rows[this.selectedRow].ItemId, rows[this.selectedRow].Quality);
        }

        Span<int> visibleColumns = stackalloc int[ColumnLabels.Length];
        var visibleCount = 0;
        for (var columnIndex = 0; columnIndex < ColumnLabels.Length; columnIndex++)
        {
            if (this.columnVisible[columnIndex])
                visibleColumns[visibleCount++] = columnIndex;
        }

        if (visibleCount == 0)
        {
            Theme.Faint("every column is hidden - tick one in \"columns\"");
            return;
        }

        var tableFlags = GwTableFlags.Sortable | GwTableFlags.Resizable | GwTableFlags.ScrollY |
                         GwTableFlags.Zebra | GwTableFlags.Selectable;

        // The table still fills the window; Glasswork sizes are logical units, so hand the height back
        // unscaled instead of letting the Look scale it a second time.
        var tableHeight = TableHeight() / MathF.Max(0.01f, Frame.Scale);
        if (!Gw.BeginTable("opportunities"u8, visibleCount, tableFlags, new Vector2(0f, tableHeight)))
            return;

        for (var index = 0; index < visibleCount; index++)
        {
            var column = visibleColumns[index];
            Gw.TableColumn(
                column == PinColumn ? "##pin" : ColumnLabels[column],
                ColumnWidths[column],
                ColumnStretch[column],
                column == this.sortColumn,
                ColumnNumeric[column]);
        }

        var visibleMask = 0;
        for (var index = 0; index < visibleCount; index++)
            visibleMask |= 1 << visibleColumns[index];

        // ImGui starts a sortable table on a column and direction of its own, and reloads its saved column
        // settings (including a stale sort column) when the visible set changes. Treat those frames as a
        // re-initialisation instead of a click on a header, and put the saved sort in before the header
        // row is laid out so its chevron is right on the first frame too.
        var tableFresh = ImGui.GetFrameCount() - this.opportunityTableFrame > 1 ||
                         visibleMask != this.opportunityVisibleMask;
        this.opportunityTableFrame = ImGui.GetFrameCount();
        this.opportunityVisibleMask = visibleMask;

        if (tableFresh)
            this.RepairTableSort(visibleColumns, visibleCount);

        Gw.TableHeaders();

        if (Gw.TableSortChanged(out var visibleColumn, out var descending))
        {
            if (!tableFresh && visibleColumn >= 0 && visibleColumn < visibleCount)
            {
                var dataColumn = visibleColumns[visibleColumn];
                var ascending = !descending;
                if (dataColumn == PinColumn)
                {
                    // The trailing action column never had a sort of its own; keep the current order.
                    this.RepairTableSort(visibleColumns, visibleCount);
                }
                else if (dataColumn != this.sortColumn || ascending != this.sortAscending)
                {
                    this.sortColumn = dataColumn;
                    this.sortAscending = ascending;
                    this.Config.SortColumn = this.sortColumn;
                    this.Config.SortAscending = this.sortAscending;
                    this.plugin.SaveConfig();
                }
            }
        }

        var limit = Math.Min(rows.Count, 250);
        var maxMagnitude = 1m;
        for (var i = 0; i < limit; i++)
        {
            var magnitude = Math.Abs(rows[i].Score.ProfitPerDay);
            if (magnitude > maxMagnitude)
                maxMagnitude = magnitude;
        }

        this.maxProfitMagnitude = maxMagnitude;

        var tableClipper = new GwTableClipper(limit);
        while (tableClipper.Step(out var first, out var last))
        {
            for (var i = first; i < last; i++)
                this.DrawRow(rows[i], i, visibleColumns);
        }

        Gw.EndTable();

        if (rows.Count > limit)
            Theme.Faint($"showing the first {limit} of {rows.Count} - narrow the filter to see the rest");
    }

    private SlotCandidate? BestCandidate(IReadOnlyList<SlotCandidate> candidates)
    {
        SlotCandidate? best = null;
        var bestProfit = decimal.MinValue;

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.Score.IsDead || candidate.Score.IsSuspect)
                continue;

            if (candidate.Score.ProfitPerDay > bestProfit)
            {
                bestProfit = candidate.Score.ProfitPerDay;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// The one figure the window exists to answer: your best slot right now. Drawn as a card
    /// above the table so the tab has a clear hero instead of opening on a wall of rows.
    /// </summary>
    private void DrawBestPick(SlotCandidate candidate)
    {
        var score = candidate.Score;
        var info = this.plugin.Catalog.Get(candidate.ItemId);
        var pinned = this.IsSlotPinned(candidate.ItemId, candidate.Quality);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.WithAlpha(Theme.Accent, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.WithAlpha(Theme.Accent, 0.32f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, this.cardFade);

        if (ImGui.BeginChild("##bestpick", new Vector2(0f, 84f), true))
        {
            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();

            Theme.Colored(Theme.Accent, "BEST PICK");

            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - 46f);
            if (ImGui.SmallButton(pinned ? "unpin" : "pin"))
                this.TogglePin(candidate.ItemId, candidate.Quality);

            UiKit.ItemIcon(UiKit.IconIdForItem(candidate.ItemId), candidate.Quality == Quality.Hq, 30f);
            ImGui.SameLine(0f, 8f);
            Theme.Colored(Theme.Text, info.Name);
            if (candidate.Quality == Quality.Hq)
            {
                ImGui.SameLine(0f, 6f);
                Theme.Colored(Theme.Hq, "HQ");
            }

            ImGui.SameLine(0f, 20f);
            var ease = 1f - MathF.Exp(-7f * ImGui.GetIO().DeltaTime);
            this.animatedBestPickProfit += ((float)score.ProfitPerDay - this.animatedBestPickProfit) * ease;
            var profitText = "+" + Money.Gil((decimal)this.animatedBestPickProfit);
            var profitPos = ImGui.GetCursorScreenPos() + new Vector2(0f, -4f);
            drawList.AddText(font, 20f, profitPos, Theme.U32(Theme.Profit), profitText);
            var profitWidth = ImGui.CalcTextSize(profitText).X * (20f / ImGui.GetFontSize());
            ImGui.Dummy(new Vector2(profitWidth + 4f, 22f));
            ImGui.SameLine(0f, 2f);
            Theme.Faint("gil/day per slot");

            var cost = score.CostPerUnit is { } unitCost ? Money.Gil(unitCost) : "-";
            Theme.Faint(
                $"price {Money.Gil(score.ListPrice)}   cost {cost}   {score.Quote.DailyVelocity:0.0} sales/day   clears in {Money.Days(score.DaysToClear)}   share {Money.Percent(score.ExpectedShare)}");
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);

        ImGui.Spacing();
    }

    private static (string Label, Vector4 Color) VerdictChip(string verdict)
    {
        if (verdict.Contains("fast"))
            return ("fast", Theme.Profit);
        if (verdict.Contains("steady"))
            return ("steady", Theme.Accent);
        if (verdict.Contains("suspicious") || verdict.Contains("no sales"))
            return ("?", Theme.Warn);
        if (verdict.Contains("dead") || verdict.Contains("unsold"))
            return ("dead", Theme.Danger);
        return ("slow", Theme.TextDim);
    }

    private FilterPreset CurrentPreset(string name) => new()
    {
        Name = name,
        Search = this.search,
        MinGilPerDay = this.minGilPerDay,
        Quality = this.qualityFilter,
        PinnedOnly = this.pinnedOnly,
        ShowDead = this.showDead,
        AffordableOnly = this.affordableOnly,
        HideStale = this.hideStale,
    };

    private void ApplyPreset(FilterPreset preset)
    {
        this.search = preset.Search;
        this.minGilPerDay = preset.MinGilPerDay;
        this.qualityFilter = preset.Quality;
        this.pinnedOnly = preset.PinnedOnly;
        this.showDead = preset.ShowDead;
        this.affordableOnly = preset.AffordableOnly;
        this.hideStale = preset.HideStale;
    }

    private void DrawRow(SlotCandidate candidate, int index, ReadOnlySpan<int> visibleColumns)
    {
        var score = candidate.Score;
        var info = this.plugin.Catalog.Get(candidate.ItemId);
        var pinned = this.IsSlotPinned(candidate.ItemId, candidate.Quality);
        var dim = score.IsDead;
        var key = (candidate.ItemId, candidate.Quality);
        var rowHeight = Frame.Look.RowHeight * Frame.Scale;

        var rowClicked = Gw.TableRow(index, index == this.selectedRow);
        var rowHovered = ImGui.IsItemHovered();
        var rowOrigin = ImGui.GetCursorScreenPos();

        // The row selectable paints hover and selection itself; a multi-selection is ours to tint.
        if (this.multiSelected.Contains(key))
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                rowOrigin,
                rowOrigin + new Vector2(ImGui.GetContentRegionAvail().X, rowHeight),
                Theme.WithAlpha(Theme.Accent, 0.16f),
                2f);
        }

        if (rowClicked && rowHovered)
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                if (!this.multiSelected.Add(key))
                    this.multiSelected.Remove(key);
            }
            else
            {
                this.detailItemId = candidate.ItemId;
                this.detailQuality = candidate.Quality;
                this.openDetail = true;
            }
        }

        if (ImGui.BeginPopupContextItem($"##ctx{candidate.ItemId}{candidate.Quality}"))
        {
            if (ImGui.MenuItem("Details..."))
            {
                this.detailItemId = candidate.ItemId;
                this.detailQuality = candidate.Quality;
                this.openDetail = true;
            }

            if (ImGui.MenuItem(pinned ? "Unpin" : "Pin to my slots"))
                this.TogglePin(candidate.ItemId, candidate.Quality);

            if (ImGui.MenuItem(this.IsWatched(candidate.ItemId, candidate.Quality) ? "Unwatch" : "Watch for alerts"))
                this.ToggleWatch(candidate.ItemId, candidate.Quality);

            if (ImGui.MenuItem("Copy item name"))
                ImGui.SetClipboardText(info.Name);

            if (ImGui.MenuItem("Open on Universalis"))
                OpenUrl($"https://universalis.app/market/{candidate.ItemId}");

            ImGui.EndPopup();
        }

        foreach (var dataColumn in visibleColumns)
        {
            Gw.TableCell();

            switch (dataColumn)
            {
                case 0:
                {
                    var nameColor = dim ? Theme.TextFaint : Theme.Text;
                    UiKit.ItemIcon(UiKit.IconIdForItem(candidate.ItemId), candidate.Quality == Quality.Hq, 18f);
                    ImGui.SameLine(0f, 6f);
                    Theme.Colored(nameColor, info.Name);
                    var nameHovered = ImGui.IsItemHovered();

                    if (score.IsStale)
                    {
                        ImGui.SameLine();
                        Theme.Colored(Theme.Warn, "!");
                    }

                    if (score.Verdict.Length > 0 && ImGui.GetContentRegionAvail().X > 80f)
                    {
                        ImGui.SameLine(0f, 8f);
                        var (verdictLabel, verdictColor) = VerdictChip(score.Verdict);
                        UiKit.Chip(verdictLabel, verdictColor);

                        ImGui.SameLine(0f, 6f);
                        UiKit.ConfidenceDots(score.Confidence);
                    }

                    if (nameHovered)
                    {
                        var tip = score.Explain;
                        if (score.CostPerUnit is { } tipCost)
                            tip += $"\ncost {Money.Exact(tipCost)} / unit ({score.CostSource})";
                        if (score.CostDetail.Length > 0)
                            tip += $"\n{score.CostDetail}";
                        if (score.CostIsPartial)
                            tip += "\ncost is a lower bound: some materials had no price";
                        if (score.Quote.PriceHistory.Length >= 2)
                            tip += $"\n30-day trend {score.TrendPercent:+0.0%;-0.0%;0%}";
                        tip += $"\ndata {score.Quote.DataAgeMinutes:0} min old, {score.Quote.HistorySamples} sales sampled, confidence {score.Confidence:P0}";
                        ImGui.SetTooltip(tip);
                    }

                    break;
                }

                case 1:
                    Gw.TableText(
                        candidate.Quality == Quality.Hq ? "HQ" : "NQ",
                        TextRole.Body,
                        Theme.ToColor(candidate.Quality == Quality.Hq ? Theme.Hq : Theme.TextDim));
                    break;

                case 2:
                    Gw.TableText(Money.Gil(score.ListPrice), TextRole.Body, Theme.ToColor(dim ? Theme.TextFaint : Theme.Text));
                    break;

                case 3:
                {
                    var costText = score.CostPerUnit is { } unitCost
                        ? Money.Gil(unitCost) + (score.CostIsPartial ? "?" : string.Empty)
                        : "-";
                    Gw.TableText(costText, TextRole.Body, Theme.ToColor(dim ? Theme.TextFaint : score.CostIsPartial ? Theme.Warn : Theme.TextDim));
                    break;
                }

                case 4:
                    Gw.TableText(dim ? "-" : Money.Gil(score.GilPerDay), TextRole.Body, Theme.ToColor(dim ? Theme.TextFaint : Theme.TextDim));
                    break;

                case 5:
                {
                    var profit = score.ProfitPerDay;
                    // Profit is the one figure that leads a row; every other number stays neutral.
                    var profitColor = dim ? Theme.TextFaint : profit <= 0m ? Theme.Danger : Theme.Profit;
                    if (!dim && this.maxProfitMagnitude > 0m)
                    {
                        var heat = Math.Clamp((float)(Math.Abs(profit) / this.maxProfitMagnitude), 0f, 1f);
                        if (heat > 0f)
                        {
                            var cellWidth = ImGui.GetContentRegionAvail().X;
                            var heatOrigin = ImGui.GetCursorScreenPos();
                            var heatHeight = ImGui.GetTextLineHeight();
                            var heatTop = heatOrigin.Y + MathF.Max(0f, (rowHeight - heatHeight) * 0.5f);
                            var heatColor = profit > 0m ? Theme.Profit : Theme.Danger;
                            ImGui.GetWindowDrawList().AddRectFilled(
                                new Vector2(heatOrigin.X, heatTop),
                                new Vector2(heatOrigin.X + (cellWidth * heat), heatTop + heatHeight),
                                Theme.WithAlpha(heatColor, 0.16f),
                                2f);
                        }
                    }

                    Gw.TableText(dim ? "-" : Money.Gil(profit), TextRole.Body, Theme.ToColor(profitColor));
                    break;
                }

                case 6:
                    Gw.TableText(score.Quote.DailyVelocity.ToString("0.0"), TextRole.Body, Theme.ToColor(dim ? Theme.TextFaint : Theme.Text));
                    break;

                case 7:
                    Gw.TableText(candidate.Quote.DistinctSellers.ToString(), TextRole.Body, Theme.ToColor(dim ? Theme.TextFaint : Theme.Text));
                    break;

                case 8:
                {
                    var share = (float)score.ExpectedShare;
                    var shareOrigin = ImGui.GetCursorScreenPos() + new Vector2(0f, 4f);
                    UiKit.Bar(ImGui.GetWindowDrawList(), shareOrigin, new Vector2(18f, 6f), share, dim ? Theme.TextFaint : Theme.Accent);
                    ImGui.Dummy(new Vector2(20f, 14f));
                    ImGui.SameLine(0f, 3f);
                    Theme.Colored(dim ? Theme.TextFaint : Theme.TextDim, Money.Percent(score.ExpectedShare));
                    break;
                }

                case 9:
                    Gw.TableText(Money.Gil(score.CapitalPerSlot), TextRole.Body, Theme.ToColor(dim ? Theme.TextFaint : Theme.TextDim));
                    break;

                case 10:
                {
                    var clearColor = dim ? Theme.Danger : score.DaysToClear <= 14 ? Theme.Text : Theme.TextDim;
                    Gw.TableText(Money.Days(score.DaysToClear), TextRole.Body, Theme.ToColor(clearColor));
                    break;
                }

                case 11:
                {
                    var series = candidate.Quote.PriceHistory;
                    var sparkPosition = ImGui.GetCursorScreenPos();
                    var sparkSize = new Vector2(104f, 24f);
                    ImGui.Dummy(sparkSize);
                    ImGui.GetWindowDrawList().AddRectFilled(sparkPosition, sparkPosition + sparkSize, Theme.WithAlpha(Theme.PanelRaised, 0.40f), 4f);

                    if (series.Length >= 2)
                    {
                        var sparkColor = score.IsFalling ? Theme.Danger : score.TrendPercent > 0.05 ? Theme.Accent : Theme.TextDim;
                        var sparkList = ImGui.GetWindowDrawList();
                        var sparkClip = sparkPosition + new Vector2(sparkSize.X * this.sparkProgress, sparkSize.Y);
                        sparkList.PushClipRect(sparkPosition, sparkClip, true);
                        DrawSparkline(sparkList, sparkPosition, sparkSize, series, Theme.WithAlpha(sparkColor, 0.9f));
                        sparkList.PopClipRect();

                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip($"30-day trend {score.TrendPercent:+0.0%;-0.0%;0%}\n(quantity-weighted daily prices)");
                    }

                    break;
                }

                case 12:
                    Gw.TableText(Money.Gil(score.ProfitPerDay * (decimal)score.DaysToClear), TextRole.Body, Theme.ToColor(dim ? Theme.TextFaint : Theme.TextDim));
                    break;

                case 13:
                    ImGui.PushID((int)candidate.ItemId * 2 + (candidate.Quality == Quality.Hq ? 1 : 0));
                    if (ImGui.SmallButton(pinned ? "unpin" : "pin"))
                        this.TogglePin(candidate.ItemId, candidate.Quality);
                    ImGui.PopID();
                    break;
            }
        }
    }

    private static void DrawSparkline(ImDrawListPtr drawList, Vector2 position, Vector2 size, ReadOnlySpan<decimal> series, uint color)
    {
        var min = decimal.MaxValue;
        var max = decimal.MinValue;

        foreach (var value in series)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        var middle = position.Y + (size.Y / 2f);

        if (max <= min)
        {
            drawList.AddLine(new Vector2(position.X, middle), new Vector2(position.X + size.X, middle), color, 1.2f);
            return;
        }

        var span = max - min;
        var step = size.X / (series.Length - 1);
        var previous = new Vector2(
            position.X,
            position.Y + size.Y - (float)((series[0] - min) / span) * size.Y);

        for (var i = 1; i < series.Length; i++)
        {
            var point = new Vector2(
                position.X + (step * i),
                position.Y + size.Y - (float)((series[i] - min) / span) * size.Y);

            drawList.AddLine(previous, point, color, 1.2f);
            previous = point;
        }
    }

    // ---------------------------------------------------------------- portfolio ----

    private void DrawPortfolio()
    {
        var config = this.Config;
        var result = this.plugin.State.Result;

        Theme.Dimmed("A slot holds one item. Given N slots and a pile of gil you are willing to keep in inventory,");
        Theme.Dimmed("this picks the combination that earns the most per day.");
        ImGui.Spacing();

        var slots = config.PortfolioSlots;
        var budgetMillions = (int)Math.Clamp(config.PortfolioBudget / 1_000_000m, 0m, 9999m);
        var changed = false;

        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("slots", ref slots))
        {
            config.PortfolioSlots = slots;
            changed = true;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("budget (M gil)", ref budgetMillions))
        {
            config.PortfolioBudget = budgetMillions * 1_000_000L;
            changed = true;
        }

        if (changed)
        {
            config.Normalize();
            this.plugin.SaveConfig();
        }

        ImGui.Spacing();

        var source = (result?.Candidates ?? Array.Empty<SlotCandidate>())
            .Where(c => !c.Score.IsDead && !c.Score.IsSuspect && c.Score.ProfitPerDay > 0m)
            .GroupBy(c => (c.ItemId, c.Quality))
            .Select(g => g.First())
            .ToList();

        if (source.Count == 0)
        {
            Theme.Dimmed("Scan first - there is nothing to allocate yet.");
            return;
        }

        // Prefer fresh data. If there is not enough of it to fill the portfolio, fall back to
        // everything rather than showing an empty plan on a quiet world.
        var freshSource = source.Where(c => !c.Score.IsStale).ToList();
        var usingFresh = freshSource.Count >= config.PortfolioSlots;
        var chosen = usingFresh ? freshSource : source;

        var candidates = chosen
            .Select(c => new PortfolioCandidate(c.ItemId, c.Quality, c.Score.CapitalPerSlot, c.Score.ProfitPerDay))
            .ToList();

        var plan = PortfolioOptimizer.Optimize(candidates, config.PortfolioSlots, (decimal)config.PortfolioBudget);

        if (usingFresh)
            Theme.Faint($"using {freshSource.Count} items with fresh data");
        else
            Theme.Colored(Theme.Warn, $"only {freshSource.Count} fresh items for {config.PortfolioSlots} slots; stale data included");

        if (Gw.Button("copy plan"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.SetClipboardText(this.BuildPortfolioCsv(plan.Picks));

        this.DrawPortfolioSummaryCard(plan);

        if (plan.Truncated)
            Theme.Colored(Theme.Warn, "Large candidate set: the plan is near-optimal rather than provably optimal.");

        ImGui.Spacing();

        var planQuotes = chosen
            .GroupBy(c => (c.ItemId, c.Quality))
            .ToDictionary(g => g.Key, g => g.First().Quote);

        // The table still fills the window; Glasswork sizes are logical units, so hand the height back
        // unscaled instead of letting the Look scale it a second time.
        var tableHeight = TableHeight() / MathF.Max(0.01f, Frame.Scale);
        if (!Gw.BeginTable("plan"u8, 7, GwTableFlags.Sortable | GwTableFlags.ScrollY | GwTableFlags.Zebra, new Vector2(0f, tableHeight)))
            return;

        Gw.TableColumn("#", 28f, stretch: false, defaultSort: this.portfolioSortColumn == 0, numeric: true);
        Gw.TableColumn("Item", 2.4f, defaultSort: this.portfolioSortColumn == 1);
        Gw.TableColumn("Q", 26f, stretch: false, defaultSort: this.portfolioSortColumn == 2);
        Gw.TableColumn("Sales/d", 52f, stretch: false, defaultSort: this.portfolioSortColumn == 3, numeric: true);
        Gw.TableColumn("Sellers", 48f, stretch: false, defaultSort: this.portfolioSortColumn == 4, numeric: true);
        Gw.TableColumn("Profit/day", 84f, stretch: false, defaultSort: this.portfolioSortColumn == 5, numeric: true);
        Gw.TableColumn("Capital", 84f, stretch: false, defaultSort: this.portfolioSortColumn == 6, numeric: true);

        // ImGui starts a sortable table on a column of its own and reloads its saved sort when the table
        // (re)initialises; treat that frame as a re-initialisation, not as a header click, and put the
        // current direction in before the header row is laid out.
        var tableFresh = ImGui.GetFrameCount() - this.portfolioTableFrame > 1;
        this.portfolioTableFrame = ImGui.GetFrameCount();

        if (tableFresh)
            this.RepairPortfolioSort();

        Gw.TableHeaders();

        if (Gw.TableSortChanged(out var sortColumn, out var descending))
        {
            if (!tableFresh && sortColumn >= 0 && sortColumn < 7)
            {
                var ascending = !descending;
                if (sortColumn != this.portfolioSortColumn || ascending != this.portfolioSortAscending)
                {
                    this.portfolioSortColumn = sortColumn;
                    this.portfolioSortAscending = ascending;
                }
            }
        }

        // The plan is rebuilt every frame; sort a reused copy so the plan's own order is left alone.
        this.portfolioRows.Clear();
        for (var index = 0; index < plan.Picks.Count; index++)
            this.portfolioRows.Add(plan.Picks[index]);

        this.portfolioSortQuotes = planQuotes;

        if (this.portfolioSortColumn != 0)
            this.portfolioRows.Sort(this.PortfolioComparisons[this.portfolioSortColumn]);

        // Column 0 is the rank, which is the plan's own order; a descending rank is that order reversed.
        if (!this.portfolioSortAscending)
            this.portfolioRows.Reverse();

        var tableClipper = new GwTableClipper(this.portfolioRows.Count);
        while (tableClipper.Step(out var first, out var last))
        {
            for (var index = first; index < last; index++)
            {
                var pick = this.portfolioRows[index];
                var quote = planQuotes.GetValueOrDefault((pick.ItemId, pick.Quality));

                Gw.TableRow(index);

                Gw.TableCell();
                Gw.TableText($"{index + 1}.", TextRole.Body, Theme.ToColor(Theme.TextFaint));

                Gw.TableCell();
                UiKit.ItemIcon(UiKit.IconIdForItem(pick.ItemId), pick.Quality == Quality.Hq, 16f);
                ImGui.SameLine(0f, 6f);
                Theme.Colored(Theme.Text, this.plugin.Catalog.Name(pick.ItemId));

                Gw.TableCell();
                Gw.TableText(pick.Quality == Quality.Hq ? "HQ" : "NQ", TextRole.Body, Theme.ToColor(pick.Quality == Quality.Hq ? Theme.Hq : Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(quote is null ? "-" : quote.DailyVelocity.ToString("0.0"), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(quote is null ? "-" : quote.DistinctSellers.ToString(), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(Money.Gil(pick.GilPerDay), TextRole.Body, Theme.ToColor(Theme.Profit));

                Gw.TableCell();
                Gw.TableText(Money.Gil(pick.CapitalPerSlot), TextRole.Body, Theme.ToColor(Theme.TextDim));
            }
        }

        Gw.EndTable();

        ImGui.Spacing();
        ImGui.Separator();
        this.DrawPinned(result);
    }

    /// <summary>The portfolio's headline figure, drawn as the same card the best pick uses.</summary>
    private void DrawPortfolioSummaryCard(PortfolioPlan plan)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.WithAlpha(Theme.Accent, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.WithAlpha(Theme.Accent, 0.32f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, this.cardFade);

        if (ImGui.BeginChild("##portfoliocard", new Vector2(0f, 80f), true))
        {
            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();

            Theme.Colored(Theme.Accent, "BEST PORTFOLIO");

            var ease = 1f - MathF.Exp(-7f * ImGui.GetIO().DeltaTime);
            this.animatedPortfolioProfit += ((float)plan.GilPerDay - this.animatedPortfolioProfit) * ease;
            var profitText = "+" + Money.Gil((decimal)this.animatedPortfolioProfit);
            var profitPos = ImGui.GetCursorScreenPos() + new Vector2(0f, -3f);
            drawList.AddText(font, 20f, profitPos, Theme.U32(Theme.Profit), profitText);
            var profitWidth = ImGui.CalcTextSize(profitText).X * (20f / ImGui.GetFontSize());
            ImGui.Dummy(new Vector2(profitWidth + 4f, 22f));
            ImGui.SameLine(0f, 2f);
            Theme.Faint("profit/day");

            Theme.Faint(
                $"{plan.Picks.Count} slots   {Money.Gil(plan.Capital)} tied up   weakest {Money.Gil(plan.ShadowSlotValue)}/day   +1M gil adds {Money.Gil(plan.MarginalGilPerMillion)}/day");
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);

        ImGui.Spacing();
    }

    private void DrawPinned(ScanResult? result)
    {
        var pinned = this.Config.Pinned;
        Theme.Colored(Theme.Text, $"My slots ({pinned.Count})");

        ImGui.SameLine();
        if (Gw.Button("share"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.SetClipboardText(this.plugin.BuildShareCode());

        ImGui.SameLine();
        if (Gw.Button("import"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.OpenPopup("##importslots");

        if (ImGui.BeginPopup("##importslots"))
        {
            ImGui.InputTextWithHint("##slotcode", "paste a share code", ref this.shareInput, 512);
            if (ImGui.Button("import"))
            {
                this.plugin.ImportShareCode(this.shareInput);
                this.shareInput = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (pinned.Count == 0)
        {
            Theme.Faint("Pin items from the Opportunities tab to model the slots you actually keep stocked.");
            return;
        }

        decimal total = 0m;

        for (var index = 0; index < pinned.Count; index++)
        {
            var slot = pinned[index];
            var candidate = result?.Candidates.FirstOrDefault(c => c.ItemId == slot.ItemId && c.Quality == (slot.Hq ? Quality.Hq : Quality.Nq));
            var name = this.plugin.Catalog.Name(slot.ItemId);

            ImGui.PushID((int)slot.ItemId * 2 + (slot.Hq ? 1 : 0));

            // Custom reorder: slot order is real (slot 1, slot 2, ...), so it gets handles rather
            // than a ReorderList, which would drop the icon, profit and verdict cells.
            Theme.Faint($"{index + 1}.");
            ImGui.SameLine(0f, 4f);
            if (ImGui.SmallButton("^##up") && index > 0)
            {
                (pinned[index - 1], pinned[index]) = (pinned[index], pinned[index - 1]);
                this.plugin.SaveConfig();
            }

            ImGui.SameLine(0f, 2f);
            if (ImGui.SmallButton("v##down") && index < pinned.Count - 1)
            {
                (pinned[index + 1], pinned[index]) = (pinned[index], pinned[index + 1]);
                this.plugin.SaveConfig();
            }

            ImGui.SameLine(0f, 6f);

            if (candidate is null)
            {
                UiKit.ItemIcon(UiKit.IconIdForItem(slot.ItemId), slot.Hq, 16f);
                ImGui.SameLine(0f, 6f);
                Theme.Colored(Theme.Warn, $"  {name}  -  not in the last scan (pin it again after a rescan)");
            }
            else
            {
                var score = SlotModel.Score(candidate.Quote, new SlotParameters
                {
                    Strategy = this.Config.Strategy,
                    TaxRate = this.Config.TaxRate,
                    StackSize = slot.StackSize,
                    DeadAfterDays = this.Config.DeadAfterDays,
                    MinimumVelocity = this.Config.MinimumVelocity,
                });

                score = SlotModel.ApplyCost(score, candidate.Cost, expectCost: this.Config.CostMode != CostMode.Revenue, stackSize: slot.StackSize);
                total += score.ProfitPerDay;

                if (ImGui.SmallButton("x"))
                {
                    pinned.Remove(slot);
                    this.plugin.SaveConfig();
                    ImGui.PopID();
                    break;
                }

                ImGui.SameLine();
                UiKit.ItemIcon(UiKit.IconIdForItem(slot.ItemId), slot.Hq, 16f);
                ImGui.SameLine(0f, 6f);
                var color = score.IsDead || score.ProfitPerDay <= 0m ? Theme.Danger : Theme.Text;
                Theme.Colored(color, $"{name,-34} {Money.Gil(score.ProfitPerDay),8}/day  {Money.Days(score.DaysToClear),6}  {score.Verdict}");
            }

            ImGui.PopID();
        }

        if (total > 0m)
            Theme.Colored(Theme.Accent, $"Pinned total: {Money.Gil(total)}/day");
    }

    // --------------------------------------------------------------------- craft ----

    /// <summary>The best recipe your bags can make right now, framed like the other hero cards.</summary>
    private void DrawCraftSummaryCard(CraftOpportunity best, int count)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.WithAlpha(Theme.Accent, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.WithAlpha(Theme.Accent, 0.32f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, this.cardFade);

        if (ImGui.BeginChild("##craftcard", new Vector2(0f, 80f), true))
        {
            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();

            Theme.Colored(Theme.Accent, "BEST CRAFT");

            UiKit.ItemIcon(UiKit.IconIdForItem(best.ItemId), false, 30f);
            ImGui.SameLine(0f, 8f);
            Theme.Colored(Theme.Text, this.plugin.Catalog.Name(best.ItemId));

            ImGui.SameLine(0f, 20f);
            var profitText = "+" + Money.Gil(best.Profit);
            var profitPos = ImGui.GetCursorScreenPos() + new Vector2(0f, -4f);
            drawList.AddText(font, 20f, profitPos, Theme.U32(Theme.Profit), profitText);
            var profitWidth = ImGui.CalcTextSize(profitText).X * (20f / ImGui.GetFontSize());
            ImGui.Dummy(new Vector2(profitWidth + 4f, 22f));
            ImGui.SameLine(0f, 2f);
            Theme.Faint("profit/craft");

            Theme.Faint($"{count} recipes ready   {best.Crafts} crafts   {best.Produced} units");
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(5);
        ImGui.PopStyleColor(2);

        ImGui.Spacing();
    }

    private void DrawCraft()
    {
        Theme.Dimmed(
            "Recipes whose materials you already hold - bags, saddlebag and the retainer you have open - " +
            "priced against the last scan. Profit subtracts " +
            "what those materials would fetch if sold instead; Owned is the revenue if you count them as free.");

        if (Gw.Button("Read stock"u8, ButtonStyle.Prominent, ButtonSize.Small))
            this.plugin.RefreshCraftPlan();

        ImGui.SameLine();
        if (this.plugin.CraftPlanUtc is { } readAt)
        {
            var retainer = string.IsNullOrEmpty(this.plugin.CraftRetainerName)
                ? "no retainer open"
                : this.plugin.CraftRetainerName;

            Theme.Faint(
                $"{this.plugin.CraftOwnedItemCount} item(s) from {this.plugin.CraftBagStacks} bag / " +
                $"{this.plugin.CraftSaddlebagStacks} saddlebag / {this.plugin.CraftRetainerStacks} retainer stack(s) " +
                $"({retainer}) at {readAt.ToLocalTime():HH:mm}");
        }
        else
            Theme.Faint("nothing read yet");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(170f);
        var craftFloorSlider = (int)Math.Clamp(this.Config.CraftMinProfit, 0, 5_000_000);
        if (ImGui.SliderInt("##craftfloor", ref craftFloorSlider, 0, 5_000_000, craftFloorSlider <= 0 ? "any profit" : "min %d profit"))
        {
            this.Config.CraftMinProfit = craftFloorSlider;
            this.plugin.SaveConfig();
        }

        if (this.plugin.IsPricingCraft)
        {
            ImGui.SameLine();
            Theme.Colored(Theme.Warn, "pricing candidates...");
        }

        var allIdeas = this.plugin.CraftPlan;

        if (allIdeas.Count == 0)
        {
            UiKit.EmptyState(
                this.plugin.IsPricingCraft ? "Pricing your bags..." : "No recipes ready",
                this.plugin.IsPricingCraft
                    ? "Pricing the items your bags can make - this fills in on its own."
                    : "No fully matchable recipes yet. Read your bags again after gathering, or check that this character holds the materials.");
            return;
        }

        var craftFloor = (decimal)this.Config.CraftMinProfit;
        var plan = craftFloor > 0m ? allIdeas.Where(idea => idea.Profit >= craftFloor).ToList() : allIdeas;

        if (plan.Count == 0)
        {
            if (UiKit.EmptyState(
                    "Nothing above the floor",
                    "Every recipe is below the minimum profit - lower the floor or read stock again.",
                    "Read stock"))
            {
                this.plugin.RefreshCraftPlan();
                Gw.Toast("Reading stock"u8, ToastKind.Info);
            }

            return;
        }

        if (Gw.Button("copy plan"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.SetClipboardText(this.BuildCraftCsv(plan));

        ImGui.Spacing();

        var bestIdea = plan.MaxBy(idea => idea.Profit);
        if (bestIdea is not null && bestIdea.Profit > 0m)
            this.DrawCraftSummaryCard(bestIdea, plan.Count);

        // The table still fills the window; Glasswork sizes are logical units, so hand the height back
        // unscaled instead of letting the Look scale it a second time.
        var tableHeight = TableHeight() / MathF.Max(0.01f, Frame.Scale);
        if (!Gw.BeginTable("craft"u8, 12, GwTableFlags.Sortable | GwTableFlags.ScrollY | GwTableFlags.Zebra, new Vector2(0f, tableHeight)))
            return;

        Gw.TableColumn("Item", 2.4f, defaultSort: this.craftSortColumn == 0);
        Gw.TableColumn("Job", 64f, stretch: false, defaultSort: this.craftSortColumn == 1);
        Gw.TableColumn("Crafts", 52f, stretch: false, defaultSort: this.craftSortColumn == 2, numeric: true);
        Gw.TableColumn("Qty", 44f, stretch: false, defaultSort: this.craftSortColumn == 3, numeric: true);
        Gw.TableColumn("Revenue", 76f, stretch: false, defaultSort: this.craftSortColumn == 4, numeric: true);
        Gw.TableColumn("Mats", 76f, stretch: false, defaultSort: this.craftSortColumn == 5, numeric: true);
        Gw.TableColumn("Profit", 76f, stretch: false, defaultSort: this.craftSortColumn == 6, numeric: true);
        Gw.TableColumn("Per unit", 78f, stretch: false, defaultSort: this.craftSortColumn == 7, numeric: true);
        Gw.TableColumn("Stack", 84f, stretch: false, defaultSort: this.craftSortColumn == 8, numeric: true);
        Gw.TableColumn("HQ", 72f, stretch: false, defaultSort: this.craftSortColumn == 9, numeric: true);
        Gw.TableColumn("Owned", 76f, stretch: false, defaultSort: this.craftSortColumn == 10, numeric: true);
        Gw.TableColumn("Clear", 52f, stretch: false, defaultSort: this.craftSortColumn == 11, numeric: true);

        // Same re-initialisation handling as the other sortable tables.
        var tableFresh = ImGui.GetFrameCount() - this.craftTableFrame > 1;
        this.craftTableFrame = ImGui.GetFrameCount();

        if (tableFresh)
            this.RepairCraftSort();

        Gw.TableHeaders();

        if (Gw.TableSortChanged(out var sortColumn, out var descending))
        {
            if (!tableFresh && sortColumn >= 0 && sortColumn < 12)
            {
                var ascending = !descending;
                if (sortColumn != this.craftSortColumn || ascending != this.craftSortAscending)
                {
                    this.craftSortColumn = sortColumn;
                    this.craftSortAscending = ascending;
                }
            }
        }

        // plan may be the plugin's own CraftPlan list, so sort a reused copy and leave that order alone.
        this.craftRows.Clear();
        for (var index = 0; index < plan.Count; index++)
            this.craftRows.Add(plan[index]);

        if (this.craftSortColumn >= 0)
        {
            this.craftRows.Sort(this.CraftComparisons[this.craftSortColumn]);
            if (!this.craftSortAscending)
                this.craftRows.Reverse();
        }

        var tableClipper = new GwTableClipper(this.craftRows.Count);
        while (tableClipper.Step(out var first, out var last))
        {
            for (var index = first; index < last; index++)
            {
                var idea = this.craftRows[index];

                Gw.TableRow(index);

                Gw.TableCell();
                Theme.Colored(Theme.Text, this.plugin.Catalog.Name(idea.ItemId));
                if (idea.UsesIntermediates)
                {
                    ImGui.SameLine();
                    Theme.Faint("+sub");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("At least one ingredient has to be crafted first, from the same materials.");
                }

                Gw.TableCell();
                var job = this.plugin.Catalog.JobAbbreviation(idea.CraftJob);
                Gw.TableText(
                    job.Length > 0 ? $"{job} {idea.RequiredLevel}" : idea.RequiredLevel.ToString(),
                    TextRole.Body,
                    Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(idea.Crafts.ToString(), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(idea.Produced.ToString(), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(Money.Gil(idea.Revenue), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                if (idea.MaterialsPriced)
                {
                    Gw.TableText(Money.Gil(idea.MaterialValue), TextRole.Body, Theme.ToColor(Theme.TextDim));
                }
                else
                {
                    TableCellText(
                        "partial",
                        TextRole.Body,
                        Theme.Warn,
                        () => "Some materials have no price, so the profit is optimistic.");
                }

                Gw.TableCell();
                Gw.TableText(Money.Gil(idea.Profit), TextRole.Body, Theme.ToColor(idea.Profit > 0m ? Theme.Accent : Theme.Danger));

                var perUnit = CraftPerUnit(idea);

                Gw.TableCell();
                Gw.TableText(Money.Gil(perUnit), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(Money.Gil(perUnit * this.Config.DefaultStackSize), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                if (idea.HqAvailable)
                {
                    TableCellText(
                        Money.Gil(idea.HqProfit),
                        TextRole.Body,
                        idea.HqProfit > idea.Profit ? Theme.Accent : Theme.TextDim,
                        () => $"HQ revenue {Money.Gil(idea.HqRevenue)} at the HQ minimum.");
                }
                else
                {
                    Gw.TableText("-", TextRole.Body, Theme.ToColor(Theme.TextFaint));
                }

                Gw.TableCell();
                Gw.TableText(Money.Gil(idea.OwnedProfit), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(Money.Days(idea.DaysToSell), TextRole.Body, Theme.ToColor(Theme.TextDim));
            }
        }

        Gw.EndTable();
    }

    // -------------------------------------------------------------- my listings ----

    private void DrawMyListings()
    {
        var config = this.Config;
        var listings = config.MyListings;

        var retainers = Retainers.All();
        if (retainers.Count > 0)
        {
            for (var index = 0; index < retainers.Count; index++)
            {
                var retainer = retainers[index];
                var free = Math.Max(0, MarketSlotsPerRetainer - retainer.MarketItemCount);
                var label = $"{retainer.Name} {retainer.MarketItemCount}/{MarketSlotsPerRetainer}";
                var color = free == 0 ? Theme.Warn : free <= 2 ? Theme.AccentBright : Theme.TextDim;

                if (index > 0)
                    ImGui.SameLine(0f, 6f);

                UiKit.Chip(label, color);
            }

            var totalListed = retainers.Sum(r => r.MarketItemCount);
            var totalFree = retainers.Sum(r => Math.Max(0, MarketSlotsPerRetainer - r.MarketItemCount));
            Theme.Faint($"{listings.Count} captured, {totalListed} listed, {totalFree} free market slot(s)");
        }

        Theme.Dimmed(
            $"{listings.Count} of your listings captured. Open the market board on an item you sell - the " +
            "plugin records your own listing from the board or from the retainer sell list and prices it within a second.");

        ImGui.Spacing();

        if (listings.Count == 0)
        {
            if (UiKit.EmptyState(
                    "Nothing captured yet",
                    "Your retainer's listing shows up next to everyone else's when you view that item, and this tab keeps it priced.",
                    "Rescan prices"))
            {
                this.plugin.StartScan();
                Gw.Toast("Scan started"u8, ToastKind.Info);
            }

            return;
        }

        var result = this.plugin.State.Result;
        var quotes = new Dictionary<(uint, Quality), MarketQuote>();
        if (result is not null)
        {
            foreach (var candidate in result.Candidates)
                quotes.TryAdd((candidate.ItemId, candidate.Quality), candidate.Quote);
        }

        var parameters = new SlotParameters
        {
            Strategy = config.Strategy,
            TaxRate = config.TaxRate,
            StackSize = config.DefaultStackSize,
            DeadAfterDays = config.DeadAfterDays,
            MinimumVelocity = config.MinimumVelocity,
        };

        // Grouped by retainer, then by name, so it is obvious which retainer lists what.
        var reviews = listings
            .OrderBy(listing => listing.RetainerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(listing => this.plugin.Catalog.Name(listing.ItemId), StringComparer.OrdinalIgnoreCase)
            .Select(listing =>
            {
                var quality = listing.Hq ? Quality.Hq : Quality.Nq;
                var quote = this.plugin.FindListingQuote(listing.ItemId, quality)
                            ?? quotes.GetValueOrDefault((listing.ItemId, quality));
                return ListingBook.Review(listing, quote, parameters);
            })
            .ToList();

        if (reviews.Any(review => review.Status == ListingStatus.Unpriced))
            this.plugin.RequestListingQuotes(listings.Select(listing => listing.ItemId));

        if (Gw.Button("copy"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.SetClipboardText(this.BuildListingsCsv(reviews));

        if (this.listingSortColumn >= 0)
        {
            Func<ListingReview, IComparable> key = this.listingSortColumn switch
            {
                1 => review => review.Listing.RetainerName,
                2 => review => review.Listing.Hq,
                3 => review => review.Listing.Quantity,
                4 => review => review.Listing.PricePerUnit,
                5 => review => review.MarketMin,
                6 => review => review.PriceDeltaPercent,
                7 => review => review.DaysToSell,
                8 => review => review.Advice,
                _ => review => this.plugin.Catalog.Name(review.Listing.ItemId),
            };

            var comparer = Comparer<IComparable>.Default;
            reviews = this.listingSortAscending
                ? reviews.OrderBy(key, comparer).ToList()
                : reviews.OrderByDescending(key, comparer).ToList();
        }

        var undercut = reviews.Count(review => review.Status == ListingStatus.AboveMarket);
        var probablySold = reviews.Count(review => review.IsProbablySold);
        var healthy = reviews.Count(review => review.IsHealthy);

        Theme.Colored(
            Theme.Text,
            $"{healthy} healthy   {undercut} above market   {probablySold} probably sold");

        var (soldCount, soldTotal) = this.plugin.SoldSince(DateTime.UtcNow.AddDays(-1));
        if (soldCount > 0)
        {
            ImGui.SameLine();
            Theme.Colored(Theme.Accent, $"-   sold in 24h: {soldCount} (~{Money.Gil(soldTotal)})");
        }

        if (Gw.Button("Rescan prices"u8, ButtonStyle.Default, ButtonSize.Small))
            this.plugin.StartScan();

        ImGui.SameLine();
        if (Gw.Button("Copy repricing list"u8, ButtonStyle.Plain, ButtonSize.Small))
            this.CopyRepricingList(reviews);

        ImGui.SameLine();
        if (Gw.Button("Check & reprice"u8, ButtonStyle.Prominent, ButtonSize.Small))
            this.plugin.CheckAllListings();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Visits each retainer, opens every listing, compares it against the live board, and " +
                "reprices the ones that should move. Needs the retainer list open at a bell. " +
                "/slotyield stop cancels.");
        }

        ImGui.SameLine();
        if (Gw.Button("Undercut all -1"u8, ButtonStyle.Tinted, ButtonSize.Small))
            this.confirmUndercutAll = true;

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Sets every listing to one gil under its current market minimum. No floor, no " +
                "threshold, no strategy - it does exactly what it says.");
        }

        if (Gw.Alert(
                "##undercutall"u8,
                ref this.confirmUndercutAll,
                "Undercut every listing?"u8,
                "Sets every listing to one gil under the current market minimum. No floor, no threshold, no undo."u8,
                "undercut all"u8,
                "cancel"u8,
                destructive: true) == DialogResult.Confirmed)
        {
            this.plugin.CheckAllListings(true);
            Gw.Toast("Repricing every listing"u8, ToastKind.Warning);
        }

        ImGui.SameLine();
        if (Gw.Button("Forget all"u8, ButtonStyle.Plain, ButtonSize.Small))
            this.confirmForgetListings = true;

        if (Gw.Alert(
                "##forgetlistings"u8,
                ref this.confirmForgetListings,
                "Forget every listing?"u8,
                "SlotYield drops its own record of your listings. Nothing is changed on the retainers."u8,
                "forget"u8,
                "cancel"u8,
                destructive: true) == DialogResult.Confirmed)
        {
            listings.Clear();
            this.plugin.SaveConfig();
            Gw.Toast("Listing record cleared"u8, ToastKind.Success);
            return;
        }

        ImGui.Spacing();

        if (Gw.TreeNode("List from bags"u8, trailing: $"{this.listingCandidates.Count}"))
        {
            if (Gw.Button("Refresh candidates"u8, ButtonStyle.Plain, ButtonSize.Small))
                this.listingCandidates = this.plugin.GetListingCandidates();

            ImGui.SameLine();
            Theme.Faint("bag items not already listed; a retainer with a free slot is picked for you");

            if (this.listingCandidates.Count == 0)
                Theme.Faint("nothing new with a known market price.");

            foreach (var candidate in this.listingCandidates)
            {
                var suffix = candidate.Hq ? " (HQ)" : string.Empty;
                Theme.Colored(
                    Theme.Text,
                    $"{this.plugin.Catalog.Name(candidate.ItemId)}{suffix} x{candidate.Quantity} at {candidate.Price:N0}");

                ImGui.SameLine();
                ImGui.PushID(((int)candidate.ItemId * 2) + (candidate.Hq ? 1 : 0));
                if (ImGui.SmallButton("List"))
                    this.plugin.ListOne(candidate.Type, candidate.Slot, candidate.ItemId, candidate.Hq, candidate.Price);
                ImGui.PopID();
            }

            Gw.TreePop();
        }

        ImGui.Spacing();

        // The table still fills the window; Glasswork sizes are logical units, so hand the height back
        // unscaled instead of letting the Look scale it a second time.
        var tableHeight = TableHeight() / MathF.Max(0.01f, Frame.Scale);
        if (!Gw.BeginTable("mylistings"u8, 10, GwTableFlags.Sortable | GwTableFlags.ScrollY | GwTableFlags.Zebra, new Vector2(0f, tableHeight)))
            return;

        Gw.TableColumn("Item", 2.2f, defaultSort: this.listingSortColumn <= 0);
        Gw.TableColumn("Retainer", 96f, stretch: false, defaultSort: this.listingSortColumn == 1);
        Gw.TableColumn("Q", 24f, stretch: false, defaultSort: this.listingSortColumn == 2);
        Gw.TableColumn("Qty", 36f, stretch: false, defaultSort: this.listingSortColumn == 3, numeric: true);
        Gw.TableColumn("Yours", 72f, stretch: false, defaultSort: this.listingSortColumn == 4, numeric: true);
        Gw.TableColumn("Market", 72f, stretch: false, defaultSort: this.listingSortColumn == 5, numeric: true);
        Gw.TableColumn("Delta", 52f, stretch: false, defaultSort: this.listingSortColumn == 6, numeric: true);
        Gw.TableColumn("Clear", 52f, stretch: false, defaultSort: this.listingSortColumn == 7, numeric: true);
        Gw.TableColumn("Status", 1.5f, defaultSort: this.listingSortColumn == 8);
        Gw.TableColumn("##actions", 30f, stretch: false);

        // ImGui starts a sortable table on a column and direction of its own, and reloads the sort saved in its
        // settings when the table (re)initialises. Treat that frame as a re-initialisation instead of a click on a
        // header, and put the saved sort in before the header row is laid out so its chevron is right on the first
        // frame too.
        var tableFresh = ImGui.GetFrameCount() - this.listingTableFrame > 1;
        this.listingTableFrame = ImGui.GetFrameCount();

        if (tableFresh)
            this.RepairListingSort();

        Gw.TableHeaders();

        if (Gw.TableSortChanged(out var sortColumn, out var descending))
        {
            if (!tableFresh && sortColumn >= 0 && sortColumn < 9)
            {
                // A declared column index is the data column: the action column sits past them.
                var ascending = !descending;
                if (sortColumn != this.listingSortColumn || ascending != this.listingSortAscending)
                {
                    this.listingSortColumn = sortColumn;
                    this.listingSortAscending = ascending;
                }
            }
            else if (!tableFresh)
            {
                // The trailing action column never had a sort of its own; keep the current order.
                this.RepairListingSort();
            }
        }

        CapturedListing? remove = null;

        var tableClipper = new GwTableClipper(reviews.Count);
        while (tableClipper.Step(out var first, out var last))
        {
            for (var index = first; index < last; index++)
            {
                var review = reviews[index];
                var listing = review.Listing;
                var quality = listing.Hq ? Quality.Hq : Quality.Nq;
                var rowQuote = this.plugin.FindListingQuote(listing.ItemId, quality)
                               ?? quotes.GetValueOrDefault((listing.ItemId, quality));

                Gw.TableRow(index);

                Gw.TableCell();
                UiKit.ItemIcon(UiKit.IconIdForItem(listing.ItemId), listing.Hq, 16f);
                ImGui.SameLine(0f, 6f);
                Theme.Colored(Theme.Text, this.plugin.Catalog.Name(listing.ItemId));

                Gw.TableCell();
                Gw.TableText(
                    string.IsNullOrWhiteSpace(listing.RetainerName) ? "?" : listing.RetainerName,
                    TextRole.Body,
                    Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(
                    quality == Quality.Hq ? "HQ" : "NQ",
                    TextRole.Body,
                    Theme.ToColor(quality == Quality.Hq ? Theme.Hq : Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(listing.Quantity.ToString(), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(Money.Gil(listing.PricePerUnit), TextRole.Body, Theme.ToColor(Theme.Text));

                Gw.TableCell();
                Func<string>? ladder = rowQuote is null ? null : () => this.PriceLadder(rowQuote, listing);
                TableCellText(
                    review.MarketMin > 0 ? Money.Gil(review.MarketMin) : "-",
                    TextRole.Body,
                    Theme.TextDim,
                    ladder);

                Gw.TableCell();
                var deltaColor = review.Status == ListingStatus.AboveMarket ? Theme.Danger : Theme.Accent;
                Gw.TableText(
                    review.Status == ListingStatus.Unpriced ? "-" : review.PriceDeltaPercent.ToString("+0%;-0%;0%"),
                    TextRole.Body,
                    Theme.ToColor(deltaColor));

                Gw.TableCell();
                Gw.TableText(Money.Days(review.DaysToSell), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                var statusColor = review.IsProbablySold
                    ? Theme.Danger
                    : review.Status switch
                    {
                        ListingStatus.Cheapest => Theme.Accent,
                        ListingStatus.AtMarket => Theme.Text,
                        ListingStatus.AboveMarket => Theme.Danger,
                        _ => Theme.TextFaint,
                    };
                var advice = review.Status == ListingStatus.Unpriced && this.plugin.IsPricingListings
                    ? "pricing..."
                    : review.Advice;

                var dotOrigin = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddCircleFilled(
                    new Vector2(dotOrigin.X + 4f, dotOrigin.Y + (ImGui.GetTextLineHeight() * 0.5f)),
                    2.6f,
                    Theme.U32(statusColor));
                ImGui.Dummy(new Vector2(12f, ImGui.GetTextLineHeight()));
                ImGui.SameLine(0f, 0f);
                Theme.Colored(statusColor, advice);

                Gw.TableCell();
                ImGui.PushID($"forget{listing.ItemId}{listing.RetainerId}");
                if (ImGui.SmallButton("x"))
                    remove = listing;
                ImGui.PopID();
            }
        }

        Gw.EndTable();

        if (remove is not null)
        {
            listings.Remove(remove);
            this.plugin.SaveConfig();
        }
    }

    private void CopyRepricingList(IReadOnlyList<ListingReview> reviews)
    {
        var lines = reviews
            .Where(review => review.NeedsReprice)
            .OrderByDescending(review => review.PriceDeltaPercent)
            .Select(review =>
            {
                var name = this.plugin.Catalog.Name(review.Listing.ItemId);
                var hq = review.Listing.Hq ? " (HQ)" : string.Empty;
                return $"{name}{hq}\t{Money.Exact(review.Listing.PricePerUnit)}\t->\t" +
                       $"{Money.Exact(review.SuggestedPrice)}\t" +
                       $"({Money.Days(review.DaysToSell)} -> {Money.Days(review.DaysIfRepriced)})";
            })
            .ToList();

        if (lines.Count == 0)
        {
            this.plugin.Print("nothing to reprice - every listing is at or under the market.");
            return;
        }

        ImGui.SetClipboardText(string.Join("\n", lines));
        this.plugin.Print($"copied {lines.Count} repricing row(s).");
    }

    /// <summary>
    /// The cheapest few prices on the board, so a single undercutter is visible instead of being
    /// hidden behind one "market minimum" number.
    /// </summary>
    private string PriceLadder(MarketQuote quote, CapturedListing listing)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{this.plugin.Catalog.Name(listing.ItemId)} - cheapest listings");

        if (quote.CheapestSteps.Length == 0)
        {
            builder.AppendLine("no listing detail cached; rescan to refresh");
        }
        else
        {
            foreach (var step in quote.CheapestSteps)
                builder.AppendLine($"{Money.Exact(step.Price)}   x{step.Listings}");
        }

        builder.Append($"you: {Money.Exact(listing.PricePerUnit)}");

        if (quote.LastUploadUtc is { } uploaded)
        {
            var age = DateTime.UtcNow - uploaded;
            var ageText = age.TotalHours < 1
                ? $"{age.TotalMinutes:0} min"
                : $"{age.TotalHours:0.0} h";
            builder.Append($"\ndata {ageText} old, {quote.ListingCount} listings, {quote.DistinctSellers} sellers");
        }

        return builder.ToString();
    }

    // --------------------------------------------------------------------- scan ----

    // ---------------------------------------------------------------- arbitrage ----

    private string BuildArbitrageCsv(IReadOnlyList<ArbitrageRow> rows)
    {
        var header = new[]
        {
            "item", "quality", "buy_world", "buy_price", "sell_world", "sell_price", "net_unit", "stack_profit", "my_world_price", "sales_per_day",
        };

        var lines = rows.Select(row => (IReadOnlyList<string>)new[]
        {
            this.plugin.Catalog.Name(row.ItemId),
            row.Quality == Quality.Hq ? "HQ" : "NQ",
            row.BuyWorld,
            row.BuyPrice.ToString("0.##", CultureInfo.InvariantCulture),
            row.SellWorld,
            row.SellPrice.ToString("0.##", CultureInfo.InvariantCulture),
            row.NetPerUnit.ToString("0.##", CultureInfo.InvariantCulture),
            (row.NetPerUnit * this.Config.DefaultStackSize).ToString("0.##", CultureInfo.InvariantCulture),
            row.HomePrice.ToString("0.##", CultureInfo.InvariantCulture),
            row.SellVelocity.ToString("0.###", CultureInfo.InvariantCulture),
        });

        return CsvExport.Build(header, lines);
    }

    /// <summary>Buy plan for exactly the rows that were ticked into the flip cart.</summary>
    private void DrawCartBuyList(IReadOnlyList<FlipCartEntry> cart)
    {
        if (cart.Count == 0)
        {
            Theme.Faint("Nothing in the cart - tick rows in the table to add them.");
            return;
        }

        foreach (var group in cart.GroupBy(entry => entry.BuyWorld, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (ImGui.SmallButton("go##" + group.Key))
                this.plugin.TravelToWorld(group.Key);

            ImGui.SameLine(0f, 8f);
            Theme.Colored(Theme.Accent, group.Key);

            decimal total = 0m;
            var count = 0;
            foreach (var entry in group)
            {
                count++;
                var name = this.plugin.Catalog.Name(entry.ItemId) + (entry.Hq ? " HQ" : string.Empty);

                Theme.Faint(name);
                ImGui.SameLine(0f, 10f);
                ImGui.SetNextItemWidth(72f);
                var quantity = entry.Quantity;
                if (ImGui.InputInt("##qty" + entry.ItemId + (entry.Hq ? "h" : "n"), ref quantity))
                {
                    entry.Quantity = Math.Clamp(quantity, 1, 999);
                    this.plugin.SaveConfig();
                }

                var cost = entry.BuyPrice * entry.Quantity;
                total += cost;

                ImGui.SameLine(0f, 10f);
                Theme.Faint($"@ {Money.Gil(entry.BuyPrice)}  =  {Money.Gil(cost)}");
            }

            Theme.Colored(Theme.TextDim, $"{count} item(s), {Money.Gil(total)} to spend");
            ImGui.Separator();
        }
    }

    private bool IsInFlipCart(ArbitrageRow row) =>
        this.Config.FlipCart.Any(entry => entry.ItemId == row.ItemId && entry.Hq == (row.Quality == Quality.Hq));

    private void ToggleFlipCart(ArbitrageRow row, bool add)
    {
        var cart = this.Config.FlipCart;
        cart.RemoveAll(entry => entry.ItemId == row.ItemId && entry.Hq == (row.Quality == Quality.Hq));

        if (add)
        {
            cart.Add(new FlipCartEntry
            {
                ItemId = row.ItemId,
                Hq = row.Quality == Quality.Hq,
                BuyWorld = row.BuyWorld,
                BuyPrice = row.BuyPrice,
                SellWorld = row.SellWorld,
                SellPrice = row.SellPrice,
                Quantity = Math.Max(1, this.Config.DefaultStackSize),
            });
        }

        this.plugin.SaveConfig();
    }

    private void DrawFlipCart()
    {
        var cart = this.Config.FlipCart;
        if (cart.Count == 0)
            return;

        var cost = cart.Sum(entry => entry.BuyPrice * entry.Quantity);
        var revenue = cart.Sum(entry => entry.SellPrice * entry.Quantity * (1m - this.Config.TaxRate));
        var net = revenue - cost;

        ImGui.Spacing();
        Theme.Colored(Theme.Gold, $"flip cart: {cart.Count} item(s), {Money.Gil(cost)} to spend, +{Money.Gil(net)} expected");

        if (Gw.Button("copy cart"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.SetClipboardText(this.BuildFlipCartCsv(cart));

        ImGui.SameLine();
        if (Gw.Button("clear cart"u8, ButtonStyle.Plain, ButtonSize.Small))
            this.confirmClearCart = true;

        if (Gw.Alert(
                "##clearcart"u8,
                ref this.confirmClearCart,
                "Clear the flip cart?"u8,
                "Removes every item from the cart. Your listings and the market board are untouched."u8,
                "clear"u8,
                "cancel"u8,
                destructive: true) == DialogResult.Confirmed)
        {
            cart.Clear();
            this.plugin.SaveConfig();
            Gw.Toast("Cart cleared"u8, ToastKind.Success);
        }

        foreach (var group in cart.GroupBy(entry => entry.BuyWorld, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var line = string.Join(", ", group.Select(entry =>
                $"{this.plugin.Catalog.Name(entry.ItemId)}{(entry.Hq ? " HQ" : string.Empty)} x{entry.Quantity}"));

            Theme.Faint($"{group.Key}: {line}");
        }

        ImGui.Spacing();
    }

    private string BuildFlipCartCsv(IReadOnlyList<FlipCartEntry> cart)
    {
        var header = new[]
        {
            "item", "quality", "buy_world", "buy_price", "quantity", "cost", "sell_world", "sell_price", "expected_net",
        };

        var lines = cart.Select(entry => (IReadOnlyList<string>)new[]
        {
            this.plugin.Catalog.Name(entry.ItemId),
            entry.Hq ? "HQ" : "NQ",
            entry.BuyWorld,
            entry.BuyPrice.ToString("0.##", CultureInfo.InvariantCulture),
            entry.Quantity.ToString(CultureInfo.InvariantCulture),
            (entry.BuyPrice * entry.Quantity).ToString("0.##", CultureInfo.InvariantCulture),
            entry.SellWorld,
            entry.SellPrice.ToString("0.##", CultureInfo.InvariantCulture),
            (entry.SellPrice * entry.Quantity * (1m - this.Config.TaxRate) - entry.BuyPrice * entry.Quantity).ToString("0.##", CultureInfo.InvariantCulture),
        });

        return CsvExport.Build(header, lines);
    }

    private void DrawBuyPopup()
    {
        if (!this.buyOpen)
            return;

        if (!Gw.Dialog("buyitem"u8, ref this.buyOpen, "Buy item"u8, new Vector2(380f, 280f)))
            return;

        Theme.Colored(Theme.Text, this.buyName);
        Theme.Faint($"on {this.buyWorld} - open the market board there first");

        var quantity = Math.Clamp(this.buyQty, 1, 999);
        if (Gw.Stepper(ref quantity, 1, 999, 1, "units"u8))
            this.buyQty = quantity;

        Theme.Faint("max gil per unit");
        ImGui.SetNextItemWidth(195f);
        var priceText = this.buyMaxPriceText;
        if (Gw.TextField(ref priceText, "max gil / unit"u8, maxLength: 12))
        {
            this.buyMaxPriceText = priceText;
            if (decimal.TryParse(priceText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0m)
                this.buyMaxPrice = parsed;
        }

        Theme.Faint(this.plugin.BuyStatus);

        if (Gw.Button("check and buy"u8, ButtonStyle.Prominent, ButtonSize.Small))
            this.plugin.StartBuy(this.buyWorld, this.buyItemId, this.buyName, this.buyHq, this.buyQty, this.buyMaxPrice);

        Gw.EndDialog();
    }

    private void DrawFlipPopup()
    {
        if (!this.flipOpen)
            return;

        if (!Gw.Dialog("flip"u8, ref this.flipOpen, "Flip calculator"u8, new Vector2(400f, 260f)))
            return;

        var report = this.plugin.Arbitrage;
        var row = report?.Rows.FirstOrDefault(candidate => candidate.ItemId == this.flipItemId && candidate.Quality == this.flipQuality);
        if (row is null)
        {
            Theme.Faint("That pair is no longer in the report.");
            if (Gw.Button("close"u8, ButtonStyle.Plain, ButtonSize.Small))
                this.flipOpen = false;

            Gw.EndDialog();
            return;
        }

        Theme.Colored(Theme.Text, this.plugin.Catalog.Name(row.ItemId));
        Theme.Faint($"{row.BuyWorld} {Money.Gil(row.BuyPrice)}  ->  {row.SellWorld} {Money.Gil(row.SellPrice)}");

        var quantity = Math.Clamp(this.flipQty, 1, 999);
        if (Gw.Stepper(ref quantity, 1, 999, 1, "units"u8))
            this.flipQty = quantity;

        var cost = row.BuyPrice * this.flipQty;
        var revenue = row.SellPrice * this.flipQty * (1m - this.Config.TaxRate);
        var net = revenue - cost;

        Theme.Faint($"cost {Money.Gil(cost)}   revenue {Money.Gil(revenue)}");
        Theme.Colored(net > 0m ? Theme.Profit : Theme.Danger, $"net {Money.Gil(net)}");

        Gw.EndDialog();
    }

    private string BuildCraftCsv(IReadOnlyList<CraftOpportunity> ideas)
    {
        var header = new[]
        {
            "item", "job", "craft_level", "crafts", "produced", "revenue", "materials", "profit", "per_unit", "stack_profit", "hq_profit", "owned_profit", "days_to_sell", "materials_priced",
        };

        var lines = ideas.Select(idea => (IReadOnlyList<string>)new[]
        {
            this.plugin.Catalog.Name(idea.ItemId),
            this.plugin.Catalog.JobAbbreviation(idea.CraftJob),
            idea.RequiredLevel.ToString(CultureInfo.InvariantCulture),
            idea.Crafts.ToString(CultureInfo.InvariantCulture),
            idea.Produced.ToString(CultureInfo.InvariantCulture),
            idea.Revenue.ToString("0.##", CultureInfo.InvariantCulture),
            idea.MaterialValue.ToString("0.##", CultureInfo.InvariantCulture),
            idea.Profit.ToString("0.##", CultureInfo.InvariantCulture),
            (idea.Produced > 0 ? idea.Profit / idea.Produced : 0m).ToString("0.##", CultureInfo.InvariantCulture),
            ((idea.Produced > 0 ? idea.Profit / idea.Produced : 0m) * this.Config.DefaultStackSize).ToString("0.##", CultureInfo.InvariantCulture),
            idea.HqProfit.ToString("0.##", CultureInfo.InvariantCulture),
            idea.OwnedProfit.ToString("0.##", CultureInfo.InvariantCulture),
            idea.DaysToSell.ToString("0.#", CultureInfo.InvariantCulture),
            idea.MaterialsPriced ? "yes" : "partial",
        });

        return CsvExport.Build(header, lines);
    }

    private string BuildPortfolioCsv(IReadOnlyList<PortfolioCandidate> picks)
    {
        var header = new[] { "item", "quality", "capital", "profit_per_day" };

        var lines = picks.Select(pick => (IReadOnlyList<string>)new[]
        {
            this.plugin.Catalog.Name(pick.ItemId),
            pick.Quality == Quality.Hq ? "HQ" : "NQ",
            pick.CapitalPerSlot.ToString("0.##", CultureInfo.InvariantCulture),
            pick.GilPerDay.ToString("0.##", CultureInfo.InvariantCulture),
        });

        return CsvExport.Build(header, lines);
    }

    private string BuildListingsCsv(IReadOnlyList<ListingReview> reviews)
    {
        var header = new[]
        {
            "item", "retainer", "quality", "quantity", "your_price", "market_min", "delta", "days_to_sell", "status", "advice",
        };

        var lines = reviews.Select(review => (IReadOnlyList<string>)new[]
        {
            this.plugin.Catalog.Name(review.Listing.ItemId),
            review.Listing.RetainerName,
            review.Listing.Hq ? "HQ" : "NQ",
            review.Listing.Quantity.ToString(CultureInfo.InvariantCulture),
            review.Listing.PricePerUnit.ToString("0.##", CultureInfo.InvariantCulture),
            review.MarketMin.ToString("0.##", CultureInfo.InvariantCulture),
            review.PriceDeltaPercent.ToString("+0%;-0%;0%", CultureInfo.InvariantCulture),
            review.DaysToSell.ToString("0.#", CultureInfo.InvariantCulture),
            review.Status.ToString(),
            review.Advice,
        });

        return CsvExport.Build(header, lines);
    }

    private int CompareArbitrage(ArbitrageRow left, ArbitrageRow right, int column) => column switch
    {
        0 => string.Compare(this.plugin.Catalog.Name(left.ItemId), this.plugin.Catalog.Name(right.ItemId), StringComparison.OrdinalIgnoreCase),
        1 => string.Compare(left.BuyWorld, right.BuyWorld, StringComparison.OrdinalIgnoreCase),
        2 => left.BuyPrice.CompareTo(right.BuyPrice),
        3 => string.Compare(left.SellWorld, right.SellWorld, StringComparison.OrdinalIgnoreCase),
        4 => left.SellPrice.CompareTo(right.SellPrice),
        5 => left.NetPerUnit.CompareTo(right.NetPerUnit),
        6 => left.NetPerUnit.CompareTo(right.NetPerUnit),
        7 => left.HomePrice.CompareTo(right.HomePrice),
        8 => left.SellVelocity.CompareTo(right.SellVelocity),
        _ => left.ItemId.CompareTo(right.ItemId),
    };

    private void DrawArbitrageTab()
    {
        var config = this.Config;
        var report = this.plugin.Arbitrage;

        Theme.Dimmed("Buy low on one world, sell high on another. Universalis prices the scan's best items on each world in your data center.");

        ImGui.SetNextItemWidth(130f);
        var worldCount = Math.Clamp(config.ArbitrageWorldCount, 2, 16);
        if (ImGui.SliderInt("##arbworlds", ref worldCount, 2, 16, "%d worlds"))
        {
            config.ArbitrageWorldCount = worldCount;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        var topItems = Math.Clamp(config.ArbitrageTopItems, 10, 400);
        if (ImGui.SliderInt("##arbtop", ref topItems, 10, 400, "top %d items"))
        {
            config.ArbitrageTopItems = topItems;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(145f);
        var minVelocity = (float)Math.Clamp(config.ArbitrageMinVelocity, 0d, 10d);
        if (ImGui.SliderFloat("##arbvel", ref minVelocity, 0f, 10f, minVelocity <= 0f ? "any demand" : "sell >= %.1f/d"))
        {
            config.ArbitrageMinVelocity = Math.Round(minVelocity, 1);
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        var sellBasis = this.Config.ArbitrageSellBasis;
        if (ImGui.BeginCombo("##sellbasis", sellBasis == 1 ? "sell: average" : "sell: listing"))
        {
            if (ImGui.Selectable("sell: listing (undercut)", sellBasis == 0))
            {
                this.Config.ArbitrageSellBasis = 0;
                this.plugin.SaveConfig();
            }

            if (ImGui.Selectable("sell: average sale", sellBasis == 1))
            {
                this.Config.ArbitrageSellBasis = 1;
                this.plugin.SaveConfig();
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Listing basis sells against the other world's cheapest listing, so a single weird sale cannot inflate the numbers. Average uses the average sale price instead.");

        ImGui.SameLine();
        var sellHomeOnly = this.Config.ArbitrageSellHomeOnly;
        if (ImGui.Checkbox("sell on my world", ref sellHomeOnly))
        {
            this.Config.ArbitrageSellHomeOnly = sellHomeOnly;
            this.plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Buy anywhere, sell only back on the world you scanned.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(135f);
        var minNet = (int)Math.Clamp(config.ArbitrageMinNet, 0, 500_000);
        if (ImGui.SliderInt("##arbnet", ref minNet, 0, 500_000, minNet <= 0 ? "any profit" : "net >= %d"))
        {
            config.ArbitrageMinNet = minNet;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        var minStack = (int)Math.Clamp(config.ArbitrageMinStack, 0, 5_000_000);
        if (ImGui.SliderInt("##arbstack", ref minStack, 0, 5_000_000, minStack <= 0 ? "any stack" : "stack >= %d"))
        {
            config.ArbitrageMinStack = minStack;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        var autoArbitrage = config.ArbitrageEnabled;
        if (ImGui.Checkbox("after scan", ref autoArbitrage))
        {
            config.ArbitrageEnabled = autoArbitrage;
            this.plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Run compare worlds automatically when a scan finishes.");

        ImGui.SameLine();
        var walkOnArrival = config.TravelWalk;
        if (ImGui.Checkbox("walk on arrival", ref walkOnArrival))
        {
            config.TravelWalk = walkOnArrival;
            this.plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("After a world change, use vnavmesh to walk to the market board.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(105f);
        var gateway = this.Config.TravelGateway;
        var gatewayLabel = gateway switch { 8 => "Limsa", 2 => "Gridania", 9 => "Ul'dah", _ => "gateway" };
        if (ImGui.BeginCombo("##gateway", gatewayLabel))
        {
            foreach (var (id, name) in new[] { (8, "Limsa"), (2, "Gridania"), (9, "Ul'dah") })
            {
                if (!ImGui.Selectable(name, gateway == id))
                    continue;

                this.Config.TravelGateway = id;
                this.plugin.SaveConfig();
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Which city Lifestream should change worlds through.");

        ImGui.SameLine();
        if (this.plugin.ArbitrageRunning)
        {
            Theme.Colored(Theme.Gold, $"comparing worlds... {this.plugin.ArbitrageDone}/{this.plugin.ArbitrageTotal}");
            ImGui.SameLine();
            if (Gw.Button("cancel"u8, ButtonStyle.Plain, ButtonSize.Small))
                this.plugin.CancelArbitrage();
        }
        else if (Gw.Button("compare worlds"u8, ButtonStyle.Prominent, ButtonSize.Small))
        {
            this.plugin.StartArbitrage();
        }

        ImGui.SameLine();
        if (Gw.Button("worlds"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.OpenPopup("##arbworlds");

        if (ImGui.BeginPopup("##arbworlds"))
        {
            var allWorlds = this.plugin.DataCenterWorlds();
            var selected = this.Config.ArbitrageWorlds;
            if (selected.Count == 0)
                Theme.Faint("auto: the whole data center");

            foreach (var world in allWorlds)
            {
                var isSelected = selected.Any(entry => entry.Equals(world, StringComparison.OrdinalIgnoreCase));
                if (ImGui.Checkbox(world + "##arbw" + world, ref isSelected))
                {
                    if (isSelected)
                        selected.Add(world);
                    else
                        selected.RemoveAll(entry => entry.Equals(world, StringComparison.OrdinalIgnoreCase));

                    this.plugin.SaveConfig();
                }
            }

            if (Gw.Button("auto (whole DC)"u8, ButtonStyle.Plain, ButtonSize.Small))
            {
                selected.Clear();
                this.plugin.SaveConfig();
            }

            ImGui.EndPopup();
        }

        ImGui.Spacing();

        if (report is null)
        {
            if (UiKit.EmptyState(
                    "No arbitrage run yet",
                    "Scan your world, then press compare worlds to price the shortlist across your data center.",
                    "Compare worlds"))
            {
                this.plugin.StartArbitrage();
                Gw.Toast("Comparing worlds"u8, ToastKind.Info);
            }

            return;
        }

        Theme.Faint($"{report.Worlds.Count} worlds ({string.Join(", ", report.Worlds)}) - {report.ComputedUtc.ToLocalTime():HH:mm}");

        this.DrawFlipCart();

        if (this.plugin.BuyRunActive)
        {
            Theme.Colored(Theme.Gold, $"buy run: {this.plugin.BuyRunStatus}");
            ImGui.SameLine();
            if (ImGui.SmallButton("cancel run"))
                this.plugin.CancelBuyRun();
        }

        var history = this.plugin.ArbitrageHistory;
        if (history.Count >= 2)
        {
            ImGui.SameLine();
            var series = new decimal[history.Count];
            for (var index = 0; index < history.Count; index++)
                series[index] = history[index].Rows;

            var sparkPosition = ImGui.GetCursorScreenPos();
            var sparkSize = new Vector2(150f, 16f);
            ImGui.Dummy(sparkSize);
            DrawSparkline(ImGui.GetWindowDrawList(), sparkPosition, sparkSize, series, Theme.U32(Theme.Accent));
        }

        if (report.Warnings.Count > 0)
            Theme.Colored(Theme.Warn, string.Join("; ", report.Warnings));

        if (report.Rows.Count == 0)
        {
            UiKit.EmptyState("No arbitrage found", "Nothing on the shortlist is cheaper on one world than it sells for on another after tax.");
            return;
        }

        var visible = report.Rows.ToList();
        var minNetValue = (decimal)config.ArbitrageMinNet;
        if (minNetValue > 0m)
            visible = visible.Where(row => row.NetPerUnit >= minNetValue).ToList();

        var stackSize = Math.Max(1, this.Config.DefaultStackSize);
        var minStackValue = (decimal)config.ArbitrageMinStack;
        if (minStackValue > 0m)
            visible = visible.Where(row => row.NetPerUnit * stackSize >= minStackValue).ToList();

        visible.Sort((left, right) => this.CompareArbitrage(left, right, this.arbitrageSortColumn));

        // The comparators above are ascending, so a descending header reverses them. This was inverted: the
        // field used to mean "descending" while the chevron drawn beside it pointed up.
        if (!this.arbitrageSortAscending)
            visible.Reverse();

        if (Gw.Button("copy list"u8, ButtonStyle.Plain, ButtonSize.Small))
            ImGui.SetClipboardText(this.BuildArbitrageCsv(visible));

        ImGui.SameLine();
        if (Gw.Button("buy list"u8, ButtonStyle.Prominent, ButtonSize.Small))
            ImGui.OpenPopup("##arbbuylist");

        if (ImGui.BeginPopup("##arbbuylist"))
        {
            if (this.plugin.BuyRunActive)
            {
                Theme.Colored(Theme.Gold, this.plugin.BuyRunStatus);
                ImGui.SameLine();
                if (Gw.Button("cancel run"u8, ButtonStyle.Plain, ButtonSize.Small))
                    this.plugin.CancelBuyRun();
            }
            else if (Gw.Button("go and buy"u8, ButtonStyle.Prominent, ButtonSize.Small))
            {
                this.plugin.StartBuyRun();
            }

            ImGui.BeginChild("##buylistscroll", new Vector2(560f, 420f), true);
            this.DrawCartBuyList(this.Config.FlipCart);
            ImGui.EndChild();
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("travel"))
        {
            var best = visible.FirstOrDefault();
            if (best is not null)
                this.plugin.TravelToWorld(best.BuyWorld);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(visible.Count > 0 ? $"Ask Lifestream to change to {visible[0].BuyWorld}" : "No rows to travel for.");

        ImGui.SameLine();
        if (ImGui.SmallButton("go to..."))
            ImGui.OpenPopup("##arbtravel");

        if (ImGui.BeginPopup("##arbtravel"))
        {
            foreach (var world in this.plugin.DataCenterWorlds())
            {
                if (ImGui.MenuItem(world))
                    this.plugin.TravelToWorld(world);
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        Theme.Faint($"{visible.Count} of {report.Rows.Count} rows");

        if (visible.Count > 0)
        {
            ImGui.SameLine();
            Theme.Colored(Theme.Profit, $"total +{Money.Gil(visible.Sum(row => row.NetPerUnit * stackSize))}");
        }

        // The table still fills the window; Glasswork sizes are logical units, so hand the height back
        // unscaled instead of letting the Look scale it a second time.
        var tableHeight = TableHeight() / MathF.Max(0.01f, Frame.Scale);
        if (!Gw.BeginTable(
                "arbitrage"u8,
                9,
                GwTableFlags.Sortable | GwTableFlags.Resizable | GwTableFlags.ScrollY | GwTableFlags.Zebra | GwTableFlags.Selectable,
                new Vector2(0f, tableHeight)))
        {
            return;
        }

        Gw.TableColumn("Item", 2.2f, defaultSort: this.arbitrageSortColumn == 0);
        Gw.TableColumn("Buy on", 110f, stretch: false, defaultSort: this.arbitrageSortColumn == 1);
        Gw.TableColumn("Buy", 80f, stretch: false, defaultSort: this.arbitrageSortColumn == 2, numeric: true);
        Gw.TableColumn("Sell on", 110f, stretch: false, defaultSort: this.arbitrageSortColumn == 3);
        Gw.TableColumn("Sell", 80f, stretch: false, defaultSort: this.arbitrageSortColumn == 4, numeric: true);
        Gw.TableColumn("Net/unit", 90f, stretch: false, defaultSort: this.arbitrageSortColumn == 5, numeric: true);
        Gw.TableColumn("Stack", 90f, stretch: false, defaultSort: this.arbitrageSortColumn == 6, numeric: true);
        Gw.TableColumn("My world", 80f, stretch: false, defaultSort: this.arbitrageSortColumn == 7, numeric: true);
        Gw.TableColumn("Sales/d", 60f, stretch: false, defaultSort: this.arbitrageSortColumn == 8, numeric: true);

        // ImGui starts a sortable table on a column and direction of its own, and reloads the sort saved in its
        // settings when the table (re)initialises. Treat that frame as a re-initialisation instead of a click on a
        // header, and put the saved sort in before the header row is laid out so its chevron is right on the first
        // frame too.
        var tableFresh = ImGui.GetFrameCount() - this.arbitrageTableFrame > 1;
        this.arbitrageTableFrame = ImGui.GetFrameCount();

        if (tableFresh)
            this.RepairArbitrageSort();

        Gw.TableHeaders();

        if (Gw.TableSortChanged(out var sortColumn, out var descending))
        {
            if (!tableFresh && sortColumn >= 0 && sortColumn < 9)
            {
                // A declared column index is the data column: the comparators take the same numbers.
                var ascending = !descending;
                if (sortColumn != this.arbitrageSortColumn || ascending != this.arbitrageSortAscending)
                {
                    this.arbitrageSortColumn = sortColumn;
                    this.arbitrageSortAscending = ascending;
                }
            }
        }

        var tableClipper = new GwTableClipper(visible.Count);
        while (tableClipper.Step(out var first, out var last))
        {
            for (var index = first; index < last; index++)
            {
                var row = visible[index];
                var inCart = this.IsInFlipCart(row);

                Gw.TableRow(index);

                if (ImGui.BeginPopupContextItem($"##arbctx{row.ItemId}{row.Quality}"))
                {
                    if (ImGui.MenuItem("Copy item name"))
                        ImGui.SetClipboardText(this.plugin.Catalog.Name(row.ItemId));

                    if (ImGui.MenuItem("Copy pair"))
                        ImGui.SetClipboardText($"{row.BuyWorld} {Money.Gil(row.BuyPrice)} -> {row.SellWorld} {Money.Gil(row.SellPrice)} (+{Money.Gil(row.NetPerUnit)})");

                    if (ImGui.MenuItem("Open on Universalis"))
                        OpenUrl($"https://universalis.app/market/{row.ItemId}");

                    if (ImGui.MenuItem($"Travel to {row.BuyWorld}"))
                        this.plugin.TravelToWorld(row.BuyWorld);

                    if (ImGui.MenuItem("Walk to market board"))
                        this.plugin.WalkToMarketBoard();

                    if (ImGui.MenuItem("Buy..."))
                    {
                        this.buyOpen = true;
                        this.buyItemId = row.ItemId;
                        this.buyHq = row.Quality == Quality.Hq;
                        this.buyName = this.plugin.Catalog.Name(row.ItemId);
                        this.buyWorld = row.BuyWorld;
                        this.buyQty = Math.Max(1, this.Config.DefaultStackSize);
                        this.buyMaxPrice = row.BuyPrice;
                        this.buyMaxPriceText = this.buyMaxPrice.ToString("0", CultureInfo.InvariantCulture);
                    }

                    if (ImGui.MenuItem("Flip calculator..."))
                    {
                        this.flipItemId = row.ItemId;
                        this.flipQuality = row.Quality;
                        this.flipQty = Math.Max(1, this.Config.DefaultStackSize);
                        this.flipOpen = true;
                    }

                    ImGui.EndPopup();
                }

                Gw.TableCell();
                if (ImGui.Checkbox("##flipcart" + row.ItemId + row.Quality, ref inCart))
                    this.ToggleFlipCart(row, inCart);

                ImGui.SameLine(0f, 4f);
                if (row.NetPerUnit * stackSize >= (decimal)config.ArbitrageTripThreshold)
                {
                    Theme.Colored(Theme.Gold, "*");
                    ImGui.SameLine(0f, 4f);
                }

                UiKit.ItemIcon(UiKit.IconIdForItem(row.ItemId), row.Quality == Quality.Hq, 16f);
                ImGui.SameLine(0f, 6f);
                Theme.Colored(Theme.Text, this.plugin.Catalog.Name(row.ItemId));
                if (row.Quality == Quality.Hq)
                {
                    ImGui.SameLine(0f, 6f);
                    Theme.Colored(Theme.Hq, "HQ");
                }

                Gw.TableCell();
                Gw.TableText(row.BuyWorld, TextRole.Body, Theme.ToColor(Theme.Accent));

                Gw.TableCell();
                Gw.TableText(Money.Gil(row.BuyPrice), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText(row.SellWorld, TextRole.Body, Theme.ToColor(Theme.Accent));

                Gw.TableCell();
                Gw.TableText(Money.Gil(row.SellPrice), TextRole.Body, Theme.ToColor(Theme.TextDim));

                Gw.TableCell();
                Gw.TableText("+" + Money.Gil(row.NetPerUnit), TextRole.Body, Theme.ToColor(Theme.Profit));

                Gw.TableCell();
                Gw.TableText("+" + Money.Gil(row.NetPerUnit * this.Config.DefaultStackSize), TextRole.Body, Theme.ToColor(Theme.Profit));

                Gw.TableCell();
                if (row.HomePrice > 0m)
                {
                    Gw.TableText(Money.Gil(row.HomePrice), TextRole.Body, Theme.ToColor(Theme.TextDim));
                }
                else
                {
                    Gw.TableText("-", TextRole.Body, Theme.ToColor(Theme.TextFaint));
                }

                Gw.TableCell();
                Gw.TableText(row.SellVelocity.ToString("0.0"), TextRole.Body, Theme.ToColor(Theme.TextFaint));
            }
        }

        Gw.EndTable();

        this.DrawFlipPopup();
        this.DrawBuyPopup();
    }

    // ---------------------------------------------------------------- scan ----

    private void DrawScanTab()
    {
        var config = this.Config;
        var state = this.plugin.State;

        if (state.IsScanning)
        {
            Gw.BeginRow("Scanning", $"{state.Phase} {state.Done}/{state.Total} {state.Detail}");
            Gw.ProgressBar(state.Fraction, 360f);
            Gw.EndRow();

            if (Gw.Button("Cancel scan"u8, ButtonStyle.Plain, ButtonSize.Small))
                this.plugin.CancelScan();
        }
        else
        {
            if (Gw.Button("Scan now"u8, ButtonStyle.Prominent))
                this.plugin.StartScan();

            ImGui.SameLine();
            if (Gw.Button("Clear cache"u8))
                this.confirmClearCache = true;

            if (Gw.Alert(
                    "##clearcache"u8,
                    ref this.confirmClearCache,
                    "Clear the market cache?"u8,
                    "Drops every cached quote and aggregate, so the next scan fetches everything again. The current result is cleared too."u8,
                    "clear cache"u8,
                    "cancel"u8,
                    destructive: true) == DialogResult.Confirmed)
            {
                this.plugin.Cache.Clear();
                this.plugin.State.Result = null;
                this.plugin.State.Diff = null;
                this.plugin.SaveConfig();
                Gw.Toast("Market cache cleared"u8, ToastKind.Success);
            }
        }

        if (state.Error.Length > 0)
            Theme.Colored(Theme.Danger, state.Error);

        if (state.Result is { } result)
        {
            ImGui.Spacing();
            Theme.Dimmed(
                $"{result.Stats.MarketableItems} marketable  -  {result.Stats.AggregatedItems} seen  -  " +
                $"{result.Stats.PricedItems} priced  -  {result.Stats.Requests} requests  -  " +
                $"{result.Stats.Duration.TotalSeconds:0.0}s" +
                (result.Stats.AggregatesFromCache ? "  -  aggregates cached" : string.Empty) +
                (result.Stats.QuotesFromCache ? "  -  quotes cached" : string.Empty));

            foreach (var warning in result.Warnings)
                Theme.Colored(Theme.Warn, "! " + warning);
        }

        ImGui.Spacing();
        ImGui.Separator();

        if (Gw.BeginSection("Appearance"u8))
        {
            this.plugin.RefreshLookCatalog();
            var lookNames = this.plugin.LookNames;
            var lookIndex = Array.IndexOf(lookNames, this.Config.LookName);
            if (lookIndex < 0)
                lookIndex = Array.FindIndex(lookNames, name => string.Equals(name, "eorzea", StringComparison.OrdinalIgnoreCase));

            if (lookIndex < 0)
                lookIndex = 0;

            Gw.BeginRow("Theme", "any Glasswork Look, or a theme in the config folder");
            ImGui.SetNextItemWidth(170f);
            if (lookNames.Length > 0 && Gw.ComboBox(ref lookIndex, this.plugin.LookLabels, "look"u8))
                this.plugin.SetLook(lookNames[lookIndex]);

            ImGui.SameLine();
            if (Gw.Button("copy JSON"u8, ButtonStyle.Plain, ButtonSize.Small))
            {
                ImGui.SetClipboardText(this.plugin.Host.Look.ToJson());
                Gw.Toast("Theme JSON copied"u8, ToastKind.Success);
            }

            Gw.EndRow();

            Gw.BeginRow("Follow game theme", "match the game's own window theme instead");
            var followGameTheme = this.Config.FollowGameTheme;
            if (Gw.Toggle(ref followGameTheme))
                this.plugin.SetFollowGameTheme(followGameTheme);

            Gw.EndRow();
        }

        Gw.EndSection();

        if (Gw.BeginSection("Window"u8))
        {
            Gw.BeginRow("DTR bar", "the gil/day readout on the server bar");
            var dtrBar = this.Config.DtrBar;
            if (Gw.Toggle(ref dtrBar))
            {
                this.Config.DtrBar = dtrBar;
                this.plugin.SaveConfig();
                this.plugin.RefreshDtr();
            }

            Gw.EndRow();

            Gw.BeginRow("Exception log", "diagnostic: first-chance exceptions to dalamud.log, after a reload");
            var exceptionLog = this.Config.ExceptionLog;
            if (Gw.Toggle(ref exceptionLog))
            {
                this.Config.ExceptionLog = exceptionLog;
                this.plugin.SaveConfig();
            }

            Gw.EndRow();
        }

        Gw.EndSection();

        if (Gw.BeginSection("World"u8))
        {
            var worlds = this.plugin.Worlds;
            var world = this.plugin.ResolveWorld();

            if (this.worldChoices.Length != worlds.Count)
                this.worldChoices = worlds.ToArray();

            Gw.BeginRow("World", "your current world unless you pin another");
            var worldIndex = Array.IndexOf(this.worldChoices, world);
            ImGui.SetNextItemWidth(240f);
            if (this.worldChoices.Length > 0 && Gw.ComboBox(ref worldIndex, this.worldChoices, "world"u8))
            {
                config.WorldOverride = this.worldChoices[worldIndex];
                this.plugin.SaveConfig();
            }

            ImGui.SameLine();
            if (Gw.Button("use current"u8, ButtonStyle.Plain, ButtonSize.Small))
            {
                config.WorldOverride = this.plugin.CurrentWorldName();
                this.plugin.SaveConfig();
            }

            Gw.EndRow();
        }

        Gw.EndSection();

        if (Gw.BeginSection("Model"u8))
        {
            Gw.BeginRow("Tax", "3% outside the city-states, 5% inside");
            ImGui.SetNextItemWidth(180f);
            var tax = config.TaxPercent;
            if (Gw.Slider(ref tax, 0f, 5f, "##tax"u8, 0f, "%.1f%%"))
                config.TaxPercent = tax;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Stack size", "units a listing holds");
            ImGui.SetNextItemWidth(180f);
            var stack = config.DefaultStackSize;
            if (Gw.Slider(ref stack, 1, 99, "##stack"u8, 1))
                config.DefaultStackSize = stack;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Dead after", "days to clear before a slot counts as dead");
            ImGui.SetNextItemWidth(180f);
            var deadDays = config.DeadAfterDays;
            if (Gw.Slider(ref deadDays, 1, 120, "##dead"u8, 1, "%d d"))
                config.DeadAfterDays = deadDays;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Minimum sales/day", "slower movers are left out of the model");
            ImGui.SetNextItemWidth(180f);
            var minVel = config.MinimumVelocity;
            if (Gw.Slider(ref minVel, 0f, 20f, "##minvel"u8, 0f, "%.1f"))
                config.MinimumVelocity = minVel;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Shortlist", "items priced exactly after the cheap pass");
            ImGui.SetNextItemWidth(180f);
            var shortlist = config.ShortlistSize;
            if (Gw.Slider(ref shortlist, 50, 1500, "##shortlist"u8, 1))
                config.ShortlistSize = shortlist;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Cache", "hours a quote stays fresh");
            ImGui.SetNextItemWidth(180f);
            var cacheHours = config.CacheHours;
            if (Gw.Slider(ref cacheHours, 1, 48, "##cache"u8, 1, "%d h"))
                config.CacheHours = cacheHours;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Auto-scan", "hours between scans, 0 for never");
            ImGui.SetNextItemWidth(180f);
            var autoScanHours = Math.Clamp(config.AutoScanHours, 0, 48);
            if (Gw.Slider(ref autoScanHours, 0, 48, "##autoscan"u8, 1, autoScanHours <= 0 ? "off" : "%d h"))
                config.AutoScanHours = autoScanHours;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Price strategy", "where you sit in the price ladder");
            var strategyIndex = Array.IndexOf(StrategyValues, config.Strategy);
            ImGui.SetNextItemWidth(220f);
            if (Gw.ComboBox(ref strategyIndex, StrategyLabels, "strategy"u8))
            {
                config.Strategy = StrategyValues[strategyIndex];
                this.plugin.SaveConfig();
            }

            Gw.EndRow();

            Gw.BeginRow("Cost basis", "revenue, buy, craft, or the cheaper of the two");
            var costIndex = Array.IndexOf(CostValues, config.CostMode);
            ImGui.SetNextItemWidth(220f);
            if (Gw.ComboBox(ref costIndex, CostLabels, "cost"u8))
            {
                config.CostMode = CostValues[costIndex];
                this.plugin.SaveConfig();
            }

            Gw.EndRow();
        }

        Gw.EndSection();

        if (Gw.BeginSection("Alerts"u8))
        {
            Gw.BeginRow("Chat alerts", "undercuts and price drops");
            var alertsOn = config.AlertsEnabled;
            if (Gw.Toggle(ref alertsOn))
            {
                config.AlertsEnabled = alertsOn;
                this.plugin.SaveConfig();
            }

            Gw.EndRow();

            Gw.BeginRow("Also as a toast", "a brief popup on the same events");
            var toasts = config.AlertToasts;
            if (Gw.Toggle(ref toasts))
            {
                config.AlertToasts = toasts;
                this.plugin.SaveConfig();
            }

            Gw.EndRow();

            Gw.BeginRow("Poll every", "minutes between checks, one request per twenty watched items");
            ImGui.SetNextItemWidth(180f);
            var alertMinutes = config.AlertIntervalMinutes;
            if (Gw.Slider(ref alertMinutes, 1, 120, "##alertpoll"u8, 1, "%d min"))
                config.AlertIntervalMinutes = alertMinutes;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Undercut by", "how far below the current listing counts as undercut");
            ImGui.SetNextItemWidth(180f);
            var undercut = config.AlertUndercutPercent;
            if (Gw.Slider(ref undercut, 0, 50, "##undercut"u8, 1, "%d%%"))
                config.AlertUndercutPercent = undercut;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Price drop", "a fall this large triggers an alert");
            ImGui.SetNextItemWidth(180f);
            var drop = config.AlertPriceDropPercent;
            if (Gw.Slider(ref drop, 1, 90, "##drop"u8, 1, "%d%%"))
                config.AlertPriceDropPercent = drop;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Repeat after", "per-item cooldown, so one market cannot spam chat");
            ImGui.SetNextItemWidth(180f);
            var cooldownMinutes = config.AlertCooldownMinutes;
            if (Gw.Slider(ref cooldownMinutes, 5, 1440, "##cooldown"u8, 5, "%d min"))
                config.AlertCooldownMinutes = cooldownMinutes;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Pinned profit floor", "after a scan, pinned items above this say so");
            ImGui.SetNextItemWidth(180f);
            var floorGil = (int)Math.Clamp(config.AlertMinProfitPerDay, 0, 2_000_000);
            if (Gw.Slider(ref floorGil, 0, 2_000_000, "##floor"u8, 1000, "%d gil/day"))
                config.AlertMinProfitPerDay = floorGil;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Toast threshold", "the best gil/day that earns a toast, 0 for none");
            ImGui.SetNextItemWidth(180f);
            var toastThreshold = (int)Math.Clamp(config.ToastThreshold, 0, 200_000);
            if (Gw.Slider(ref toastThreshold, 0, 200_000, "##toast"u8, 1000, toastThreshold <= 0 ? "off" : "%d/day"))
                config.ToastThreshold = toastThreshold;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Check now", "/slotyield alerts does the same");
            if (Gw.Button("Check now"u8, ButtonStyle.Plain, ButtonSize.Small))
                this.plugin.RunAlertPoll(true);

            Gw.EndRow();
        }

        Gw.EndSection();

        if (Gw.BeginSection("Repricing"u8))
        {
            Gw.BeginRow("Minimum move", "gil between the old and new price before it is worth touching");
            ImGui.SetNextItemWidth(180f);
            var minMove = config.RepriceMinMoveGil;
            if (Gw.Slider(ref minMove, 0, 5000, "##minmove"u8, 10, "%d gil"))
                config.RepriceMinMoveGil = minMove;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Floor", "a price below this share of the current one is refused; 0 disables it");
            ImGui.SetNextItemWidth(180f);
            var floorPercent = config.RepriceFloorPercent;
            if (Gw.Slider(ref floorPercent, 0, 100, "##floorpercent"u8, 1, "%d%%"))
                config.RepriceFloorPercent = floorPercent;
            if (Gw.Committed)
                this.plugin.SaveConfig();
            Gw.EndRow();

            Gw.BeginRow("Auto-fill asking price", "when listing; /slotyield price does it on demand");
            var autoPrice = config.AutoPriceSellDialog;
            if (Gw.Toggle(ref autoPrice))
            {
                config.AutoPriceSellDialog = autoPrice;
                this.plugin.SaveConfig();
            }

            Gw.EndRow();
        }

        Gw.EndSection();

        ImGui.Spacing();
        Theme.Faint(
            "A slot earns (units you capture per day) x (net price). You capture 1/(sellers at your price + 1) " +
            "of the market's sales, because buyers work down the cheapest listings first. A deal takes 99 units " +
            "to clear, so slow items are flagged as dead slots rather than quietly tying up gil. Data: Universalis; " +
            "this plugin only reads - it never buys, lists, or moves your character.");
    }

    // ------------------------------------------------------------------ helpers ----

    private static string StrategyLabel(PricingStrategy strategy) => strategy switch
    {
        PricingStrategy.UndercutMinimum => "undercut the minimum",
        PricingStrategy.MatchAverageSale => "match the average sale",
        _ => "match the minimum",
    };

    private static string CostLabel(CostMode mode) => mode switch
    {
        CostMode.Revenue => "revenue only (no costs)",
        CostMode.Market => "buy the finished item",
        CostMode.Craft => "craft it",
        _ => "craft or buy, whichever is cheaper",
    };

    private string CategoryName(uint itemId)
    {
        if (this.categoryNames.TryGetValue(itemId, out var cached))
            return cached;

        string name;
        Plugin.SuppressTracer = true;
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            var item = sheet.GetRow(itemId);
            name = item.ItemUICategory.Value.Name.ToString();
        }
        catch
        {
            name = string.Empty;
        }
        finally
        {
            Plugin.SuppressTracer = false;
        }

        this.categoryNames[itemId] = name;
        return name;
    }

    /// <summary>
    /// The category combo's items for a scan. The scan is immutable, so the set is built once per scan
    /// instead of once per frame.
    /// </summary>
    private List<string> CategoryChoices(ScanResult result)
    {
        if (ReferenceEquals(result, this.categoryChoicesSource))
            return this.categoryChoices;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in result.Candidates)
        {
            var name = this.CategoryName(candidate.ItemId);
            if (name.Length > 0)
                set.Add(name);
        }

        this.categoryChoices.Clear();
        this.categoryChoices.AddRange(set.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        this.categoryChoicesSource = result;
        return this.categoryChoices;
    }

    /// <summary>The level you would need to craft or equip an item; 0 when the plugin cannot tell.</summary>
    private int RequiredLevel(uint itemId)
    {
        if (this.requiredLevels.TryGetValue(itemId, out var cached))
            return cached;

        var level = 0;

        var recipe = this.plugin.Recipes.GetRecipe(itemId);
        if (recipe is { CraftLevel: > 0 })
        {
            level = recipe.CraftLevel;
        }
        else
        {
            Plugin.SuppressTracer = true;
            try
            {
                var item = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRow(itemId);
                level = item.LevelEquip;
            }
            catch
            {
                level = 0;
            }
            finally
            {
                Plugin.SuppressTracer = false;
            }
        }

        this.requiredLevels[itemId] = level;
        return level;
    }

    /// <summary>
    /// The filtered, sorted opportunities rows. Filtering thousands of candidates and sorting them every
    /// frame is wasted work: the scan is immutable, so the list is rebuilt only when the scan or one of
    /// the filter/output-order inputs changes. Pinned, watchlist and budget edits are part of the key
    /// because the pinned/affordable/watched chips read them.
    /// </summary>
    private List<SlotCandidate> FilteredRows(ScanResult result)
    {
        if (this.rowsCacheValid
            && ReferenceEquals(result, this.rowsCacheSource)
            && this.rowsCacheSearch == this.search
            && this.rowsCacheMinGilPerDay == this.minGilPerDay
            && this.rowsCacheQuality == this.qualityFilter
            && this.rowsCacheCategory == this.categoryFilter
            && this.rowsCacheLevelCap == this.levelCap
            && this.rowsCacheMinStack == this.minStackProfit
            && this.rowsCachePinnedOnly == this.pinnedOnly
            && this.rowsCacheShowDead == this.showDead
            && this.rowsCacheAffordableOnly == this.affordableOnly
            && this.rowsCacheHideStale == this.hideStale
            && this.rowsCacheWatchedOnly == this.watchedOnly
            && this.rowsCacheSortColumn == this.sortColumn
            && this.rowsCacheSortAscending == this.sortAscending
            && this.rowsCachePinnedCount == this.Config.Pinned.Count
            && this.rowsCacheWatchedCount == this.Config.Watchlist.Count
            && this.rowsCacheBudget == this.Config.PortfolioBudget)
        {
            return this.rowsCache!;
        }

        var rows = this.Filter(result.Candidates);
        this.Sort(rows);

        this.rowsCache = rows;
        this.rowsCacheSource = result;
        this.rowsCacheSearch = this.search;
        this.rowsCacheMinGilPerDay = this.minGilPerDay;
        this.rowsCacheQuality = this.qualityFilter;
        this.rowsCacheCategory = this.categoryFilter;
        this.rowsCacheLevelCap = this.levelCap;
        this.rowsCacheMinStack = this.minStackProfit;
        this.rowsCachePinnedOnly = this.pinnedOnly;
        this.rowsCacheShowDead = this.showDead;
        this.rowsCacheAffordableOnly = this.affordableOnly;
        this.rowsCacheHideStale = this.hideStale;
        this.rowsCacheWatchedOnly = this.watchedOnly;
        this.rowsCacheSortColumn = this.sortColumn;
        this.rowsCacheSortAscending = this.sortAscending;
        this.rowsCachePinnedCount = this.Config.Pinned.Count;
        this.rowsCacheWatchedCount = this.Config.Watchlist.Count;
        this.rowsCacheBudget = this.Config.PortfolioBudget;
        this.rowsCacheValid = true;
        return rows;
    }

    private List<SlotCandidate> Filter(IReadOnlyList<SlotCandidate> source)
    {
        IEnumerable<SlotCandidate> rows = source;

        if (!this.showDead)
            rows = rows.Where(c => !c.Score.IsDead && !c.Score.IsSuspect);

        if (this.qualityFilter == 1)
            rows = rows.Where(c => c.Quality == Quality.Nq);
        else if (this.qualityFilter == 2)
            rows = rows.Where(c => c.Quality == Quality.Hq);

        if (this.pinnedOnly)
            rows = rows.Where(c => this.IsSlotPinned(c.ItemId, c.Quality));

        if (this.affordableOnly)
            rows = rows.Where(c => c.Score.CapitalPerSlot <= this.Config.PortfolioBudget);

        if (this.hideStale)
            rows = rows.Where(c => !c.Score.IsStale);

        if (this.watchedOnly)
            rows = rows.Where(c => this.IsWatched(c.ItemId, c.Quality));

        if (this.categoryFilter.Length > 0)
            rows = rows.Where(c => this.CategoryName(c.ItemId).Equals(this.categoryFilter, StringComparison.OrdinalIgnoreCase));

        if (this.levelCap > 0)
            rows = rows.Where(c => this.RequiredLevel(c.ItemId) <= this.levelCap);

        if (this.minStackProfit > 0)
            rows = rows.Where(c => c.Score.ProfitPerDay * (decimal)c.Score.DaysToClear >= this.minStackProfit);

        // 0 is "no minimum" by design (the slider labels it that way): unprofitable rows stay
        // visible so a slot that loses gil can be found and unpinned.
        if (this.minGilPerDay > 0)
            rows = rows.Where(c => c.Score.ProfitPerDay >= this.minGilPerDay);

        if (this.search.Length > 0)
            rows = rows.Where(c => this.plugin.Catalog.Name(c.ItemId).Contains(this.search, StringComparison.OrdinalIgnoreCase));

        return rows.ToList();
    }

    private void Sort(List<SlotCandidate> rows)
    {
        Comparison<SlotCandidate> comparison = this.sortColumn switch
        {
            0 => (a, b) => string.Compare(this.plugin.Catalog.Name(a.ItemId), this.plugin.Catalog.Name(b.ItemId), StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Quality.CompareTo(b.Quality),
            2 => (a, b) => a.Score.ListPrice.CompareTo(b.Score.ListPrice),
            3 => (a, b) => (a.Score.CostPerUnit ?? 0m).CompareTo(b.Score.CostPerUnit ?? 0m),
            4 => (a, b) => a.Score.GilPerDay.CompareTo(b.Score.GilPerDay),
            5 => (a, b) => a.Score.ProfitPerDay.CompareTo(b.Score.ProfitPerDay),
            6 => (a, b) => a.Quote.DailyVelocity.CompareTo(b.Quote.DailyVelocity),
            7 => (a, b) => a.Quote.DistinctSellers.CompareTo(b.Quote.DistinctSellers),
            8 => (a, b) => a.Score.ExpectedShare.CompareTo(b.Score.ExpectedShare),
            9 => (a, b) => a.Score.CapitalPerSlot.CompareTo(b.Score.CapitalPerSlot),
            10 => (a, b) => a.Score.DaysToClear.CompareTo(b.Score.DaysToClear),
            11 => (a, b) => a.Score.TrendPercent.CompareTo(b.Score.TrendPercent),
            12 => (a, b) => (a.Score.ProfitPerDay * (decimal)a.Score.DaysToClear).CompareTo(b.Score.ProfitPerDay * (decimal)b.Score.DaysToClear),
            _ => (a, b) => a.Score.ProfitPerDay.CompareTo(b.Score.ProfitPerDay),
        };

        rows.Sort(comparison);

        // The comparators above are ascending, so a descending request has to reverse them.
        // This was inverted: "v" (descending) used to show the ascending list.
        if (!this.sortAscending)
            rows.Reverse();
    }

    /// <summary>
    /// Puts the saved sort back into the table after it (re)initialises: ImGui would otherwise keep its own
    /// default column, or a column index restored from its saved settings for a different visible set.
    /// </summary>
    private void RepairTableSort(ReadOnlySpan<int> visibleColumns, int visibleCount)
    {
        var visible = -1;
        for (var index = 0; index < visibleCount; index++)
        {
            if (visibleColumns[index] == this.sortColumn)
            {
                visible = index;
                break;
            }
        }

        // A hidden sort column has no header to point at; the rows keep their saved order anyway.
        if (visible < 0)
            return;

        ImGuiP.TableSetColumnSortDirection(
            visible,
            this.sortAscending ? ImGuiSortDirection.Ascending : ImGuiSortDirection.Descending,
            false);
    }

    /// <summary>
    /// Puts the saved my-listings sort back into the table after it (re)initialises: ImGui would otherwise keep
    /// its own default column, or a sort restored from its saved settings. Until a column is picked the rows stay
    /// grouped by retainer, and the chevron points at Item descending so that clicking it still sorts ascending,
    /// the way the hand-rolled header did.
    /// </summary>
    private void RepairListingSort()
    {
        var column = this.listingSortColumn >= 0 ? this.listingSortColumn : 0;
        var ascending = this.listingSortColumn >= 0 && this.listingSortAscending;
        ImGuiP.TableSetColumnSortDirection(
            column,
            ascending ? ImGuiSortDirection.Ascending : ImGuiSortDirection.Descending,
            false);
    }

    /// <summary>Puts the saved arbitrage sort back into the table after it (re)initialises, like the others.</summary>
    private void RepairArbitrageSort()
    {
        ImGuiP.TableSetColumnSortDirection(
            this.arbitrageSortColumn,
            this.arbitrageSortAscending ? ImGuiSortDirection.Ascending : ImGuiSortDirection.Descending,
            false);
    }

    /// <summary>Puts the portfolio sort back after the table re-initialises, so the chevron matches the rows.</summary>
    private void RepairPortfolioSort()
    {
        ImGuiP.TableSetColumnSortDirection(
            this.portfolioSortColumn,
            this.portfolioSortAscending ? ImGuiSortDirection.Ascending : ImGuiSortDirection.Descending,
            false);
    }

    /// <summary>
    /// Puts the craft sort back after the table re-initialises. Until a column is picked the rows keep the
    /// planner's order (best profit first), and the chevron points at Profit descending so clicking it
    /// still sorts ascending, the way the hand-rolled header did.
    /// </summary>
    private void RepairCraftSort()
    {
        var column = this.craftSortColumn >= 0 ? this.craftSortColumn : 6;
        var ascending = this.craftSortColumn >= 0 && this.craftSortAscending;
        ImGuiP.TableSetColumnSortDirection(
            column,
            ascending ? ImGuiSortDirection.Ascending : ImGuiSortDirection.Descending,
            false);
    }

    /// <summary>Portfolio comparators in data-column order; column 0 is the rank, handled as plan order.</summary>
    private Comparison<PortfolioCandidate>[] PortfolioComparisons => this.portfolioComparisons ??=
        new Comparison<PortfolioCandidate>[]
        {
            (a, b) => 0,
            (a, b) => string.Compare(this.plugin.Catalog.Name(a.ItemId), this.plugin.Catalog.Name(b.ItemId), StringComparison.OrdinalIgnoreCase),
            (a, b) => a.Quality.CompareTo(b.Quality),
            (a, b) => (this.PortfolioQuote(a)?.DailyVelocity ?? 0d).CompareTo(this.PortfolioQuote(b)?.DailyVelocity ?? 0d),
            (a, b) => (this.PortfolioQuote(a)?.DistinctSellers ?? 0).CompareTo(this.PortfolioQuote(b)?.DistinctSellers ?? 0),
            (a, b) => a.GilPerDay.CompareTo(b.GilPerDay),
            (a, b) => a.CapitalPerSlot.CompareTo(b.CapitalPerSlot),
        };

    private MarketQuote? PortfolioQuote(PortfolioCandidate candidate) =>
        this.portfolioSortQuotes?.GetValueOrDefault((candidate.ItemId, candidate.Quality));

    /// <summary>Craft comparators in data-column order, matching what each cell shows.</summary>
    private Comparison<CraftOpportunity>[] CraftComparisons => this.craftComparisons ??=
        new Comparison<CraftOpportunity>[]
        {
            (a, b) => string.Compare(this.plugin.Catalog.Name(a.ItemId), this.plugin.Catalog.Name(b.ItemId), StringComparison.OrdinalIgnoreCase),
            (a, b) =>
            {
                var job = string.Compare(this.plugin.Catalog.JobAbbreviation(a.CraftJob), this.plugin.Catalog.JobAbbreviation(b.CraftJob), StringComparison.OrdinalIgnoreCase);
                return job != 0 ? job : a.RequiredLevel.CompareTo(b.RequiredLevel);
            },
            (a, b) => a.Crafts.CompareTo(b.Crafts),
            (a, b) => a.Produced.CompareTo(b.Produced),
            (a, b) => a.Revenue.CompareTo(b.Revenue),
            (a, b) => a.MaterialValue.CompareTo(b.MaterialValue),
            (a, b) => a.Profit.CompareTo(b.Profit),
            (a, b) => CraftPerUnit(a).CompareTo(CraftPerUnit(b)),
            (a, b) => (CraftPerUnit(a) * this.Config.DefaultStackSize).CompareTo(CraftPerUnit(b) * this.Config.DefaultStackSize),
            (a, b) => a.HqProfit.CompareTo(b.HqProfit),
            (a, b) => a.OwnedProfit.CompareTo(b.OwnedProfit),
            (a, b) => a.DaysToSell.CompareTo(b.DaysToSell),
        };

    private static decimal CraftPerUnit(CraftOpportunity idea) => idea.Produced > 0 ? idea.Profit / idea.Produced : 0m;

    private bool IsSlotPinned(uint itemId, Quality quality) =>
        this.Config.Pinned.Any(p => p.ItemId == itemId && p.Hq == (quality == Quality.Hq));

    private bool IsWatched(uint itemId, Quality quality)
        => this.Config.Watchlist.Any(watched => watched.ItemId == itemId && watched.Hq == (quality == Quality.Hq));

    private void ToggleWatch(uint itemId, Quality quality)
    {
        var wanted = quality == Quality.Hq;
        var existing = this.Config.Watchlist.FirstOrDefault(watched => watched.ItemId == itemId && watched.Hq == wanted);

        if (existing is not null)
        {
            this.Config.Watchlist.Remove(existing);
        }
        else
        {
            this.Config.Watchlist.Add(new WatchedItem
            {
                ItemId = itemId,
                Hq = wanted,
            });
        }

        this.plugin.SaveConfig();
    }

    private void TogglePin(uint itemId, Quality quality)
    {
        var wanted = quality == Quality.Hq;
        var existing = this.Config.Pinned.FirstOrDefault(p => p.ItemId == itemId && p.Hq == wanted);

        if (existing is not null)
        {
            this.Config.Pinned.Remove(existing);
            this.undoSlot = existing;
            this.undoUtc = DateTime.UtcNow;
        }
        else
        {
            this.Config.Pinned.Add(new PinnedSlot
            {
                ItemId = itemId,
                Hq = wanted,
                StackSize = this.Config.DefaultStackSize,
            });
        }

        this.plugin.SaveConfig();
    }

    private string BuildCsv(IReadOnlyList<SlotCandidate> rows)
    {
        var header = new[]
        {
            "item", "quality", "price", "cost", "cost_source", "cost_detail", "revenue_per_day", "profit_per_day",
            "sales_per_day", "sellers", "share", "capital", "days_to_clear", "trend_30d", "verdict",
        };

        var lines = rows.Select(candidate => (IReadOnlyList<string>)new[]
        {
            this.plugin.Catalog.Name(candidate.ItemId),
            candidate.Quality == Quality.Hq ? "HQ" : "NQ",
            candidate.Score.ListPrice.ToString("0.##", CultureInfo.InvariantCulture),
            candidate.Score.CostPerUnit is { } unitCost
                ? unitCost.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty,
            candidate.Score.CostSource,
            candidate.Score.CostDetail,
            candidate.Score.GilPerDay.ToString("0.##", CultureInfo.InvariantCulture),
            candidate.Score.ProfitPerDay.ToString("0.##", CultureInfo.InvariantCulture),
            candidate.Quote.DailyVelocity.ToString("0.###", CultureInfo.InvariantCulture),
            candidate.Quote.DistinctSellers.ToString(CultureInfo.InvariantCulture),
            candidate.Score.ExpectedShare.ToString("0.####", CultureInfo.InvariantCulture),
            candidate.Score.CapitalPerSlot.ToString("0.##", CultureInfo.InvariantCulture),
            candidate.Score.DaysToClear.ToString("0.#", CultureInfo.InvariantCulture),
            candidate.Score.TrendPercent.ToString("0.####", CultureInfo.InvariantCulture),
            candidate.Score.Verdict,
        });

        return CsvExport.Build(header, lines);
    }

    // ------------------------------------------------------------------ changes ----

    private void DrawChanges()
    {
        var diff = this.plugin.State.Diff;

        if (diff is null)
        {
            ImGui.Spacing();
            Theme.Dimmed("No previous scan for this world yet.");
            ImGui.TextWrapped(
                "Run a scan now, then run another later. This tab lists what moved in between: price " +
                "drops and rises over 5%, new entries, and items that fell off the list.");
            return;
        }

        Theme.Dimmed(
            $"Compared with {diff.PreviousUtc.ToLocalTime():g}  -  {diff.PreviousCount} tracked then, {diff.CurrentCount} now");

        if (!diff.HasAnything)
        {
            ImGui.Spacing();
            Theme.Dimmed("Nothing moved by more than 5%.");
            return;
        }

        ImGui.Spacing();
        this.DrawDeltaSection("Price drops", diff.Drops, Theme.Danger);
        this.DrawDeltaSection("Price rises", diff.Rises, Theme.Accent);
        this.DrawEntrySection("New in the list", diff.NewEntries, Theme.Gold);
        this.DrawEntrySection("Left the tracked list", diff.Gone, Theme.TextDim);
    }

    private void DrawDeltaSection(string title, IReadOnlyList<EntryDelta> deltas, Vector4 color)
    {
        if (deltas.Count == 0)
            return;

        if (Gw.BeginSection(title))
        {
            foreach (var delta in deltas)
            {
                var name = Truncate(this.plugin.Catalog.Name(delta.ItemId), 36);
                var quality = delta.Quality == Quality.Hq ? "HQ" : "NQ";
                var percent = delta.PriceChangePercent.ToString("+0.0%;-0.0%;0%");
                var profit = delta.ProfitChange >= 0m
                    ? "+" + Money.Gil(delta.ProfitChange)
                    : "-" + Money.Gil(-delta.ProfitChange);

                Gw.Text(
                    $"  {name,-36} {quality}  {Money.Gil(delta.Previous.Price),8} -> {Money.Gil(delta.Current.Price),8}   {percent,-7} profit {profit}",
                    TextRole.Mono,
                    Theme.ToColor(color));
            }
        }

        Gw.EndSection();
    }

    private void DrawEntrySection(string title, IReadOnlyList<SnapshotEntry> entries, Vector4 color)
    {
        if (entries.Count == 0)
            return;

        if (Gw.BeginSection(title))
        {
            foreach (var entry in entries)
            {
                var name = Truncate(this.plugin.Catalog.Name(entry.ItemId), 36);
                var quality = entry.Quality == Quality.Hq ? "HQ" : "NQ";
                Gw.Text(
                    $"  {name,-36} {quality}  {Money.Gil(entry.Price),8}   {Money.Gil(entry.ProfitPerDay),9}/day",
                    TextRole.Mono,
                    Theme.ToColor(color));
            }
        }

        Gw.EndSection();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "~";

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best effort.
        }
    }
}
