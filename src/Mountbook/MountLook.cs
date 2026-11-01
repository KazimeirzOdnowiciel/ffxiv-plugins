using System;
using Glasswork;

namespace Mountbook;

/// <summary>
/// The row metric every Mountbook surface draws with: the Look's list-row height, shortened for a table
/// that is one line of ink per row.
/// </summary>
/// <remarks>
/// <para>
/// Glasswork sizes a table row from <see cref="Look.RowHeight"/> alone - <c>Gw.TableRow</c> passes it as
/// the row's height and pads the cell by zero, so no cell work can make a row shorter. Eorzea sets that
/// token to 48 logical units, which is a <em>list</em> row: an icon beside a title and a subtitle. The
/// dense table is 13 px of ink and 369 rows deep, so the same metric drew 18 rows per screen and three
/// in a 660 px preview pane, more space than the facts in it.
/// </para>
/// <para>
/// The token is the frame's, not the table's - <c>Frame.Look</c> is set once per frame from the host's
/// Look and there is no per-widget override - so the density is applied to the Look the plugin hands its
/// host, and the previews apply it to the Look they render with. A golden that kept the old metric would
/// show rows nobody plays with, and the table is the surface this project has the most of.
/// </para>
/// <para>
/// Two consequences worth knowing: it overrides a theme's own <see cref="Look.RowHeight"/> (the colours
/// and every other token survive the clone), and it does not apply while the player has
/// <c>FollowGameTheme</c> on, because the host then builds its Look from the game's theme on every
/// restyle and a Look set from here is restyled back.
/// </para>
/// </remarks>
public static class MountLook
{
    /// <summary>
    /// Height of one row in the dense table, in logical units: five fit the 171 px pane the table gets
    /// in the 1040x660 preview, where 48 fitted three.
    /// </summary>
    public const float RowHeight = 32f;

    /// <summary>The same Look with Mountbook's row metric.</summary>
    /// <param name="look">The Look to copy, usually <see cref="Look.Eorzea()"/> or a named theme.</param>
    /// <returns>A clone of <paramref name="look"/> whose row height is <see cref="RowHeight"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="look"/> is null.</exception>
    public static Look Dense(Look look)
    {
        ArgumentNullException.ThrowIfNull(look);

        Look dense = look.Clone();
        dense.RowHeight = RowHeight;
        return dense;
    }
}
