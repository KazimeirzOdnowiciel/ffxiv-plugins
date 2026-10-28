using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using SlotYield.Core;
using SlotYield.Game;
using SlotYield.Market;

namespace SlotYield.UI;

public sealed class MainWindow : Window
{
    /// <summary>Every retainer can hold twenty market listings.</summary>
    private const int MarketSlotsPerRetainer = 20;

    private IReadOnlyList<(InventoryType Type, int Slot, uint ItemId, bool Hq, int Quantity, int Price)> listingCandidates =
        Array.Empty<(InventoryType, int, uint, bool, int, int)>();

    private readonly Plugin plugin;

    private string search = string.Empty;
    private int sortColumn = 5;
    private bool sortAscending;
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
    private PinnedSlot? undoSlot;
    private DateTime undoUtc;
    private bool watchedOnly;

    public IReadOnlyCollection<(uint ItemId, Quality Quality)> MultiSelection => this.multiSelected;

    private static readonly string[] TabLabels =
    {
        "Opportunities", "Changes", "Portfolio", "Craft", "My listings", "Arbitrage", "Scan",
    };

    private static readonly string[] ColumnLabels =
    {
        "Item", "Q", "Price", "Cost", "Revenue/d", "Profit/d", "Sales/d", "Sellers", "Share", "Capital", "Clear", "30d", "",
    };

    private readonly bool[] columnVisible =
    {
        true, true, true, true, true, true, true, true, true, true, true, true, true,
    };
    private int minGilPerDay;
    private bool pinnedOnly;
    private bool affordableOnly;
    private bool hideStale;
    private bool drawFailureReported;
    private int listingSortColumn = -1;
    private bool listingSortAscending;

    public MainWindow(Plugin plugin)
        : base("Slot Yield###SlotYieldMain")
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

    /// <summary>Sliders update the in-memory config every frame so they drag smoothly, but the
    /// config is only written to disk when the drag ends.</summary>
    private void SaveAfterEdit()
    {
        if (ImGui.IsItemDeactivatedAfterEdit())
            this.plugin.SaveConfig();
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

    /// <summary>
    /// ImGui's stock frame/checkbox/combo colours are what made the window read as a debug
    /// tool. Scoped to this window only, so other plugins keep their own look.
    /// </summary>
    private void PushControlStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.PanelHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.PanelHover);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.PanelHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.WithAlpha(Theme.Accent, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.WithAlpha(Theme.Accent, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Lerp(Theme.PanelRaised, Theme.Accent, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Lerp(Theme.PanelRaised, Theme.Accent, 0.42f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Theme.WithAlpha(Theme.Background, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.WithAlpha(Theme.Text, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.WithAlpha(Theme.Text, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.WithAlpha(Theme.Accent, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Lerp(Theme.PanelRaised, Theme.Accent, 0.50f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Separator, Theme.Hairline);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.Hairline);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, Theme.Border);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Theme.WithAlpha(Theme.Background, 0f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, Theme.WithAlpha(Theme.Text, 0.030f));

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
    }

    private void PopControlStyle()
    {
        ImGui.PopStyleVar(5);
        ImGui.PopStyleColor(23);
    }

    /// <summary>Table height that fills the window instead of stranding empty space below.</summary>
    private static float TableHeight() => MathF.Max(220f, ImGui.GetContentRegionAvail().Y - 4f);

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

        this.PushControlStyle();
        try
        {
            this.DrawContent();
        }
        finally
        {
            this.PopControlStyle();
        }
    }

    private void DrawContent()
    {
        var deltaTime = ImGui.GetIO().DeltaTime;
        this.cardFade += (1f - this.cardFade) * (1f - MathF.Exp(-10f * deltaTime));
        this.sparkProgress += (1f - this.sparkProgress) * (1f - MathF.Exp(-8f * deltaTime));

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
        if (this.openDetail)
        {
            ImGui.SetNextWindowSize(new Vector2(460f, 400f), ImGuiCond.Appearing);
            ImGui.OpenPopup("##itemdetail");
            this.openDetail = false;
        }

        if (!ImGui.BeginPopup("##itemdetail"))
            return;

        var result = this.plugin.State.Result;
        var candidate = result?.Candidates.FirstOrDefault(c => c.ItemId == this.detailItemId && c.Quality == this.detailQuality);
        if (candidate is null)
        {
            Theme.Dimmed("Not in the current scan.");
            ImGui.EndPopup();
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
        if (ImGui.Button(this.IsSlotPinned(candidate.ItemId, candidate.Quality) ? "unpin" : "pin to slot"))
            this.TogglePin(candidate.ItemId, candidate.Quality);

        ImGui.SameLine();
        if (ImGui.Button("universalis"))
            OpenUrl($"https://universalis.app/market/{candidate.ItemId}");

        ImGui.SameLine();
        if (ImGui.Button("history"))
            this.plugin.OpenHistory(candidate.ItemId, candidate.Quality);

        ImGui.SameLine();
        if (ImGui.Button("close"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
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

        this.tabUnderlineX += (activeMin - this.tabUnderlineX) * (1f - MathF.Exp(-14f * ImGui.GetIO().DeltaTime));

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

        this.stripPulse = MathF.Max(0f, this.stripPulse - (ImGui.GetIO().DeltaTime * 2f));

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

        var ringPos = ImGui.GetCursorScreenPos();
        var ringFraction = budget > 0m ? Math.Clamp((float)(capital / budget), 0f, 1f) : 0f;
        UiKit.Ring(ImGui.GetWindowDrawList(), ringPos + new Vector2(11f, 11f), 11f, 3.5f, ringFraction, capital > budget ? Theme.Warn : Theme.Accent);
        ImGui.Dummy(new Vector2(26f, 22f));
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
            var pulseSeries = new decimal[history.Count];
            for (var index = 0; index < history.Count; index++)
                pulseSeries[index] = history[index].GilPerDay;
            DrawSparkline(ImGui.GetWindowDrawList(), pulsePos, new Vector2(pulseWidth, pulseHeight), pulseSeries, Theme.U32(Theme.Accent));
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
            UiKit.EmptyState(
                "No scan yet",
                "A scan walks every marketable item on your world, ranks them by how much gil a slot could realistically earn, and prices the shortlist exactly. It takes one to three minutes and stays cached for a few hours.");
            if (ImGui.Button("Scan now"))
                this.plugin.StartScan();

            ImGui.SameLine();
            if (ImGui.Button("Open scan settings"))
                this.plugin.MainWindow.IsOpen = true;

            return;
        }

        var best = this.BestCandidate(result.Candidates);
        if (best is not null)
            this.DrawBestPick(best);

        UiKit.SectionHeader("All opportunities");

        // Filters.
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.F))
            this.focusSearch = true;

        ImGui.SetNextItemWidth(220f);
        if (this.focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            this.focusSearch = false;
        }

        ImGui.InputTextWithHint("##search", "filter by item name...", ref this.search, 64);
        if (ImGui.IsItemActive() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            this.search = string.Empty;
        ImGui.SameLine(0f, 8f);
        // 0 means "no minimum" and is deliberate: unprofitable rows stay visible so a losing
        // slot can be spotted. Say that instead of printing "min 0", which read as ">= 0".
        ImGui.SetNextItemWidth(150f);
        var minFormat = this.minGilPerDay <= 0 ? "no minimum" : "min %d profit/day";
        ImGui.SliderInt("##min", ref this.minGilPerDay, 0, 200_000, minFormat);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("0 shows every row, including ones that would lose gil. Raise it to hide them.");
        ImGui.SameLine();

        var quality = this.qualityFilter;
        ImGui.SetNextItemWidth(90f);
        if (ImGui.BeginCombo("##quality", quality == 0 ? "NQ+HQ" : quality == 1 ? "NQ" : "HQ"))
        {
            if (ImGui.Selectable("NQ+HQ", quality == 0))
                this.qualityFilter = 0;
            if (ImGui.Selectable("NQ", quality == 1))
                this.qualityFilter = 1;
            if (ImGui.Selectable("HQ", quality == 2))
                this.qualityFilter = 2;
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var categoryChoices = this.CategoryChoices(result.Candidates);
        ImGui.SetNextItemWidth(130f);
        if (ImGui.BeginCombo("##category", this.categoryFilter.Length == 0 ? "all categories" : this.categoryFilter))
        {
            if (ImGui.Selectable("all categories", this.categoryFilter.Length == 0))
                this.categoryFilter = string.Empty;

            foreach (var categoryChoice in categoryChoices)
            {
                if (ImGui.Selectable(categoryChoice, this.categoryFilter == categoryChoice))
                    this.categoryFilter = categoryChoice;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        var levelCap = Math.Clamp(this.levelCap, 0, 100);
        if (ImGui.SliderInt("##level", ref levelCap, 0, 100, levelCap <= 0 ? "any level" : "level %d"))
            this.levelCap = levelCap;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide items whose craft or equip level is above this. 0 shows everything.");

        ImGui.SameLine();
        if (ImGui.SmallButton("my level"))
            this.levelCap = Plugin.PlayerState.Level;

        ImGui.SameLine();
        UiKit.ToggleChip("pinned", ref this.pinnedOnly);
        ImGui.SameLine();
        UiKit.ToggleChip("dead", ref this.showDead);
        ImGui.SameLine();
        UiKit.ToggleChip("affordable", ref this.affordableOnly);
        ImGui.SameLine();
        UiKit.ToggleChip("fresh only", ref this.hideStale);

        ImGui.SameLine();
        UiKit.ToggleChip("watched", ref this.watchedOnly);

        ImGui.SameLine();
        if (ImGui.SmallButton("reset"))
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
        if (ImGui.SmallButton("save filter"))
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
        if (ImGui.SmallButton("presets"))
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
        if (ImGui.SmallButton("columns"))
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

        var rows = this.Filter(result.Candidates);
        this.Sort(rows);

        ImGui.SameLine();
        Theme.Faint($"{rows.Count} shown");

        if (this.multiSelected.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"pin {this.multiSelected.Count}"))
            {
                foreach (var (itemId, pinQuality) in this.multiSelected)
                {
                    if (!this.IsSlotPinned(itemId, pinQuality))
                        this.TogglePin(itemId, pinQuality);
                }

                this.multiSelected.Clear();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("compare"))
                this.plugin.ToggleCompare();

            ImGui.SameLine();
            if (ImGui.SmallButton("x"))
                this.multiSelected.Clear();
        }

        if (this.undoSlot is not null && (DateTime.UtcNow - this.undoUtc).TotalSeconds < 10)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("undo unpin"))
            {
                this.Config.Pinned.Add(this.undoSlot);
                this.Config.Normalize();
                this.plugin.SaveConfig();
                this.undoSlot = null;
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

        if (this.plugin.State.Diff is { HasAnything: true } changes)
        {
            ImGui.SameLine();
            Theme.Faint(
                $"| since last scan: {changes.Drops.Count} down, {changes.Rises.Count} up, {changes.NewEntries.Count} new");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("copy CSV"))
        {
            ImGui.SetClipboardText(this.BuildCsv(rows));
            this.plugin.Print($"Copied {rows.Count} rows as CSV.");
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, this.Config.CompactRows ? new Vector2(6f, 1f) : new Vector2(6f, 6f));
        if (!ImGui.BeginTable("##slots", 13, flags, new Vector2(0f, TableHeight())))
        {
            ImGui.PopStyleVar();
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.4f, 0);
        ImGui.TableSetupColumn("Q", ImGuiTableColumnFlags.WidthFixed, 26f, 1);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 70f, 2);
        ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.WidthFixed, 70f, 3);
        ImGui.TableSetupColumn("Revenue/d", ImGuiTableColumnFlags.WidthFixed, 78f, 4);
        ImGui.TableSetupColumn("Profit/d", ImGuiTableColumnFlags.WidthFixed, 78f, 5);
        ImGui.TableSetupColumn("Sales/d", ImGuiTableColumnFlags.WidthFixed, 58f, 6);
        ImGui.TableSetupColumn("Sellers", ImGuiTableColumnFlags.WidthFixed, 52f, 7);
        ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthFixed, 48f, 8);
        ImGui.TableSetupColumn("Capital", ImGuiTableColumnFlags.WidthFixed, 72f, 9);
        ImGui.TableSetupColumn("Clear", ImGuiTableColumnFlags.WidthFixed, 52f, 10);
        ImGui.TableSetupColumn("30d", ImGuiTableColumnFlags.WidthFixed, 112f, 11);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 40f, 12);

        for (var columnIndex = 0; columnIndex < this.columnVisible.Length; columnIndex++)
            ImGui.TableSetColumnEnabled(columnIndex, this.columnVisible[columnIndex]);

        ImGui.TableSetupScrollFreeze(0, 1);
        this.DrawHeaderRow();

        var limit = Math.Min(rows.Count, 250);
        var maxMagnitude = 1m;
        for (var i = 0; i < limit; i++)
        {
            var magnitude = Math.Abs(rows[i].Score.ProfitPerDay);
            if (magnitude > maxMagnitude)
                maxMagnitude = magnitude;
        }

        this.maxProfitMagnitude = maxMagnitude;

        for (var i = 0; i < limit; i++)
            this.DrawRow(rows[i], i);

        ImGui.EndTable();
        ImGui.PopStyleVar();

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

    private void DrawHeaderRow()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        this.HeaderButton("Item", 0, ImGuiTableColumnFlags.WidthStretch);
        this.HeaderButton("Q", 1, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Price", 2, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Cost", 3, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Revenue/d", 4, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Profit/d", 5, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Sales/d", 6, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Sellers", 7, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Share", 8, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Capital", 9, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("Clear", 10, ImGuiTableColumnFlags.WidthFixed);
        this.HeaderButton("30d", 11, ImGuiTableColumnFlags.WidthFixed);

        ImGui.TableNextColumn();
    }

    private void HeaderButton(string label, int column, ImGuiTableColumnFlags flags)
    {
        ImGui.TableNextColumn();
        if (ImGui.SmallButton(label + "##h" + column))
        {
            if (this.sortColumn == column)
            {
                this.sortAscending = !this.sortAscending;
            }
            else
            {
                this.sortColumn = column;
                this.sortAscending = false;
            }

            this.Config.SortColumn = this.sortColumn;
            this.Config.SortAscending = this.sortAscending;
            this.plugin.SaveConfig();
        }

        if (this.sortColumn == column)
        {
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var centerX = max.X + 6f;
            var centerY = (min.Y + max.Y) * 0.5f;
            if (this.sortAscending)
                drawList.AddTriangleFilled(
                    new Vector2(centerX - 4f, centerY + 2f),
                    new Vector2(centerX + 4f, centerY + 2f),
                    new Vector2(centerX, centerY - 3f),
                    Theme.U32(Theme.Accent));
            else
                drawList.AddTriangleFilled(
                    new Vector2(centerX - 4f, centerY - 2f),
                    new Vector2(centerX + 4f, centerY - 2f),
                    new Vector2(centerX, centerY + 3f),
                    Theme.U32(Theme.Accent));
        }
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

    private void DrawRow(SlotCandidate candidate, int index)
    {
        var score = candidate.Score;
        var info = this.plugin.Catalog.Get(candidate.ItemId);
        var pinned = this.IsSlotPinned(candidate.ItemId, candidate.Quality);
        var dim = score.IsDead;

        ImGui.TableNextRow();

        var rowTop = ImGui.GetCursorScreenPos().Y;
        var rowBottom = rowTop + MathF.Max(ImGui.GetTextLineHeight(), 20f) + 8f;
        var mouseY = ImGui.GetMousePos().Y;
        if (mouseY >= rowTop && mouseY <= rowBottom)
        {
            var rowMin = new Vector2(ImGui.GetCursorScreenPos().X, rowTop);
            var rowMax = new Vector2(rowMin.X + ImGui.GetContentRegionAvail().X, rowBottom);
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, Theme.WithAlpha(Theme.Accent, 0.10f), 2f);
        }

        if (index == this.selectedRow)
        {
            var barMin = new Vector2(ImGui.GetCursorScreenPos().X - 2f, rowTop);
            ImGui.GetWindowDrawList().AddRectFilled(barMin, barMin + new Vector2(3f, rowBottom - rowTop), Theme.U32(Theme.Accent));
        }

        if (this.multiSelected.Contains((candidate.ItemId, candidate.Quality)))
        {
            var selMin = new Vector2(ImGui.GetCursorScreenPos().X, rowTop);
            var selMax = new Vector2(selMin.X + ImGui.GetContentRegionAvail().X, rowBottom);
            ImGui.GetWindowDrawList().AddRectFilled(selMin, selMax, Theme.WithAlpha(Theme.Accent, 0.16f), 2f);
        }
        ImGui.TableNextColumn();

        var nameColor = dim ? Theme.TextFaint : Theme.Text;
        UiKit.ItemIcon(UiKit.IconIdForItem(candidate.ItemId), candidate.Quality == Quality.Hq, 18f);
        ImGui.SameLine(0f, 6f);
        Theme.Colored(nameColor, info.Name);
        var nameHovered = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && ImGui.GetIO().KeyCtrl)
        {
            var key = (candidate.ItemId, candidate.Quality);
            if (!this.multiSelected.Add(key))
                this.multiSelected.Remove(key);
        }

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
        {            var tip = score.Explain;
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

        ImGui.TableNextColumn();
        Theme.Colored(candidate.Quality == Quality.Hq ? Theme.Hq : Theme.TextDim, candidate.Quality == Quality.Hq ? "HQ" : "NQ");

        ImGui.TableNextColumn();
        Theme.Colored(dim ? Theme.TextFaint : Theme.Text, Money.Gil(score.ListPrice));

        ImGui.TableNextColumn();
        var costText = score.CostPerUnit is { } unitCost
            ? Money.Gil(unitCost) + (score.CostIsPartial ? "?" : string.Empty)
            : "-";
        Theme.Colored(dim ? Theme.TextFaint : score.CostIsPartial ? Theme.Warn : Theme.TextDim, costText);

        ImGui.TableNextColumn();
        Theme.Colored(dim ? Theme.TextFaint : Theme.TextDim, dim ? "-" : Money.Gil(score.GilPerDay));

        ImGui.TableNextColumn();
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
                var heatColor = profit > 0m ? Theme.Profit : Theme.Danger;
                ImGui.GetWindowDrawList().AddRectFilled(
                    heatOrigin,
                    heatOrigin + new Vector2(cellWidth * heat, ImGui.GetTextLineHeight()),
                    Theme.WithAlpha(heatColor, 0.16f),
                    2f);
            }
        }

        Theme.Colored(profitColor, dim ? "-" : Money.Gil(profit));

        ImGui.TableNextColumn();
        Theme.Colored(dim ? Theme.TextFaint : Theme.Text, score.Quote.DailyVelocity.ToString("0.0"));

        ImGui.TableNextColumn();
        Theme.Colored(dim ? Theme.TextFaint : Theme.Text, candidate.Quote.DistinctSellers.ToString());

        ImGui.TableNextColumn();
        var share = (float)score.ExpectedShare;
        var shareOrigin = ImGui.GetCursorScreenPos() + new Vector2(0f, 4f);
        UiKit.Bar(ImGui.GetWindowDrawList(), shareOrigin, new Vector2(18f, 6f), share, dim ? Theme.TextFaint : Theme.Accent);
        ImGui.Dummy(new Vector2(20f, 14f));
        ImGui.SameLine(0f, 3f);
        Theme.Colored(dim ? Theme.TextFaint : Theme.TextDim, Money.Percent(score.ExpectedShare));

        ImGui.TableNextColumn();
        Theme.Colored(dim ? Theme.TextFaint : Theme.TextDim, Money.Gil(score.CapitalPerSlot));

        ImGui.TableNextColumn();
        var clearColor = dim ? Theme.Danger : score.DaysToClear <= 14 ? Theme.Text : Theme.TextDim;
        Theme.Colored(clearColor, Money.Days(score.DaysToClear));

        ImGui.TableNextColumn();
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

        ImGui.TableNextColumn();
        ImGui.PushID((int)candidate.ItemId * 2 + (candidate.Quality == Quality.Hq ? 1 : 0));
        if (ImGui.SmallButton(pinned ? "unpin" : "pin"))
            this.TogglePin(candidate.ItemId, candidate.Quality);
        ImGui.PopID();
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

        this.DrawPortfolioSummaryCard(plan);

        if (plan.Truncated)
            Theme.Colored(Theme.Warn, "Large candidate set: the plan is near-optimal rather than provably optimal.");

        ImGui.Spacing();

        var planQuotes = chosen
            .GroupBy(c => (c.ItemId, c.Quality))
            .ToDictionary(g => g.Key, g => g.First().Quote);

        if (ImGui.BeginTable("##plan", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
                                          ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0f, TableHeight())))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28f, 0);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.4f, 1);
            ImGui.TableSetupColumn("Q", ImGuiTableColumnFlags.WidthFixed, 26f, 2);
            ImGui.TableSetupColumn("Sales/d", ImGuiTableColumnFlags.WidthFixed, 52f, 3);
            ImGui.TableSetupColumn("Sellers", ImGuiTableColumnFlags.WidthFixed, 48f, 4);
            ImGui.TableSetupColumn("Profit/day", ImGuiTableColumnFlags.WidthFixed, 84f, 5);
            ImGui.TableSetupColumn("Capital", ImGuiTableColumnFlags.WidthFixed, 84f, 6);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            foreach (var label in new[] { "#", "Item", "Q", "Sales/d", "Sellers", "Profit/day", "Capital" })
            {
                ImGui.TableNextColumn();
                Theme.Faint(label);
            }

            var rank = 1;
            foreach (var pick in plan.Picks)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Theme.Faint(rank++ + ".");

                ImGui.TableNextColumn();
                UiKit.ItemIcon(UiKit.IconIdForItem(pick.ItemId), pick.Quality == Quality.Hq, 16f);
                ImGui.SameLine(0f, 6f);
                Theme.Colored(Theme.Text, this.plugin.Catalog.Name(pick.ItemId));

                ImGui.TableNextColumn();
                Theme.Colored(pick.Quality == Quality.Hq ? Theme.Hq : Theme.TextDim, pick.Quality == Quality.Hq ? "HQ" : "NQ");

                var quote = planQuotes.GetValueOrDefault((pick.ItemId, pick.Quality));
                ImGui.TableNextColumn();
                Theme.Colored(Theme.TextDim, quote is null ? "-" : quote.DailyVelocity.ToString("0.0"));

                ImGui.TableNextColumn();
                Theme.Colored(Theme.TextDim, quote is null ? "-" : quote.DistinctSellers.ToString());

                ImGui.TableNextColumn();
                Theme.Colored(Theme.Profit, Money.Gil(pick.GilPerDay));

                ImGui.TableNextColumn();
                Theme.Dimmed(Money.Gil(pick.CapitalPerSlot));
            }

            ImGui.EndTable();
        }

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
        if (ImGui.SmallButton("share"))
            ImGui.SetClipboardText(this.plugin.BuildShareCode());

        ImGui.SameLine();
        if (ImGui.SmallButton("import"))
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

        foreach (var slot in pinned)
        {
            var candidate = result?.Candidates.FirstOrDefault(c => c.ItemId == slot.ItemId && c.Quality == (slot.Hq ? Quality.Hq : Quality.Nq));
            var name = this.plugin.Catalog.Name(slot.ItemId);

            ImGui.PushID((int)slot.ItemId * 2 + (slot.Hq ? 1 : 0));

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

        if (ImGui.Button("Read stock"))
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

        if (this.plugin.IsPricingCraft)
        {
            ImGui.SameLine();
            Theme.Colored(Theme.Warn, "pricing candidates...");
        }

        var plan = this.plugin.CraftPlan;

        if (plan.Count == 0)
        {
            UiKit.EmptyState(
                this.plugin.IsPricingCraft ? "Pricing your bags..." : "No recipes ready",
                this.plugin.IsPricingCraft
                    ? "Pricing the items your bags can make - this fills in on its own."
                    : "No fully matchable recipes yet. Read your bags again after gathering, or check that this character holds the materials.");
            return;
        }

        ImGui.Spacing();

        var bestIdea = plan.MaxBy(idea => idea.Profit);
        if (bestIdea is not null && bestIdea.Profit > 0m)
            this.DrawCraftSummaryCard(bestIdea, plan.Count);

        if (!ImGui.BeginTable(
                "##craft",
                10,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0f, TableHeight())))
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.4f, 0);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 64f, 1);
        ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, 52f, 2);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 44f, 3);
        ImGui.TableSetupColumn("Revenue", ImGuiTableColumnFlags.WidthFixed, 76f, 4);
        ImGui.TableSetupColumn("Mats", ImGuiTableColumnFlags.WidthFixed, 76f, 5);
        ImGui.TableSetupColumn("Profit", ImGuiTableColumnFlags.WidthFixed, 76f, 6);
        ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 72f, 7);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 76f, 8);
        ImGui.TableSetupColumn("Clear", ImGuiTableColumnFlags.WidthFixed, 52f, 9);

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        foreach (var label in new[] { "Item", "Job", "Crafts", "Qty", "Revenue", "Mats", "Profit", "HQ", "Owned", "Clear" })
        {
            ImGui.TableNextColumn();
            Theme.Faint(label);
        }

        foreach (var idea in plan)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            Theme.Colored(Theme.Text, this.plugin.Catalog.Name(idea.ItemId));

            if (idea.UsesIntermediates)
            {
                ImGui.SameLine();
                Theme.Faint("+sub");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("At least one ingredient has to be crafted first, from the same materials.");
            }

            ImGui.TableNextColumn();
            var job = this.plugin.Catalog.JobAbbreviation(idea.CraftJob);
            Theme.Colored(
                Theme.TextDim,
                job.Length > 0 ? $"{job} {idea.RequiredLevel}" : idea.RequiredLevel.ToString());

            ImGui.TableNextColumn();
            Theme.Dimmed(idea.Crafts.ToString());

            ImGui.TableNextColumn();
            Theme.Dimmed(idea.Produced.ToString());

            ImGui.TableNextColumn();
            Theme.Dimmed(Money.Gil(idea.Revenue));

            ImGui.TableNextColumn();
            if (idea.MaterialsPriced)
            {
                Theme.Dimmed(Money.Gil(idea.MaterialValue));
            }
            else
            {
                Theme.Colored(Theme.Warn, "partial");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Some materials have no price, so the profit is optimistic.");
            }

            ImGui.TableNextColumn();
            Theme.Colored(idea.Profit > 0m ? Theme.Accent : Theme.Danger, Money.Gil(idea.Profit));

            ImGui.TableNextColumn();
            if (idea.HqAvailable)
            {
                Theme.Colored(idea.HqProfit > idea.Profit ? Theme.Accent : Theme.TextDim, Money.Gil(idea.HqProfit));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"HQ revenue {Money.Gil(idea.HqRevenue)} at the HQ minimum.");
            }
            else
            {
                Theme.Faint("-");
            }

            ImGui.TableNextColumn();
            Theme.Dimmed(Money.Gil(idea.OwnedProfit));

            ImGui.TableNextColumn();
            Theme.Dimmed(Money.Days(idea.DaysToSell));
        }

        ImGui.EndTable();
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
            UiKit.EmptyState(
                "Nothing captured yet",
                "Your retainer's listing shows up next to everyone else's when you view that item, and this tab keeps it priced.");
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

        if (ImGui.Button("Rescan prices"))
            this.plugin.StartScan();

        ImGui.SameLine();
        if (ImGui.Button("Copy repricing list"))
            this.CopyRepricingList(reviews);

        ImGui.SameLine();
        if (ImGui.Button("Check & reprice"))
            this.plugin.CheckAllListings();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Visits each retainer, opens every listing, compares it against the live board, and " +
                "reprices the ones that should move. Needs the retainer list open at a bell. " +
                "/slotyield stop cancels.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Undercut all -1"))
            this.plugin.CheckAllListings(true);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Sets every listing to one gil under its current market minimum. No floor, no " +
                "threshold, no strategy - it does exactly what it says.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Forget all"))
        {
            listings.Clear();
            this.plugin.SaveConfig();
            return;
        }

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("List from bags"))
        {
            if (ImGui.Button("Refresh candidates"))
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
        }

        ImGui.Spacing();

        if (!ImGui.BeginTable(
                "##mylistings",
                10,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0f, TableHeight())))
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.2f, 0);
        ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthFixed, 96f, 1);
        ImGui.TableSetupColumn("Q", ImGuiTableColumnFlags.WidthFixed, 24f, 2);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 36f, 3);
        ImGui.TableSetupColumn("Yours", ImGuiTableColumnFlags.WidthFixed, 72f, 4);
        ImGui.TableSetupColumn("Market", ImGuiTableColumnFlags.WidthFixed, 72f, 5);
        ImGui.TableSetupColumn("Delta", ImGuiTableColumnFlags.WidthFixed, 52f, 6);
        ImGui.TableSetupColumn("Clear", ImGuiTableColumnFlags.WidthFixed, 52f, 9);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.5f, 8);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30f, 9);

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        var labels = new[] { "Item", "Retainer", "Q", "Qty", "Yours", "Market", "Delta", "Clear", "Status" };

        for (var column = 0; column < labels.Length; column++)
        {
            ImGui.TableNextColumn();

            var header = labels[column];

            if (this.listingSortColumn == column)
                header += this.listingSortAscending ? "  ^" : "  v";

            ImGui.TableHeader(header);

            if (!ImGui.IsItemClicked())
                continue;

            if (this.listingSortColumn == column)
                this.listingSortAscending = !this.listingSortAscending;
            else
            {
                this.listingSortColumn = column;
                this.listingSortAscending = true;
            }
        }

        ImGui.TableNextColumn();

        CapturedListing? remove = null;

        foreach (var review in reviews)
        {
            var listing = review.Listing;
            var quality = listing.Hq ? Quality.Hq : Quality.Nq;
            var rowQuote = this.plugin.FindListingQuote(listing.ItemId, quality)
                           ?? quotes.GetValueOrDefault((listing.ItemId, quality));

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            UiKit.ItemIcon(UiKit.IconIdForItem(listing.ItemId), listing.Hq, 16f);
            ImGui.SameLine(0f, 6f);
            Theme.Colored(Theme.Text, this.plugin.Catalog.Name(listing.ItemId));

            ImGui.TableNextColumn();
            Theme.Colored(
                Theme.TextDim,
                string.IsNullOrWhiteSpace(listing.RetainerName) ? "?" : listing.RetainerName);

            ImGui.TableNextColumn();
            Theme.Colored(quality == Quality.Hq ? Theme.Hq : Theme.TextDim, quality == Quality.Hq ? "HQ" : "NQ");

            ImGui.TableNextColumn();
            Theme.Dimmed(listing.Quantity.ToString());

            ImGui.TableNextColumn();
            Theme.Colored(Theme.Text, Money.Gil(listing.PricePerUnit));

            ImGui.TableNextColumn();
            Theme.Dimmed(review.MarketMin > 0 ? Money.Gil(review.MarketMin) : "-");

            if (ImGui.IsItemHovered() && rowQuote is not null)
                ImGui.SetTooltip(this.PriceLadder(rowQuote, listing));

            ImGui.TableNextColumn();
            var deltaColor = review.Status == ListingStatus.AboveMarket ? Theme.Danger : Theme.Accent;
            Theme.Colored(
                deltaColor,
                review.Status == ListingStatus.Unpriced
                    ? "-"
                    : review.PriceDeltaPercent.ToString("+0%;-0%;0%"));

            ImGui.TableNextColumn();
            Theme.Dimmed(Money.Days(review.DaysToSell));

            ImGui.TableNextColumn();
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

            ImGui.TableNextColumn();
            ImGui.PushID($"forget{listing.ItemId}{listing.RetainerId}");
            if (ImGui.SmallButton("x"))
                remove = listing;
            ImGui.PopID();
        }

        ImGui.EndTable();

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
        if (this.plugin.ArbitrageRunning)
            Theme.Colored(Theme.Gold, "comparing worlds...");
        else if (ImGui.Button("compare worlds"))
            this.plugin.StartArbitrage();

        ImGui.SameLine();
        if (ImGui.SmallButton("worlds"))
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

            if (ImGui.Button("auto (whole DC)"))
            {
                selected.Clear();
                this.plugin.SaveConfig();
            }

            ImGui.EndPopup();
        }

        ImGui.Spacing();

        if (report is null)
        {
            UiKit.EmptyState("No arbitrage run yet", "Scan your world, then press compare worlds to price the shortlist across your data center.");
            return;
        }

        Theme.Faint($"{report.Worlds.Count} worlds ({string.Join(", ", report.Worlds)}) - {report.ComputedUtc.ToLocalTime():HH:mm}");

        if (report.Warnings.Count > 0)
            Theme.Colored(Theme.Warn, string.Join("; ", report.Warnings));

        if (report.Rows.Count == 0)
        {
            UiKit.EmptyState("No arbitrage found", "Nothing on the shortlist is cheaper on one world than it sells for on another after tax.");
            return;
        }

        if (!ImGui.BeginTable(
                "##arbitrage",
                8,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0f, TableHeight())))
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.2f, 0);
        ImGui.TableSetupColumn("Buy on", ImGuiTableColumnFlags.WidthFixed, 110f, 1);
        ImGui.TableSetupColumn("Buy", ImGuiTableColumnFlags.WidthFixed, 80f, 2);
        ImGui.TableSetupColumn("Sell on", ImGuiTableColumnFlags.WidthFixed, 110f, 3);
        ImGui.TableSetupColumn("Sell", ImGuiTableColumnFlags.WidthFixed, 80f, 4);
        ImGui.TableSetupColumn("Net/unit", ImGuiTableColumnFlags.WidthFixed, 90f, 5);
        ImGui.TableSetupColumn("My world", ImGuiTableColumnFlags.WidthFixed, 80f, 6);
        ImGui.TableSetupColumn("Sales/d", ImGuiTableColumnFlags.WidthFixed, 60f, 7);

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        foreach (var label in new[] { "Item", "Buy on", "Buy", "Sell on", "Sell", "Net/unit", "My world", "Sales/d" })
        {
            ImGui.TableNextColumn();
            Theme.Faint(label);
        }

        foreach (var row in report.Rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            UiKit.ItemIcon(UiKit.IconIdForItem(row.ItemId), row.Quality == Quality.Hq, 16f);
            ImGui.SameLine(0f, 6f);
            Theme.Colored(Theme.Text, this.plugin.Catalog.Name(row.ItemId));
            if (row.Quality == Quality.Hq)
            {
                ImGui.SameLine(0f, 6f);
                Theme.Colored(Theme.Hq, "HQ");
            }

            ImGui.TableNextColumn();
            Theme.Colored(Theme.Accent, row.BuyWorld);

            ImGui.TableNextColumn();
            Theme.Dimmed(Money.Gil(row.BuyPrice));

            ImGui.TableNextColumn();
            Theme.Colored(Theme.Accent, row.SellWorld);

            ImGui.TableNextColumn();
            Theme.Dimmed(Money.Gil(row.SellPrice));

            ImGui.TableNextColumn();
            Theme.Colored(Theme.Profit, "+" + Money.Gil(row.NetPerUnit));

            ImGui.TableNextColumn();
            if (row.HomePrice > 0m)
                Theme.Dimmed(Money.Gil(row.HomePrice));
            else
                Theme.Faint("-");

            ImGui.TableNextColumn();
            Theme.Faint(row.SellVelocity.ToString("0.0"));
        }

        ImGui.EndTable();
    }

    private void DrawScanTab()
    {
        var config = this.Config;
        var state = this.plugin.State;

        if (state.IsScanning)
        {
            ImGui.ProgressBar(state.Fraction, new Vector2(-1f, 14f), $"{state.Phase} {state.Done}/{state.Total} {state.Detail}");
            if (ImGui.Button("Cancel scan"))
                this.plugin.CancelScan();
        }
        else
        {
            if (ImGui.Button("Scan now"))
                this.plugin.StartScan();

            ImGui.SameLine();
            if (ImGui.Button("Clear cache"))
            {
                this.plugin.Cache.Clear();
                this.plugin.State.Result = null;
                this.plugin.State.Diff = null;
                this.plugin.SaveConfig();
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

        var compactRows = this.Config.CompactRows;
        if (ImGui.Checkbox("compact rows", ref compactRows))
        {
            this.Config.CompactRows = compactRows;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        var dtrBar = this.Config.DtrBar;
        if (ImGui.Checkbox("dtr bar", ref dtrBar))
        {
            this.Config.DtrBar = dtrBar;
            this.plugin.SaveConfig();
            this.plugin.RefreshDtr();
        }

        ImGui.SameLine();
        var themeVariant = this.Config.Theme;
        ImGui.SetNextItemWidth(110f);
        if (ImGui.BeginCombo("##theme", themeVariant == ThemeVariant.Graphite ? "graphite" : "parchment"))
        {
            if (ImGui.Selectable("graphite", themeVariant == ThemeVariant.Graphite))
            {
                this.Config.Theme = ThemeVariant.Graphite;
                Theme.Apply(ThemeVariant.Graphite);
                this.plugin.SaveConfig();
            }

            if (ImGui.Selectable("parchment", themeVariant == ThemeVariant.Parchment))
            {
                this.Config.Theme = ThemeVariant.Parchment;
                Theme.Apply(ThemeVariant.Parchment);
                this.plugin.SaveConfig();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var exceptionLog = this.Config.ExceptionLog;
        if (ImGui.Checkbox("exception log", ref exceptionLog))
        {
            this.Config.ExceptionLog = exceptionLog;
            this.plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Diagnostic: log first-chance exceptions to dalamud.log. Applies after a plugin reload.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        var toastThreshold = (int)Math.Clamp(this.Config.ToastThreshold, 0, 200_000);
        if (ImGui.SliderInt("##toast", ref toastThreshold, 0, 200_000, toastThreshold <= 0 ? "no alert" : "alert >= %d/day"))
        {
            this.Config.ToastThreshold = toastThreshold;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        var autoScanHours = Math.Clamp(this.Config.AutoScanHours, 0, 48);
        if (ImGui.SliderInt("##autoscan", ref autoScanHours, 0, 48, autoScanHours <= 0 ? "no auto-scan" : "auto every %dh"))
        {
            this.Config.AutoScanHours = autoScanHours;
            this.plugin.SaveConfig();
        }

        UiKit.SectionHeader("World");
        var worlds = this.plugin.Worlds;
        var world = this.plugin.ResolveWorld();

        ImGui.SetNextItemWidth(240f);
        if (ImGui.BeginCombo("##world", string.IsNullOrEmpty(world) ? "select a world" : world))
        {
            foreach (var name in worlds)
            {
                if (!ImGui.Selectable(name, name == world))
                    continue;

                config.WorldOverride = name;
                this.plugin.SaveConfig();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("use current"))
        {
            config.WorldOverride = this.plugin.CurrentWorldName();
            this.plugin.SaveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();
        UiKit.SectionHeader("Model");

        var strategy = config.Strategy;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("##strategy", StrategyLabel(strategy)))
        {
            foreach (var value in Enum.GetValues<PricingStrategy>())
            {
                if (!ImGui.Selectable(StrategyLabel(value), value == strategy))
                    continue;

                config.Strategy = value;
                this.plugin.SaveConfig();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        Theme.Faint("where you sit in the price ladder");

        var costMode = config.CostMode;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("##costmode", CostLabel(costMode)))
        {
            foreach (var value in Enum.GetValues<CostMode>())
            {
                if (!ImGui.Selectable(CostLabel(value), value == costMode))
                    continue;

                config.CostMode = value;
                this.plugin.SaveConfig();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        Theme.Faint("cost basis: revenue, buy, craft, or the cheaper of the two");

        var tax = config.TaxPercent;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderFloat("tax %", ref tax, 0f, 5f, "%.1f%%"))
            config.TaxPercent = tax;

        this.SaveAfterEdit();

        ImGui.SameLine();
        Theme.Faint("3% outside the city-states, 5% inside");

        var stack = config.DefaultStackSize;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("stack size", ref stack, 1, 99))
            config.DefaultStackSize = stack;

        this.SaveAfterEdit();

        var deadDays = config.DeadAfterDays;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("dead after (days)", ref deadDays, 1, 120))
            config.DeadAfterDays = deadDays;

        this.SaveAfterEdit();

        var minVel = config.MinimumVelocity;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderFloat("min sales/day", ref minVel, 0f, 20f, "%.1f"))
            config.MinimumVelocity = minVel;

        this.SaveAfterEdit();

        var shortlist = config.ShortlistSize;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("shortlist", ref shortlist, 50, 1500))
            config.ShortlistSize = shortlist;

        this.SaveAfterEdit();

        ImGui.SameLine();
        Theme.Faint("items priced exactly after the cheap pass");

        var cacheHours = config.CacheHours;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("cache (hours)", ref cacheHours, 1, 48))
            config.CacheHours = cacheHours;

        this.SaveAfterEdit();

        ImGui.Spacing();
        ImGui.Separator();
        UiKit.SectionHeader("Alerts");

        var alertsOn = config.AlertsEnabled;
        if (ImGui.Checkbox("chat alerts for undercuts and price drops", ref alertsOn))
        {
            config.AlertsEnabled = alertsOn;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        var toasts = config.AlertToasts;
        if (ImGui.Checkbox("also as a toast", ref toasts))
        {
            config.AlertToasts = toasts;
            this.plugin.SaveConfig();
        }

        var alertMinutes = config.AlertIntervalMinutes;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("poll every (min)", ref alertMinutes, 1, 120))
            config.AlertIntervalMinutes = alertMinutes;

        this.SaveAfterEdit();

        ImGui.SameLine();
        Theme.Faint("one quote request per twenty items watched");

        var undercut = config.AlertUndercutPercent;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("undercut by %", ref undercut, 0, 50))
            config.AlertUndercutPercent = undercut;

        this.SaveAfterEdit();

        var drop = config.AlertPriceDropPercent;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("price drop %", ref drop, 1, 90))
            config.AlertPriceDropPercent = drop;

        this.SaveAfterEdit();

        var cooldownMinutes = config.AlertCooldownMinutes;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("repeat after (min)", ref cooldownMinutes, 5, 1440))
            config.AlertCooldownMinutes = cooldownMinutes;

        this.SaveAfterEdit();

        ImGui.SameLine();
        Theme.Faint("per item, so one collapsing market does not spam chat");

        var floorGil = (int)Math.Clamp(config.AlertMinProfitPerDay, 0, 2_000_000);
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("pinned profit floor (gil/day)", ref floorGil, 0, 2_000_000))
            config.AlertMinProfitPerDay = floorGil;

        this.SaveAfterEdit();

        ImGui.SameLine();
        Theme.Faint("after a scan, pinned items that climb above this say so");

        if (ImGui.Button("Check now"))
            this.plugin.RunAlertPoll(true);

        ImGui.SameLine();
        Theme.Faint("/slotyield alerts does the same");

        ImGui.Spacing();
        ImGui.Separator();
        UiKit.SectionHeader("Repricing");

        var minMove = config.RepriceMinMoveGil;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("min move (gil)", ref minMove, 0, 5000))
            config.RepriceMinMoveGil = minMove;

        this.SaveAfterEdit();

        ImGui.SameLine();
        var floorPercent = config.RepriceFloorPercent;
        ImGui.SetNextItemWidth(180f);

        if (ImGui.SliderInt("floor %", ref floorPercent, 0, 100))
            config.RepriceFloorPercent = floorPercent;

        this.SaveAfterEdit();

        ImGui.SameLine();
        Theme.Faint("drops below this share of the current price are refused; 0 disables the floor");

        var autoPrice = config.AutoPriceSellDialog;
        if (ImGui.Checkbox("auto-fill the asking price when listing", ref autoPrice))
        {
            config.AutoPriceSellDialog = autoPrice;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        Theme.Faint("/slotyield price does it on demand");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextWrapped(
            "Model: a slot earns (units you capture per day) x (net price). You capture 1/(sellers at your " +
            "price + 1) of the market's sales, because buyers work down the cheapest listings first. A deal " +
            "takes 99 units to clear, so slow items are flagged as dead slots rather than quietly tying up gil.");

        ImGui.Spacing();
        Theme.Faint(
            "Data: Universalis. This plugin only reads - it never buys, lists, or moves your character.");
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

    private List<string> CategoryChoices(IReadOnlyList<SlotCandidate> candidates)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var name = this.CategoryName(candidate.ItemId);
            if (name.Length > 0)
                set.Add(name);
        }

        return set.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
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
            _ => (a, b) => a.Score.ProfitPerDay.CompareTo(b.Score.ProfitPerDay),
        };

        rows.Sort(comparison);

        // The comparators above are ascending, so a descending request has to reverse them.
        // This was inverted: "v" (descending) used to show the ascending list.
        if (!this.sortAscending)
            rows.Reverse();
    }

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

        UiKit.SectionHeader(title, color);

        foreach (var delta in deltas)
        {
            var name = Truncate(this.plugin.Catalog.Name(delta.ItemId), 36);
            var quality = delta.Quality == Quality.Hq ? "HQ" : "NQ";
            var percent = delta.PriceChangePercent.ToString("+0.0%;-0.0%;0%");
            var profit = delta.ProfitChange >= 0m
                ? "+" + Money.Gil(delta.ProfitChange)
                : "-" + Money.Gil(-delta.ProfitChange);

            Theme.Colored(
                color,
                $"  {name,-36} {quality}  {Money.Gil(delta.Previous.Price),8} -> {Money.Gil(delta.Current.Price),8}   {percent,-7} profit {profit}");
        }
    }

    private void DrawEntrySection(string title, IReadOnlyList<SnapshotEntry> entries, Vector4 color)
    {
        if (entries.Count == 0)
            return;

        UiKit.SectionHeader(title, color);

        foreach (var entry in entries)
        {
            var name = Truncate(this.plugin.Catalog.Name(entry.ItemId), 36);
            var quality = entry.Quality == Quality.Hq ? "HQ" : "NQ";
            Theme.Colored(color, $"  {name,-36} {quality}  {Money.Gil(entry.Price),8}   {Money.Gil(entry.ProfitPerDay),9}/day");
        }
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
