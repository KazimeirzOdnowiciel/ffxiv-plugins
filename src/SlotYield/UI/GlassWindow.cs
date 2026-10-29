using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Glasswork;
using Glasswork.Backend;

namespace SlotYield.UI;

/// <summary>
/// Shared shell for Slot Yield's windows: no ImGui background, because the glass is the
/// background, the Look's colours pushed over ImGui's stock style, and one
/// <see cref="Glasswork.Dalamud.GlassworkHost.DrawWindowBackground"/> call per frame with the
/// rounding the glass follows (N9, N11).
/// </summary>
/// <remarks>
/// ImGui's stock frame/checkbox/combo colours are what made the window read as a debug tool, and
/// pushing them here rather than in one window is what lets the Look restyle the whole plugin.
/// The push and pop counts are constants on purpose: an imbalance trips an ImGui assertion.
/// </remarks>
public abstract class GlassWindow : Window
{
    /// <summary>How many <c>ImGuiCol</c> entries <see cref="PreDraw"/> pushes.</summary>
    private const int PushedColors = 30;

    /// <summary>How many style vars <see cref="PreDraw"/> pushes, rounding included.</summary>
    private const int PushedVars = 6;

    private readonly Plugin plugin;

    protected GlassWindow(Plugin plugin, string title, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        : base(title, flags | ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
    }

    /// <summary>Set once a draw has failed, so a persistent fault is logged on the transition (N4).</summary>
    private bool drawFailureLogged;

    /// <summary>
    /// Logs a window's draw failure. A body that throws must not leave the Look's style push on
    /// ImGui's stack, and must not log every frame while the fault persists.
    /// </summary>
    protected void ReportDrawFailure(System.Exception exception)
    {
        if (this.drawFailureLogged)
            return;

        this.drawFailureLogged = true;
        Plugin.Log.Error(exception, "[SlotYield] A window failed to draw.");
    }

    /// <summary>Clears the failure flag once a draw succeeds again.</summary>
    protected void ReportDrawRecovered()
    {
        if (!this.drawFailureLogged)
            return;

        this.drawFailureLogged = false;
        Plugin.Log.Debug("[SlotYield] A window recovered after a draw failure.");
    }

    /// <summary>The pane's corner radius in logical units; the glass follows it.</summary>
    protected virtual float Radius => 14f;

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, this.Radius * this.plugin.Host.Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Theme.TextFaint);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Background);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Border);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.PanelHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.PanelHover);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.PanelHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.WithAlpha(Theme.Accent, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.WithAlpha(Theme.Accent, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Lerp(Theme.PanelRaised, Theme.Accent, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Lerp(Theme.PanelRaised, Theme.Accent, 0.42f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Theme.WithAlpha(Theme.Background, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.WithAlpha(Theme.Text, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.WithAlpha(Theme.Text, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.WithAlpha(Theme.Accent, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Lerp(Theme.PanelRaised, Theme.Accent, 0.50f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Separator, Theme.Hairline);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.Hairline);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, Theme.Border);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Theme.WithAlpha(Theme.Background, 0f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, Theme.WithAlpha(Theme.Text, 0.030f));

        base.PreDraw();
    }

    public override void PostDraw()
    {
        base.PostDraw();

        ImGui.PopStyleColor(PushedColors);
        ImGui.PopStyleVar(PushedVars);
    }

    /// <summary>
    /// Paints the Look's own panel material behind this window's body, below its title bar. Call as
    /// the first thing in Draw, after the window has begun.
    /// </summary>
    /// <remarks>
    /// This deliberately does not use <c>GlassworkHost.DrawWindowBackground</c>: that builds its
    /// material from <c>GlassMaterial.ForAppearance</c>, so every Look gets the same pane and
    /// switching themes leaves the window background unchanged. <c>Paint.Material</c> with
    /// <c>look.PanelMaterial</c> is the theme-aware path (and handles solid Looks, whose background
    /// this window no longer lets ImGui paint because of <c>NoBackground</c>). The glass falls back
    /// to a flat fill when the device is not ready or the GPU budget is spent.
    /// </remarks>
    protected void DrawGlass()
    {
        var look = this.plugin.Host.Look;
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        // The title bar is ImGui's and was already drawn with TitleBg; the panel sits under the body.
        var bodyMin = new Vector2(min.X, min.Y + ImGui.GetFrameHeight());
        if (max.Y <= bodyMin.Y || max.X <= bodyMin.X)
            return;

        drawList.PushClipRect(bodyMin, max, false);

        Paint.Material(
            drawList,
            Rect.FromSize(min, max - min),
            Corners.All(this.Radius * Frame.Scale),
            look.CornerStyle,
            in look.PanelMaterial,
            in look,
            Frame.Glass,
            Frame.Scale);

        drawList.PopClipRect();
    }
}
