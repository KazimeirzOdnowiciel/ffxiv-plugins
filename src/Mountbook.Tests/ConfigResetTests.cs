using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The one rule the reset-to-defaults control is built on: it asks before it takes something the player
/// entered, and it does not ask when all it would lose is a setting.
/// </summary>
public sealed class ConfigResetTests
{
    [Fact]
    public void AnEmptyConfigNeedsNoConfirmation()
    {
        // Nothing but settings: a filter, a theme, a sort order. One click is the whole interaction.
        Assert.False(ConfigReset.NeedsConfirmation(new ConfigResetImpact(0, 0, 0, 0)));
    }

    [Fact]
    public void AFullWantedListNeedsConfirmation()
    {
        // The case the rule exists for: a hunting list a player cannot retype from memory.
        Assert.True(ConfigReset.NeedsConfirmation(new ConfigResetImpact(1, 0, 0, 0)));
        Assert.True(ConfigReset.NeedsConfirmation(new ConfigResetImpact(40, 0, 0, 0)));
    }

    [Fact]
    public void AGrindRecordAloneNeedsConfirmation()
    {
        Assert.True(ConfigReset.NeedsConfirmation(new ConfigResetImpact(0, 1, 0, 0)));
    }

    [Fact]
    public void APriceWatchAloneNeedsConfirmation()
    {
        Assert.True(ConfigReset.NeedsConfirmation(new ConfigResetImpact(0, 0, 1, 0)));
    }

    [Fact]
    public void ASavedViewAloneNeedsConfirmation()
    {
        // A preset is a name the player typed over five filter settings: the settings are one click away, the
        // name is not, which is what puts it on the side of the line that asks.
        Assert.True(ConfigReset.NeedsConfirmation(new ConfigResetImpact(0, 0, 0, 1)));
    }

    [Fact]
    public void TheImpactCountsEverythingItWouldTake()
    {
        var impact = new ConfigResetImpact(3, 2, 1, 4);

        Assert.Equal(10, impact.Total);
    }
}
