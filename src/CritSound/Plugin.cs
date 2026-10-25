using System;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CritSound;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/critsound";

    private readonly SoundBank sounds;
    private readonly ConfigWindow window;

    private long lastPlayedAt;

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Normalize();

        this.sounds = new SoundBank(PluginInterface, Log);
        this.window = new ConfigWindow(this);

        PluginInterface.UiBuilder.Draw += this.window.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Crit Sound settings.",
            ShowInHelp = true,
        });

        FlyTextGui.FlyTextCreated += this.OnFlyTextCreated;

        Log.Information("[CritSound] Loaded. {Count} sound(s) available.", this.sounds.Available.Count);
    }

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IFlyTextGui FlyTextGui { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    public static string DisplayName => "Crit Sound";

    public Configuration Configuration { get; }

    public SoundBank Sounds => this.sounds;

    public void Dispose()
    {
        FlyTextGui.FlyTextCreated -= this.OnFlyTextCreated;
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.window.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        this.sounds.Dispose();
    }

    public void OpenConfig() => this.window.IsOpen = true;

    public void SaveConfig() => PluginInterface.SavePluginConfig(this.Configuration);

    public void Preview(CritTrigger trigger)
    {
        var slot = this.Configuration.Slot(trigger);
        if (slot.File.Length > 0)
            this.sounds.Play(slot.File, slot.Volume);
    }

    private void OnCommand(string command, string args) => this.OpenConfig();

    private void OnFlyTextCreated(
        ref FlyTextKind kind,
        ref int val1,
        ref int val2,
        ref SeString text1,
        ref SeString text2,
        ref uint color,
        ref uint icon,
        ref uint damageTypeIcon,
        ref float yOffset,
        ref bool handled)
    {
        var config = this.Configuration;
        if (!config.Enabled || val1 <= 0)
            return;

        if (config.OnlyWhenGameUiVisible && GameGui.GameUiHidden)
            return;

        var isAutoOrDot = kind is FlyTextKind.AutoAttackOrDot
            or FlyTextKind.AutoAttackOrDotDh
            or FlyTextKind.AutoAttackOrDotCrit
            or FlyTextKind.AutoAttackOrDotCritDh;

        if (isAutoOrDot && !config.IncludeAutoAttacksAndDots)
            return;

        CritTrigger? trigger = kind switch
        {
            FlyTextKind.DamageCritDh or FlyTextKind.AutoAttackOrDotCritDh => CritTrigger.CritDirectHit,
            FlyTextKind.DamageCrit or FlyTextKind.AutoAttackOrDotCrit => CritTrigger.Crit,
            FlyTextKind.DamageDh or FlyTextKind.AutoAttackOrDotDh => CritTrigger.DirectHit,
            _ => null,
        };

        if (trigger is null)
            return;

        var slot = config.Slot(trigger.Value);
        if (!slot.Enabled || slot.File.Length == 0)
            return;

        var now = Environment.TickCount64;
        if (now - this.lastPlayedAt < config.MinIntervalMs)
            return;

        this.lastPlayedAt = now;
        this.sounds.Play(slot.File, slot.Volume);
    }
}
