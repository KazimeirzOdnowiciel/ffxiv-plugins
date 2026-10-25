using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DesynthLens.UI;

public sealed class MainWindow : Window
{
    private static readonly Vector4 Accent = new(0.36f, 0.85f, 0.67f, 1f);
    private static readonly Vector4 Danger = new(0.90f, 0.42f, 0.40f, 1f);
    private static readonly Vector4 TextDim = new(0.65f, 0.67f, 0.72f, 1f);
    private static readonly Vector4 Faint = new(0.50f, 0.52f, 0.58f, 1f);

    private readonly Plugin plugin;
    private int sortColumn = 3;
    private bool sortAscending;

    public MainWindow(Plugin plugin)
        : base("Desynth Lens###DesynthLensMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(880f, 480f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620f, 300f),
            MaximumSize = new Vector2(2000f, 1400f),
        };
    }

    public override void OnOpen()
    {
        this.plugin.Configuration.WindowOpen = true;
        this.plugin.Configuration.Save();
    }

    public override void OnClose()
    {
        this.plugin.Configuration.WindowOpen = false;
        this.plugin.Configuration.Save();
    }

    public override void Draw()
    {
        if (ImGui.Button("Capture last result"))
            this.plugin.Capture();

        ImGui.SameLine();

        if (this.plugin.IsCapturing)
            ImGui.TextColored(TextDim, this.plugin.Status);
        else
            ImGui.TextColored(TextDim, this.plugin.Status);

        if (this.plugin.CapturedAt is { } at)
        {
            ImGui.SameLine();
            ImGui.TextColored(Faint, $"last captured {at.ToLocalTime():HH:mm}");
        }

        ImGui.TextColored(Faint, "Destiny the item, then this records what you got and what it is worth.");
        ImGui.TextColored(Faint, "Results come from the game's own desynthesis window - no guessed tables.");

        ImGui.Separator();

        var rows = this.plugin.Records.ToList();

        if (rows.Count == 0)
        {
            ImGui.TextColored(Faint, "Nothing recorded yet.");
            return;
        }

        rows = Sort(rows, this.sortColumn, this.sortAscending);

        var totalTimes = rows.Sum(row => row.Times);
        var totalValue = rows.Sum(row => row.TotalValue);
        var totalProfit = rows.Sum(row => row.TotalProfit);
        var priced = rows.Count(row => row.BuyPrice > 0);

        ImGui.TextColored(
            TextDim,
            $"{rows.Count} item(s), {totalTimes} desynth(s), {Money(totalValue)} gil of yield" +
            (priced > 0 ? $", {Money(totalProfit)} profit on {priced} priced item(s)" : string.Empty));

        if (ImGui.SmallButton("Clear"))
        {
            this.plugin.Configuration.Records.Clear();
            this.plugin.Configuration.Save();
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy ledger"))
            ImGui.SetClipboardText(string.Join(Environment.NewLine, rows.Select(row =>
                $"{row.Name}: {row.Times}x, avg {Average(row):N0}, best {row.BestValue:N0}, last {row.LastYield}")));

        if (!ImGui.BeginTable("##ledger", 7,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0f, -1f)))
            return;

        var labels = new[] { "Item", "Times", "Avg yield", "Buy", "Avg profit", "Last yield", "When" };

        ImGui.TableSetupColumn(labels[0], ImGuiTableColumnFlags.WidthStretch, 2.4f, 0);
        ImGui.TableSetupColumn(labels[1], ImGuiTableColumnFlags.WidthFixed, 48f, 1);
        ImGui.TableSetupColumn(labels[2], ImGuiTableColumnFlags.WidthFixed, 78f, 2);
        ImGui.TableSetupColumn(labels[3], ImGuiTableColumnFlags.WidthFixed, 78f, 3);
        ImGui.TableSetupColumn(labels[4], ImGuiTableColumnFlags.WidthFixed, 78f, 4);
        ImGui.TableSetupColumn(labels[5], ImGuiTableColumnFlags.WidthStretch, 2.8f, 5);
        ImGui.TableSetupColumn(labels[6], ImGuiTableColumnFlags.WidthFixed, 64f, 6);

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        for (var i = 0; i < labels.Length; i++)
        {
            ImGui.TableNextColumn();

            var label = labels[i] + (this.sortColumn == i ? (this.sortAscending ? " ^" : " v") : string.Empty);
            ImGui.TableHeader(label);

            if (!ImGui.IsItemClicked())
                continue;

            if (this.sortColumn == i)
                this.sortAscending = !this.sortAscending;
            else
            {
                this.sortColumn = i;
                this.sortAscending = i switch
                {
                    0 => true,
                    1 => false,
                    _ => false,
                };
            }
        }

        foreach (var row in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Name);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"{row.Name} desynthesised {row.Times}x\n" +
                    $"average yield {Average(row):N0}, best {row.BestValue:N0}\n" +
                    (row.Untradeable
                        ? "untradeable - no market price\n"
                        : $"bought for {row.BuyPrice:N0}, average profit {AverageProfit(row):N0}\n") +
                    $"last: {row.LastYield}");
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(TextDim, row.Times.ToString());

            ImGui.TableNextColumn();
            ImGui.TextColored(TextDim, Money(Average(row)));

            ImGui.TableNextColumn();
            if (row.Untradeable)
                ImGui.TextColored(Faint, "untradeable");
            else
                ImGui.TextColored(TextDim, row.BuyPrice > 0 ? Money(row.BuyPrice) : "-");

            ImGui.TableNextColumn();
            ImGui.TextColored(AverageProfit(row) >= 0 ? Accent : Danger, Money(AverageProfit(row)));

            ImGui.TableNextColumn();
            ImGui.TextColored(TextDim, row.LastYield);

            ImGui.TableNextColumn();
            ImGui.TextColored(Faint, row.LastUtc.ToLocalTime().ToString("MMM d HH:mm"));
        }

        ImGui.EndTable();
    }

    private static decimal AverageProfit(DesynthRecord record) =>
        record.Times <= 0 ? 0m : record.TotalProfit / record.Times;

    private static decimal Average(DesynthRecord record) =>
        record.Times <= 0 ? 0m : record.TotalValue / record.Times;

    private static List<DesynthRecord> Sort(List<DesynthRecord> rows, int column, bool ascending)
    {
        Func<DesynthRecord, IComparable> key = column switch
        {
            1 => row => row.Times,
            2 => row => Average(row),
            3 => row => row.BuyPrice,
            4 => row => AverageProfit(row),
            5 => row => row.LastYield,
            6 => row => row.LastUtc,
            _ => row => row.Name,
        };

        var comparer = Comparer<IComparable>.Default;
        return ascending
            ? rows.OrderBy(key, comparer).ToList()
            : rows.OrderByDescending(key, comparer).ToList();
    }

    private static string Money(decimal value)
    {
        if (value >= 1_000_000m)
            return (value / 1_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "M";

        if (value >= 10_000m)
            return (value / 1_000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";

        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
