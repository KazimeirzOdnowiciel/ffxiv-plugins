using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The token prices buried in the dataset's source lines. 51 of its 423 lines are a price and the rest are
/// prose, so the parse has to be strict: a wrong read turns a lore sentence into a shop and a wrong
/// "enough to buy it" is worse than showing nothing.
/// </summary>
public sealed class MountCostsTests
{
    [Fact]
    public void APriceAndATokenNameParse()
    {
        MountCost? cost = MountCosts.Parse("12,000 Seafarer's Cowries");

        Assert.NotNull(cost);
        Assert.Equal(12000, cost!.Amount);
        Assert.Equal("Seafarer's Cowries", cost.Currency);
        Assert.Equal(string.Empty, cost.Note);
    }

    [Fact]
    public void MGPAndASingleTokenBothParse()
    {
        Assert.Equal(200000, MountCosts.Parse("200,000 MGP")!.Amount);
        Assert.Equal(1, MountCosts.Parse("1 Khloe's Silver Certificate of Commendation")!.Amount);
    }

    [Fact]
    public void ATrailingRequirementIsKeptAsANoteNotPartOfTheName()
    {
        // The name is what the item lookup is keyed on; "Sanctuary Rank 10" is a requirement behind the
        // price and is kept so the line can still say so.
        MountCost? cost = MountCosts.Parse("12,000 Seafarer's Cowries (Sanctuary Rank 10)");

        Assert.NotNull(cost);
        Assert.Equal("Seafarer's Cowries", cost!.Currency);
        Assert.Equal("Sanctuary Rank 10", cost.Note);
    }

    [Theory]
    [InlineData("At level 30 or above, speak with the Wandering Minstrel")]
    [InlineData("Obtain 30 mounts.")]
    [InlineData("The Bowl of Embers (Extreme)")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12,000")]
    public void AnythingElseIsNotAPrice(string text) => Assert.Null(MountCosts.Parse(text));

    [Fact]
    public void APriceWithNoTokenNameIsNotAPrice() =>
        Assert.Null(MountCosts.Parse("12,000   "));

    [Fact]
    public void DescribeSaysWhatIsStillNeeded()
    {
        MountCost cost = MountCosts.Parse("12,000 Seafarer's Cowries")!;

        Assert.Equal("12,000 Seafarer's Cowries - you have 3,400, 8,600 to go", MountCosts.Describe(cost, 3400));
    }

    [Fact]
    public void DescribeSaysWhenThePlayerHasEnough()
    {
        MountCost cost = MountCosts.Parse("200,000 MGP")!;

        Assert.Equal("200,000 MGP - you have 350,001, enough to buy it", MountCosts.Describe(cost, 350001));
        Assert.Equal("200,000 MGP - you have 200,000, enough to buy it", MountCosts.Describe(cost, 200000));
    }

    [Fact]
    public void DescribeWithoutACountShowsOnlyThePrice()
    {
        MountCost cost = MountCosts.Parse("5,000 Trophy Crystals")!;

        Assert.Equal("5,000 Trophy Crystals", MountCosts.Describe(cost, null));
    }
}
