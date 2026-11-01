using System;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Windows;

namespace Mountbook.Previews;

/// <summary>
/// One preview scene: a window's size, Look and script, plus the body it draws. Every scene in this
/// assembly derives from this rather than from <see cref="IPreviewScreen"/> directly, because
/// <see cref="Draw"/> is where the window's own panel is painted and a scene that skipped it would render
/// its content on transparency - the seam that put a checkerboard in the bottom-right corner of
/// <c>narrow.png</c> and <c>minimum.png</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IPreviewScreen"/> asks for "the window's content, background included: the window itself
/// draws nothing", and the preview's window is in fact created with <c>NoBackground</c>. So the background
/// is the scene's job, and in game it is <see cref="UI.GlassWindow.DrawGlass"/>: the Look's
/// <c>PanelMaterial</c> over the window's rect, rounded by the window's radius. <see cref="PreviewSurface"/>
/// is that call without a <c>Plugin</c> to ask for the host.
/// </para>
/// <para>
/// <see cref="Draw"/> is deliberately not virtual: the panel and the body are one action, in that order, and
/// a scene supplies only the second half. What a scene may vary is <see cref="SurfaceRadius"/>, because the
/// settings and wanted windows round their glass less than the browse window does.
/// </para>
/// </remarks>
public abstract class PreviewScene : IPreviewScreen
{
    /// <summary>The golden's file name without <c>.png</c>; <c>--scene</c> picks a scene by it.</summary>
    public abstract string Name { get; }

    /// <summary>The window's size in logical units; the image is this plus a backdrop margin on every side.</summary>
    public abstract Vector2 WindowSize { get; }

    /// <summary>The Look the scene is drawn with, which is the Look the plugin hands its host.</summary>
    public abstract Look Look { get; }

    /// <summary>Input the preview plays into the window; empty for a scene shown at rest.</summary>
    public virtual ReadOnlySpan<PreviewInput> Script => [];

    /// <summary>
    /// The corner radius of the panel behind the scene, in logical units. It is the radius of the window the
    /// scene stands in for (<see cref="MainWindow.SurfaceRadius"/> by default) rather than a preview constant,
    /// so a golden cannot show a frame the window does not draw.
    /// </summary>
    protected virtual float SurfaceRadius => MainWindow.SurfaceRadius;

    /// <summary>Paints the window's panel, then draws the scene into it. Sealed: see the remarks.</summary>
    public void Draw()
    {
        PreviewSurface.Draw(SurfaceRadius);
        DrawScene();
    }

    /// <summary>Draws everything inside the window: the view, and nothing of its background.</summary>
    protected abstract void DrawScene();
}
