using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The settings window. Until this existed the config UI was the only surface with no golden, so a
/// padding or wording regression in it was invisible to the gate.
/// </summary>
public sealed class ConfigPreview : IPreviewScreen
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
    public string Name => name;

    /// <summary>
    /// The window's default size, so the golden matches what a first-time user sees - grown once the
    /// reset-to-defaults row landed, because at 900 the About section's last button fell below the frame and a
    /// golden that clips the block it draws is not evidence that the block works.
    /// </summary>
    public Vector2 WindowSize => new(560f, 1060f);

    public Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw()
    {
        // The window paints its own glass before drawing the body (GlassWindow.DrawGlass). The preview
        // has to do the same, otherwise the golden shows the sections floating on transparency.
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var look = Look;
        var glass = Frame.Glass;

        drawList.PushClipRect(min, max, false);

        Paint.Material(
            drawList,
            Rect.FromSize(min, max - min),
            Corners.All(20f * Frame.Scale),
            Look.CornerStyle,
            in look.PanelMaterial,
            in look,
            glass,
            Frame.Scale);

        drawList.PopClipRect();

        view.Draw(state);
    }

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
/// the wanted list, the grind records and the watches with it, so the sentence, the two answers and the way
/// they fit need an image of their own - the default <c>config</c> scene shows the button that opens it and
/// nothing else about it.
/// </summary>
public sealed class ConfigResetPreview : IPreviewScreen
{
    private readonly ConfigPreview scene;

    /// <summary>Fills the confirmation with counts a player would recognise, so the sentence is real.</summary>
    public ConfigResetPreview()
    {
        ConfigViewState state = ConfigPreview.CreateState();
        state.ResetImpact = new ConfigResetImpact(12, 3, 1);
        state.ResetConfirmOpen = true;

        scene = new ConfigPreview("config-reset", state);
    }

    /// <summary>The golden's file name: <c>goldens/config-reset.png</c>.</summary>
    public string Name => scene.Name;

    public Vector2 WindowSize => scene.WindowSize;

    public Look Look => scene.Look;

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => scene.Draw();
}
