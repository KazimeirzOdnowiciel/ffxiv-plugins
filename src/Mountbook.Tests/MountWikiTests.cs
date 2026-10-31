using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>The wiki URL a mount's detail pane opens.</summary>
public sealed class MountWikiTests
{
    [Fact]
    public void SimpleNamesBecomeThePagePath()
    {
        Assert.Equal("https://ffxiv.gamerescape.com/wiki/Aithon", MountWiki.Url("Aithon"));
    }

    [Fact]
    public void SpacesBecomeUnderscores()
    {
        Assert.Equal(
            "https://ffxiv.gamerescape.com/wiki/Auspicious_Kamuy",
            MountWiki.Url("Auspicious Kamuy"));
    }

    [Fact]
    public void ApostrophesAreEscaped()
    {
        Assert.Equal(
            "https://ffxiv.gamerescape.com/wiki/Byakko%27s_Whiskers",
            MountWiki.Url("Byakko's Whiskers"));
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        Assert.Equal("https://ffxiv.gamerescape.com/wiki/Aithon", MountWiki.Url("  Aithon  "));
    }

    [Fact]
    public void NullIsRejected() => Assert.Throws<System.ArgumentNullException>(() => MountWiki.Url(null!));
}
