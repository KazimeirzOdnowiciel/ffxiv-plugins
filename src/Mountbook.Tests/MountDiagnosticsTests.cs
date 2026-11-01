using System;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>The diagnostics block a bug report carries.</summary>
public sealed class MountDiagnosticsTests
{
    [Fact]
    public void CarriesTheFiveFactsInOrder()
    {
        var text = MountDiagnostics.Build(
            "v0.1.0.0",
            "136 of 369 mounts collected.",
            "369 mounts, 356 with obtain data",
            "ffxivcollect.com, 2026-11-01, schema v2",
            "eorzea",
            false);

        Assert.StartsWith("Mountbook v0.1.0.0", text, StringComparison.Ordinal);
        Assert.Contains("Collection: 136 of 369 mounts collected.", text, StringComparison.Ordinal);
        Assert.Contains("Catalog: 369 mounts, 356 with obtain data", text, StringComparison.Ordinal);
        Assert.Contains("Obtain data: ffxivcollect.com, 2026-11-01, schema v2", text, StringComparison.Ordinal);
        Assert.EndsWith("Theme: eorzea", text, StringComparison.Ordinal);
        Assert.Equal(5, text.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void SaysWhenTheThemeFollowsTheGame()
    {
        var text = MountDiagnostics.Build("v1", "0 of 0 mounts collected.", "0 mounts, 0 with obtain data", "none", "eorzea", true);

        Assert.EndsWith("Theme: eorzea (following the game theme)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesTheSnapshotLineSoABugReportNamesTheData()
    {
        // A report that says "the patch column is empty" is unanswerable without knowing which
        // snapshot the player runs; the dataset line is what makes it answerable.
        var text = MountDiagnostics.Build(
            "v1",
            "1 of 2 mounts collected.",
            "2 mounts, 2 with obtain data",
            "ffxivcollect.com, 2026-11-01, schema v2 (built offline: no obtain data refreshed)",
            "neutral",
            false);

        Assert.Contains("Obtain data: ffxivcollect.com, 2026-11-01, schema v2 (built offline", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => MountDiagnostics.Build(null!, "p", "c", "d", "t", false));

    [Fact]
    public void RejectsANullDatasetLine() =>
        Assert.Throws<ArgumentNullException>(() => MountDiagnostics.Build("v", "p", "c", null!, "t", false));
}
