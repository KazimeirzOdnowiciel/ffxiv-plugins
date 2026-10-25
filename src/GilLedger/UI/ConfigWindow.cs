using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GilLedger.UI;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Gil Ledger - Settings###GilLedgerConfig")
    {
        this.plugin = plugin;
        this.Size = new Vector2(480, 320);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    private Configuration Config => this.plugin.Configuration;

    public override void Draw()
    {
        var changed = false;

        ImGui.TextWrapped("A ledger of the gil that moves through your pocket while this plugin is running.");

        ImGui.Separator();
        changed |= this.Checkbox("Enabled", () => this.Config.Enabled, v => this.Config.Enabled = v);
        changed |= this.Checkbox(
            "Reconcile the balance (record changes the game never explains)",
            () => this.Config.ReconcileBalance,
            v => this.Config.ReconcileBalance = v);
        changed |= this.Checkbox(
            "Label retainer sales (a sale is matched to the balance spike that follows)",
            () => this.Config.WatchRetainerSales,
            v => this.Config.WatchRetainerSales = v);

        ImGui.Spacing();
        ImGui.TextWrapped(
            "Sales that sit on a retainer only show up once you withdraw the gil. Every amount is the real " +
            "balance change; the label is the plugin's best guess.");

        if (changed)
        {
            this.Config.Normalize();
            this.plugin.SaveConfig();
        }

        ImGui.Separator();

        if (ImGui.Button("Clear the ledger"))
        {
            this.Config.Entries.Clear();
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
