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

    // ------------------------------------------------------------------ header ----

    private void DrawHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var balance = this.plugin.Ledger.LastGil;

        var p = ImGui.GetCursorScreenPos();
        const float coinRadius = 14f;
        DrawCoin(drawList, p + new Vector2(coinRadius, coinRadius + 2f), coinRadius);

        var numPos = p + new Vector2((coinRadius * 2f) + 12f, 1f);
        drawList.AddText(font, 22f, numPos, Theme.U32(Theme.Gold), Money.Exact(balance));
        ImGui.Dummy(new Vector2(0f, 32f));

        var entries = this.Config.Entries;
        var net = Pnl.Net(entries);
        ColoredText(Theme.TextFaint, "pocket balance");
        ImGui.SameLine(0f, 16f);
        ColoredText(net >= 0 ? Theme.Green : Theme.Danger, $"session {Money.Signed(net)}");

        var todayNet = Pnl.TodayNet(entries);
        if (todayNet != 0)
        {
            ImGui.SameLine(0f, 16f);
            ColoredText(todayNet >= 0 ? Theme.Green : Theme.Danger, $"today {Money.Signed(todayNet)}");
        }

        DrawGoldRule();
    }

    // ------------------------------------------------------------------ ledger ----

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

        var limit = Math.Min(entries.Count, this.showCount);
        for (var index = 0; index < limit; index++)
        {
            var entry = entries[index];
            ImGui.TableNextColumn();
            ColoredText(Theme.TextFaint, entry.Utc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            DrawChip(KindLabel(entry.Kind), Theme.KindColor(entry.Kind));

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

    // ----------------------------------------------------------------- summary ----

    private void DrawSummaryTab()
    {
        var entries = this.Config.Entries;
        if (entries.Count == 0)
        {
            ColoredText(Theme.TextFaint, "Nothing to summarise yet.");
            return;
        }

        var recentCount = Math.Min(entries.Count, 90);
        var recent = new List<long>(recentCount);
        for (var index = recentCount - 1; index >= 0; index--)
            recent.Add(entries[index].BalanceAfter);

        if (recent.Count >= 2)
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(ImGui.GetContentRegionAvail().X, 40f);
            DrawSparkline(drawList, pos, size, recent, Theme.WithAlpha(Theme.Gold, 0.85f));
            ImGui.Dummy(size);
            ColoredText(Theme.TextFaint, "pocket balance, last 90 entries");
            ImGui.Spacing();
        }

        ColoredText(Theme.Text, "By category (this session)");
        var kinds = Pnl.ByKind(entries);
        foreach (var row in kinds)
            ColoredText(
                row.Total >= 0 ? Theme.Green : Theme.Danger,
                $"{KindLabel(row.Kind),-12} {Money.Signed(row.Total)} gil");

        ImGui.Spacing();
        DrawGoldRule();

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

    // ---------------------------------------------------------------- drawing ----

    private static void DrawCoin(ImDrawListPtr drawList, Vector2 center, float radius)
    {
        var outer = new Vector2[6];
        var inner = new Vector2[6];

        for (var i = 0; i < 6; i++)
        {
            var angle = (float)(((60.0 * i) - 30.0) * Math.PI / 180.0);
            outer[i] = new Vector2(
                center.X + (radius * (float)Math.Cos(angle)),
                center.Y + (radius * (float)Math.Sin(angle)));

            var ir = radius * 0.58f;
            inner[i] = new Vector2(
                center.X + (ir * (float)Math.Cos(angle)),
                center.Y + (ir * (float)Math.Sin(angle)));
        }

        drawList.AddConvexPolyFilled(ref outer[0], 6, Theme.U32(Theme.CoinGold));
        drawList.AddConvexPolyFilled(ref inner[0], 6, Theme.U32(Theme.CoinDark));
    }

    private static void DrawChip(string label, Vector4 color)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var textSize = ImGui.CalcTextSize(label);
        const float padX = 7f;
        const float padY = 2.5f;
        var size = new Vector2(textSize.X + (padX * 2f), textSize.Y + (padY * 2f));

        drawList.AddRectFilled(pos, pos + size, Theme.WithAlpha(color, 0.15f), size.Y / 2f);
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + new Vector2(padX, padY), Theme.U32(color), label);
        ImGui.Dummy(size);
    }

    private static void DrawSparkline(ImDrawListPtr drawList, Vector2 pos, Vector2 size, IReadOnlyList<long> series, uint color)
    {
        var middle = pos.Y + (size.Y / 2f);

        if (series.Count < 2)
        {
            drawList.AddLine(new Vector2(pos.X, middle), new Vector2(pos.X + size.X, middle), color, 1.2f);
            return;
        }

        long min = long.MaxValue;
        long max = long.MinValue;
        foreach (var value in series)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        if (max <= min)
        {
            drawList.AddLine(new Vector2(pos.X, middle), new Vector2(pos.X + size.X, middle), color, 1.2f);
            return;
        }

        var span = max - min;
        var step = size.X / (series.Count - 1);
        var previous = new Vector2(pos.X, pos.Y + size.Y - ((float)((series[0] - min) / (double)span) * size.Y));

        for (var i = 1; i < series.Count; i++)
        {
            var point = new Vector2(
                pos.X + (step * i),
                pos.Y + size.Y - ((float)((series[i] - min) / (double)span) * size.Y));

            drawList.AddLine(previous, point, color, 1.2f);
            previous = point;
        }
    }

    private static void DrawGoldRule()
    {
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0f, 4f);
        drawList.AddLine(pos, pos + new Vector2(width, 0f), Theme.WithAlpha(Theme.Gold, 0.22f), 1f);
        ImGui.Dummy(new Vector2(width, 9f));
    }

    // ------------------------------------------------------------------- misc ----

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
