using System;
using System.Numerics;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SlotYield.Core;

namespace SlotYield.UI;

/// <summary>A larger price-history chart with axes, for one item at a time.</summary>
public sealed class HistoryWindow : Window
{
    private readonly Plugin plugin;
    private uint itemId;
    private Quality quality = Quality.Nq;

    public HistoryWindow(Plugin plugin)
        : base("Slot Yield - history###SlotYieldHistory")
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

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail();
        var width = available.X;
        var height = MathF.Max(90f, available.Y - 26f);
        var chartMin = origin;
        var chartMax = origin + new Vector2(width, height);

        var min = decimal.MaxValue;
        var max = decimal.MinValue;
        foreach (var value in series)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        if (max <= min)
            max = min + 1m;

        drawList.AddRectFilled(chartMin, chartMax, Theme.WithAlpha(Theme.PanelRaised, 0.35f));

        for (var grid = 0; grid <= 3; grid++)
        {
            var y = chartMin.Y + (height * grid / 3f);
            drawList.AddLine(new Vector2(chartMin.X, y), new Vector2(chartMax.X, y), Theme.WithAlpha(Theme.Text, 0.06f));
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(chartMin.X + 4f, y + 1f), Theme.U32(Theme.TextFaint), Money.Gil(max - ((max - min) * grid / 3m)));
        }

        var previous = Vector2.Zero;
        for (var index = 0; index < series.Length; index++)
        {
            var x = chartMin.X + (width * index / (series.Length - 1));
            var normalized = (float)((series[index] - min) / (max - min));
            var point = new Vector2(x, chartMax.Y - (height * normalized));
            if (index > 0)
                drawList.AddLine(previous, point, Theme.U32(Theme.Accent), 1.6f);
            previous = point;
        }

        ImGui.Dummy(new Vector2(width, height + 4f));
        Theme.Faint($"{series.Length} days   low {Money.Gil(min)}   high {Money.Gil(max)}   now {Money.Gil(series[series.Length - 1])}");
    }
}
