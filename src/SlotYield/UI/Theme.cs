using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlotYield.UI;

public enum ThemeVariant
{
    Graphite,
    Parchment,
}

/// <summary>
/// Slot Yield's palette: graphite panels, warm ledger text, brass as structure.
/// Brass is the game's own market/gil colour, so the window reads as part of FFXIV rather
/// than as a generic dark dashboard; green appears only on profit figures, never as chrome.
/// The palette is swappable at runtime so a light "parchment" variant can be offered.
/// </summary>
public static class Theme
{
    public static Vector4 Background { get; private set; }
    public static Vector4 Panel { get; private set; }
    public static Vector4 PanelHover { get; private set; }
    public static Vector4 PanelRaised { get; private set; }

    public static Vector4 Border { get; private set; }
    public static Vector4 Hairline { get; private set; }

    public static Vector4 Text { get; private set; }
    public static Vector4 TextDim { get; private set; }
    public static Vector4 TextFaint { get; private set; }

    /// <summary>Structural accent: brass, used for selection, focus and the slot strip.</summary>
    public static Vector4 Accent { get; private set; }

    /// <summary>Brighter brass for the one emphasised figure on a row.</summary>
    public static Vector4 AccentBright { get; private set; }

    public static Vector4 Gold { get; private set; }

    /// <summary>Profit only. Never used for chrome.</summary>
    public static Vector4 Profit { get; private set; }

    public static Vector4 Warn { get; private set; }
    public static Vector4 Danger { get; private set; }
    public static Vector4 Hq { get; private set; }

    static Theme() => Apply(ThemeVariant.Graphite);

    public static void Apply(ThemeVariant variant)
    {
        if (variant == ThemeVariant.Parchment)
        {
            Background = new Vector4(0.925f, 0.905f, 0.865f, 0.98f);
            Panel = new Vector4(0.965f, 0.945f, 0.905f, 1f);
            PanelHover = new Vector4(0.895f, 0.870f, 0.820f, 1f);
            PanelRaised = new Vector4(0.835f, 0.810f, 0.760f, 1f);
            Border = new Vector4(0.250f, 0.220f, 0.180f, 0.22f);
            Hairline = new Vector4(0.250f, 0.220f, 0.180f, 0.12f);
            Text = new Vector4(0.140f, 0.130f, 0.110f, 1f);
            TextDim = new Vector4(0.360f, 0.340f, 0.300f, 1f);
            TextFaint = new Vector4(0.500f, 0.470f, 0.420f, 1f);
            Accent = new Vector4(0.630f, 0.460f, 0.130f, 1f);
            AccentBright = new Vector4(0.780f, 0.600f, 0.200f, 1f);
            Gold = new Vector4(0.700f, 0.540f, 0.180f, 1f);
            Profit = new Vector4(0.180f, 0.450f, 0.220f, 1f);
            Warn = new Vector4(0.620f, 0.400f, 0.100f, 1f);
            Danger = new Vector4(0.620f, 0.170f, 0.150f, 1f);
            Hq = new Vector4(0.240f, 0.360f, 0.600f, 1f);
            return;
        }

        Background = new Vector4(0.0549f, 0.0706f, 0.0863f, 0.97f);
        Panel = new Vector4(0.0784f, 0.1020f, 0.1255f, 1f);
        PanelHover = new Vector4(0.1020f, 0.1333f, 0.1686f, 1f);
        PanelRaised = new Vector4(0.1216f, 0.1569f, 0.1922f, 1f);
        Border = new Vector4(0.902f, 0.855f, 0.776f, 0.10f);
        Hairline = new Vector4(0.902f, 0.855f, 0.776f, 0.06f);
        Text = new Vector4(0.902f, 0.882f, 0.839f, 1f);
        TextDim = new Vector4(0.680f, 0.655f, 0.600f, 1f);
        TextFaint = new Vector4(0.520f, 0.500f, 0.460f, 1f);
        Accent = new Vector4(0.788f, 0.635f, 0.153f, 1f);
        AccentBright = new Vector4(0.890f, 0.780f, 0.400f, 1f);
        Gold = new Vector4(0.878f, 0.745f, 0.353f, 1f);
        Profit = new Vector4(0.498f, 0.690f, 0.412f, 1f);
        Warn = new Vector4(0.851f, 0.635f, 0.294f, 1f);
        Danger = new Vector4(0.706f, 0.333f, 0.306f, 1f);
        Hq = new Vector4(0.561f, 0.663f, 0.851f, 1f);
    }

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
