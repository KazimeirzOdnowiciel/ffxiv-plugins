using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;

namespace SlotYield.UI;

public enum ThemeVariant
{
    Graphite,
    Parchment,
}

/// <summary>
/// Slot Yield's palette, read from the Glasswork <see cref="Look"/> the host owns.
/// </summary>
/// <remarks>
/// N9: colour comes from the Look, never from literals. One assignment to
/// <c>Plugin.Host.Look</c> restyles every widget, and this facade keeps the plugin's long-standing
/// <c>Theme.*</c> names working on top of that, so the ~700 existing call sites did not have to
/// change. The brass-on-graphite palette is preserved by the Eorzea Look, which is the game's own
/// market/gil styling: gold accent, bronze hairlines, warm off-white ink.
/// </remarks>
public static class Theme
{
    /// <summary>The current Look, bound once by <see cref="Bind"/> at load and when the user switches.</summary>
    public static Look Look { get; private set; } = Look.Eorzea();

    public static Vector4 Background { get; private set; }
    public static Vector4 Panel { get; private set; }
    public static Vector4 PanelHover { get; private set; }
    public static Vector4 PanelRaised { get; private set; }

    public static Vector4 Border { get; private set; }
    public static Vector4 Hairline { get; private set; }

    public static Vector4 Text { get; private set; }
    public static Vector4 TextDim { get; private set; }
    public static Vector4 TextFaint { get; private set; }

    /// <summary>Structural accent: the Look's accent, used for selection, focus and the slot strip.</summary>
    public static Vector4 Accent { get; private set; }

    /// <summary>Brighter accent for the one emphasised figure on a row.</summary>
    public static Vector4 AccentBright { get; private set; }

    public static Vector4 Gold { get; private set; }

    /// <summary>Profit only. Never used for chrome.</summary>
    public static Vector4 Profit { get; private set; }

    public static Vector4 Warn { get; private set; }
    public static Vector4 Danger { get; private set; }
    public static Vector4 Hq { get; private set; }

    /// <summary>
    /// Rebinds every token to <paramref name="look"/>. Call when the Look changes, never per frame.
    /// </summary>
    public static void Bind(Look look)
    {
        Look = look;

        Background = look.Background.ToVector4();
        Panel = look.SecondaryBackground.ToVector4();
        PanelHover = look.Fill.ToVector4();
        PanelRaised = look.TertiaryBackground.ToVector4();

        Border = look.Separator.ToVector4();
        Hairline = look.Separator.WithAlpha(look.Separator.A * 0.55f).ToVector4();

        Text = look.Label.ToVector4();
        TextDim = look.SecondaryLabel.ToVector4();
        TextFaint = look.TertiaryLabel.ToVector4();

        Accent = look.Accent.ToVector4();
        AccentBright = look.Accent.Lighten(0.18f).ToVector4();
        Gold = look.Accent.ToVector4();

        Profit = look.Success.ToVector4();
        Warn = look.Warning.ToVector4();
        Danger = look.Destructive.ToVector4();
        Hq = look.Info.ToVector4();
    }

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    /// <summary>Adapter for the <c>Gw</c> widgets, which take a <see cref="Glasswork.Color"/>.</summary>
    public static Glasswork.Color ToColor(Vector4 color) => new(color.X, color.Y, color.Z, color.W);

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
