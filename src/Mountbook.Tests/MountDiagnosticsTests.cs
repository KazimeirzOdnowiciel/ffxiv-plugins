using System;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>The diagnostics block a bug report carries.</summary>
public sealed class MountDiagnosticsTests
{
    [Fact]
    public void CarriesTheFourFactsInOrder()
    {
        var text = MountDiagnostics.Build("v0.1.0.0", "136 of 369 mounts collected.", "369 mounts, 356 with obtain data", "eorzea", false);

        Assert.StartsWith("Mountbook v0.1.0.0", text, StringComparison.Ordinal);
        Assert.Contains("Collection: 136 of 369 mounts collected.", text, StringComparison.Ordinal);
        Assert.Contains("Catalog: 369 mounts, 356 with obtain data", text, StringComparison.Ordinal);
        Assert.EndsWith("Theme: eorzea", text, StringComparison.Ordinal);
        Assert.Equal(4, text.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void SaysWhenTheThemeFollowsTheGame()
    {
        var text = MountDiagnostics.Build("v1", "0 of 0 mounts collected.", "0 mounts, 0 with obtain data", "eorzea", true);

        Assert.EndsWith("Theme: eorzea (following the game theme)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => MountDiagnostics.Build(null!, "p", "c", "t", false));
}
