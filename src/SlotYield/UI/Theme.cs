using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlotYield.UI;

/// <summary>
/// Slot Yield's palette: graphite panels, warm ledger text, brass as structure.
/// Brass is the game's own market/gil colour, so the window reads as part of FFXIV rather
/// than as a generic dark dashboard; green appears only on profit figures, never as chrome.
/// </summary>
public static class Theme
{
    public static readonly Vector4 Background = new(0.0549f, 0.0706f, 0.0863f, 0.97f);
    public static readonly Vector4 Panel = new(0.0784f, 0.1020f, 0.1255f, 1f);
    public static readonly Vector4 PanelHover = new(0.1020f, 0.1333f, 0.1686f, 1f);
    public static readonly Vector4 PanelRaised = new(0.1216f, 0.1569f, 0.1922f, 1f);

    public static readonly Vector4 Border = new(0.902f, 0.855f, 0.776f, 0.10f);
    public static readonly Vector4 Hairline = new(0.902f, 0.855f, 0.776f, 0.06f);

    public static readonly Vector4 Text = new(0.902f, 0.882f, 0.839f, 1f);
    public static readonly Vector4 TextDim = new(0.604f, 0.580f, 0.525f, 1f);
    public static readonly Vector4 TextFaint = new(0.420f, 0.404f, 0.369f, 1f);

    /// <summary>Structural accent: brass, used for selection, focus and the slot strip.</summary>
    public static readonly Vector4 Accent = new(0.788f, 0.635f, 0.153f, 1f);

    /// <summary>Brighter brass for the one emphasised figure on a row.</summary>
    public static readonly Vector4 AccentBright = new(0.890f, 0.780f, 0.400f, 1f);

    public static readonly Vector4 Gold = new(0.878f, 0.745f, 0.353f, 1f);

    /// <summary>Profit only. Never used for chrome.</summary>
    public static readonly Vector4 Profit = new(0.498f, 0.690f, 0.412f, 1f);

    public static readonly Vector4 Warn = new(0.851f, 0.635f, 0.294f, 1f);
    public static readonly Vector4 Danger = new(0.706f, 0.333f, 0.306f, 1f);
    public static readonly Vector4 Hq = new(0.561f, 0.663f, 0.851f, 1f);

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static uint WithAlpha(Vector4 color, float alpha) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, color.W * alpha));

    public static Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            a.Z + ((b.Z - a.Z) * t),
            a.W + ((b.W - a.W) * t));
    }

    public static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void Colored(uint color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void Dimmed(string text) => Colored(TextDim, text);

    public static void Faint(string text) => Colored(TextFaint, text);

    public static void Muted(string text) => Colored(TextDim, text);
}
