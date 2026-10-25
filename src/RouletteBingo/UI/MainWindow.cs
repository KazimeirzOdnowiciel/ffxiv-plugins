using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using RouletteBingo.Core;
using RouletteBingo.Detection;

namespace RouletteBingo.UI;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;

    private DateTime bingoFlashUntil = DateTime.MinValue;
    private string bingoFlashText = string.Empty;
    private DateTime autoFlashUntil = DateTime.MinValue;
    private string autoFlashText = string.Empty;
    private DateTime statusUntil = DateTime.MinValue;
    private string statusText = string.Empty;
    private string loadCode = string.Empty;

    public MainWindow(Plugin plugin)
        : base("Roulette Bingo###RouletteBingoMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(720, 800);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 360),
            MaximumSize = new Vector2(1600, 1800),
        };

        plugin.Session.LineCompleted += this.OnLineCompleted;
        plugin.Session.AutoMarked += this.OnAutoMarked;
    }

    private DutySession Session => this.plugin.Session;

    private float Scale => this.plugin.Configuration.UiScale;

    public override void OnOpen()
    {
        this.plugin.Configuration.WindowOpen = true;
        this.plugin.SaveConfig();
    }

    public override void OnClose()
    {
        this.plugin.Configuration.WindowOpen = false;
        this.plugin.SaveConfig();
    }

    public override void Draw()
    {
        var card = this.Session.Card;

        if (!this.plugin.Configuration.Enabled)
        {
            ImGui.TextWrapped("Roulette Bingo is disabled. Turn it back on in the settings.");
            if (ImGui.Button("Open settings"))
                this.plugin.OpenConfig();

            return;
        }

        if (card is null)
        {
            this.DrawEmptyState();
            return;
        }

        this.DrawHeader(card);
        this.DrawProgress(card);
        CardGrid.Draw(card, this.Scale, this.plugin.Configuration.CompactGrid, index => this.Session.Toggle(index));
        this.DrawStatusLine(card);
        this.DrawFooter(card);
        this.DrawStatsTeaser(card);
    }

    // ------------------------------------------------------------------ header ----

    private void DrawHeader(BingoCard card)
    {
        var scale = Math.Clamp(this.Scale, 0.8f, 1.5f);
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();

        var labelPos = ImGui.GetCursorScreenPos();
        drawList.AddText(
            font,
            10f * scale,
            labelPos,
            Theme.WithAlpha(Theme.Gold, 0.9f),
            card.Finished ? "R O U L E T T E   B I N G O   -   R U N   O V E R" : "R O U L E T T E   B I N G O");
        ImGui.Dummy(new Vector2(0f, 16f * scale));

        var titlePos = ImGui.GetCursorScreenPos();
        drawList.AddText(font, 20f * scale, titlePos, Theme.U32(Theme.Text), card.DutyName);
        ImGui.Dummy(new Vector2(0f, 26f * scale));

        var meta = $"{BingoCard.DescribeKind(card.Kind)}  -  {Detectors.FormatClock(this.Session.Elapsed)}  -  seed {card.Seed:x8}";
        if (card.Finished)
            meta += "  -  finished";

        ColoredText(Theme.TextFaint, meta);

        var subParts = new System.Collections.Generic.List<string> { $"card {this.plugin.Configuration.TotalCards + 1}" };
        if (this.Session.Wipes > 0)
            subParts.Add($"{this.Session.Wipes} wipe{(this.Session.Wipes == 1 ? string.Empty : "s")}");
        if (this.plugin.Configuration.CursedMode)
            subParts.Add("cursed mode");
        if (this.plugin.Configuration.AutoStartCards)
            subParts.Add("auto-detect on");

        ColoredText(Theme.TextFaint, string.Join("  -  ", subParts));
        ImGui.Spacing();
    }

    // ---------------------------------------------------------------- progress ----

    private void DrawProgress(BingoCard card)
    {
        var lines = card.CompletedLines;
        var fraction = card.MarkedCount / (float)CardGenerator.CellCount;

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Theme.Gold);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Panel);
        ImGui.ProgressBar(fraction, new Vector2(-1f, 12f * this.Scale), $"{card.MarkedCount}/{CardGenerator.CellCount}");
        ImGui.PopStyleColor(2);

        if (lines.Count == 0)
        {
            ColoredText(Theme.TextFaint, "no bingo yet - five in a row does it");
            return;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            ColoredText(Theme.Gold, $"BINGO: {lines[i].Label}");
        }
    }

    // ------------------------------------------------------------------ status ----

    private void DrawStatusLine(BingoCard card)
    {
        var now = DateTime.UtcNow;

        if (now < this.bingoFlashUntil)
        {
            var alpha = Math.Clamp((float)(this.bingoFlashUntil - now).TotalSeconds / 3f, 0.15f, 1f);
            var pos = ImGui.GetCursorScreenPos();
            var height = 22f * this.Scale;
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(pos, pos + new Vector2(ImGui.GetContentRegionAvail().X, height), Theme.WithAlpha(Theme.Gold, 0.14f * alpha), 4f);
            drawList.AddRect(pos, pos + new Vector2(ImGui.GetContentRegionAvail().X, height), Theme.WithAlpha(Theme.Gold, 0.5f * alpha), 4f);
            drawList.AddText(ImGui.GetFont(), 12f * this.Scale, pos + new Vector2(10f, 4f * this.Scale), Theme.WithAlpha(Theme.Gold, alpha), $"BINGO - {this.bingoFlashText}");
            ImGui.Dummy(new Vector2(0f, height));
            return;
        }

        if (now < this.autoFlashUntil)
        {
            ColoredText(Theme.WithAlpha(Theme.Gold, 0.85f), $"auto-marked: {this.autoFlashText}");
            return;
        }

        if (now < this.statusUntil)
        {
            ColoredText(Theme.WithAlpha(Theme.Gold, 0.9f), this.statusText);
            return;
        }

        var hint = card.Finished
            ? "Run over. Copy the report, roll a new card, or load a friend's code."
            : "Click squares as they happen. Hover to read the fine print.";
        ColoredText(Theme.TextFaint, hint);
    }

    // ------------------------------------------------------------------ footer ----

    private void DrawFooter(BingoCard card)
    {
        ImGui.Spacing();

        if (ImGui.Button("New card"))
            this.Session.StartCard();

        ImGui.SameLine();
        if (ImGui.Button("Copy report"))
        {
            ImGui.SetClipboardText(this.Session.CurrentReport());
            this.SetStatus("Report copied to clipboard.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy code"))
        {
            ImGui.SetClipboardText(this.Session.CurrentCode());
            this.SetStatus($"Card code copied: {this.Session.CurrentCode()}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Load code..."))
        {
            this.loadCode = string.Empty;
            ImGui.OpenPopup("##bingoloadcode");
        }

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            this.plugin.OpenConfig();

        ImGui.SameLine();
        var compact = this.plugin.Configuration.CompactGrid;
        if (ImGui.Checkbox("Compact", ref compact))
        {
            this.plugin.Configuration.CompactGrid = compact;
            this.plugin.SaveConfig();
        }

        this.DrawLoadPopup();
    }

    private void DrawLoadPopup()
    {
        if (!ImGui.BeginPopup("##bingoloadcode"))
            return;

        ImGui.TextUnformatted("Paste a card code (RB1....) from a friend:");
        ImGui.SetNextItemWidth(340f);
        ImGui.InputText("##code", ref this.loadCode, 128, ImGuiInputTextFlags.None, (ImGui.ImGuiInputTextCallbackDelegate?)null);

        if (ImGui.Button("Load"))
        {
            if (this.Session.LoadCode(this.loadCode))
            {
                this.SetStatus("Loaded shared card.");
                ImGui.CloseCurrentPopup();
            }
            else
            {
                this.SetStatus("That does not look like a valid card code.");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void DrawStatsTeaser(BingoCard card)
    {
        var config = this.plugin.Configuration;
        if (config.SquareCounts.Count == 0)
            return;

        ImGui.Spacing();
        var topId = Detectors.TopSquares(config.SquareCounts, 1).FirstOrDefault();
        if (topId is null)
            return;

        var square = this.plugin.Pool.All.FirstOrDefault(s => s.Id == topId);
        if (square is null)
            return;

        ColoredText(
            Theme.TextFaint,
            $"Your most-marked square, ever: \"{square.Text}\" ({config.SquareCounts[topId]}x)");
    }

    // ------------------------------------------------------------------ empty -----

    private void DrawEmptyState()
    {
        var scale = Math.Clamp(this.Scale, 0.8f, 1.5f);
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        drawList.AddText(ImGui.GetFont(), 10f * scale, pos, Theme.WithAlpha(Theme.Gold, 0.9f), "R O U L E T T E   B I N G O");
        ImGui.Dummy(new Vector2(0f, 16f * scale));

        pos = ImGui.GetCursorScreenPos();
        drawList.AddText(ImGui.GetFont(), 20f * scale, pos, Theme.U32(Theme.Text), "No card in play");
        ImGui.Dummy(new Vector2(0f, 28f * scale));

        ImGui.TextWrapped(
            "A card is rolled automatically when a duty starts. You can also roll one by hand - useful for " +
            "open-world nonsense, hunt trains, or a friend's stream.");

        ImGui.Spacing();
        if (ImGui.Button("Roll a card now"))
            this.Session.StartCard();

        ImGui.SameLine();
        if (ImGui.Button("Load code..."))
        {
            this.loadCode = string.Empty;
            ImGui.OpenPopup("##bingoloadcode");
        }

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            this.plugin.OpenConfig();

        ImGui.Spacing();
        ColoredText(Theme.TextFaint, $"Pool: {this.plugin.Pool.All.Count} squares.");
        ColoredText(Theme.TextFaint, "Cards are seeded: the code is the card, so a friend can replay the exact same board.");

        this.DrawLoadPopup();
    }

    // ------------------------------------------------------------------ events ----

    private void OnLineCompleted(BingoCard card, string label)
    {
        this.bingoFlashUntil = DateTime.UtcNow.AddSeconds(3);
        this.bingoFlashText = label;
    }

    private void OnAutoMarked(BingoCard card, System.Collections.Generic.IReadOnlyList<int> hits)
    {
        this.autoFlashUntil = DateTime.UtcNow.AddSeconds(4);
        this.autoFlashText = string.Join(", ", hits.Select(i => card.Cells[i].Text));
    }

    private void SetStatus(string text)
    {
        this.statusText = text;
        this.statusUntil = DateTime.UtcNow.AddSeconds(4);
    }

    private static void ColoredText(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void ColoredText(uint color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
