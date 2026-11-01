using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

public sealed class MountProgressTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(1, 2, 50)]
    [InlineData(700, 700, 100)]
    [InlineData(1, 3, 33)]
    [InlineData(3, 4, 75)]
    [InlineData(5, 0, 0)]
    [InlineData(5, -1, 0)]
    public void PercentRoundsDownAndNeverDividesByZero(int owned, int total, int expected)
    {
        Assert.Equal(expected, MountProgress.Percent(owned, total));
    }

    [Fact]
    public void RefreshToastNamesBothCounts() =>
        Assert.Equal("Refreshed: 342 of 369 collected", MountProgress.RefreshToast(342, 369));

    [Fact]
    public void RefreshToastIsHonestWhenNothingIsCollected() =>
        Assert.Equal("Refreshed: 0 of 12 collected", MountProgress.RefreshToast(0, 12));

    [Fact]
    public void RefreshToastIsHonestBeforeTheCatalogIsBuilt() =>
        Assert.Equal("Refreshed: 0 of 0 collected", MountProgress.RefreshToast(0, 0));

    [Fact]
    public void RefreshToastIsHonestWhenEverythingIsCollected() =>
        Assert.Equal("Refreshed: 369 of 369 collected", MountProgress.RefreshToast(369, 369));
}
