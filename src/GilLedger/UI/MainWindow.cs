using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GilLedger.Core;

namespace GilLedger.UI;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;

    private int showCount = 300;

    public MainWindow(Plugin plugin)
        : base("Gil Ledger###GilLedgerMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(720, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 300),
            MaximumSize = new Vector2(1400, 1400),
        };
    }

    private Configuration Config => this.plugin.Configuration;

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
        if (!this.Config.Enabled)
        {
            ImGui.TextWrapped("Gil Ledger is disabled. Turn it back on in the settings.");
            if (ImGui.Button("Open settings"))
                this.plugin.OpenConfig();

            return;
        }

        this.DrawHeader();

        if (!ImGui.BeginTabBar("##gilledger-tabs"))
            return;

        if (ImGui.BeginTabItem("Ledger"))
        {
            this.DrawLedgerTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Summary"))
        {
            this.DrawSummaryTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawHeader()
    {
        var entries = this.Config.Entries;

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Gold);
        ImGui.TextUnformatted("G I L   L E D G E R");
        ImGui.PopStyleColor();

        var balance = this.plugin.Ledger.LastGil;
        ImGui.TextUnformatted($"Pocket balance: {Money.Exact(balance)} gil");

        var net = Pnl.Net(entries);
        ColoredText(net >= 0 ? Theme.Green : Theme.Danger, $"Net this session: {Money.Signed(net)} gil");

        var today = Pnl.ByDay(entries, 1).FirstOrDefault();
        if (today is not null)
            ColoredText(today.Net >= 0 ? Theme.Green : Theme.Danger, $"Today: {Money.Signed(today.Net)} gil");

        ImGui.Spacing();
        ImGui.Separator();
    }

    private void DrawLedgerTab()
    {
        var entries = this.Config.Entries;

        ImGui.SetNextItemWidth(160f);
        ImGui.SliderInt("rows", ref this.showCount, 25, 1000, "%d");
        ImGui.SameLine();

        if (ImGui.SmallButton("copy CSV"))
        {
            ImGui.SetClipboardText(this.BuildCsv(entries));
            this.plugin.Print("Copied the ledger as CSV.");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("clear"))
        {
            entries.Clear();
            this.plugin.SaveConfig();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("settings"))
            this.plugin.OpenConfig();

        if (entries.Count == 0)
        {
            ImGui.Spacing();
            ColoredText(Theme.TextFaint, "Nothing recorded yet. Buy or sell something and it shows up here.");
            return;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings;

        if (!ImGui.BeginTable("##entries", 5, flags, new Vector2(0f, -1f)))
            return;

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 96f, 0);
        ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthFixed, 92f, 1);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 92f, 2);
        ImGui.TableSetupColumn("Balance", ImGuiTableColumnFlags.WidthFixed, 92f, 3);
        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 2f, 4);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var shown = entries.Take(this.showCount).ToList();
        foreach (var entry in shown)
        {
            ImGui.TableNextColumn();
            ColoredText(Theme.TextFaint, entry.Utc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(KindLabel(entry.Kind));

            ImGui.TableNextColumn();
            ColoredText(entry.Amount >= 0 ? Theme.Green : Theme.Danger, Money.Signed(entry.Amount));

            ImGui.TableNextColumn();
            ColoredText(Theme.TextDim, Money.Exact(entry.BalanceAfter));

            ImGui.TableNextColumn();
            ColoredText(Theme.TextDim, Truncate(entry.Note, 90));
            if (ImGui.IsItemHovered() && entry.Note.Length > 90)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(entry.Note);
                ImGui.EndTooltip();
            }
        }

        ImGui.EndTable();
    }

    private void DrawSummaryTab()
    {
        var entries = this.Config.Entries;
        if (entries.Count == 0)
        {
            ColoredText(Theme.TextFaint, "Nothing to summarise yet.");
            return;
        }

        ColoredText(Theme.Text, "By category (this session)");
        var kinds = Pnl.ByKind(entries);
        foreach (var row in kinds)
            ColoredText(
                row.Total >= 0 ? Theme.Green : Theme.Danger,
                $"{KindLabel(row.Kind),-12} {Money.Signed(row.Total)} gil");

        ImGui.Spacing();
        ImGui.Separator();

        ColoredText(Theme.Text, "By day");
        var days = Pnl.ByDay(entries, 14);

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.NoSavedSettings;

        if (!ImGui.BeginTable("##days", 4, flags))
            return;

        ImGui.TableSetupColumn("Day", ImGuiTableColumnFlags.WidthStretch, 2f, 0);
        ImGui.TableSetupColumn("Income", ImGuiTableColumnFlags.WidthFixed, 110f, 1);
        ImGui.TableSetupColumn("Expense", ImGuiTableColumnFlags.WidthFixed, 110f, 2);
        ImGui.TableSetupColumn("Net", ImGuiTableColumnFlags.WidthFixed, 110f, 3);
        ImGui.TableHeadersRow();

        foreach (var row in days)
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            ColoredText(Theme.Green, Money.Exact(row.Income));

            ImGui.TableNextColumn();
            ColoredText(Theme.Danger, Money.Exact(row.Expense));

            ImGui.TableNextColumn();
            ColoredText(row.Net >= 0 ? Theme.Green : Theme.Danger, Money.Signed(row.Net));
        }

        ImGui.EndTable();
    }

    private string BuildCsv(IEnumerable<GilEntry> entries)
    {
        var header = new[] { "time", "kind", "amount", "balance", "note" };
        var rows = entries.Select(e => new[]
        {
            e.Utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            e.Kind.ToString(),
            e.Amount.ToString(CultureInfo.InvariantCulture),
            e.BalanceAfter.ToString(CultureInfo.InvariantCulture),
            e.Note,
        });

        var builder = new System.Text.StringBuilder();
        builder.Append(string.Join(',', header)).Append('\n');
        foreach (var row in rows)
            builder.Append(string.Join(',', row.Select(CsvEscape))).Append('\n');

        return builder.ToString();
    }

    private static string CsvEscape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string KindLabel(GilKind kind) => kind switch
    {
        GilKind.Sale => "Sale",
        GilKind.Purchase => "Purchase",
        GilKind.ListingTax => "Listing tax",
        GilKind.Repair => "Repair",
        GilKind.Venture => "Venture",
        GilKind.VendorSell => "Vendor sell",
        GilKind.Retainer => "Retainer",
        _ => "Unknown",
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "...";

    private static void ColoredText(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
