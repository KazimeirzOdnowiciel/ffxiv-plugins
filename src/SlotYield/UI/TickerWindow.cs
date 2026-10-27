using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SlotYield.Core;

namespace SlotYield.UI;

/// <summary>A tiny always-visible list of the top opportunities, for use while playing.</summary>
public sealed class TickerWindow : Window
{
    private readonly Plugin plugin;

    public TickerWindow(Plugin plugin)
        : base("Slot Yield - ticker###SlotYieldTicker")
    {
        this.plugin = plugin;
        this.Size = new Vector2(300f, 150f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.Flags = ImGuiWindowFlags.NoResize;
        this.BgAlpha = 0.92f;
    }

    public override void Draw()
    {
        var result = this.plugin.State.Result;
        if (result is null)
        {
            Theme.Faint("no scan yet");
            return;
        }

        var top = result.Candidates
            .Where(c => !c.Score.IsDead && !c.Score.IsSuspect && c.Score.ProfitPerDay > 0m)
            .OrderByDescending(c => c.Score.ProfitPerDay)
            .Take(3)
            .ToList();

        if (top.Count == 0)
        {
            Theme.Faint("nothing profitable");
            return;
        }

        foreach (var candidate in top)
        {
            UiKit.ItemIcon(UiKit.IconIdForItem(candidate.ItemId), candidate.Quality == Core.Quality.Hq, 18f);
            ImGui.SameLine(0f, 6f);
            Theme.Colored(Theme.Text, this.plugin.Catalog.Name(candidate.ItemId));
            ImGui.SameLine(0f, 8f);
            Theme.Colored(Theme.Profit, "+" + Money.Gil(candidate.Score.ProfitPerDay));
        }
    }
}
