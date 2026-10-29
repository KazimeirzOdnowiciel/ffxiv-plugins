using System;
using System.Numerics;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Glasswork.Backend;
using Glasswork.Charts;
using SlotYield.Core;

namespace SlotYield.UI;

/// <summary>A larger price-history chart with axes, for one item at a time.</summary>
public sealed class HistoryWindow : GlassWindow
{
    private readonly Plugin plugin;
    private uint itemId;
    private Quality quality = Quality.Nq;

    /// <summary>Chart inputs are rebuilt only when the item or the sample count changes.</summary>
    private uint chartItem;
    private float[] chartValues = Array.Empty<float>();
    private ChartSeries[] chartSeries = Array.Empty<ChartSeries>();

    public HistoryWindow(Plugin plugin)
        : base(plugin, "Slot Yield - history###SlotYieldHistory")
    {
        this.plugin = plugin;
        this.Size = new Vector2(440f, 320f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Show(uint newItemId, Quality newQuality)
    {
        this.itemId = newItemId;
        this.quality = newQuality;
        this.IsOpen = true;
    }

    public override void Draw()
    {
        try
        {
            this.DrawBody();
            this.ReportDrawRecovered();
        }
        catch (Exception ex)
        {
            this.ReportDrawFailure(ex);
        }
    }

    private void DrawBody()
    {
        this.DrawGlass();

        var result = this.plugin.State.Result;
        if (result is null)
        {
            Theme.Faint("No scan yet.");
            return;
        }

        var choices = result.Candidates
            .Where(c => c.Quote.PriceHistory.Length >= 2)
            .OrderByDescending(c => c.Score.ProfitPerDay)
            .Take(60)
            .ToList();

        if (choices.Count == 0)
        {
            Theme.Faint("No price history in the current scan.");
            return;
        }

        var current = choices.FirstOrDefault(c => c.ItemId == this.itemId && c.Quality == this.quality) ?? choices[0];
        this.itemId = current.ItemId;
        this.quality = current.Quality;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##historyitem", this.plugin.Catalog.Name(current.ItemId)))
        {
            foreach (var choice in choices)
            {
                var label = this.plugin.Catalog.Name(choice.ItemId) + (choice.Quality == Quality.Hq ? " (HQ)" : string.Empty);
                if (ImGui.Selectable(label, choice == current))
                {
                    this.itemId = choice.ItemId;
                    this.quality = choice.Quality;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        var series = current.Quote.PriceHistory;

        if (this.chartItem != current.ItemId || this.chartValues.Length != series.Length)
        {
            this.chartItem = current.ItemId;
            this.chartValues = new float[series.Length];
            for (var index = 0; index < series.Length; index++)
                this.chartValues[index] = (float)series[index];

            this.chartSeries = new[] { new ChartSeries(this.chartValues, "price", Theme.ToColor(Theme.Accent)) };
        }

        var available = ImGui.GetContentRegionAvail();
        var style = new ChartStyle
        {
            Axes = true,
            Grid = true,
            Hover = true,
            Height = MathF.Max(90f, available.Y - 26f),
        };

        Gw.Chart("##history"u8, ChartKind.Line, this.chartSeries, style, available.X);

        var min = decimal.MaxValue;
        var max = decimal.MinValue;
        foreach (var value in series)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        Theme.Faint($"{series.Length} days   low {Money.Gil(min)}   high {Money.Gil(max)}   now {Money.Gil(series[series.Length - 1])}");
    }
}
