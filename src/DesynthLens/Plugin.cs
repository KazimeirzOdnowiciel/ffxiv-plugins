using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DesynthLens.Game;
using DesynthLens.Market;
using DesynthLens.UI;

namespace DesynthLens;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/desynthlens";
    public const string AliasName = "/dlens";

    private readonly WindowSystem windowSystem = new("DesynthLens");
    private readonly MainWindow window;

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }

    public MarketPrices Prices { get; }

    public bool IsCapturing { get; private set; }

    public string Status { get; private set; } = "Desynth something and the result is recorded here.";

    public DateTime? CapturedAt { get; private set; }

    private bool disposed;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Normalize();
        Prices = new MarketPrices();

        this.window = new MainWindow(this);
        this.windowSystem.AddWindow(this.window);
        this.window.IsOpen = Configuration.WindowOpen;

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.Open;
        PluginInterface.UiBuilder.OpenConfigUi += this.Open;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Desynth Lens. Options: capture, clear.",
        });

        try
        {
            CommandManager.AddHandler(AliasName, new CommandInfo(this.OnCommand)
            {
                HelpMessage = "Open Desynth Lens.",
            });
        }
        catch
        {
            // alias already taken
        }

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SalvageResult", this.OnSalvageResult);
    }

    public IReadOnlyList<DesynthRecord> Records =>
        this.Configuration.Records
            .OrderByDescending(record => record.Times > 0 ? record.TotalValue / record.Times : 0m)
            .ToList();

    public void Open()
    {
        this.window.IsOpen = true;
        Configuration.WindowOpen = true;
    }

    public void Dispose()
    {
        this.disposed = true;

        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SalvageResult", this.OnSalvageResult);
        CommandManager.RemoveHandler(CommandName);

        if (CommandManager.Commands.ContainsKey(AliasName))
            CommandManager.RemoveHandler(AliasName);

        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= this.Open;
        PluginInterface.UiBuilder.OpenConfigUi -= this.Open;
        this.windowSystem.RemoveAllWindows();
        this.Prices.Dispose();
        Configuration.Save();
    }

    private void OnCommand(string command, string args)
    {
        switch ((args ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "capture":
                this.Capture();
                break;

            case "clear":
                Configuration.Records.Clear();
                Configuration.Save();
                ChatGui.Print("[Desynth Lens] Ledger cleared.");
                break;

            default:
                this.Open();
                break;
        }
    }

    private void OnSalvageResult(AddonEvent type, AddonArgs args)
    {
        // The results land in the agent a moment after the window appears.
        _ = Task.Run(async () =>
        {
            await Task.Delay(600).ConfigureAwait(false);

            if (this.disposed)
                return;

            await Framework.RunOnFrameworkThread(this.Capture).ConfigureAwait(false);
        });
    }

    public void Capture()
    {
        if (this.IsCapturing)
            return;

        if (!SalvageReader.TryRead(out var itemId, out var results))
        {
            this.Status = "No desynthesis result to read. Open the desynthesis window with an item first.";
            return;
        }

        var world = PlayerState.CurrentWorld.Value.Name.ToString();
        if (string.IsNullOrWhiteSpace(world))
            return;

        var ids = results.Select(result => result.ItemId).Append(itemId).Distinct().ToList();
        this.IsCapturing = true;
        this.Status = "pricing the result...";

        _ = Task.Run(async () =>
        {
            try
            {
                var prices = await this.Prices
                    .FetchAsync(world, ids, null, CancellationToken.None)
                    .ConfigureAwait(false);

                if (this.disposed)
                    return;

                await Framework.RunOnFrameworkThread(() => this.Apply(itemId, results, prices)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Desynth Lens] Capture failed.");
                this.Status = $"capture failed: {ex.Message}";
                this.IsCapturing = false;
            }
        });
    }

    private void Apply(uint itemId, List<(uint ItemId, int Quantity)> results, Dictionary<uint, decimal> prices)
    {
        var value = 0m;
        var complete = true;
        var parts = new List<string>(results.Count);

        foreach (var (resultId, quantity) in results)
        {
            var name = this.ItemName(resultId);

            if (!prices.TryGetValue(resultId, out var price) || price <= 0)
            {
                complete = false;
                parts.Add(quantity > 1 ? $"{name} x{quantity} (no price)" : $"{name} (no price)");
                continue;
            }

            value += price * quantity;
            parts.Add(quantity > 1 ? $"{name} x{quantity}" : name);
        }

        var untradeable = this.IsUntradeable(itemId);
        var buyPrice = !untradeable && prices.TryGetValue(itemId, out var ownPrice) && ownPrice > 0 ? ownPrice : 0m;
        var profit = buyPrice > 0 ? value - buyPrice : 0m;
        var yield = string.Join(" + ", parts);
        var now = DateTime.UtcNow;
        var index = Configuration.Records.FindIndex(record => record.ItemId == itemId);

        if (index >= 0)
        {
            var existing = Configuration.Records[index];
            Configuration.Records[index] = existing with
            {
                Name = this.ItemName(itemId),
                Times = existing.Times + 1,
                TotalValue = existing.TotalValue + value,
                BestValue = Math.Max(existing.BestValue, value),
                LastValue = value,
                BuyPrice = buyPrice,
                TotalProfit = existing.TotalProfit + profit,
                Untradeable = untradeable,
                LastYield = yield,
                LastUtc = now,
            };
        }
        else
        {
            Configuration.Records.Add(new DesynthRecord(
                itemId,
                this.ItemName(itemId),
                1,
                value,
                value,
                value,
                yield,
                now,
                buyPrice,
                profit,
                untradeable));
        }

        Configuration.Save();

        this.IsCapturing = false;
        this.CapturedAt = now;
        this.Status = complete
            ? buyPrice > 0
                ? $"{this.ItemName(itemId)} -> {yield} = {value:N0} gil ({(profit >= 0 ? "+" : string.Empty)}{profit:N0} on a {buyPrice:N0} buy)"
                : $"{this.ItemName(itemId)} -> {yield} = {value:N0} gil (input has no listing)"
            : $"{this.ItemName(itemId)} -> {yield} = {value:N0} gil (some results unpriced)";

        Log.Information(
            "[Desynth Lens] Captured {Item}: {Yield} worth {Value}, buy {Buy}, profit {Profit} ({Complete}).",
            this.ItemName(itemId),
            yield,
            value,
            buyPrice,
            profit,
            complete);
    }

    private bool IsUntradeable(uint itemId)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            return sheet is not null && sheet.TryGetRow(itemId, out var item) && item.IsUntradable;
        }
        catch
        {
            return false;
        }
    }

    private string ItemName(uint itemId)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            return sheet is not null && sheet.TryGetRow(itemId, out var item)
                ? item.Name.ToString() ?? $"Item {itemId}"
                : $"Item {itemId}";
        }
        catch
        {
            return $"Item {itemId}";
        }
    }
}
