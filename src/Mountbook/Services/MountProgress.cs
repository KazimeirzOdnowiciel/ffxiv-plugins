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
}
