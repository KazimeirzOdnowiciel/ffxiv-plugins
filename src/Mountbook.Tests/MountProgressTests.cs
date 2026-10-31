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
}
