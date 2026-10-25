using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DutyRecap.Core;
using DutyRecap.UI;

namespace DutyRecap;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/recap";

    private readonly WindowSystem windowSystem = new("DutyRecap");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        PluginConfig = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        PluginConfig.Normalize();

        this.Session = new RecapSession(this);

        this.mainWindow = new MainWindow(this);
        this.configWindow = new ConfigWindow(this);
        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.configWindow);

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Duty Recap.",
            ShowInHelp = true,
        });

        this.Session.Attach();

        if (PluginConfig.WindowOpen)
            this.mainWindow.IsOpen = true;

        Log.Information("[DutyRecap] Loaded with {Count} saved runs.", PluginConfig.History.Count);
    }

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IDutyState DutyState { get; private set; } = null!;

    [PluginService]
    internal static IPartyList PartyList { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    public static Configuration PluginConfig { get; private set; } = null!;

    public Configuration Configuration => PluginConfig;

    public RecapSession Session { get; }

    public void Dispose()
    {
        this.Session.Dispose();

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

    public void OpenRecap() => this.OpenMain();

    public void Print(string message) => ChatGui.Print($"[Duty Recap] {message}");

    private void OnCommand(string command, string args)
    {
        switch ((args ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "copy":
                var last = PluginConfig.History.Count > 0 ? PluginConfig.History[0] : null;
                if (last is null)
                {
                    this.Print("No completed run to copy yet.");
                    break;
                }

                var elapsed = last.FinishedUtc - last.StartedUtc;
                var line = $"{last.DutyName}, {Detection.Detectors.FormatClock(elapsed)}, {last.Wipes} wipe{(last.Wipes == 1 ? string.Empty : "s")}, " +
                           $"{last.TotalDeaths} death{(last.TotalDeaths == 1 ? string.Empty : "s")}. Grade {last.Grade}.";
                Dalamud.Bindings.ImGui.ImGui.SetClipboardText(line);
                this.Print("Last recap copied to the clipboard.");
                break;

            case "clear":
                PluginConfig.History.Clear();
                SaveConfig();
                this.Print("History cleared.");
                break;

            default:
                this.OpenMain();
                break;
        }
    }
}
