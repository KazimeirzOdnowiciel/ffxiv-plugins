using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>The "Newest first" order, and that it stays the mirror of "Oldest first".</summary>
public sealed class MountOrderTests
{
    [Fact]
    public void NewestFirstPutsTheHighestPatchFirst()
    {
        var rows = Apply(
            MountOrder.NewestFirst,
            Item("A", "2.1"),
            Item("B", "7.0"),
            Item("C", "6.5"));

        Assert.Equal(new[] { "B", "C", "A" }, Names(rows));
    }

    [Fact]
    public void OldestFirstIsTheMirror()
    {
        var rows = Apply(
            MountOrder.OldestFirst,
            Item("A", "2.1"),
            Item("B", "7.0"),
            Item("C", "6.5"));

        Assert.Equal(new[] { "A", "C", "B" }, Names(rows));
    }

    [Fact]
    public void EqualPatchesFallBackToTheName()
    {
        var rows = Apply(
            MountOrder.NewestFirst,
            Item("Zebra", "6.0"),
            Item("Alpha", "6.0"));

        Assert.Equal(new[] { "Alpha", "Zebra" }, Names(rows));
    }

    [Fact]
    public void EmptyPatchesSortAfterNumberedOnesInNewestFirst()
    {
        var rows = Apply(
            MountOrder.NewestFirst,
            Item("Unknown", string.Empty),
            Item("Known", "6.1"));

        Assert.Equal(new[] { "Known", "Unknown" }, Names(rows));
    }

    private static IReadOnlyList<MountShelfItem> Apply(MountOrder order, params MountShelfItem[] items) =>
        new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, order).Apply(items);

    private static string[] Names(IReadOnlyList<MountShelfItem> items)
    {
        var names = new string[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            names[index] = items[index].Name;
        }

        return names;
    }

    private static MountShelfItem Item(string name, string patch) =>
        new(1u, name, 0u, 0u, 1, patch, false, string.Empty, string.Empty, string.Empty, []);
}
