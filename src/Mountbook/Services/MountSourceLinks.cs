using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// Turns a "How to obtain" line into something the player can open, when the dataset says the line
/// names a real record.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1): the caller opens the URL.
/// </para>
/// <para>
/// The dataset attaches <c>relatedType</c>/<c>relatedId</c> to exactly the rows that name a duty, a quest
/// or an achievement - 140 of 423 lines - and leaves them empty for prose ("At level 30 or above, speak
/// with the Wandering Minstrel..."). That split is the rule used here: a row with a record is a title the
/// wiki has a page for, and a row without one is a sentence that would land on a missing page. Linking
/// the prose would look like a feature and behave like a 404.
/// </para>
/// <para>
/// The page is Gamer Escape, the same wiki the detail pane already links to for the mount itself, so a
/// source link cannot rot differently from the link above it. Third-party databases were considered and
/// left out: their item/quest routes are hash fragments this plugin cannot verify, and a deep link that
/// silently lands on an empty shell is worse than the wiki page.
/// </para>
/// </remarks>
public static class MountSourceLinks
{
    /// <summary>The related kinds the dataset uses and the wiki has pages for.</summary>
    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "Quest",
        "Instance",
        "Achievement",
    };

    /// <summary>True when <paramref name="line"/> names a record the wiki can be asked about.</summary>
    /// <param name="line">The source line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is null.</exception>
    public static bool CanLink(MountSourceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.RelatedId != 0
            && !string.IsNullOrWhiteSpace(line.Detail)
            && KnownKinds.Contains(line.RelatedKind);
    }

    /// <summary>
    /// The wiki page for the record <paramref name="line"/> names, or null when the line is prose rather
    /// than a record.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is null.</exception>
    public static string? Url(MountSourceLine line) =>
        CanLink(line) ? MountWiki.Url(line.Detail) : null;
}
