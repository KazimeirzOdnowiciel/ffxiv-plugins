using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// The "recently collected" bookkeeping: which mounts were already owned the last time the window looked,
/// and when each newly collected one was first seen.
/// </summary>
/// <remarks>
/// Kept pure so it can be tested without the game (T1) - the wiring layer only decides *when* to call it and
/// persists the two sets. The first run must never mark the whole collection as recent: with no stored
/// snapshot the current collection becomes the baseline and nothing is marked.
/// </remarks>
public static class MountRecent
{
    /// <summary>How long a mount stays marked as recently collected.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    /// <summary>How many ids are remembered, newest first, so the config cannot grow without bound.</summary>
    public const int Capacity = 24;

    /// <summary>
    /// Reconciles the stored snapshot with what is owned now.
    /// </summary>
    /// <param name="known">
    /// The ids owned at the previous look, or <c>null</c> when there is no baseline yet. Null and an
    /// empty set are different answers: null means "never looked", so nothing may be marked; an empty
    /// set means "last time you owned nothing", so the next mount to arrive is genuinely new.
    /// </param>
    /// <param name="ownedNow">The ids owned now.</param>
    /// <param name="recent">The stored id → unix-seconds map of first-seen times.</param>
    /// <param name="nowUnixSeconds">The current time, in unix seconds.</param>
    /// <param name="knownChanged">True when the snapshot differs from the one passed in.</param>
    /// <param name="recentChanged">True when the recent map differs (new entries or pruned ones).</param>
    /// <returns>The recent map the caller should persist (a new dictionary).</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static Dictionary<uint, long> Reconcile(
        IReadOnlySet<uint>? known,
        IReadOnlySet<uint> ownedNow,
        IReadOnlyDictionary<uint, long> recent,
        long nowUnixSeconds,
        out bool knownChanged,
        out bool recentChanged)
    {
        ArgumentNullException.ThrowIfNull(ownedNow);
        ArgumentNullException.ThrowIfNull(recent);

        var next = new Dictionary<uint, long>(recent.Count + ownedNow.Count);
        var oldest = nowUnixSeconds - (long)Window.TotalSeconds;

        // Keep what is still inside the window and still owned.
        foreach (KeyValuePair<uint, long> entry in recent)
        {
            if (entry.Value >= oldest && ownedNow.Contains(entry.Key))
            {
                next[entry.Key] = entry.Value;
            }
        }

        // No baseline yet: everything owned becomes the baseline and nothing is "recent". Emptiness is
        // not the test - a player who owned nothing last time has a baseline, and their first mount is
        // the one case this bookkeeping exists for.
        if (known is not null)
        {
            foreach (uint id in ownedNow)
            {
                if (!known.Contains(id))
                {
                    next[id] = nowUnixSeconds;
                }
            }
        }

        // Trim first, then compare. Comparing before the cap made a stored map larger than Capacity
        // report "nothing changed", so the caller persisted nothing and the cap was never enforced -
        // the caller's extra Count check downstream was the symptom of this ordering.
        Dictionary<uint, long> trimmed = Trim(next);

        recentChanged = !SameEntries(trimmed, recent);
        knownChanged = known is null || !SameIds(known, ownedNow);

        return trimmed;
    }

    /// <summary>Whether two snapshots hold the same ids; null is handled by the caller.</summary>
    private static bool SameIds(IReadOnlySet<uint> left, IReadOnlySet<uint> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (uint id in right)
        {
            if (!left.Contains(id))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the trimmed map is exactly what the caller already has stored.</summary>
    private static bool SameEntries(
        IReadOnlyDictionary<uint, long> next,
        IReadOnlyDictionary<uint, long> stored)
    {
        if (next.Count != stored.Count)
        {
            return false;
        }

        foreach (KeyValuePair<uint, long> entry in next)
        {
            if (!stored.TryGetValue(entry.Key, out var stamp) || stamp != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Drops the oldest entries until at most <see cref="Capacity"/> remain.</summary>
    private static Dictionary<uint, long> Trim(Dictionary<uint, long> entries)
    {
        if (entries.Count <= Capacity)
        {
            return entries;
        }

        // Timestamp first, then id: List.Sort is not stable, so with equal stamps (a reconcile that
        // marks several mounts in the same second) two runs over the same input kept different ids and
        // the "New" set flapped between launches. The same total-order rule MountQuery follows.
        var ordered = new List<KeyValuePair<uint, long>>(entries);
        ordered.Sort((left, right) =>
        {
            int byTime = right.Value.CompareTo(left.Value);
            return byTime != 0 ? byTime : left.Key.CompareTo(right.Key);
        });

        var trimmed = new Dictionary<uint, long>(Capacity);

        for (var index = 0; index < Capacity; index++)
        {
            trimmed[ordered[index].Key] = ordered[index].Value;
        }

        return trimmed;
    }
}
