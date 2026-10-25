using System.Numerics;
using Dalamud.Bindings.ImGui;
using DutyRecap.Detection;

namespace DutyRecap.UI;

public static class Theme
{
    public static readonly Vector4 Background = new(0.043f, 0.051f, 0.067f, 0.98f);
    public static readonly Vector4 Panel = new(0.082f, 0.094f, 0.118f, 1f);
    public static readonly Vector4 PanelHover = new(0.118f, 0.134f, 0.165f, 1f);
    public static readonly Vector4 Border = new(1f, 1f, 1f, 0.07f);
    public static readonly Vector4 Text = new(0.88f, 0.90f, 0.94f, 1f);
    public static readonly Vector4 TextDim = new(0.55f, 0.59f, 0.66f, 1f);
    public static readonly Vector4 TextFaint = new(0.38f, 0.41f, 0.47f, 1f);
    public static readonly Vector4 Gold = new(0.87f, 0.72f, 0.35f, 1f);
    public static readonly Vector4 Green = new(0.42f, 0.78f, 0.50f, 1f);
    public static readonly Vector4 Danger = new(0.90f, 0.35f, 0.40f, 1f);

    public static readonly Vector4 Tank = new(0.38f, 0.58f, 0.85f, 1f);
    public static readonly Vector4 Healer = new(0.36f, 0.76f, 0.55f, 1f);
    public static readonly Vector4 Dps = new(0.86f, 0.42f, 0.42f, 1f);

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static uint WithAlpha(Vector4 color, float alpha) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, color.W * alpha));

    public static Vector4 RoleColor(uint jobId) =>
        Detectors.IsTankJob(jobId) ? Tank :
        Detectors.IsHealerJob(jobId) ? Healer : Dps;

    public static Vector4 GradeColor(string grade) => grade switch
    {
        "S" => Gold,
        "A" => Green,
        "B" => new Vector4(0.42f, 0.66f, 0.94f, 1f),
        "C" => new Vector4(0.88f, 0.52f, 0.26f, 1f),
        _ => Danger,
    };
}
