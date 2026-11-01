namespace Mountbook.Tests;

using System;
using Mountbook.Services;
using Xunit;

/// <summary>
/// The settings window shows the themes folder on one line; these pin the tail-keeping rule with a measurer
/// that charges ten units per character, so every assertion is arithmetic rather than a screenshot.
/// </summary>
public sealed class PathLabelTests
{
    private const string Path = @"C:\a\bb\ccc\dddd";

    /// <summary>Ten units per character, the ellipsis included.</summary>
    private static float Count(string text) => text.Length * 10f;

    [Fact]
    public void APathThatFitsIsReturnedWhole()
    {
        Assert.Equal(Path, PathLabel.Shorten(Path, 200f, Count));
    }

    [Fact]
    public void ALongPathKeepsItsTailAndStartsWithThePrefix()
    {
        // 100 units is ten characters: "...\dddd" (eight) fits, "...\ccc\dddd" (twelve) does not.
        Assert.Equal(@"...\dddd", PathLabel.Shorten(Path, 100f, Count));
    }

    [Fact]
    public void MoreRoomKeepsMoreSegments()
    {
        // 130 units is thirteen characters: the twelve-character candidate fits, the fifteen does not.
        Assert.Equal(@"...\ccc\dddd", PathLabel.Shorten(Path, 130f, Count));
    }

    [Fact]
    public void TheResultIsNeverWiderThanTheLine()
    {
        for (var width = 40f; width <= 200f; width += 10f)
        {
            string label = PathLabel.Shorten(Path, width, Count);

            Assert.True(Count(label) <= width, $"'{label}' measures {Count(label)} at width {width}");
        }
    }

    [Fact]
    public void ASingleOverlongSegmentIsTrimmedFromTheEndInstead()
    {
        // When even "...\name" does not fit there is no tail worth keeping: the name is trimmed with ASCII
        // dots, which is the same glyph a preview can draw.
        string label = PathLabel.Shorten(@"C:\averylongfoldername", 100f, Count);

        Assert.EndsWith("...", label);
        Assert.False(label.StartsWith(PathLabel.Prefix, StringComparison.Ordinal));
        Assert.True(Count(label) <= 100f);
    }

    [Fact]
    public void EitherSeparatorSplitsAndTheLabelUsesTheWindowsOne()
    {
        // Splitting accepts either, because the path is a string a theme folder could have come from. The
        // label always draws the Windows separator: this path is the plugin's own folder on a Windows client.
        Assert.Equal(@"...\themes", PathLabel.Shorten("/home/lukat/Mountbook/themes", 100f, Count));
    }

    [Fact]
    public void AnEmptyPathStaysEmpty()
    {
        Assert.Equal(string.Empty, PathLabel.Shorten(string.Empty, 100f, Count));
    }
}
