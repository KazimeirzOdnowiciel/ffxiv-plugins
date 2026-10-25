using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DutyRecap.UI;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Duty Recap - Settings###DutyRecapConfig")
    {
        this.plugin = plugin;
        this.Size = new Vector2(480, 360);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    private Configuration Config => this.plugin.Configuration;

    public override void Draw()
    {
        var changed = false;

        ImGui.TextWrapped("A scorecard for every duty: who died, how often, and how cursed the run was.");

        ImGui.Separator();
        changed |= this.Checkbox("Enabled", () => this.Config.Enabled, v => this.Config.Enabled = v);
        changed |= this.Checkbox(
            "Open the recap automatically when a duty completes",
            () => this.Config.AutoOpenOnComplete,
            v => this.Config.AutoOpenOnComplete = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Detection");
        changed |= this.Checkbox("Party deaths", () => this.Config.DetectDeaths, v => this.Config.DetectDeaths = v);
        changed |= this.Checkbox(
            "Vulnerability stacks",
            () => this.Config.DetectVulnStacks,
            v => this.Config.DetectVulnStacks = v);
        changed |= this.Checkbox("Party chat chatter", () => this.Config.DetectChat, v => this.Config.DetectChat = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Storage");
        changed |= this.Checkbox(
            "Store party chat lines to read later",
            () => this.Config.StoreChatLog,
            v => this.Config.StoreChatLog = v);

        var limit = this.Config.ChatLineLimit;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderInt("Lines per run", ref limit, 5, 120))
        {
            this.Config.ChatLineLimit = limit;
            changed = true;
        }

        if (changed)
        {
            this.Config.Normalize();
            this.plugin.SaveConfig();
        }

        ImGui.Separator();

        if (ImGui.Button("Clear history"))
        {
            this.Config.History.Clear();
            this.plugin.SaveConfig();
        }
    }

    private bool Checkbox(string label, System.Func<bool> getter, System.Action<bool> setter)
    {
        var value = getter();
        if (!ImGui.Checkbox(label, ref value))
            return false;

        setter(value);
        return true;
    }
}
