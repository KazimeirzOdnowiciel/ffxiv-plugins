using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The sheet writes mount names for mid-sentence use; the book shows them like the Mount Guide does.
/// These cases pin the rule that makes acronyms survive.
/// </summary>
public sealed class MountNameTests
{
    [Theory]
    [InlineData("battle bear", "Battle Bear")]
    [InlineData("reveling kamuy", "Reveling Kamuy")]
    [InlineData("company chocobo", "Company Chocobo")]
    [InlineData("magitek death claw", "Magitek Death Claw")]
    public void LowercaseWordsAreCapitalised(string sheet, string display)
    {
        Assert.Equal(display, MountName.ToDisplay(sheet));
    }

    [Theory]
    [InlineData("SDS Fenrir")]
    [InlineData("Demi-Ozma")]
    [InlineData("Midgardsormr")]
    [InlineData("Fenrir")]
    [InlineData("Aithon")]
    public void AlreadyCapitalisedNamesAreLeftAlone(string name)
    {
        Assert.Equal(name, MountName.ToDisplay(name));
    }

    [Fact]
    public void HyphenSegmentsAreCapitalised()
    {
        Assert.Equal("Demi-Ozma", MountName.ToDisplay("demi-ozma"));
    }

    [Fact]
    public void BracketSegmentsAreCapitalised()
    {
        Assert.Equal("Broken Heart (Right)", MountName.ToDisplay("broken heart (right)"));
    }

    [Fact]
    public void MixedWordsKeepTheirCapitals()
    {
        // "kamuy" is lowercase but "Auspicious" already carries a capital.
        Assert.Equal("Auspicious Kamuy", MountName.ToDisplay("auspicious kamuy"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyInputIsReturnedUnchanged(string name)
    {
        Assert.Equal(name, MountName.ToDisplay(name));
    }

    [Fact]
    public void ApostrophesStartASegment()
    {
        Assert.Equal("K'sante", MountName.ToDisplay("k'sante"));
    }
}
