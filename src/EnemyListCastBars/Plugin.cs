using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EnemyListCastBars.Windows;
using KamiToolKit;

namespace EnemyListCastBars;

/// <summary>
/// Entry point of the plugin.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/enemycastbars";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem = new("EnemyListCastBars");
    private readonly ConfigWindow configWindow;

    private bool kamiToolKitReady;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IAddonLifecycle addonLifecycle,
        ICommandManager commandManager,
        IDataManager dataManager,
        IGameGui gameGui,
        IPluginLog pluginLog)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        this.Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _ = this.InitializeKamiToolKitAsync(pluginInterface, pluginLog);

        this.Controller = new EnemyListCastBarController(
            addonLifecycle,
            gameGui,
            dataManager,
            pluginLog,
            this.Configuration,
            () => this.kamiToolKitReady);

        this.configWindow = new ConfigWindow(this);
        this.windowSystem.AddWindow(this.configWindow);

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Enemy List Cast Bars settings.",
        });

        pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += this.OpenConfigUi;
    }

    public Configuration Configuration { get; }

    public EnemyListCastBarController Controller { get; }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenConfigUi;

        this.commandManager.RemoveHandler(CommandName);
        this.Controller.Dispose();

        this.windowSystem.RemoveAllWindows();
    }

    /// <summary>Persists the current <see cref="Configuration"/> to disk.</summary>
    public void SaveConfiguration() => this.pluginInterface.SavePluginConfig(this.Configuration);

    private async Task InitializeKamiToolKitAsync(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        try
        {
            await KamiToolKitLibrary.InitializeAsync(pluginInterface, "Enemy List Cast Bars").ConfigureAwait(false);
            this.kamiToolKitReady = true;
        }
        catch (Exception exception)
        {
            pluginLog.Error(exception, "Failed to initialize KamiToolKit, enemy list cast bars stay disabled for this session.");
        }
    }

    private void OpenConfigUi() => this.configWindow.Toggle();

    private void OnCommand(string command, string arguments) => this.OpenConfigUi();
}
