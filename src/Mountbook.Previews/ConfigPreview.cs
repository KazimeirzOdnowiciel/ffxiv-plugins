using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Glasswork.Backend.Preview;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The settings window. Until this existed the config UI was the only surface with no golden, so a
/// padding or wording regression in it was invisible to the gate.
/// </summary>
public sealed class ConfigPreview : IPreviewScreen
{
    private readonly ConfigView view = new();

    private readonly ConfigViewState state = new()
    {
        ProgressText = "136 of 369 mounts collected.",
        ProgressFraction = 136f / 369f,
        CatalogText = "369 mounts, 356 with obtain data",
        VersionText = "v0.1.0.0",
        ThemeLabels = ["Eorzea", "Graphite", "Parchment"],
        ThemeNames = ["eorzea", "Graphite", "Parchment"],
        ThemeIndex = 0,
        FollowGameTheme = false,
        ThemesDirectory = @"C:\Users\you\AppData\Roaming\XIVLauncher\pluginConfigs\Mountbook\themes",
    };

    /// <summary>Also the golden's file name: <c>goldens/config.png</c>.</summary>
    public string Name => "config";

    /// <summary>The window's default size, so the golden matches what a first-time user sees.</summary>
    public Vector2 WindowSize => new(560f, 900f);

    public Look Look { get; } = Look.Eorzea();

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
}
