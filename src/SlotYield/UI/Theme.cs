using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlotYield.UI;

public static class Theme
{
    public static readonly Vector4 Background = new(0.035f, 0.047f, 0.063f, 0.98f);
    public static readonly Vector4 Panel = new(0.063f, 0.082f, 0.106f, 1f);
    public static readonly Vector4 PanelHover = new(0.09f, 0.115f, 0.145f, 1f);
    public static readonly Vector4 Border = new(1f, 1f, 1f, 0.07f);
    public static readonly Vector4 Text = new(0.85f, 0.90f, 0.94f, 1f);
    public static readonly Vector4 TextDim = new(0.56f, 0.62f, 0.68f, 1f);
    public static readonly Vector4 TextFaint = new(0.38f, 0.43f, 0.49f, 1f);
    public static readonly Vector4 Accent = new(0.36f, 0.85f, 0.67f, 1f);
    public static readonly Vector4 Gold = new(0.89f, 0.73f, 0.38f, 1f);
    public static readonly Vector4 Warn = new(0.92f, 0.67f, 0.30f, 1f);
    public static readonly Vector4 Danger = new(0.89f, 0.39f, 0.43f, 1f);
    public static readonly Vector4 Hq = new(0.55f, 0.74f, 0.96f, 1f);

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
