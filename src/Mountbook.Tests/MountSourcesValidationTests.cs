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
        // Ids 61+ rather than 1+: 4 and 5 are the curated Legacy mounts, and a synthetic Quest row filed under
        // one of them is a drift by design (MountAvailabilityRules.Curated).
        string baseline = Snapshot((61, "7.0", 1), (62, "7.0", 1), (63, "7.0", 1), (64, "7.0", 1));
        string candidate = Snapshot((61, "7.0", 1), (62, "7.0", 1), (63, "7.0", 1), (64, "7.0", 1), (65, "7.05", 1));

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

    [Fact]
    public void ASnapshotWithoutASchemaVersionFails()
    {
        // The first shipped shape: rows parse, but the document cannot say which generator wrote it,
        // so a field added since would go missing without a word.
        string candidate = SnapshotWithHeader(string.Empty, (1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.SchemaMissingCode, Codes(report));
        Assert.Equal(1, report.CandidateCount);
    }

    [Fact]
    public void ASnapshotFromAnotherGeneratorVersionFails()
    {
        string candidate = SnapshotWithHeader("  \"schemaVersion\": 1,\n", (1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.SchemaVersionCode, Codes(report));
        Assert.Contains("v1", ReportText(report));
    }

    [Fact]
    public void ASnapshotWithoutAttributionFails()
    {
        // The obtain data is a volunteer project's work; shipping it uncredited is the one thing the
        // licence asks for, so the build refuses rather than leaving it to review.
        string candidate = SnapshotWithHeader(
            "  \"schemaVersion\": 3,\n  \"attributionUrl\": \"https://ffxivcollect.com\",\n",
            (1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.AttributionMissingCode, Codes(report));
    }

    [Fact]
    public void ASnapshotOverTheSizeBudgetFails()
    {
        // The file is embedded in the assembly and downloaded by every player, so the budget is a
        // build gate and not a note. 200 bytes makes any real snapshot fail without faking a size.
        string candidate = Snapshot((1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1, MaxBytes = 200 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.SizeBudgetCode, Codes(report));
        Assert.True(report.CandidateBytes > 200, $"The measurement never ran: {report.CandidateBytes} bytes.");
    }

    [Fact]
    public void TheApisDescriptionCopyFails()
    {
        // The game's MountTransient sheet already carries the flavour text, and the site's copy is
        // what would take the file from 83 KB to 251 KB.
        string candidate = Snapshot((1, "7.0", 1)).Replace(
            "\"itemId\": 0,",
            "\"itemId\": 0, \"tooltip\": \"Remember to attack while it's tail's up!\",",
            StringComparison.Ordinal);

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.DescriptionCopyCode, Codes(report));
    }

    [Fact]
    public void ASheetOnlySnapshotIsAWarningAndNotAnError()
    {
        // The disaster-recovery shape: the site is gone, so the snapshot is the sheet's rows with no
        // patch and no source refresh. It must ship as a warning - a reader has to accept it - and
        // the row band must not fire on it, or the only way out of an outage is a broken build.
        string candidate = SnapshotWithHeader(
            "  \"schemaVersion\": 4,\n"
            + "  \"enrichment\": \"sheet-only\",\n"
            + "  \"attribution\": \"Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.\",\n"
            + "  \"attributionUrl\": \"https://ffxivcollect.com\",\n",
            (1, string.Empty, 0),
            (2, string.Empty, 0));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid, report.ToText());
        Assert.Contains(MountSourcesValidator.SheetOnlyCode, Codes(report));
        Assert.Contains(MountSourcesValidator.PatchMissingCode, Codes(report));
        Assert.Equal(0, report.ErrorCount);
        Assert.DoesNotContain(MountSourcesValidator.RowCountCode, Codes(report));
        // Nothing was compared offline, so the missing cross-check must not be asked for twice.
        Assert.DoesNotContain(MountSourcesValidator.CrossCheckMissingCode, Codes(report));
    }

    [Fact]
    public void AFullSnapshotWithoutASheetCrossCheckIsAWarningAndNotAnError()
    {
        // A machine without the game can still regenerate the data; it just cannot verify the two
        // facts the file does not carry. The report has to say so, without blocking the build.
        string candidate = SnapshotWithHeader(
            "  \"schemaVersion\": 4,\n"
            + "  \"enrichment\": \"full\",\n"
            + "  \"attribution\": \"Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.\",\n"
            + "  \"attributionUrl\": \"https://ffxivcollect.com\",\n",
            (1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid, report.ToText());
        Assert.Contains(MountSourcesValidator.CrossCheckMissingCode, Codes(report));
        Assert.Equal(1, report.WarningCount);
    }

    [Fact]
    public void ACrossCheckThatCoversFewerRowsThanTheSnapshotFails()
    {
        // The block is the evidence the comparison happened. Covering fewer rows than the file carries
        // means some mounts were never compared, which is not the same thing as "they agreed".
        string candidate = SnapshotWithHeader(
            "  \"schemaVersion\": 4,\n"
            + "  \"enrichment\": \"full\",\n"
            + "  \"sheetCrossCheck\": { \"sheet\": \"Mount\", \"fields\": [\"ExtraSeats\", \"IsAirborne\"], \"mounts\": 1, \"seats\": 1, \"movement\": 1 },\n"
            + "  \"attribution\": \"Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.\",\n"
            + "  \"attributionUrl\": \"https://ffxivcollect.com\",\n",
            (1, "7.0", 1),
            (2, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.CrossCheckDriftCode, Codes(report));
        Assert.Contains("covers 1 of the snapshot's 2", ReportText(report), StringComparison.Ordinal);
    }

    [Fact]
    public void ACrossCheckThatDisagreedFails()
    {
        // The generator refuses to write a disagreeing snapshot, so reaching this means the file was
        // edited by hand or the sheet facts came from another game build. Either way it must not ship.
        string candidate = SnapshotWithHeader(
            "  \"schemaVersion\": 4,\n"
            + "  \"enrichment\": \"full\",\n"
            + "  \"sheetCrossCheck\": { \"sheet\": \"Mount\", \"fields\": [\"ExtraSeats\", \"IsAirborne\"], \"mounts\": 2, \"seats\": 1, \"movement\": 2 },\n"
            + "  \"attribution\": \"Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.\",\n"
            + "  \"attributionUrl\": \"https://ffxivcollect.com\",\n",
            (1, "7.0", 1),
            (2, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.CrossCheckDriftCode, Codes(report));
        Assert.Contains("1/2 seat counts", ReportText(report), StringComparison.Ordinal);
    }

    [Fact]
    public void ACrossCheckOfOtherSheetColumnsFails()
    {
        // A block that compared Order and UIPriority says nothing about seats or movement, however
        // healthy its counts look.
        string candidate = SnapshotWithHeader(
            "  \"schemaVersion\": 4,\n"
            + "  \"enrichment\": \"full\",\n"
            + "  \"sheetCrossCheck\": { \"sheet\": \"Mount\", \"fields\": [\"Order\", \"UIPriority\"], \"mounts\": 1, \"seats\": 1, \"movement\": 1 },\n"
            + "  \"attribution\": \"Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.\",\n"
            + "  \"attributionUrl\": \"https://ffxivcollect.com\",\n",
            (1, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.CrossCheckFieldsCode, Codes(report));
    }

    [Fact]
    public void AClassTheRulesDoNotDeriveIsADrift()
    {
        // The field is generator output. A row that states another class than its own obtain lines give is a hand
        // edit or a build with other rules, and either one puts a badge and a filter on different answers.
        string candidate = Snapshot((1, "7.0", 1)).Replace(
            "\"availability\": \"available\"",
            "\"availability\": \"premium\"",
            StringComparison.Ordinal);

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.AvailabilityDriftCode, Codes(report));
        Assert.Contains("1: file premium, rules available", ReportText(report), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclassifiableRowWithObtainLinesFails()
    {
        // A type this build does not know, on a row that has lines: somebody has to decide what it is, so the
        // snapshot must not ship with the row reading "No data".
        string candidate = Snapshot((1, "7.0", 1)).Replace(
            "\"type\": \"Quest\"",
            "\"type\": \"Brand New Type\"",
            StringComparison.Ordinal);

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.AvailabilityMissingCode, Codes(report));
    }

    [Fact]
    public void NoCountsBlockIsAWarningAndAWrongOneIsAnError()
    {
        string noBlock = Snapshot((1, "7.0", 1)).Replace(
            "  \"availability\": { \"available\": 1 },\n",
            string.Empty,
            StringComparison.Ordinal);

        MountSourcesValidationReport warning = MountSourcesValidator.Validate(
            noBlock,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(warning.IsValid, warning.ToText());
        Assert.Contains(MountSourcesValidator.AvailabilityCountsMissingCode, Codes(warning));

        string wrongCount = Snapshot((1, "7.0", 1)).Replace(
            "  \"availability\": { \"available\": 1 },",
            "  \"availability\": { \"available\": 7 },",
            StringComparison.Ordinal);

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            wrongCount,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Contains(MountSourcesValidator.AvailabilityCountsCode, Codes(report));
        Assert.Contains("block 7, rows 1", ReportText(report), StringComparison.Ordinal);
    }

    [Fact]
    public void AFullSnapshotWithNoPatchIsStillAnError()
    {
        // The other direction: a snapshot that claims the site answered and then carries no patch is
        // a broken generation, not a fallback.
        string candidate = Snapshot((1, string.Empty, 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid);
        Assert.Equal(MountSourcesIssueSeverity.Error, SeverityOf(report, MountSourcesValidator.PatchMissingCode));
    }

    private static string ReportText(MountSourcesValidationReport report) => report.ToText();

    private static MountSourcesIssueSeverity SeverityOf(MountSourcesValidationReport report, string code)
    {
        foreach (MountSourcesIssue issue in report.Issues)
        {
            if (string.Equals(issue.Code, code, StringComparison.Ordinal))
            {
                return issue.Severity;
            }
        }

        throw new InvalidOperationException($"No \"{code}\" finding in:\n{report.ToText()}");
    }

    [Fact]
    public void AnObtainTypeOutsideTheTaxonomyFails()
    {
        // The filter, the grouping and the counts are keyed by the type: a type nobody has taught the plugin
        // would land in an unlabelled section, so it is refused rather than accepted quietly.
        string candidate = Snapshot((1, "7.0", 1)).Replace("\"type\": \"Quest\"", "\"type\": \"Brand New Type\"");

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid, report.ToText());
        Assert.Contains(MountSourcesValidator.SourceTypeUnknownCode, Codes(report));
        Assert.Contains("Brand New Type", report.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnObtainLineWithoutATypeFails()
    {
        // A line the filter cannot place is invisible to the source filter and to its counts, while the pane
        // still draws the text - the one state a player cannot reason about.
        string candidate = Snapshot((1, "7.0", 1)).Replace("\"type\": \"Quest\"", "\"type\": \"\"");

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.False(report.IsValid, report.ToText());
        Assert.Contains(MountSourcesValidator.SourceTypeMissingCode, Codes(report));
    }

    [Fact]
    public void ATypedSnapshotPassesTheTypeChecks()
    {
        string candidate = Snapshot((1, "7.0", 2), (2, "7.0", 1));

        MountSourcesValidationReport report = MountSourcesValidator.Validate(
            candidate,
            options: new MountSourcesValidationOptions { MinimumCount = 1 });

        Assert.True(report.IsValid, report.ToText());
        Assert.DoesNotContain(MountSourcesValidator.SourceTypeMissingCode, Codes(report));
        Assert.DoesNotContain(MountSourcesValidator.SourceTypeUnknownCode, Codes(report));
    }

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
    /// Builds a snapshot in the generator's shape: the v3 header this build reads, including the
    /// agreeing sheet cross-check a normal online run records, then one row per tuple with
    /// <c>count</c> obtain lines. Tests that are about a missing or different header use
    /// <see cref="SnapshotWithHeader"/> or edit the version in place.
    /// </summary>
    private static string Snapshot(params (uint Id, string Patch, int Sources)[] rows) =>
        SnapshotWithHeader(
            "  \"schemaVersion\": 4,\n"
            + "  \"enrichment\": \"full\",\n"
            + CrossCheckLine(rows.Length)
            + "  \"attribution\": \"Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.\",\n"
            + "  \"attributionUrl\": \"https://ffxivcollect.com\",\n",
            rows);

    /// <summary>The agreeing cross-check block a normal online run writes for <paramref name="rows"/> rows.</summary>
    private static string CrossCheckLine(int rows) =>
        "  \"sheetCrossCheck\": { \"sheet\": \"Mount\", \"fields\": [\"ExtraSeats\", \"IsAirborne\"], "
        + "\"mounts\": " + rows.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ", \"seats\": " + rows.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ", \"movement\": " + rows.ToString(System.Globalization.CultureInfo.InvariantCulture) + " },\n";

    /// <summary>
    /// The header's per-class availability block for <paramref name="rows"/>, in the generator's shape: the rows
    /// this helper builds are one Quest line each, so a row with a source is Available and one without is Unknown.
    /// </summary>
    private static string AvailabilityLine(params (uint Id, string Patch, int Sources)[] rows)
    {
        var available = 0;
        var unknown = 0;

        foreach ((uint _, string _, int sources) in rows)
        {
            if (sources > 0)
            {
                available++;
            }
            else
            {
                unknown++;
            }
        }

        var parts = new List<string>(2);

        if (available > 0)
        {
            parts.Add("\"available\": " + available.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (unknown > 0)
        {
            parts.Add("\"unknown\": " + unknown.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return "  \"availability\": { " + string.Join(", ", parts) + " },\n";
    }

    /// <summary>
    /// Builds a snapshot with a caller-supplied header: the way to test "no schemaVersion" (an empty
    /// header) or any other document-level fact without hand-writing a whole file.
    /// </summary>
    private static string SnapshotWithHeader(string headerLines, params (uint Id, string Patch, int Sources)[] rows)
    {
        var json = new StringBuilder();
        json.Append("{\n  \"generated\": \"2026-11-01\",\n  \"source\": \"ffxivcollect.com\",\n");
        json.Append(headerLines);

        // The per-class block the generator writes, unless the caller supplied one: the rows this helper
        // builds are one Quest line each, so a row with a source is Available and one without is Unknown.
        if (!headerLines.Contains("\"availability\":", StringComparison.Ordinal))
        {
            json.Append(AvailabilityLine(rows));
        }
        json.Append("  \"mounts\": [\n");

        for (var index = 0; index < rows.Length; index++)
        {
            (uint id, string patch, int sources) = rows[index];
            json.Append("    { \"id\": ").Append(id);
            json.Append(", \"patch\": \"").Append(patch).Append('"');
            json.Append(", \"itemId\": 0, \"owned\": null, \"tradeable\": null, \"customMusic\": null, \"availability\": \"");
            json.Append(sources > 0 ? "available" : "unknown");
            json.Append("\", \"sources\": [");

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
