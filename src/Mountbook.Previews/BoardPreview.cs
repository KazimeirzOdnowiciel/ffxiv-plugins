using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;
using Mountbook.Windows;

namespace Mountbook.Previews;

/// <summary>
/// The wanted board: the mounts the player starred and does not own, one row each, with the first source
/// line and the tracker's progress.
/// </summary>
/// <remarks>
/// The only golden that draws <see cref="WantedBoardView"/>. The rows come from the same twelve sample
/// mounts as the shelf goldens, with three of them starred and one already collected, so the golden also
/// proves that an owned wanted mount is not on the board.
/// </remarks>
public sealed class BoardPreview : PreviewScene
{
    private readonly WantedBoardView view = new();

    private readonly BoardViewState state;

    public BoardPreview()
    {
        IReadOnlyList<MountShelfItem> all = MountbookPreview.SampleMounts();

        // Every unowned sample mount is starred, plus one that is already collected: Auspicious Kamuy is
        // wanted and owned, so the golden proves a finished grind leaves the board.
        var wanted = new HashSet<uint> { 205u, 412u, 420u, 470u };

        var grinds = new Dictionary<uint, MountGrindRecord>
        {
            [412] = new() { Have = 30, Want = 100, Note = "Two more V&C runs", UpdatedAt = FixedNow - (3 * 3600) },
        };

        state = new BoardViewState
        {
            NowUnix = FixedNow,
            Sort = MountBoardSort.Patch,
            Rows = WantedBoard.Build(
                all,
                wanted,
                grinds,
                // Two priced rows and one that has never been asked about, so both the price line and its
                // "no price read" branch are in the image.
                id => id switch
                {
                    205u => 1_450_000,
                    412u => 2_999_999,
                    _ => 0,
                },
                MountBoardSort.Patch),
        };
    }

    /// <summary>A frozen clock: an age in the golden must not depend on when the preview ran.</summary>
    private const long FixedNow = 1_800_000_000;

    /// <summary>Also the golden's file name: <c>goldens/board.png</c>.</summary>
    public override string Name => "board";

    /// <summary>A control scene: a press on the "Cheapest" cell must move the pill and re-sort the rows.</summary>
    private static readonly PreviewInput[] ScriptInputs =
    [
        new(3, PreviewInputKind.Press, new Vector2(400f, 74f)),
        new(20, PreviewInputKind.Move, new Vector2(20f, 300f)),
    ];

    public override ReadOnlySpan<PreviewInput> Script => ScriptInputs;

    public override Vector2 WindowSize => new(560f, 620f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    /// <summary>The board is <c>WantedBoardWindow</c>, whose glass rounds to 20 rather than the browse 22.</summary>
    protected override float SurfaceRadius => WantedBoardWindow.SurfaceRadius;

    protected override void DrawScene() => view.Draw(state);
}

/// <summary>
/// The detail pane with the grind tracker filled in: the bar, the count, the two steppers and a note.
/// </summary>
/// <remarks>
/// Split from <c>main</c> rather than swapping that scene over, because the tracker only draws for a mount
/// that costs tokens (or one the player already tracked) and the main golden's selection costs none. The
/// selected mount is the sample with a token price in its sources, so the row is in exactly the place the
/// live window puts it: under the price line, above the source rows.
/// </remarks>
public sealed class GrindPreview : PreviewScene
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public GrindPreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            RarestMissing = MountRarityInsight.Find(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name).OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = 412u,
            ItemName = "Genie of the Lamp Whistle",
            MarketCanCheck = true,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
            CostText = "100 Corvosi Manuscripts - you have 1,900, 98,100 to go",
            GrindVisible = true,
            GrindHave = 30,
            GrindWant = 100,
            GrindNote = "Two more V&C runs this week",
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/grind.png</c>.</summary>
    public override string Name => "grind";

    public override Vector2 WindowSize => new(1040f, 660f);

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    public override ReadOnlySpan<PreviewInput> Script => [];

    protected override void DrawScene() => view.Draw(state);
}
