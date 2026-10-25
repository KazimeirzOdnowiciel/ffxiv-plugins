using System;
using System.IO;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SlotYield.Game;

/// <summary>
/// Writes the retainer sell window's live structure to a file: addon addresses, AtkValues, and the
/// agent's own fields. This is the only honest way to build an input driver on a machine where the
/// game cannot be debugged directly - read what the window actually holds, then target that.
/// </summary>
public static unsafe class AddonDump
{
    private static readonly string[] Addons =
    {
        "RetainerSellList",
        "ContextMenu",
        "RetainerSell",
        "RetainerList",
        "SelectString",
        "SelectYesno",
    };

    /// <summary>
    /// Opens the sell/adjust dialog for a market slot without touching the price. Used to learn
    /// whether that call works for an existing listing before any automation relies on it.
    /// </summary>
    public static string TryOpenSellDialog(InventoryType container, int slot)
    {
        try
        {
            var agent = AgentRetainer.Instance();
            if (agent is null)
                return "agent unavailable (is a retainer open?)";

            agent->OpenRetainerSell(container, (ushort)slot);
            return $"requested the sell dialog for slot {slot}";
        }
        catch (Exception ex)
        {
            return $"open failed: {ex.Message}";
        }
    }

    public static string TryOpenSellDialog(int slot)
    {
        try
        {
            var agent = AgentRetainer.Instance();
            if (agent is null)
                return "agent unavailable (is a retainer open?)";

            agent->OpenRetainerSell(InventoryType.RetainerMarket, (ushort)slot);
            return $"requested the sell dialog for market slot {slot}";
        }
        catch (Exception ex)
        {
            return $"open failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes a price into the open sell dialog's value array and reports the change. It does not
    /// confirm anything: the experiment is "does the dialog reflect this", not "does it sell".
    /// </summary>
    public static string TrySetPrice(int price)
    {
        try
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSell", 1);

            if (addon is null)
                return "RetainerSell is not open";

            var values = addon->AtkValuesSpan;

            if (values.Length < 6)
                return $"unexpected AtkValues count ({values.Length})";

            var before = values[5].Int;
            var count = values.Length;

            var copy = stackalloc AtkValue[count];
            for (var i = 0; i < count; i++)
                copy[i] = values[i];

            copy[5].Int = price;
            copy[5].Type = AtkValueType.Int;

            var applied = addon->SetAtkValues((uint)count, copy);
            var after = addon->AtkValuesSpan.Length > 5 ? addon->AtkValuesSpan[5].Int : -1;

            return $"SetAtkValues={applied}, value[5] {before} -> {after} (wanted {price}); dialog only, nothing confirmed";
        }
        catch (Exception ex)
        {
            return $"set price failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Walks the sell dialog's node tree, finds its numeric input and sets the value through the
    /// component API. Reports every component it saw so a miss is diagnosable.
    /// </summary>
    public static string TrySetNumericInput(int price)
    {
        try
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSell", 1);

            if (addon is null)
                return "RetainerSell is not open";

            var manager = addon->UldManager;
            var nodes = manager.NodeList;
            var count = (int)manager.NodeListCount;
            var report = new StringBuilder();
            var found = 0;

            for (var i = 0; i < count; i++)
            {
                var node = nodes[i];
                if (node is null)
                    continue;

                if (node->Type != NodeType.Component)
                    continue;

                var component = ((AtkComponentNode*)node)->Component;
                if (component is null)
                    continue;

                var type = component->GetComponentType();
                report.AppendLine($"  node {node->NodeId} component {type}");

                if (type != ComponentType.NumericInput)
                    continue;

                ((AtkComponentNumericInput*)component)->SetValue(price);
                found++;
                report.AppendLine($"    -> SetValue({price})");
            }

            return found > 0
                ? $"set {found} numeric input(s) to {price}\n{report}"
                : $"no numeric input found ({count} nodes)\n{report}";
        }
        catch (Exception ex)
        {
            return $"numeric input probe failed: {ex.Message}";
        }
    }

    public static string Write(string directory)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"retainer UI dump {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        Agent(builder);

        foreach (var name in Addons)
            DumpAddon(builder, name);

        var path = Path.Combine(directory, $"retainer-ui-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, builder.ToString());
        return path;
    }

    private static void Agent(StringBuilder builder)
    {
        try
        {
            var agent = AgentRetainer.Instance();
            builder.AppendLine();
            builder.AppendLine("== AgentRetainer");

            if (agent is null)
            {
                builder.AppendLine("  not available");
                return;
            }

            builder.AppendLine($"  addon id {agent->AddonId}, sell list addon {agent->RetainerSellListAddonId}, sell addon {agent->RetainerSellAddonId}");
            builder.AppendLine($"  entries {agent->SellListEntryCount}");
            builder.AppendLine($"  selected slot {agent->SellItemInventorySlot}, container {agent->SellItemInventoryType}, unit price {agent->SellItemUnitPrice}");

            var entries = agent->SellListEntries;

            var liveCount = Math.Min(entries.Length, Math.Max(0, agent->SellListEntryCount));

            for (var i = 0; i < liveCount; i++)
            {
                var entry = entries[i];
                if (entry.ItemId == 0)
                    continue;

                builder.AppendLine(
                    $"  [{i}] slot {entry.InventorySlot} item {entry.ItemId} x{entry.Quantity} " +
                    $"unit '{entry.UnitPriceText}' total '{entry.TotalPriceText}'");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"  agent read failed: {ex.Message}");
        }
    }

    private static void DumpAddon(StringBuilder builder, string name)
    {
        builder.AppendLine();
        builder.AppendLine($"== {name}");

        try
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name, 1);

            if (addon is null)
            {
                builder.AppendLine("  not open");
                return;
            }

            builder.AppendLine($"  address 0x{(nint)addon:X}, visible {addon->IsVisible}, values {addon->AtkValuesCount}");

            var values = addon->AtkValuesSpan;
            var count = Math.Min(values.Length, 48);

            for (var i = 0; i < count; i++)
            {
                var value = values[i];
                builder.AppendLine(
                    $"  v{i}: type {value.Type} int {value.Int} uint {value.UInt} float {value.Float:0.###} str '{SafeString(&value)}'");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"  read failed: {ex.Message}");
        }
    }

    private static string SafeString(AtkValue* value)
    {
        try
        {
            return value->Type == AtkValueType.String ? value->GetValueAsString() ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
