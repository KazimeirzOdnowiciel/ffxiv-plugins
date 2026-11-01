using System;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;
using Mountbook.Windows;

namespace Mountbook.Previews;

/// <summary>
/// The settings window. Until this existed the config UI was the only surface with no golden, so a
/// padding or wording regression in it was invisible to the gate.
/// </summary>
public sealed class ConfigPreview : PreviewScene
{
    private readonly ConfigView view = new();

    private readonly ConfigViewState state;

    private readonly string name;

    /// <summary>Draws the window as a first-time user sees it: every control closed, nothing pending.</summary>
    public ConfigPreview()
        : this("config", CreateState())
    {
    }

    /// <summary>Draws the same window from another state, for the scenes that prove one of its branches.</summary>
    /// <param name="name">The scene name, which is also the golden's file name.</param>
    /// <param name="state">What the body draws.</param>
    internal ConfigPreview(string name, ConfigViewState state)
    {
        this.name = name;
        this.state = state;
    }

    /// <summary>Also the golden's file name: <c>goldens/config.png</c>.</summary>
    public override string Name => name;

    /// <summary>
    /// The window's default size, so the golden matches what a first-time user sees - grown once the
    /// reset-to-defaults row landed, because at 900 the About section's last button fell below the frame and a
    /// golden that clips the block it draws is not evidence that the block works.
    /// </summary>
    public override Vector2 WindowSize => new(560f, 1060f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    /// <summary>The settings window rounds its glass to 20, not the browse window's 22.</summary>
    protected override float SurfaceRadius => ConfigWindow.SurfaceRadius;

    /// <summary>Draws the body into the panel <see cref="PreviewScene.Draw"/> painted.</summary>
    protected override void DrawScene() => DrawBody();

    /// <summary>The settings sections themselves, without the panel behind them.</summary>
    internal void DrawBody() => view.Draw(state);

    /// <summary>The sample settings window every config scene starts from.</summary>
    internal static ConfigViewState CreateState() => new()
    {
        ProgressText = "136 of 369 mounts collected.",
        ProgressFraction = 136f / 369f,
        CatalogText = "369 mounts, 356 with obtain data",
        DatasetText = "ffxivcollect.com, 2026-11-01, schema v3",
        DatasetSource = "ffxivcollect.com",
        VersionText = "v0.1.0.0",
        ThemeLabels = ["Eorzea", "Graphite", "Parchment"],
        ThemeNames = ["eorzea", "Graphite", "Parchment"],
        ThemeIndex = 0,
        FollowGameTheme = false,
        ThemesDirectory = @"C:\Users\you\AppData\Roaming\XIVLauncher\pluginConfigs\Mountbook\themes",
    };
}

/// <summary>
/// The reset-to-defaults confirmation, drawn open. It is the one branch of the settings window that can take
/// the wanted list, the grind records, the watches and the saved views with it, so the sentence, the two answers and the way
/// they fit need an image of their own - the default <c>config</c> scene shows the button that opens it and
/// nothing else about it.
/// </summary>
public sealed class ConfigResetPreview : PreviewScene
{
    private readonly ConfigPreview scene;

    /// <summary>Fills the confirmation with counts a player would recognise, so the sentence is real.</summary>
    public ConfigResetPreview()
    {
        ConfigViewState state = ConfigPreview.CreateState();
        state.ResetImpact = new ConfigResetImpact(12, 3, 1, 2);
        state.ResetConfirmOpen = true;

        scene = new ConfigPreview("config-reset", state);
    }

    /// <summary>The golden's file name: <c>goldens/config-reset.png</c>.</summary>
    public override string Name => scene.Name;

    public override Vector2 WindowSize => scene.WindowSize;

    public override Look Look => scene.Look;

    public override ReadOnlySpan<PreviewInput> Script => [];

    /// <summary>The confirmation is the settings window's own, so it rounds and paints like one.</summary>
    protected override float SurfaceRadius => ConfigWindow.SurfaceRadius;

    protected override void DrawScene() => scene.DrawBody();
}
