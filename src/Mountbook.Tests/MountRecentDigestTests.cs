using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The recently-collected digest: newest first, names resolved from the catalog, ages in whole days, and
/// a stable order when two mounts were collected in the same second.
/// </summary>
public sealed class MountRecentDigestTests
{
    private const long Now = 1_000_000_000L;
    private const long Day = 86_400L;

    [Fact]
    public void NewestFirstWithNamesFromTheCatalog()
    {
        var recent = new Dictionary<uint, long>
        {
            [1u] = Now - (10 * Day),
            [2u] = Now - (2 * Day),
        };

        IReadOnlyList<RecentCollectedLine> lines = MountRecentDigest.Build(recent, Items(), Now);

        Assert.Equal(new[] { "Lamp", "Chocobo" }, Names(lines));
    }

    [Fact]
    public void IdsTheCatalogNoLongerCarriesAreSkipped()
    {
        var recent = new Dictionary<uint, long> { [1u] = Now - Day, [999u] = Now - Day };

        IReadOnlyList<RecentCollectedLine> lines = MountRecentDigest.Build(recent, Items(), Now);

        RecentCollectedLine line = Assert.Single(lines);
        Assert.Equal("Chocobo", line.Name);
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(3600, "today")]
    [InlineData(Day, "yesterday")]
    [InlineData(Day + 3600, "yesterday")]
    [InlineData(3 * Day, "3 days ago")]
    [InlineData(13 * Day, "13 days ago")]
    public void AgesAreWholeDays(long elapsed, string expected) =>
        Assert.Equal(expected, MountRecentDigest.Age(Now, Now - elapsed));

    [Fact]
    public void AFutureStampReadsAsTodayRatherThanNegative()
    {
        Assert.Equal("today", MountRecentDigest.Age(Now, Now + (5 * Day)));
    }

    [Fact]
    public void EqualTimestampsPrintInTheSameOrderEveryRun()
    {
        var recent = new Dictionary<uint, long>
        {
            [2u] = Now,
            [1u] = Now,
        };

        Assert.Equal(new[] { "Chocobo", "Lamp" }, Names(MountRecentDigest.Build(recent, Items(), Now)));
        Assert.Equal(new[] { "Chocobo", "Lamp" }, Names(MountRecentDigest.Build(recent, Items(), Now)));
    }

    [Fact]
    public void TheSummaryCountsInWordsThatRead()
    {
        Assert.Equal("1 mount collected in the last two weeks", MountRecentDigest.Summary(1));
        Assert.Equal("4 mounts collected in the last two weeks", MountRecentDigest.Summary(4));
    }

    [Fact]
    public void AnEmptyMapIsAnEmptyDigest()
    {
        Assert.Empty(MountRecentDigest.Build(new Dictionary<uint, long>(), Items(), Now));
    }

    private static string[] Names(IReadOnlyList<RecentCollectedLine> lines)
    {
        var names = new string[lines.Count];

        for (var index = 0; index < lines.Count; index++)
        {
            names[index] = lines[index].Name;
        }

        return names;
    }

    private static IReadOnlyList<MountShelfItem> Items() =>
    [
        new(1u, "Chocobo", 0u, 0u, 1, "2.0", true, string.Empty, string.Empty, string.Empty, []),
        new(2u, "Lamp", 0u, 0u, 1, "7.45", true, string.Empty, string.Empty, string.Empty, []),
    ];
}
