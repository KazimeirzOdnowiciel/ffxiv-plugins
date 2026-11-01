namespace Mountbook.Tests;

using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

/// <summary>
/// The ellipsis rule, tested against a counting measurer instead of a font: ten units per character, so a
/// width is a character count and every assertion is arithmetic rather than a screenshot.
/// </summary>
public sealed class TextFitTests
{
    /// <summary>A measurer that charges every character the same, including the ellipsis.</summary>
    private static float Count(string text) => text.Length * 10f;

    [Fact]
    public void TextThatFitsIsReturnedUnchanged()
    {
        Assert.Equal("Aithon", TextFit.Fit("Aithon", 60f, Count));
    }

    [Fact]
    public void TextThatExactlyFitsIsNotTrimmed()
    {
        // The comparison is inclusive upstream too: a label that measures the pane's width draws whole, and
        // trimming it there used to lose the last letter for no reason.
        Assert.Equal("Aithon", TextFit.Fit("Aithon", 60f, Count));
    }

    [Fact]
    public void LongTextIsTrimmedAndEndsInTheEllipsis()
    {
        string fitted = TextFit.Fit("Auspicious Kamuy", 100f, Count);

        Assert.EndsWith(TextFit.Ellipsis, fitted);
        Assert.True(Count(fitted) <= 100f, $"'{fitted}' measures {Count(fitted)}, over the 100 it was given");
        Assert.True(fitted.Length < "Auspicious Kamuy".Length);
    }

    [Fact]
    public void TheTrimmedTextIsTheWidestOneThatFits()
    {
        // 100 units is ten characters: eight of prefix plus the ellipsis, not seven, and not nine.
        Assert.Equal("Auspiciou" + TextFit.Ellipsis, TextFit.Fit("Auspicious Kamuy", 100f, Count));
    }

    [Fact]
    public void TheCutNeverLeavesATrailingSpace()
    {
        // A prefix that ends mid-phrase cuts at a space, and "Black  ..." reads like a bug.
        string fitted = TextFit.Fit("Black Chocobo", 70f, Count);

        Assert.Equal("Black" + TextFit.Ellipsis, fitted);
    }

    [Fact]
    public void EmptyTextStaysEmpty()
    {
        // An empty label is not "too wide": nothing draws, which is what the absent rarity line relies on.
        Assert.Equal(string.Empty, TextFit.Fit(string.Empty, 10f, Count));
    }

    [Fact]
    public void TooNarrowForEvenTheEllipsisStillReturnsALabel()
    {
        // Below one character plus the ellipsis there is nothing that fits; a single character and the
        // ellipsis is still better than an empty badge, and the caller's clip handles the overhang.
        string fitted = TextFit.Fit("Aithon", 5f, Count);

        Assert.Equal("A" + TextFit.Ellipsis, fitted);
    }

    [Fact]
    public void MeasureIsCalledWithTheCandidatesItCompares()
    {
        // The view memoises the measurer, so the contract is "ask about full text first, then about
        // candidates" - a measurer that counts asks would silently be a cache miss every frame otherwise.
        var asked = new List<string>();

        float Counting(string text)
        {
            asked.Add(text);
            return Count(text);
        }

        TextFit.Fit("Aithon", 60f, Counting);

        Assert.Equal(["Aithon"], asked);
    }
}
