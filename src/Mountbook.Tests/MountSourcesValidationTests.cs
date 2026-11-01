using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The patch-day validator. Every case here is a way a regeneration can go wrong quietly: a partial
/// fetch, an id the game does not have, a mount that lost its "How to obtain" list, or a patch value
/// the plugin cannot place in an expansion. Each is asserted by its code, because the runbook tells
/// whoever reads the failure what to do next by that name.
/// </summary>
public sealed class MountSourcesValidationTests
{
    [Fact]
    public void TheShippedDatasetPassesAgainstItself()
    {
        string dataset = ReadShippedDataset();

        MountSourcesValidationReport report = MountSourcesValidator.Validate(dataset, dataset);

        Assert.True(report.IsValid, report.ToText());
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(0, report.WarningCount);
        Assert.Contains(MountSourcesValidator.SheetSkippedCode, Codes(report));
    }

    [Fact]
    public void TheRowCountAgreesWithTheReader()
    {
        // The validator counts what MountShelf parses, so the two cannot drift: a reader that starts
        // dropping rows would otherwise report a band it is not measuring.
        string dataset = ReadShippedDataset();

        MountSourcesValidationReport report = MountSourcesValidator.Validate(dataset, dataset);

        Assert.Equal(MountShelf.Parse(dataset).Count, report.CandidateCount);
    }

    [Fact]
    public void AShrunkenSnapshotFailsOnTheBand()
    {
        string baseline = Snapshot((1, "7.0", 1), (2, "7.0", 1), (3, "7.0", 1), (4, "7.0", 1));
        string candidate = Snapshot((1, "7.0", 1), (2, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1, AllowedShrink = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.RowCountCode, Codes(report));
    }

    [Fact]
    public void AGrownSnapshotFailsOnTheBand()
    {
        string baseline = Snapshot((1, "7.0", 1));
        string candidate = Snapshot((1, "7.0", 1), (2, "7.0", 1), (3, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1, AllowedGrowth = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.RowCountCode, Codes(report));
    }

    [Fact]
    public void AChangeInsideTheBandPasses()
    {
        string baseline = Snapshot((1, "7.0", 1), (2, "7.0", 1), (3, "7.0", 1), (4, "7.0", 1));
        string candidate = Snapshot((1, "7.0", 1), (2, "7.0", 1), (3, "7.0", 1), (4, "7.0", 1), (5, "7.05", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid, report.ToText());
        Assert.DoesNotContain(MountSourcesValidator.RowCountCode, Codes(report));
        Assert.Contains(MountSourcesValidator.MountAddedCode, Codes(report));
        Assert.Contains(MountSourcesValidator.PatchAddedCode, Codes(report));
    }

    [Fact]
    public void AnEmptySnapshotFailsTheFloorWithoutABaseline()
    {
        MountSourcesValidationReport report = MountSourcesValidator.Validate(Snapshot());

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.RowCountCode, Codes(report));
    }

    [Fact]
    public void AnIdThatIsNotAMountFails()
    {
        string candidate = Snapshot((1, "7.0", 1), (99, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            sheetIdsText: "1\n2\n3\n",
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.UnknownIdCode, Codes(report));
        Assert.Contains("99", ReportText(report));
    }

    [Fact]
    public void AMissingSheetIsANoteAndNotASilentPass()
    {
        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            Snapshot((1, "7.0", 1)),
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid);
        Assert.Equal(0, report.SheetCount);
        Assert.Contains(MountSourcesValidator.SheetSkippedCode, Codes(report));
    }

    [Fact]
    public void ADuplicateIdFails()
    {
        string candidate = Snapshot((1, "7.0", 1), (1, "7.05", 2));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.DuplicateIdCode, Codes(report));
    }

    [Fact]
    public void ARowWithoutAnIdFails()
    {
        string candidate = Snapshot((0, "7.0", 1), (1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.InvalidIdCode, Codes(report));
    }

    [Fact]
    public void AMountTheBaselineHadCannotDisappear()
    {
        string baseline = Snapshot((1, "7.0", 1), (2, "7.0", 1));
        string candidate = Snapshot((1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1, AllowedShrink = 5 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.MountDisappearedCode, Codes(report));
        Assert.Contains("2", ReportText(report));
    }

    [Fact]
    public void AMountThatLosesItsSourcesFails()
    {
        string baseline = Snapshot((1, "7.0", 2), (2, "7.0", 1));
        string candidate = Snapshot((1, "7.0", 0), (2, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.SourcesLostCode, Codes(report));
    }

    [Fact]
    public void AMountThatNeverHadSourcesIsNotAFailure()
    {
        // A mount with no obtain data is a fact about the site, not a regression; only losing lines
        // that were there is worth failing over.
        string baseline = Snapshot((1, "7.0", 0));
        string candidate = Snapshot((1, "7.0", 0));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid, report.ToText());
        Assert.DoesNotContain(MountSourcesValidator.SourcesLostCode, Codes(report));
    }

    [Fact]
    public void AMissingPatchFails()
    {
        string candidate = Snapshot((1, string.Empty, 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.PatchMissingCode, Codes(report));
    }

    [Fact]
    public void APatchOutsideEveryKnownExpansionFails()
    {
        // 8.0 is the next expansion: until MountBreakdown.Expansions learns it, every mount added in
        // the patch would be filtered into "Other", which is exactly what a patch day must catch.
        string candidate = Snapshot((1, "8.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.PatchNotInSheetCode, Codes(report));
        Assert.Contains("8.0", ReportText(report));
    }

    [Fact]
    public void AChangedPatchIsAWarningAndNotAnError()
    {
        string baseline = Snapshot((1, "7.0", 1));
        string candidate = Snapshot((1, "7.05", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid, report.ToText());
        Assert.Equal(1, report.WarningCount);
        Assert.Contains(MountSourcesValidator.PatchChangedCode, Codes(report));
        Assert.Contains("7.0", ReportText(report));
        Assert.Contains("7.05", ReportText(report));
    }

    [Fact]
    public void FindingsAreOrderedErrorsBeforeWarningsBeforeNotes()
    {
        string baseline = Snapshot((1, "7.0", 1));
        string candidate = Snapshot((1, "7.05", 0), (2, "7.1", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        var lastSeverity = MountSourcesIssueSeverity.Error;
        foreach (MountSourcesIssue issue in report.Issues)
        {
            Assert.True(issue.Severity >= lastSeverity, $"\"{issue.Code}\" is out of order in:\n{report.ToText()}");
            lastSeverity = issue.Severity;
        }
    }

    [Fact]
    public void TheMarkdownReportNamesTheOutcomeAndEveryFinding()
    {
        string candidate = Snapshot((1, string.Empty, 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        string markdown = report.ToMarkdown();

        Assert.Contains("FAILED", markdown, StringComparison.Ordinal);
        Assert.Contains(MountSourcesValidator.PatchMissingCode, markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1\n2\n3\n", 3)]
    [InlineData("# the mount sheet\n7\n\n  12  \n", 2)]
    [InlineData("mount_id,name,chara_id\n1,\"company chocobo\",1\n2,\"\",1\n", 2)]
    public void SheetIdsReadFromLinesOrTheModelMapCsv(string text, int expected)
    {
        HashSet<uint> ids = MountSourcesValidator.ParseSheetIds(text);

        Assert.Equal(expected, ids.Count);
    }

    [Fact]
    public void SheetIdsWithoutAMountIdAreABrokenFileAndNotAnEmptySheet()
    {
        // An empty set would report every mount as unknown, which reads like a data problem instead
        // of the broken file it is.
        Assert.Throws<FormatException>(() => MountSourcesValidator.ParseSheetIds("# nothing here\n"));
        Assert.Throws<FormatException>(() => MountSourcesValidator.ParseSheetIds("mount_id,name\n1,a\nnot-an-id\n"));
    }

    [Fact]
    public void OwnershipDriftIsReportedAsANoteAndNotADataChange()
    {
        // The site recounts ownership daily, so a regeneration's diff always carries a few
        // percentages. Saying so keeps a reviewer from reading them as the patch's data change.
        string baseline = Snapshot((1, "7.0", 1)).Replace("\"owned\": null", "\"owned\": 1.0", StringComparison.Ordinal);
        string candidate = Snapshot((1, "7.0", 1)).Replace("\"owned\": null", "\"owned\": 1.5", StringComparison.Ordinal);

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            baseline,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid, report.ToText());
        Assert.Contains(MountSourcesValidator.OwnershipDriftCode, Codes(report));
    }

    private static string ReportText(MountSourcesValidationReport report) => report.ToText();

    private static IReadOnlyList<string> Codes(MountSourcesValidationReport report)
    {
        var codes = new List<string>(report.Issues.Count);
        foreach (MountSourcesIssue issue in report.Issues)
        {
            codes.Add(issue.Code);
        }

        return codes;
    }

    private static string ReadShippedDataset()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "mount-sources.json");
        Assert.True(File.Exists(path), $"The dataset was not copied next to the test binary: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Builds a snapshot in the generator's shape: one row per tuple, with <c>count</c> obtain lines.
    /// </summary>
    private static string Snapshot(params (uint Id, string Patch, int Sources)[] rows)
    {
        var json = new StringBuilder();
        json.Append("{\n  \"generated\": \"2026-11-01\",\n  \"source\": \"ffxivcollect.com\",\n  \"mounts\": [\n");

        for (var index = 0; index < rows.Length; index++)
        {
            (uint id, string patch, int sources) = rows[index];
            json.Append("    { \"id\": ").Append(id);
            json.Append(", \"patch\": \"").Append(patch).Append('"');
            json.Append(", \"itemId\": 0, \"owned\": null, \"tradeable\": null, \"customMusic\": null, \"sources\": [");

            for (var line = 0; line < sources; line++)
            {
                if (line > 0)
                {
                    json.Append(", ");
                }

                json.Append("{ \"type\": \"Quest\", \"text\": \"source ").Append(line + 1);
                json.Append("\", \"relatedType\": null, \"relatedId\": 0 }");
            }

            json.Append("] }");
            json.Append(index == rows.Length - 1 ? "\n" : ",\n");
        }

        json.Append("  ]\n}\n");
        return json.ToString();
    }
}
