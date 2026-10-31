using System;
using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The recently-collected bookkeeping: the first run is a baseline, later runs mark additions, old or lost
/// entries drop out, and the map stays bounded.
/// </summary>
public sealed class MountRecentTests
{
    private const long Now = 1_000_000_000L;
    private const long Day = 86_400L;

    [Fact]
    public void FirstRunIsOnlyABaseline()
    {
        // Null, not an empty set: the player has never been looked at, so nothing may be marked.
        var recent = MountRecent.Reconcile(
            null,
            new HashSet<uint> { 1u, 2u, 3u },
            new Dictionary<uint, long>(),
            Now,
            out var knownChanged,
            out var recentChanged);

        Assert.Empty(recent);
        Assert.True(knownChanged);
        Assert.False(recentChanged);
    }

    [Fact]
    public void NewlyCollectedMountsAreMarked()
    {
        var recent = MountRecent.Reconcile(
            new HashSet<uint> { 1u, 2u },
            new HashSet<uint> { 1u, 2u, 3u },
            new Dictionary<uint, long>(),
            Now,
            out var knownChanged,
            out var recentChanged);

        Assert.Equal(new[] { 3u }, recent.Keys);
        Assert.Equal(Now, recent[3u]);
        Assert.True(knownChanged);
        Assert.True(recentChanged);
    }

    [Fact]
    public void EntriesOlderThanTheWindowAreDropped()
    {
        var recent = MountRecent.Reconcile(
            new HashSet<uint> { 1u, 2u },
            new HashSet<uint> { 1u, 2u },
            new Dictionary<uint, long> { [1u] = Now - (15 * Day), [2u] = Now - Day },
            Now,
            out _,
            out var recentChanged);

        Assert.Equal(new[] { 2u }, recent.Keys);
        Assert.True(recentChanged);
    }

    [Fact]
    public void UncollectedMountsLeaveTheRecentSet()
    {
        var recent = MountRecent.Reconcile(
            new HashSet<uint> { 1u },
            new HashSet<uint>(),
            new Dictionary<uint, long> { [1u] = Now - Day },
            Now,
            out _,
            out _);

        Assert.Empty(recent);
    }

    [Fact]
    public void NothingChangesWhenNothingChanged()
    {
        var known = new HashSet<uint> { 1u, 2u };
        var stored = new Dictionary<uint, long> { [1u] = Now - Day };

        var recent = MountRecent.Reconcile(known, known, stored, Now, out var knownChanged, out var recentChanged);

        Assert.False(knownChanged);
        Assert.False(recentChanged);
        Assert.Equal(stored, recent);
    }

    [Fact]
    public void TheMapStaysAtCapacityNewestFirst()
    {
        var known = new HashSet<uint>();
        var owned = known;

        for (uint id = 1; id <= MountRecent.Capacity + 5; id++)
        {
            owned.Add(id);
        }

        // oldest first, so trimming must keep the highest ids
        var stored = new Dictionary<uint, long>();
        for (var index = 0; index < owned.Count; index++)
        {
            stored[(uint)(index + 1)] = Now - (long)(owned.Count - index);
        }

        var recent = MountRecent.Reconcile(known, owned, stored, Now, out _, out _);

        Assert.Equal(MountRecent.Capacity, recent.Count);
        Assert.DoesNotContain(1u, recent.Keys);
        Assert.Contains((uint)(MountRecent.Capacity + 5), recent.Keys);
    }

    [Fact]
    public void AnEmptyBaselineStillMarksTheFirstMount()
    {
        // A fresh character owns nothing on the first look. That is a baseline, not the absence of one,
        // and the first mount they earn is exactly the mount this feature exists to celebrate.
        var recent = MountRecent.Reconcile(
            new HashSet<uint>(),
            new HashSet<uint> { 42u },
            new Dictionary<uint, long>(),
            Now,
            out var knownChanged,
            out var recentChanged);

        Assert.Equal(Now, recent[42u]);
        Assert.True(knownChanged);
        Assert.True(recentChanged);
    }

    [Fact]
    public void AStoredMapOverTheCapIsTrimmedAndReportedAsAChange()
    {
        var stored = new Dictionary<uint, long>();
        var owned = new HashSet<uint>();

        for (uint id = 1; id <= MountRecent.Capacity + 6; id++)
        {
            stored[id] = Now - Day;
            owned.Add(id);
        }

        var recent = MountRecent.Reconcile(owned, owned, stored, Now, out _, out var recentChanged);

        // The cap is enforced even when nothing else moved: comparing the untrimmed map against the
        // stored one reported "unchanged" and the caller then persisted nothing, so 30 entries stayed.
        Assert.Equal(MountRecent.Capacity, recent.Count);
        Assert.True(recentChanged);
    }

    [Fact]
    public void TrimWithEqualTimestampsIsDeterministic()
    {
        // Several mounts collected in the same second is the normal case; the old unstable sort could
        // keep a different subset on every launch, so the New markers flapped.
        var stored = new Dictionary<uint, long>();
        var owned = new HashSet<uint>();

        for (uint id = 1; id <= MountRecent.Capacity + 6; id++)
        {
            stored[id] = Now - Day;
            owned.Add(id);
        }

        var first = MountRecent.Reconcile(owned, owned, stored, Now, out _, out _);
        var second = MountRecent.Reconcile(owned, owned, stored, Now, out _, out _);

        Assert.Equal(SortedKeys(first), SortedKeys(second));
        Assert.Equal(1u, SortedKeys(first)[0]);
        Assert.Equal((uint)MountRecent.Capacity, SortedKeys(first)[^1]);
    }

    private static uint[] SortedKeys(Dictionary<uint, long> map)
    {
        var keys = new uint[map.Count];
        map.Keys.CopyTo(keys, 0);
        Array.Sort(keys);
        return keys;
    }
}
