using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RouletteBingo.Core;
using RouletteBingo.UI;

namespace RouletteBingo;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/bingo";

    public static readonly string[] Snark =
    {
        "Noted.",
        "The court accepts this.",
        "Bold claim.",
        "Screenshot or it did not happen.",
        "Your honour, the tank did it.",
        "We all saw it.",
        "No further questions.",
        "This will be in the report.",
        "The table remembers.",
        "Unfortunately, yes.",
        "Objection overruled.",
        "Someone write that down.",
        "That is going in the minutes.",
    };

    public static readonly string[] RareSnark =
    {
        "That is a rare one. Frame it.",
        "Of course it happened. It always happens.",
        "You cannot prove it, but you also cannot disprove it.",
        "I believe you. I have seen worse.",
        "That square has been waiting all week.",
    };

    public static readonly string[] CursedSnark =
    {
        "That one is illegal.",
        "We do not talk about that square.",
        "You are not supposed to be able to mark that.",
        "The board is cursed. You did this.",
        "Somebody call a mentor. Or do not.",
    };

    private readonly WindowSystem windowSystem = new("RouletteBingo");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        PluginConfig = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        PluginConfig.Normalize();

        SquarePool = SuggestionPool.LoadEmbedded();
        Session = new DutySession(this);

        this.mainWindow = new MainWindow(this);
        this.configWindow = new ConfigWindow(this);
        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.configWindow);

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Roulette Bingo. Options: new, report, code, load <code>, compact, stats.",
            ShowInHelp = true,
        });

        Session.Attach();
        Session.Resume();

        if (PluginConfig.WindowOpen)
            this.mainWindow.IsOpen = true;

        Log.Information("[RouletteBingo] Loaded with {Count} squares.", SquarePool.All.Count);
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

    public static SuggestionPool SquarePool { get; private set; } = null!;

    public Configuration Configuration => PluginConfig;

    public SuggestionPool Pool => SquarePool;

    public DutySession Session { get; }

    public MainWindow MainWindow => this.mainWindow;

    public event Action? CardChanged;

    public void Dispose()
    {
        Session.Dispose();
        Session.SaveCardState();
        SaveConfig();

        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        this.windowSystem.RemoveAllWindows();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(PluginConfig);

    public void OpenConfig() => this.configWindow.IsOpen = true;

    public void OpenMain()
    {
        this.mainWindow.IsOpen = true;
        this.mainWindow.BringToFront();
    }

    public void NotifyCardChanged() => this.CardChanged?.Invoke();

    public void Print(string message) => ChatGui.Print($"[Bingo] {message}");

    private void OnCommand(string command, string args)
    {
        var trimmed = (args ?? string.Empty).Trim();
        var split = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = split.Length > 0 ? split[0].ToLowerInvariant() : string.Empty;
        var rest = split.Length > 1 ? split[1] : string.Empty;

        switch (verb)
        {
            case "":
                this.mainWindow.Toggle();
                break;

            case "new":
                Session.StartCard();
                this.mainWindow.IsOpen = true;
                this.Print("New card rolled.");
                break;

            case "report":
                ImGui.SetClipboardText(Session.CurrentReport());
                this.Print("Report copied to clipboard.");
                break;

            case "code":
                var code = Session.CurrentCode();
                if (code.Length == 0)
                {
                    this.Print("No active card.");
                    break;
                }

                ImGui.SetClipboardText(code);
                this.Print($"Card code: {code}");
                break;

            case "load":
                if (rest.Length == 0)
                {
                    this.Print("Usage: /bingo load RB1....");
                    break;
                }

                this.Print(Session.LoadCode(rest) ? "Shared card loaded." : "That is not a valid card code.");
                this.mainWindow.IsOpen = true;
                break;

            case "compact":
                PluginConfig.CompactGrid = !PluginConfig.CompactGrid;
                SaveConfig();
                this.mainWindow.IsOpen = true;
                this.Print(PluginConfig.CompactGrid ? "Compact grid on." : "Compact grid off.");
                break;

            case "stats":
                this.PrintStats();
                break;

            default:
                this.Print("Options: new, report, code, load <code>, compact, stats.");
                break;
        }
    }

    private void PrintStats()
    {
        var config = PluginConfig;
        this.Print($"{config.TotalCards} cards, {config.TotalBingos} bingos.");

        var top = Detection.Detectors.TopSquares(config.SquareCounts, 3).ToList();
        if (top.Count == 0)
            return;

        foreach (var id in top)
        {
            var square = SquarePool.All.FirstOrDefault(s => s.Id == id);
            if (square is not null)
                this.Print($"  {config.SquareCounts[id]}x  {square.Text}");
        }
    }
}
