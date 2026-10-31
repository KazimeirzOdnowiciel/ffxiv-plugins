using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>One line of the "recently collected" digest: which mount, and how long ago it was first seen.</summary>
/// <param name="Id">The mount's id.</param>
/// <param name="Name">The display name, from the catalog.</param>
/// <param name="FirstSeenUnixSeconds">When the book first saw this mount as collected.</param>
/// <param name="When">The age as it is printed: <c>today</c>, <c>yesterday</c>, <c>3 days ago</c>.</param>
public sealed record RecentCollectedLine(uint Id, string Name, long FirstSeenUnixSeconds, string When);

/// <summary>
/// Turns the persisted recent-collected map into something a player can read, newest first.
/// </summary>
/// <remarks>
/// Pure and Dalamud-free: the caller passes the clock (T1). The ages are whole days since the mount was
/// first seen rather than calendar-day boundaries - "yesterday" means "between one and two days ago", which
/// is what a player means by it and what a test can pin without a time zone.
/// </remarks>
public static class MountRecentDigest
{
    private const long SecondsPerDay = 86_400L;

    /// <summary>
    /// The mounts collected inside the window, newest first, with their ages. Ids the catalog no longer
    /// carries are skipped rather than printed as a number.
    /// </summary>
    /// <param name="recent">The persisted id → first-seen map.</param>
    /// <param name="items">The catalog, used only to resolve names.</param>
    /// <param name="nowUnixSeconds">The current time, in unix seconds.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static IReadOnlyList<RecentCollectedLine> Build(
        IReadOnlyDictionary<uint, long> recent,
        IReadOnlyList<MountShelfItem> items,
        long nowUnixSeconds)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(items);

        var names = new Dictionary<uint, string>(items.Count);

        foreach (MountShelfItem item in items)
        {
            names[item.Id] = item.Name;
        }

        var lines = new List<RecentCollectedLine>(recent.Count);

        foreach (KeyValuePair<uint, long> entry in recent)
        {
            if (names.TryGetValue(entry.Key, out string? name))
            {
                lines.Add(new RecentCollectedLine(entry.Key, name, entry.Value, Age(nowUnixSeconds, entry.Value)));
            }
        }

        // Newest first, then by id: the map's order is not guaranteed, and two mounts collected in the same
        // second must print in the same order on every run.
        lines.Sort((left, right) =>
        {
            int byTime = right.FirstSeenUnixSeconds.CompareTo(left.FirstSeenUnixSeconds);
            return byTime != 0 ? byTime : left.Id.CompareTo(right.Id);
        });

        return lines;
    }

    /// <summary>The one-line heading, e.g. <c>3 mounts collected in the last two weeks</c>.</summary>
    /// <param name="count">How many mounts the digest carries.</param>
    public static string Summary(int count) =>
        count == 1
            ? "1 mount collected in the last two weeks"
            : string.Concat(
                count.ToString(CultureInfo.InvariantCulture),
                " mounts collected in the last two weeks");

    /// <summary>How the age is printed. A stamp in the future reads as today rather than as a negative.</summary>
    /// <param name="nowUnixSeconds">The current time, in unix seconds.</param>
    /// <param name="firstSeen">When the mount was first seen, in unix seconds.</param>
    public static string Age(long nowUnixSeconds, long firstSeen)
    {
        long elapsed = nowUnixSeconds - firstSeen;

        if (elapsed < SecondsPerDay)
        {
            return "today";
        }

        long days = elapsed / SecondsPerDay;

        return days == 1
            ? "yesterday"
            : string.Concat(days.ToString(CultureInfo.InvariantCulture), " days ago");
    }
}
