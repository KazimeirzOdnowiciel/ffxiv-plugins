using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The sections the card grid draws. Two rules matter and both are easy to get wrong: a section never
/// reorders the mounts inside it (the query already decided that), and the sections themselves are
/// ordered by a fixed rule rather than by whichever mount happens to come first.
/// </summary>
public sealed class MountGroupsTests
{
    [Fact]
    public void NoGroupingIsOneUntitledSection()
    {
        MountShelfItem[] items = [Item(1, "Alpha", "7.0"), Item(2, "Beta", "2.0")];

        IReadOnlyList<MountGroup> groups = MountGroups.Build(items, MountGrouping.None);

        MountGroup group = Assert.Single(groups);
        Assert.Equal(string.Empty, group.Title);
        Assert.Equal(2, group.Items.Count);
    }

    [Fact]
    public void ExpansionSectionsFollowReleaseOrderNotTheListsOrder()
    {
        // The list is ordered by name here, so first-appearance order would give Dawntrail before
        // A Realm Reborn, which reads as a mistake rather than as a sort.
        MountShelfItem[] items =
        [
            Item(1, "Alpha", "7.0"),
            Item(2, "Beta", "2.0"),
            Item(3, "Gamma", "6.1"),
            Item(4, "Delta", "3.3"),
        ];

        IReadOnlyList<MountGroup> groups = MountGroups.Build(items, MountGrouping.Expansion);

        Assert.Equal(
            new[] { "A Realm Reborn", "Heavensward", "Endwalker", "Dawntrail" },
            Titles(groups));
    }

    [Fact]
    public void APatchlessMountGetsItsOwnSectionLast()
    {
        MountShelfItem[] items = [Item(1, "Alpha", string.Empty), Item(2, "Beta", "7.0")];

        IReadOnlyList<MountGroup> groups = MountGroups.Build(items, MountGrouping.Expansion);

        Assert.Equal(new[] { "Dawntrail", "Expansion unknown" }, Titles(groups));
    }

    [Fact]
    public void PatchSectionsAreNewestFirstWithTheUnrecordedOneLast()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha", "6.1"),
            Item(2, "Beta", string.Empty),
            Item(3, "Gamma", "7.05"),
            Item(4, "Delta", "7.0"),
        ];

        IReadOnlyList<MountGroup> groups = MountGroups.Build(items, MountGrouping.Patch);

        Assert.Equal(new[] { "7.05", "7.0", "6.1", MountGroups.NoPatch }, Titles(groups));
    }

    [Fact]
    public void SourceSectionsAreAlphabeticalAndTheSourcelessOneLast()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha", "7.0", "Trial"),
            Item(2, "Beta", "7.0", string.Empty),
            Item(3, "Gamma", "7.0", "achievement"),
        ];

        IReadOnlyList<MountGroup> groups = MountGroups.Build(items, MountGrouping.Source);

        Assert.Equal(new[] { "achievement", "Trial", MountGroups.NoSource }, Titles(groups));
    }

    [Fact]
    public void AGroupKeepsTheOrderTheQueryChose()
    {
        // "Cheapest first" must survive grouping: the section reorders nothing.
        MountShelfItem[] items = [Item(3, "Gamma", "7.0"), Item(1, "Alpha", "7.0"), Item(2, "Beta", "7.0")];

        MountGroup group = Assert.Single(MountGroups.Build(items, MountGrouping.Expansion));

        Assert.Equal(new[] { "Gamma", "Alpha", "Beta" }, Names(group.Items));
    }

    [Fact]
    public void EveryMountLandsInExactlyOneSection()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha", "2.0", "Quest"),
            Item(2, "Beta", "7.05", "Trial"),
            Item(3, "Gamma", string.Empty, string.Empty),
        ];

        foreach (MountGrouping grouping in new[]
        {
            MountGrouping.Expansion, MountGrouping.Patch, MountGrouping.Source,
        })
        {
            var seen = new List<uint>();

            foreach (MountGroup group in MountGroups.Build(items, grouping))
            {
                foreach (MountShelfItem item in group.Items)
                {
                    seen.Add(item.Id);
                }
            }

            // Grouping reorders across sections by design, so what has to hold is that nothing is lost
            // and nothing is duplicated - not the sequence.
            seen.Sort();
            Assert.Equal(new uint[] { 1, 2, 3 }, seen);
        }
    }

    [Fact]
    public void AnEmptyListIsOneEmptySection()
    {
        MountGroup group = Assert.Single(MountGroups.Build([], MountGrouping.Expansion));

        Assert.Empty(group.Items);
    }

    private static string[] Titles(IReadOnlyList<MountGroup> groups)
    {
        var titles = new string[groups.Count];

        for (var index = 0; index < groups.Count; index++)
        {
            titles[index] = groups[index].Title;
        }

        return titles;
    }

    private static string[] Names(IReadOnlyList<MountShelfItem> items)
    {
        var names = new string[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            names[index] = items[index].Name;
        }

        return names;
    }

    private static MountShelfItem Item(uint id, string name, string patch, string category = "")
    {
        IReadOnlyList<MountSourceLine> sources = category.Length == 0
            ? []
            : [new MountSourceLine(category, "some detail", string.Empty, 0)];

        return new MountShelfItem(id, name, 0u, 0u, 1, patch, false, string.Empty, string.Empty, string.Empty, sources);
    }
}
