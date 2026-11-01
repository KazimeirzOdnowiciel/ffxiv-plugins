using System;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The main window at a maximised size: the same sample mounts as <c>main</c>, in a 1920x1080 window,
/// so the layout is checked where the player actually runs it rather than only at 1040 px.
/// </summary>
/// <remarks>
/// Every earlier Golden in this project was between 600 and 1090 px wide, while the live window is
/// 1949x1084 and the whole 0.2.0 milestone is density work - card pitch, column widths, where a header
/// action lands. Those are exactly the things that break in the *other* direction at a wide size: the
/// per-expansion strip and the four chrome buttons share the header, the grid gains columns, and the
/// filter column's fixed share leaves the detail pane a different width. A 1040-only set cannot see any
/// of it.
/// <para>
/// It draws the state <see cref="MountbookPreview.CreateState"/> builds, so the two scenes differ only
/// in window size - a diff between <c>main</c> and <c>wide</c> is a diff caused by width alone.
/// </para>
/// </remarks>
public sealed class WidePreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state = MountbookPreview.CreateState();

    /// <summary>Also the golden's file name: <c>goldens/wide.png</c>.</summary>
    public override string Name => "wide";

    /// <summary>The client's own window size on a 3440x1440 display (see <c>STATE.md</c>).</summary>
    public override Vector2 WindowSize => new(1920f, 1080f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}
