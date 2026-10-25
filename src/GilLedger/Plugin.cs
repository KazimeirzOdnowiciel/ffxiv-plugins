using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GilLedger.Core;
using GilLedger.Game;
using GilLedger.UI;

namespace GilLedger;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/gil";

    private readonly WindowSystem windowSystem = new("GilLedger");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    private DateTime nextPoll = DateTime.MinValue;

    public Plugin()
    {
        PluginConfig = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        PluginConfig.Normalize();

        this.Ledger = new Ledger(this);

        this.mainWindow = new MainWindow(this);
        this.configWindow = new ConfigWindow(this);
        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.configWindow);

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Gil Ledger. Options: export, clear.",
            ShowInHelp = true,
        });

        ChatGui.ChatMessage += this.OnChatMessage;
        Framework.Update += this.OnUpdate;

        if (PluginConfig.WindowOpen)
            this.mainWindow.IsOpen = true;

        Log.Information("[GilLedger] Loaded with {Count} entries.", PluginConfig.Entries.Count);
    }

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    public static Configuration PluginConfig { get; private set; } = null!;

    public Configuration Configuration => PluginConfig;

    public Ledger Ledger { get; }

    public void Dispose()
    {
        ChatGui.ChatMessage -= this.OnChatMessage;
        Framework.Update -= this.OnUpdate;

        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        this.windowSystem.RemoveAllWindows();
        SaveConfig();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(PluginConfig);

    public void OpenConfig() => this.configWindow.IsOpen = true;

    public void OpenMain()
    {
        this.mainWindow.IsOpen = true;
        this.mainWindow.BringToFront();
    }

    public void Print(string message) => ChatGui.Print($"[Gil Ledger] {message}");

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var text = message.Message.TextValue;
            this.Ledger.OnChat(message.LogKind, text);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[GilLedger] Chat parse failed.");
        }
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now < this.nextPoll)
            return;

        this.nextPoll = now.AddSeconds(1);

        try
        {
            this.Ledger.Tick(GilReader.Gil(), now);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[GilLedger] Balance poll failed.");
        }
    }

    private void OnCommand(string command, string args)
    {
        switch ((args ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "export":
                System.Text.StringBuilder builder = new();
                // Reuse the window's CSV builder by opening the window and copying there is awkward;
                // build a minimal CSV here instead.
                builder.AppendLine("time,kind,amount,balance,note");
                foreach (var e in PluginConfig.Entries)
                    builder.AppendLine($"{e.Utc.ToLocalTime():s},{e.Kind},{e.Amount},{e.BalanceAfter},{Csv(e.Note)}");

                Dalamud.Bindings.ImGui.ImGui.SetClipboardText(builder.ToString());
                this.Print("Ledger copied to the clipboard as CSV.");
                break;

            case "clear":
                PluginConfig.Entries.Clear();
                SaveConfig();
                this.Print("Ledger cleared.");
                break;

            default:
                this.OpenMain();
                break;
        }
    }

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
}
