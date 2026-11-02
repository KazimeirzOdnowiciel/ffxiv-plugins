using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The table parked part-way down its list, with no heading touched: the picture the reader is looking at
/// when they decide to sort, and the one that has to show them a heading to click.
/// </summary>
/// <remarks>
/// <para>
/// This pair exists because every other table scene draws the list at scroll 0, where the heading is on
/// screen whatever the table does with its scroll - so none of them could show the defect this one exists
/// for. On the old shape the header row was an ordinary first row of a table 11852 px tall, drawn inside
/// the <c>##mounts</c> child: scrolling the list took the heading off the top with the rows, and a column
/// could only be clicked at the very top of the list. Here the wheel parks the table mid-book and the
/// heading is still at the top of its rect, over rows that have moved.
/// </para>
/// <para>
/// <see cref="TableScrollSortedPreview"/> is the matching half: the same park, then a click on Seats, which
/// is only reachable because the heading stayed where it is. Both images put the header row in the same
/// place and the first visible row on the same pixel - a sort moves the rows and keeps the scroll - so the
/// second image is also the measurement that a sort leaves the reader's place alone.
/// </para>
/// <para>
/// The clicks are the same coordinates <see cref="SortClickBook"/> measured for the header row of this
/// window's table, which is the point: the heading does not move, so neither does its click target. On the
/// old build the same click landed on a row instead - it selected a mount and reordered nothing - which is
/// what makes these goldens fail there rather than pass for the wrong reason.
/// </para>
/// <para>
/// Nothing here reads the game, the config or the clock, so the images hold from run to run.
/// </para>
/// </remarks>
public sealed class TableScrollPreview : TableScrollScene
{
    /// <summary>Also the golden's file name: <c>goldens/tablescroll.png</c>.</summary>
    public override string Name => "tablescroll";

    /// <inheritdoc/>
    protected override bool Sorts => false;
}

/// <summary>
/// The same parked table after a click on the Seats heading: rows reordered under the frozen header, the
/// caret on Seats, and the list's scroll untouched.
/// </summary>
/// <remarks>
/// Seats is the click that the sibling card <c>t_91edd54b</c> was about - it used to re-lay the columns out
/// as well as reorder the rows - so this image holds both rules at once: the columns are where the untouched
/// image has them, and the heading row was reachable at a scroll the old shape could not reach it from.
/// </remarks>
public sealed class TableScrollSortedPreview : TableScrollScene
{
    /// <summary>Also the golden's file name: <c>goldens/tablescroll-sorted.png</c>.</summary>
    public override string Name => "tablescroll-sorted";

    /// <inheritdoc/>
    protected override bool Sorts => true;
}

/// <summary>
/// The book, the window, and the parked-scroll script both scenes share.
/// </summary>
/// <remarks>
/// The state is <see cref="SortClickScene"/>'s: the same long book, the same one table id and the same
/// header coordinates, so the four images read as one table in four states rather than as four tables. What
/// this pair adds is the wheel: one turn of eight notches over the body parks the list 640 px down, which is
/// the park the client readback measured the defect at.
/// </remarks>
public abstract class TableScrollScene : PreviewScene
{
    /// <summary>The one book both scenes draw.</summary>
    private static readonly IReadOnlyList<MountShelfItem> Book = SortClickBook.Build();

    private readonly MountbookView view = new();

    private readonly MountViewState state;

    /// <summary>The order the drawn rows were built for, so a click's new order is what the next frame draws.</summary>
    private MountOrder drawnOrder;

    /// <summary>Builds the state: the whole book, the table, the name order, one row selected.</summary>
    protected TableScrollScene()
    {
        state = new MountViewState
        {
            Items = Query(MountOrder.Name),
            Expansions = MountBreakdown.ByExpansion(Book),
            RarestMissing = MountRarityInsight.Find(Book),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name).OwnershipCounts(Book, null),
            Prices = SortClickBook.Prices,
            Owned = SortClickBook.Owned,
            Total = Book.Count,
            SelectedId = Book[0].Id,
            ItemName = "Aithon Whistle",
            SourceOptions = MountbookPreview.SourceOptions(Book),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(Book),
            Order = MountOrder.Name,
            ViewMode = MountViewMode.Table,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
        };

        drawnOrder = state.Order;
    }

    /// <summary>Whether this scene clicks the Seats heading after parking the list.</summary>
    protected abstract bool Sorts { get; }

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    /// <summary>The window <see cref="SortClickScene"/> is read in, so the header coordinates are its.</summary>
    public override Vector2 WindowSize => new(1180f, 820f);

    /// <summary>The wheel first, then the Seats click when <see cref="Sorts"/>, and the pointer off the window.</summary>
    public override ReadOnlySpan<PreviewInput> Script => Sorts ? ParkAndClick : ParkOnly;

    protected override void DrawScene()
    {
        // The window re-queries when the view reports a new order; the preview does the same here, so the
        // rows a click asks for are the rows the next frame draws.
        if (state.Order != drawnOrder)
        {
            state.Items = Query(state.Order);
            drawnOrder = state.Order;
        }

        view.Draw(state);
    }

    /// <summary>The book filtered and ordered the way the window would, prices included for the Price column.</summary>
    /// <param name="order">The order the rows are drawn in.</param>
    private static IReadOnlyList<MountShelfItem> Query(MountOrder order) =>
        new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, order).Apply(Book, prices: SortClickBook.Prices);

    /// <summary>
    /// The wheel over the body: eight notches down is the 640 px park the client measured the defect at, and
    /// they are one event rather than a scroll per frame so the position does not depend on how many frames
    /// the script spends turning.
    /// </summary>
    private static readonly PreviewInput[] ParkOnly =
    [
        new(2, PreviewInputKind.Move, SortClickBook.Body),
        new(4, PreviewInputKind.Wheel, SortClickBook.Body, Wheel: -8f),
        new(10, PreviewInputKind.Move, SortClickBook.Away),

        // The run lasts until the last input's frame, and every spring in the picture - the header's hover,
        // the caret's turn - needs the quiet before it is taken.
        new(70, PreviewInputKind.Move, SortClickBook.Away),
    ];

    /// <summary>The parked list, then the Seats heading - which is only reachable because it did not move.</summary>
    private static readonly PreviewInput[] ParkAndClick =
    [
        new(2, PreviewInputKind.Move, SortClickBook.Body),
        new(4, PreviewInputKind.Wheel, SortClickBook.Body, Wheel: -8f),
        new(10, PreviewInputKind.Move, SortClickBook.SeatsHeader),
        new(12, PreviewInputKind.Press, SortClickBook.SeatsHeader),
        new(13, PreviewInputKind.Release, SortClickBook.SeatsHeader),
        new(20, PreviewInputKind.Move, SortClickBook.Away),
        new(70, PreviewInputKind.Move, SortClickBook.Away),
    ];
}
