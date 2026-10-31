using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;

namespace Mountbook.UI;

/// <summary>
/// Mountbook's palette, read from the Glasswork <see cref="Look"/> the host owns.
/// </summary>
/// <remarks>
/// N9: colour comes from the Look, never from literals. One assignment to <c>Plugin.Host.Look</c>
/// restyles every widget; this facade is the slot-yield bridge that gives the hand-painted parts
/// (window style push, toasts, rules) the same tokens the <c>Gw</c> widgets use, and it must be
/// rebound when the Look changes - never read per frame.
/// </remarks>
public static class Theme
{
    /// <summary>The current Look, bound at load and again whenever the user switches it.</summary>
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

    /// <summary>Structural accent: the Look's accent, used for selection and focus.</summary>
    public static Vector4 Accent { get; private set; }

    /// <summary>Brighter accent for the one emphasised figure on a row.</summary>
    public static Vector4 AccentBright { get; private set; }

    public static Vector4 Warn { get; private set; }

    public static Vector4 Danger { get; private set; }

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

        Warn = look.Warning.ToVector4();
        Danger = look.Destructive.ToVector4();
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
}
