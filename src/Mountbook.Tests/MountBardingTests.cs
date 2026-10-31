using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// Barding support, read from the four gear-slot columns: a slot the mount accepts is
/// <c>0x10000 | variant</c>, and a slot it does not is zero. The bit test is the point - a small
/// non-zero variant id must not read as "this mount wears barding".
/// </summary>
public sealed class MountBardingTests
{
    [Fact]
    public void ACompanyChocoboWearsBarding() =>
        Assert.True(MountBarding.Supports(65536u, 65536u, 65537u, 0u));

    [Fact]
    public void ALegacyChocoboWearsItInEverySlot() =>
        Assert.True(MountBarding.Supports(65537u, 65537u, 65537u, 65537u));

    [Fact]
    public void AMountWithNoGearSlotsDoesNot() =>
        Assert.False(MountBarding.Supports(0u, 0u, 0u, 0u));

    [Fact]
    public void ASmallVariantIdIsNotASlot()
    {
        // The failure this prevents: "any non-zero value means barding" would badge a mount whose
        // column happens to hold a plain variant number.
        Assert.False(MountBarding.IsSlot(1u));
        Assert.False(MountBarding.Supports(1u, 0u, 0u, 0u));
        Assert.False(MountBarding.Supports(0u, 7u, 0u, 0u));
    }

    [Fact]
    public void OneSlotIsEnoughInAnyPosition()
    {
        Assert.True(MountBarding.Supports(65536u, 0u, 0u, 0u));
        Assert.True(MountBarding.Supports(0u, 65536u, 0u, 0u));
        Assert.True(MountBarding.Supports(0u, 0u, 65537u, 0u));
        Assert.True(MountBarding.Supports(0u, 0u, 0u, 65536u));
    }

    [Fact]
    public void HigherVariantsStillCount()
    {
        Assert.True(MountBarding.IsSlot(0x10002u));
        Assert.True(MountBarding.IsSlot(uint.MaxValue));
    }
}
