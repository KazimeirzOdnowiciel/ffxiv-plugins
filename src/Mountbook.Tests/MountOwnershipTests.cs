using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The ownership filter is persisted as an enum on disk, so every member - including the newer
/// <see cref="MountOwnership.Wanted"/> - has to survive the same migration path (CONTRIBUTING N5).
/// <c>Configuration.Migrate</c> runs exactly this rule on the value it read back.
/// </summary>
public sealed class MountOwnershipTests
{
    [Theory]
    [InlineData(MountOwnership.All)]
    [InlineData(MountOwnership.Owned)]
    [InlineData(MountOwnership.Unowned)]
    [InlineData(MountOwnership.Wanted)]
    public void EveryKnownValueSurvivesMigration(MountOwnership value)
    {
        Assert.Equal(value, MountOwnershipRules.Normalize(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void AnUnknownPersistedValueMigratesToAll(int persisted)
    {
        Assert.Equal(MountOwnership.All, MountOwnershipRules.Normalize((MountOwnership)persisted));
    }

    [Fact]
    public void WantedIsAppendedSoPersistedNumbersDoNotShift()
    {
        // The config stores the enum as its number; inserting a member anywhere but the end would
        // silently reinterpret every existing user's saved filter.
        Assert.Equal(0, (int)MountOwnership.All);
        Assert.Equal(1, (int)MountOwnership.Owned);
        Assert.Equal(2, (int)MountOwnership.Unowned);
        Assert.Equal(3, (int)MountOwnership.Wanted);
    }
}
