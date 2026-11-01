using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The ownership chips with the counts a full book carries - three hundred mounts, two hundred and thirty-three
/// of them unowned - at the window's 820x520 minimum.
/// </summary>
/// <remarks>
/// <para>
/// Every other scene counts the twelve-mount sample, so its widest chip is "Unowned 3" (38 px) and the row has
/// never had to hold a label that grew with a count. The real book's "Unowned 233" is 26 px wider, at 64.5, and
/// the row used to pick its shape from a fixed 380 px column rather than from that label: at a 380 px column
/// the control measured 247 px, its cells 61.7, and the 4 px inset either side left 57.7 of ink for a 64.5 px
/// label. This image is the tripwire - the widest count the book can show, drawn whole at the tightest window.
/// </para>
/// <para>
/// The header agrees with the chips (67 of 300 collected, 233 still missing) because a scene whose counters
/// contradicted its filters would be a picture of a state that cannot exist.
/// </para>
/// </remarks>
public sealed class ChipCountsPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public ChipCountsPreview()
    {
        state = ChipCounts.State();
    }

    /// <summary>Also the golden's file name: <c>goldens/chipcounts.png</c>.</summary>
    public override string Name => "chipcounts";

    /// <summary>The smallest window the plugin allows: the tightest column the chips have to fit.</summary>
    public override Vector2 WindowSize => new(820f, 520f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>
/// The same counts in a column too narrow to hold them, where the row changes shape: one picker instead of four
/// clipped chips.
/// </summary>
/// <remarks>
/// The fallback exists for a long translation at a small window, and it is the branch this measurement made
/// rarer: the four English labels need 284 px, while a language whose widest chip is 100 px wide needs 424 -
/// more than the 328 px column the 620-wide <c>narrow</c> scene gives it. The window here is 520 wide, below the
/// 820 the plugin enforces, exactly as <c>narrow</c> is: the point of the image is the control's shape at its
/// own limit, which no other golden draws.
/// </remarks>
public sealed class ChipComboPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public ChipComboPreview()
    {
        state = ChipCounts.State();
    }

    /// <summary>Also the golden's file name: <c>goldens/chipcombo.png</c>.</summary>
    public override string Name => "chipcombo";

    /// <summary>Narrow enough that the measured row no longer fits: a 266 px column against a 284 px row.</summary>
    public override Vector2 WindowSize => new(520f, 700f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>The state both chip-count scenes draw, so the two images differ only in the width they draw into.</summary>
internal static class ChipCounts
{
    /// <summary>A book of 300 mounts with 233 unowned, laid over the frozen twelve-mount sample.</summary>
    internal static MountViewState State()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();

        return new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            RarestMissing = MountRarityInsight.Find(items),

            // The book's own shape, in MountOwnership order: All, Owned, Unowned, Wanted.
            OwnershipCounts = [300, 67, 233, 12],
            Owned = 67,
            Total = 300,
            SelectedId = items[0].Id,
            SourceOptions = MountbookPreview.SourceOptions(items),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(items),
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
        };
    }
}
