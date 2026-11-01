using System;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The main window in the light variant of the Look the plugin ships: the same sample mounts as
/// <c>main</c>, so the image differs by theme alone.
/// </summary>
/// <remarks>
/// The other twenty-three scenes render <see cref="Look.Eorzea"/> and nothing else, which means a change to the
/// Glasswork Look - a lighter label, a card fill that stops separating from the background - can leave every
/// golden green while the half of the player base running the light theme reads grey on grey. The preview tool
/// has a <c>--light</c> switch, but it does not apply with <c>--assembly</c> ("a screen brings its own size, Look
/// and script"), so the variant is a scene: <see cref="Look.EorzeaLight"/>, the light face of the same family
/// the plugin actually sets, not a Look assembled by hand.
/// </remarks>
public sealed class LightPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state = MountbookPreview.CreateState();

    /// <summary>Also the golden's file name: <c>goldens/light.png</c>.</summary>
    public override string Name => "light";

    /// <summary>The flagship window size, so a diff against <c>main</c> can only be the Look.</summary>
    public override Vector2 WindowSize => new(1040f, 660f);

    public override Look Look { get; } = MountLook.Dense(Look.EorzeaLight());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>
/// The main window with the Look's accessibility switches on: increased contrast and reduced transparency,
/// which is the variant a player with low vision or a washed-out screen runs.
/// </summary>
/// <remarks>
/// Same argument as <see cref="LightPreview"/> - the tool's <c>--contrast</c> cannot be applied to an assembly,
/// so the variant has to be a scene - and the same two switches that switch turns on: <see cref="Look.IncreaseContrast"/>
/// and <see cref="Look.ReduceTransparency"/> on the shipped Look, with every colour, size and space token copied
/// unchanged. That is the point: the goldens are proving the *components* honour the switches (opaque fills,
/// panel frames, the thicker focus ring, no sheen), not that a hand-picked palette can be made to look right
/// (N9). The plugin has no contrast toggle of its own; a theme JSON can set both switches, and a Look change
/// that broke legibility under them would otherwise reach only the players already struggling.
/// </remarks>
public sealed class ContrastPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state = MountbookPreview.CreateState();

    /// <summary>Also the golden's file name: <c>goldens/contrast.png</c>.</summary>
    public override string Name => "contrast";

    /// <summary>The flagship window size, so a diff against <c>main</c> can only be the Look.</summary>
    public override Vector2 WindowSize => new(1040f, 660f);

    public override Look Look { get; } = Accessible(MountLook.Dense(Look.Eorzea()));

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);

    /// <summary>
    /// The same Look with the accessibility switches on. Both are flags the components read while drawing, so
    /// nothing is re-coloured here - flipping them is the whole change, and hand-tuning a fill would be the
    /// hand-picked colour N9 forbids.
    /// </summary>
    /// <param name="look">The Look the plugin ships, already densified by <see cref="MountLook.Dense"/>.</param>
    /// <returns>A copy of <paramref name="look"/> with <c>IncreaseContrast</c> and <c>ReduceTransparency</c> on.</returns>
    private static Look Accessible(Look look)
    {
        Look accessible = look.Clone();
        accessible.IncreaseContrast = true;
        accessible.ReduceTransparency = true;
        return accessible;
    }
}
