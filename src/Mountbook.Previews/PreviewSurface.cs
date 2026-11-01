using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;

namespace Mountbook.Previews;

/// <summary>
/// Paints the window's own panel behind a preview scene, which every scene has to do before it draws a
/// view: <see cref="Glasswork.Backend.Preview.IPreviewScreen"/> asks for "the window's content, background
/// included" because the preview's window is created with <c>NoBackground</c> and draws nothing itself.
/// </summary>
/// <remarks>
/// <para>
/// In game that panel is <see cref="UI.GlassWindow.DrawGlass"/>: <c>Paint.Material</c> with the Look's
/// <c>PanelMaterial</c> over <c>GetWindowPos()</c>..<c>GetWindowPos() + GetWindowSize()</c>, rounded by the
/// window's own radius, clipped to the window. This is the same call without a <c>Plugin</c> to ask for the
/// host, which is what a preview has.
/// </para>
/// <para>
/// Skipping it does not draw a wrong background, it draws none: the region the card does not cover keeps
/// the frame buffer's alpha, so the preview composites its own backdrop there. That is invisible over most
/// of the procedural backdrop and it was not invisible at the bottom right, where the backdrop carries a
/// grey checker decal (a reference pattern for the glass blur, drawn by the previewer's
/// <c>BackdropImage.Procedural</c> over 56-96% of its width and 74-94% of its height). Below the flagship
/// size the card stops covering that corner, the card's shadow is all that is left, and the checker read as
/// "the window leaves alpha 0 in its padding" - the defect <c>narrow.png</c> and <c>minimum.png</c> were
/// re-baselined for. The pixels were never a rendering fault in the window; the scenes were not drawing the
/// window's background at all.
/// </para>
/// <para>
/// Two consequences worth keeping in mind. The radius is the <em>window's</em>
/// (<see cref="Windows.MainWindow.SurfaceRadius"/> and friends) rather than a number of the preview's, so a
/// golden cannot show a frame the window does not draw. And the panel is painted over the whole window rect
/// including the top padding, not from below a title bar: a preview window is created with
/// <c>NoTitleBar</c>, so it has no bar to start under.
/// </para>
/// </remarks>
internal static class PreviewSurface
{
    /// <summary>Paints the panel the window's own <c>DrawGlass</c> paints, at the window's current rect.</summary>
    /// <param name="radius">The scene's window radius in logical units, from that window's <c>SurfaceRadius</c>.</param>
    public static void Draw(float radius)
    {
        Look look = Frame.Look;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 min = ImGui.GetWindowPos();
        Vector2 max = min + ImGui.GetWindowSize();

        if (max.Y <= min.Y || max.X <= min.X)
        {
            return;
        }

        drawList.PushClipRect(min, max, false);

        Paint.Material(
            drawList,
            Rect.FromSize(min, max - min),
            Corners.All(radius * Frame.Scale),
            look.CornerStyle,
            look.PanelMaterial,
            look,
            Frame.Glass,
            Frame.Scale);

        drawList.PopClipRect();
    }
}
