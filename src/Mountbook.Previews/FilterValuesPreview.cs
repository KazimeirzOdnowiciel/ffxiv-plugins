using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The filter block with a real value in every combo, at the window's 820x520 minimum - the narrowest column
/// that still pairs two controls on a row, so the tightest field the labels have to survive.
/// </summary>
/// <remarks>
/// <para>
/// Every other scene draws the filters at their defaults ("All expansions", "Any source (12)", "Sort: Name",
/// "No grouping"), which is the short case. The combos are label-free now and their entries carry their own
/// dimension, so the long case - "Sort: Commonest first", "Group: Patch", a source with a name and a count in
/// it - is exactly the text that decides whether the pair split works, and it was reachable in no golden.
/// </para>
/// <para>
/// At scale 1 the filter column measures 438 px here, so each field is 215 px wide. "Sort: Commonest first"
/// (180.9 px) and "V&amp;C Dungeon (1)" both draw whole; the longest source name in the taxonomy ("Cosmic
/// Exploration", 206 px without its prefix) is the one that would clip at this size and fit at the default
/// window. That is what this image pins; a field that loses its whole value here is a regression.
/// </para>
/// <para>
/// The list is the scene's frozen sample, not the result of these filters - the subject is the filter block's
/// own text at its tightest width, and a preview never runs the query.
/// </para>
/// </remarks>
public sealed class FilterValuesPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public FilterValuesPreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        foreach (MountShelfItem item in items)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            RarestMissing = MountRarityInsight.Find(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = items[0].Id,
            SourceOptions = MountbookPreview.SourceOptions(items),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(items),

            // The one source type the sample carries two of, so the entry shows a name and a count together.
            SourceFilter = MountSourceKinds.Parse("V&C Dungeon"),
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.CommonestFirst,
            GroupBy = MountGrouping.Patch,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/filters.png</c>.</summary>
    public override string Name => "filters";

    /// <summary>The window's enforced minimum: the narrowest column that still pairs the combos.</summary>
    public override Vector2 WindowSize => new(820f, 520f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>
/// A control scene: the Cards/Table switch now shares its row with the search field, so a press on its "Table"
/// cell has to reach the switch and not the field underneath the pointer.
/// </summary>
/// <remarks>
/// <para>
/// The block was seven full-width rows, where every control owned its whole row and a hit test could not
/// go wrong. Sharing a row means the field, the switch and the gutter between them have to agree about where
/// one ends and the other starts, and a golden of the drawn rows proves only the pixels, not the hit test: a
/// switch drawn in the right place over a field whose item rect runs past it looks identical and does nothing
/// when clicked. The frame this golden keeps is the last one, so the image is Table selected and the table
/// drawn: both the press and the mode change are in it.
/// </para>
/// <para>
/// The scripted point is 597,191 because a <see cref="PreviewInput"/> is not in ImGui's screen space: measured
/// with a probe that wrote <c>ImGui.GetIO().MousePos</c> per frame, an input of (597,191) reaches the pointer at
/// (621,215) - 24 px lower and righter - and the switch's item rect is 553..645 by 193..237 under the default
/// font the compare run uses (576..645 by 181..225 under the system font a plain <c>--scene</c> run uses).
/// 621,215 is inside both, which is what makes the one script work in either mode. Press alone was not enough:
/// the segmented control commits on the release.
/// </remarks>
public sealed class SwitchPressPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public SwitchPressPreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        foreach (MountShelfItem item in items)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            RarestMissing = MountRarityInsight.Find(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SourceOptions = MountbookPreview.SourceOptions(items),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(items),
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/switchtap.png</c>.</summary>
    public override string Name => "switchtap";

    /// <summary>The default window, where the switch sits beside the search field.</summary>
    public override Vector2 WindowSize => new(1040f, 660f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    /// <summary>A press on the "Table" cell, then a move away so the hover ease is not in the image.</summary>
    private static readonly PreviewInput[] ScriptInputs =
    [
        new(2, PreviewInputKind.Move, new Vector2(597f, 191f)),
        new(4, PreviewInputKind.Press, new Vector2(597f, 191f)),
        new(5, PreviewInputKind.Release, new Vector2(597f, 191f)),
        new(20, PreviewInputKind.Move, new Vector2(20f, 600f)),
    ];

    public override ReadOnlySpan<PreviewInput> Script => ScriptInputs;

    protected override void DrawScene() => view.Draw(state);
}
