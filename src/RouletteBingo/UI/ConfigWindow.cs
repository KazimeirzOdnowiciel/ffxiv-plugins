using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using RouletteBingo.Core;
using RouletteBingo.Detection;

namespace RouletteBingo.UI;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Roulette Bingo - Settings###RouletteBingoConfig")
    {
        this.plugin = plugin;
        this.Size = new Vector2(560, 660);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(1200, 1600),
        };
    }

    private Configuration Config => this.plugin.Configuration;

    public override void Draw()
    {
        var changed = false;

        ImGui.TextWrapped("A card for every duty. Mark what you witness, argue about the rest.");

        ImGui.Separator();
        changed |= this.Checkbox("Enabled", () => this.Config.Enabled, v => this.Config.Enabled = v);
        changed |= this.Checkbox(
            "Roll a card automatically when a duty starts",
            () => this.Config.AutoStartCards,
            v => this.Config.AutoStartCards = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Detection");
        ImGui.TextWrapped("Everything below is optional. The rest of the board is clicked by hand.");
        changed |= this.Checkbox("Party deaths", () => this.Config.AutoDetectDeaths, v => this.Config.AutoDetectDeaths = v);
        changed |= this.Checkbox(
            "Background party chat (o/, gg, ty, mb...)",
            () => this.Config.AutoDetectChat,
            v => this.Config.AutoDetectChat = v);
        changed |= this.Checkbox(
            "Three vulnerability stacks",
            () => this.Config.AutoDetectVulnStacks,
            v => this.Config.AutoDetectVulnStacks = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Manners");
        changed |= this.Checkbox(
            "Deadpan commentary in your own echo channel",
            () => this.Config.Snark,
            v => this.Config.Snark = v);
        changed |= this.Checkbox(
            "Announce bingos in party chat",
            () => this.Config.AnnounceBingoInParty,
            v => this.Config.AnnounceBingoInParty = v);

        if (this.Config.AnnounceBingoInParty)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Danger);
            ImGui.TextWrapped(
                "This posts one short message to your party when you complete a line. Most people will not know " +
                "what it means. That is the bit. It is off by default for a reason.");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Board");
        changed |= this.Checkbox(
            "Cursed mode (cards built from the deep end of the pool)",
            () => this.Config.CursedMode,
            v => this.Config.CursedMode = v);
        changed |= this.Checkbox(
            "Compact grid (tiny 5x5 dots, good for combat)",
            () => this.Config.CompactGrid,
            v => this.Config.CompactGrid = v);
        changed |= this.Checkbox(
            "Show recent runs",
            () => this.Config.ShowHistory,
            v => this.Config.ShowHistory = v);

        var scale = this.Config.UiScale;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("UI scale", ref scale, 0.75f, 1.5f, "%.2fx"))
        {
            this.Config.UiScale = scale;
            changed = true;
        }

        if (changed)
        {
            this.Config.Normalize();
            this.plugin.SaveConfig();
        }

        ImGui.Separator();
        this.DrawPoolSection();

        if (this.Config.ShowHistory)
        {
            ImGui.Separator();
            this.DrawHistorySection();
        }

        ImGui.Separator();
        this.DrawStatsSection();
    }

    private bool Checkbox(string label, Func<bool> getter, Action<bool> setter)
    {
        var value = getter();
        if (!ImGui.Checkbox(label, ref value))
            return false;

        setter(value);
        return true;
    }

    private void DrawPoolSection()
    {
        var pool = this.plugin.Pool.All;
        ImGui.TextUnformatted($"Square pool: {pool.Count}");

        foreach (var group in pool.GroupBy(s => s.Category).OrderByDescending(g => g.Count()))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.CategoryColor(group.Key));
            ImGui.TextUnformatted($"{Suggestion.CategoryName(group.Key),-10} {group.Count(),3}");
            ImGui.PopStyleColor();
        }
    }

    private void DrawHistorySection()
    {
        var history = this.Config.History;
        ImGui.TextUnformatted("Recent runs");

        if (history.Count == 0)
        {
            ColoredText(Theme.TextFaint, "Nothing yet. Go queue for something you will regret.");
            return;
        }

        foreach (var run in history.Take(6))
        {
            var when = run.FinishedUtc.ToLocalTime().ToString("MMM d HH:mm");
            var result = run.Completed ? "cleared" : "left";
            ColoredText(
                Theme.TextDim,
                $"{when}  {Truncate(run.DutyName, 30),-30}  {run.Marked,2}/25  {run.Bingos} bingo  {result}");
        }
    }

    private void DrawStatsSection()
    {
        var config = this.Config;
        ImGui.TextUnformatted($"Lifetime: {config.TotalCards} cards, {config.TotalBingos} bingos");

        var top = Detectors.TopSquares(config.SquareCounts, 5).ToList();
        if (top.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Your curses");
            foreach (var id in top)
            {
                var square = this.plugin.Pool.All.FirstOrDefault(s => s.Id == id);
                if (square is null)
                    continue;

                ColoredText(Theme.TextDim, $"  {config.SquareCounts[id],3}x  {Truncate(square.Text, 52)}");
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Reset lifetime stats"))
        {
            config.SquareCounts.Clear();
            config.History.Clear();
            config.TotalCards = 0;
            config.TotalBingos = 0;
            this.plugin.SaveConfig();
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "...";

    private static void ColoredText(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
