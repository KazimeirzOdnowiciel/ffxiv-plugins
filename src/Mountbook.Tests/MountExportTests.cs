using System;
using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The "copy missing" export: what goes on the clipboard, in what order, and the toast wording.
/// </summary>
public sealed class MountExportTests
{
    [Fact]
    public void MissingNamesKeepsListOrderAndSkipsOwned()
    {
        var text = MountExport.MissingNames(Shelf(
            ("Alpha", true),
            ("Beta", false),
            ("Gamma", true),
            ("Delta", false)));

        Assert.Equal("Beta\nDelta", text);
    }

    [Fact]
    public void MissingNamesIsEmptyWhenEverythingIsCollected()
    {
        Assert.Equal(string.Empty, MountExport.MissingNames(Shelf(("Alpha", true))));
    }

    [Fact]
    public void MissingNamesIsEmptyForAnEmptyList()
    {
        Assert.Equal(string.Empty, MountExport.MissingNames([]));
    }

    [Fact]
    public void MissingNamesRejectsNull() => Assert.Throws<ArgumentNullException>(() => MountExport.MissingNames(null!));

    [Theory]
    [InlineData(0, "Nothing missing here.")]
    [InlineData(1, "Copied 1 missing mount.")]
    [InlineData(3, "Copied 3 missing mounts.")]
    public void MissingSummaryCountsOnlyUncollected(int missing, string expected)
    {
        var items = new List<MountShelfItem>
        {
            Item("Owned", true),
        };

        for (var index = 0; index < missing; index++)
        {
            items.Add(Item($"Missing {index}", false));
        }

        Assert.Equal(expected, MountExport.MissingSummary(items));
    }

    private static List<MountShelfItem> Shelf(params (string Name, bool Owned)[] rows)
    {
        var items = new List<MountShelfItem>(rows.Length);

        foreach ((string name, bool owned) in rows)
        {
            items.Add(Item(name, owned));
        }

        return items;
    }

    private static MountShelfItem Item(string name, bool owned) =>
        new(1u, name, 0u, 0u, 1, string.Empty, owned, string.Empty, string.Empty, string.Empty, []);
}
