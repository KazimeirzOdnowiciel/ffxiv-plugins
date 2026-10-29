using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SlotYield.Core;
using SlotYield.Market;

namespace SlotYield.UI;

/// <summary>Side-by-side stats for the rows currently multi-selected.</summary>
public sealed class CompareWindow : GlassWindow
{
    private readonly Plugin plugin;
    private readonly MainWindow main;

    public CompareWindow(Plugin plugin, MainWindow main)
        : base(plugin, "Slot Yield - compare###SlotYieldCompare")
    {
        this.plugin = plugin;
        this.main = main;
        this.Size = new Vector2(580f, 320f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
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

        var selection = this.main.MultiSelection
            .Select(key => result.Candidates.FirstOrDefault(c => c.ItemId == key.ItemId && c.Quality == key.Quality))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .Take(3)
            .ToList();

        if (selection.Count == 0)
        {
            Theme.Faint("Ctrl-click rows in Opportunities to compare them here.");
            return;
        }

        if (!ImGui.BeginTable("##compare", selection.Count + 1, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 110f);
        foreach (var candidate in selection)
            ImGui.TableSetupColumn(this.plugin.Catalog.Name(candidate.ItemId), ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableNextColumn();
        foreach (var candidate in selection)
        {
            ImGui.TableNextColumn();
            Theme.Colored(Theme.Text, this.plugin.Catalog.Name(candidate.ItemId) + (candidate.Quality == Quality.Hq ? " (HQ)" : string.Empty));
        }

        Row(selection, "price", candidate => Money.Gil(candidate.Score.ListPrice));
        Row(selection, "revenue/day", candidate => Money.Gil(candidate.Score.GilPerDay));
        Row(selection, "profit/day", candidate => Money.Gil(candidate.Score.ProfitPerDay));
        Row(selection, "sales/day", candidate => candidate.Quote.DailyVelocity.ToString("0.0"));
        Row(selection, "sellers", candidate => candidate.Quote.DistinctSellers.ToString());
        Row(selection, "share", candidate => candidate.Score.ExpectedShare.ToString("P0"));
        Row(selection, "capital", candidate => Money.Gil(candidate.Score.CapitalPerSlot));
        Row(selection, "clears", candidate => Money.Days(candidate.Score.DaysToClear));
        Row(selection, "trend", candidate => candidate.Score.TrendPercent.ToString("+0.0%;-0.0%;0%"));
        Row(selection, "verdict", candidate => candidate.Score.Verdict);

        ImGui.EndTable();
    }

    private static void Row(System.Collections.Generic.IReadOnlyList<SlotCandidate> selection, string label, Func<SlotCandidate, string> value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        Theme.Faint(label);

        foreach (var candidate in selection)
        {
            ImGui.TableNextColumn();
            Theme.Dimmed(value(candidate));
        }
    }
}
