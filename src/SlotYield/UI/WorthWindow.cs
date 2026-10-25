using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SlotYield.Core;

namespace SlotYield.UI;

/// <summary>
/// Compact, leave-on-screen valuation of everything you own. Deliberately small: total, trend, top
/// categories, and how many stacks could not be priced.
/// </summary>
public sealed class WorthWindow : Window
{
    private readonly Plugin plugin;

    public WorthWindow(Plugin plugin)
        : base("Net worth###SlotYieldWorth")
    {
        this.plugin = plugin;
        this.Size = new Vector2(320f, 260f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260f, 190f),
            MaximumSize = new Vector2(560f, 720f),
        };
    }

    public override void OnOpen()
    {
        this.plugin.Configuration.WorthOpen = true;
        this.plugin.SaveConfig();
    }

    public override void OnClose()
    {
        this.plugin.Configuration.WorthOpen = false;
        this.plugin.SaveConfig();
    }

    private string? expandedCategory;

    public override void Draw()
    {
        var snapshot = this.plugin.WorthSnapshot;

        if (snapshot is null)
        {
            Theme.Dimmed(this.plugin.WorthRefreshing ? "measuring your holdings..." : "not measured yet.");

            if (ImGui.Button("Measure now"))
                this.plugin.RefreshWorth();

            return;
        }

        ImGui.TextUnformatted(Money.Gil(snapshot.Total));

        var history = this.plugin.Configuration.WorthHistory;
        var change = NetWorth.ChangeSince(history.Take(Math.Max(0, history.Count - 1)).ToList(), snapshot.Total);
        var week = ChangeOver(history, snapshot.Total, 7);

        ImGui.SameLine();

        if (Math.Abs(change) > 0.0001)
        {
            Theme.Colored(change >= 0 ? Theme.Accent : Theme.Danger, $"{change:+0.0%;-0.0%}");
            ImGui.SameLine();
            Theme.Faint("since last point");
        }
        else
        {
            Theme.Faint("total");
        }

        if (Math.Abs(week) > 0.0001)
        {
            Theme.Colored(
                week >= 0 ? Theme.Accent : Theme.Danger,
                $"{week:+0.0%;-0.0%} 7d");
        }

        this.DrawSparkline(history.Select(point => point.Total).ToList());

        var compact = this.plugin.Configuration.WorthCompact;

        if (!compact)
        {
            ImGui.Spacing();

            foreach (var slice in snapshot.Slices.Take(6))
            {
                var open = this.expandedCategory == slice.Category;

                if (ImGui.SmallButton($"{(open ? "-" : "+")}##{slice.Category}"))
                    this.expandedCategory = open ? null : slice.Category;

                ImGui.SameLine();
                Theme.Colored(Theme.Text, slice.Category);
                ImGui.SameLine();
                Theme.Dimmed(Money.Gil(slice.Value));

                if (!open)
                    continue;

                foreach (var item in snapshot.Items.Where(entry => entry.Category == slice.Category).Take(6))
                {
                    Theme.Faint($"    {this.plugin.Catalog.Name(item.ItemId)} x{item.Quantity}  {Money.Gil(item.Value)}");
                }
            }

            if (snapshot.Slices.Count > 6)
                Theme.Faint($"+ {snapshot.Slices.Count - 6} more categories");

            ImGui.Spacing();
            Theme.Faint($"gil {Money.Gil(snapshot.Gil)}   {snapshot.Stacks} stacks");

            if (snapshot.UnpricedItems > 0)
            {
                Theme.Colored(Theme.Warn, $"{snapshot.UnpricedItems} stack(s) had no market price");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Those stacks are counted in 'stacks' but contribute nothing to the total.");
            }
        }

        ImGui.Spacing();

        if (ImGui.Button("Refresh"))
            this.plugin.RefreshWorth();

        ImGui.SameLine();

        var mode = this.plugin.Configuration.WorthMode;
        ImGui.SetNextItemWidth(150f);
        if (ImGui.BeginCombo("##worthmode", mode == WorthValuation.MinListing ? "min listing" : "average sale"))
        {
            foreach (var value in Enum.GetValues<WorthValuation>())
            {
                if (!ImGui.Selectable(value == WorthValuation.MinListing ? "min listing" : "average sale", value == mode))
                    continue;

                this.plugin.Configuration.WorthMode = value;
                this.plugin.SaveConfig();
                this.plugin.RefreshWorth();
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Minimum listing = liquidation value today; average sale = what it has been selling for.");

        var compactToggle = this.plugin.Configuration.WorthCompact;
        if (ImGui.Checkbox("compact", ref compactToggle))
        {
            this.plugin.Configuration.WorthCompact = compactToggle;
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        Theme.Faint(this.plugin.WorthRefreshing
            ? "refreshing..."
            : $"updated {this.plugin.WorthUpdatedAt.ToLocalTime():HH:mm}");
    }

    /// <summary>Change against the newest history point at least this many days old.</summary>
    private static double ChangeOver(IReadOnlyList<WorthPoint> history, decimal current, int days)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        var point = history
            .Where(entry => entry.Day <= cutoff)
            .OrderByDescending(entry => entry.Day)
            .FirstOrDefault();

        if (point is null || point.Total <= 0)
            return 0;

        return (double)((current - point.Total) / point.Total);
    }

    private void DrawSparkline(List<decimal> values)
    {
        if (values.Count < 2)
        {
            Theme.Faint("no history yet - one point per day");
            return;
        }

        var height = 42f;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        if (range <= 0)
            range = 1m;

        var points = new Vector2[values.Count];

        for (var i = 0; i < values.Count; i++)
        {
            var x = origin.X + (width * i / Math.Max(1, values.Count - 1));
            var normalized = (float)((values[i] - min) / range);
            var y = origin.Y + height - (normalized * height);
            points[i] = new Vector2(x, y);
        }

        var color = Theme.WithAlpha(Theme.Accent, 0.9f);

        for (var i = 1; i < points.Length; i++)
            drawList.AddLine(points[i - 1], points[i], color, 1.5f);
        ImGui.Dummy(new Vector2(width, height + 2f));
    }
}
