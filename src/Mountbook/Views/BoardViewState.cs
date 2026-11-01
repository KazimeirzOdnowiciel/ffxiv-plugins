using System.Collections.Generic;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>
/// Everything the wanted board draws: its rows, the order they are in, and the moment the ages are
/// measured from.
/// </summary>
/// <remarks>
/// The view reads and writes only this object (T6, T7), which is what lets the headless preview draw the
/// board against a golden. The clock is read by the window and handed in, because a view that asked the
/// wall clock itself could not be compared with an image.
/// </remarks>
public sealed class BoardViewState
{
    /// <summary>The rows to draw, already filtered and ordered by <see cref="WantedBoard.Build"/>.</summary>
    public IReadOnlyList<BoardRow> Rows { get; set; } = [];

    /// <summary>Which order the rows are in. The view can change it; the window persists the change.</summary>
    public MountBoardSort Sort { get; set; }

    /// <summary>Unix seconds now, so a row's record can say how long ago it changed.</summary>
    public long NowUnix { get; set; }
}
