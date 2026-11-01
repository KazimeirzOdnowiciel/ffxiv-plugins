using System.Globalization;

namespace Mountbook.Services;

/// <summary>The collection counter shared by the header and the detail pane.</summary>
public static class MountProgress
{
    /// <summary>
    /// The integer percentage of the collection owned. A non-positive total is zero rather than a
    /// division by zero, so the window can draw before the catalog is built.
    /// </summary>
    public static int Percent(int owned, int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return (int)((long)owned * 100 / total);
    }

    /// <summary>
    /// The one-line report a forced re-read puts in a toast, e.g. <c>Refreshed: 342 of 369 collected</c>.
    /// </summary>
    /// <remarks>
    /// Kept here rather than inline in the window so the wording is testable without a window: the
    /// Refresh action only exists to say something the two-second tick cannot, and a number the player
    /// can hold against the game is that something.
    /// </remarks>
    public static string RefreshToast(int owned, int total)
    {
        return string.Concat(
            "Refreshed: ",
            owned.ToString(CultureInfo.InvariantCulture),
            " of ",
            total.ToString(CultureInfo.InvariantCulture),
            " collected");
    }
}
