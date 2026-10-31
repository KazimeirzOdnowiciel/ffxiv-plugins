using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mountbook.Services;

/// <summary>
/// Builds the text the "copy missing" action puts on the clipboard: one mount per line, in the order
/// the list is already in, so the result matches what the player is looking at.
/// </summary>
/// <remarks>
/// Kept Dalamud-free and pure so it can be tested without the game (T1): the window only decides
/// *when* to copy.
/// </remarks>
public static class MountExport
{
    /// <summary>
    /// The names of every uncollected mount in <paramref name="items"/>, one per line. An empty list
    /// (everything collected, or nothing on screen) produces an empty string rather than a blank line.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static string MissingNames(IReadOnlyList<MountShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var builder = new StringBuilder();

        for (var index = 0; index < items.Count; index++)
        {
            MountShelfItem item = items[index];

            if (item.Owned)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(item.Name);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A one-line summary for the toast after copying, e.g. <c>Copied 12 missing mounts.</c>, with the
    /// singular handled.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static string MissingSummary(IReadOnlyList<MountShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var missing = 0;

        for (var index = 0; index < items.Count; index++)
        {
            if (!items[index].Owned)
            {
                missing++;
            }
        }

        return missing switch
        {
            0 => "Nothing missing here.",
            1 => "Copied 1 missing mount.",
            _ => string.Concat(
                "Copied ",
                missing.ToString(CultureInfo.InvariantCulture),
                " missing mounts."),
        };
    }
}
