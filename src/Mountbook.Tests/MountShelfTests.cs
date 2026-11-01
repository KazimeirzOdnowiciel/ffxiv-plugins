using System;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The JSON parser is the one place the shipped dataset shape is interpreted, so it is tested
/// against small inline documents as well as the real file (see <see cref="MountSourcesDataTests"/>).
/// </summary>
public sealed class MountShelfTests
{
    [Fact]
    public void ParsesTheFullShape()
    {
        const string json = """
            {
              "generated": "2026-10-30",
              "source": "ffxivcollect.com",
              "mounts": [
                {
                  "id": 430,
                  "patch": "7.57",
                  "itemId": 52261,
                  "sources": [
                    { "type": "Gold Saucer", "text": "430 MGC", "relatedType": null, "relatedId": 0 },
                    { "type": "Achievement", "text": "Mount collector", "relatedType": "Achievement", "relatedId": 1234 }
                  ]
                },
                {
                  "id": 1,
                  "patch": "2.0",
                  "itemId": 6001,
                  "sources": []
                }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.Equal(2, shelf.Count);

        Assert.True(shelf.TryGet(430, out MountSources mount));
        Assert.Equal("7.57", mount.Patch);
        Assert.Equal(52261u, mount.ItemId);
        Assert.Equal(2, mount.Lines.Count);
        Assert.Equal(new MountSourceLine("Gold Saucer", "430 MGC", "", 0), mount.Lines[0]);
        Assert.Equal(new MountSourceLine("Achievement", "Mount collector", "Achievement", 1234), mount.Lines[1]);

        Assert.True(shelf.TryGet(1, out MountSources early));
        Assert.Equal("2.0", early.Patch);
        Assert.Empty(early.Lines);
    }

    [Fact]
    public void NullRelatedFieldsBecomeEmptyAndZero()
    {
        const string json = """
            {
              "mounts": [
                {
                  "id": 7,
                  "patch": "3.0",
                  "itemId": 1,
                  "sources": [
                    { "type": "Quest", "text": "A quest", "relatedType": null, "relatedId": null }
                  ]
                }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(7, out MountSources mount));
        MountSourceLine line = Assert.Single(mount.Lines);
        Assert.Equal("Quest", line.Category);
        Assert.Equal("A quest", line.Detail);
        Assert.Equal(string.Empty, line.RelatedKind);
        Assert.Equal(0u, line.RelatedId);
    }

    [Fact]
    public void AbsentRelatedFieldsAndItemIdBecomeEmptyAndZero()
    {
        const string json = """
            {
              "mounts": [
                {
                  "id": 8,
                  "patch": "4.0",
                  "sources": [
                    { "type": "Event", "text": "An event" }
                  ]
                }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(8, out MountSources mount));
        Assert.Equal(0u, mount.ItemId);
        MountSourceLine line = Assert.Single(mount.Lines);
        Assert.Equal(string.Empty, line.RelatedKind);
        Assert.Equal(0u, line.RelatedId);
    }

    [Fact]
    public void AbsentSourcesArrayIsAnEmptyList()
    {
        const string json = """
            { "mounts": [ { "id": 9, "patch": "5.0", "itemId": 2 } ] }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(9, out MountSources mount));
        Assert.Empty(mount.Lines);
    }

    [Fact]
    public void NullSourcesArrayIsAnEmptyList()
    {
        const string json = """
            { "mounts": [ { "id": 10, "patch": "6.0", "itemId": 3, "sources": null } ] }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(10, out MountSources mount));
        Assert.Empty(mount.Lines);
    }

    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        const string json = """
            {
              "unexpected": true,
              "mounts": [
                {
                  "id": 11,
                  "name": "Not read by the parser",
                  "patch": "7.0",
                  "itemId": 4,
                  "owned": "2.5%",
                  "sources": [
                    { "type": "Trial", "text": "A trial", "owned": "2.5%" }
                  ]
                }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(11, out MountSources mount));
        Assert.Equal("7.0", mount.Patch);
        Assert.Equal(4u, mount.ItemId);
        Assert.Single(mount.Lines);
    }

    [Fact]
    public void WhitespaceOnlySourceTextIsDropped()
    {
        // The generator trims and skips a blank source line; a blank row in the pane is worse than a
        // missing one, so the reader applies the same rule to a hand-edited dataset.
        const string json = """
            {
              "mounts": [
                {
                  "id": 13,
                  "patch": "7.0",
                  "sources": [
                    { "type": "Quest", "text": "   " },
                    { "type": "Trial", "text": "A trial" }
                  ]
                }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(13, out MountSources mount));
        MountSourceLine line = Assert.Single(mount.Lines);
        Assert.Equal("Trial", line.Category);
    }

    [Fact]
    public void PropertyNamesAreMatchedCaseInsensitively()
    {
        const string json = """
            {
              "Mounts": [
                {
                  "ID": 12,
                  "Patch": "2.1",
                  "ItemID": 5,
                  "Sources": [
                    { "Type": "Tribal", "Text": "A tribe", "RelatedType": "Quest", "RelatedID": 3 }
                  ]
                }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(12, out MountSources mount));
        Assert.Equal("2.1", mount.Patch);
        Assert.Equal(5u, mount.ItemId);
        MountSourceLine line = Assert.Single(mount.Lines);
        Assert.Equal("Tribal", line.Category);
        Assert.Equal("Quest", line.RelatedKind);
        Assert.Equal(3u, line.RelatedId);
    }

    [Fact]
    public void MissingMountReturnsFalseInsteadOfThrowing()
    {
        MountShelf shelf = MountShelf.Parse("""{ "mounts": [ { "id": 1, "patch": "2.0" } ] }""");

        Assert.False(shelf.TryGet(999, out _));
    }

    [Fact]
    public void EmptyDocumentHasNoMounts()
    {
        MountShelf shelf = MountShelf.Parse("{}");

        Assert.Equal(0, shelf.Count);
    }

    [Fact]
    public void ReadsTheAvailabilityClassAndTheBlocksCounts()
    {
        const string json = """
            {
              "schemaVersion": 4,
              "availability": { "available": 1, "premium": 1 },
              "mounts": [
                { "id": 1, "availability": "available", "sources": [ { "type": "Trial", "text": "a trial" } ] },
                { "id": 2, "availability": "premium", "sources": [ { "type": "Premium", "text": "Online Store" } ] },
                { "id": 3, "sources": [ { "type": "Trial", "text": "a trial" } ] }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(1, out MountSources trial));
        Assert.Equal(MountAvailability.Available, trial.Availability);

        Assert.True(shelf.TryGet(2, out MountSources premium));
        Assert.Equal(MountAvailability.Premium, premium.Availability);

        // A row that states nothing is Unknown, which is what an older snapshot's every row reads as: the reader
        // may never invent a class, because the class is data and guessing it here would hide a stale file.
        Assert.True(shelf.TryGet(3, out MountSources silent));
        Assert.Equal(MountAvailability.Unknown, silent.Availability);

        Assert.True(shelf.HasAvailabilityCounts);
        Assert.Equal(2, shelf.AvailabilityCounts.Count);
        Assert.Equal(MountAvailability.Available, shelf.AvailabilityCounts[0].Class);
        Assert.Equal(1, shelf.AvailabilityCounts[1].Mounts);
    }

    [Fact]
    public void ASnapshotWithNoAvailabilityBlockReadsAsNoCountsRatherThanZeros()
    {
        // The settings window and the validator both tell "no counts recorded" apart from "every class is empty",
        // so an absent block must not arrive as a list of zeros.
        MountShelf shelf = MountShelf.Parse("""{ "schemaVersion": 4, "mounts": [ { "id": 1 } ] }""");

        Assert.False(shelf.HasAvailabilityCounts);
        Assert.Empty(shelf.AvailabilityCounts);
    }

    [Fact]
    public void AnAvailabilityValueThisBuildDoesNotKnowReadsAsUnknown()
    {
        MountShelf shelf = MountShelf.Parse(
            """{ "schemaVersion": 4, "mounts": [ { "id": 1, "availability": "someday" } ] }""");

        Assert.True(shelf.TryGet(1, out MountSources row));
        Assert.Equal(MountAvailability.Unknown, row.Availability);
    }

    [Fact]
    public void ReadsTheHeaderThisBuildUnderstands()
    {
        const string json = """
            {
              "schemaVersion": 4,
              "generated": "2026-11-01",
              "source": "ffxivcollect.com",
              "enrichment": "full",
              "attribution": "Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.",
              "attributionUrl": "https://ffxivcollect.com",
              "mounts": [ { "id": 1, "patch": "2.0", "sources": [] } ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.Equal(MountShelf.CurrentSchemaVersion, shelf.SchemaVersion);
        Assert.Equal("2026-11-01", shelf.Generated);
        Assert.Equal("ffxivcollect.com", shelf.Source);
        Assert.Equal(MountShelfHeader.FullEnrichment, shelf.Enrichment);
        Assert.True(shelf.HasAttribution);
        Assert.False(shelf.IsStale);
        Assert.False(shelf.IsNewer);
        Assert.False(shelf.IsSheetOnly);
        Assert.Equal(1, shelf.Count);
    }

    [Fact]
    public void ASnapshotWithNoHeaderIsReadAsStaleAndNotRefused()
    {
        // The first shipped shape. Losing every "How to obtain" line because the file predates the
        // header would be a far worse outcome than reading it and saying it is old.
        MountShelf shelf = MountShelf.Parse("""{ "mounts": [ { "id": 1, "patch": "2.0" } ] }""");

        Assert.Equal(1, shelf.Count);
        Assert.Equal(MountShelf.LegacySchemaVersion, shelf.SchemaVersion);
        Assert.True(shelf.IsStale);
        Assert.False(shelf.HasAttribution);
        Assert.Equal(string.Empty, shelf.AttributionUrl);
    }

    [Fact]
    public void ANegativeSchemaVersionIsTreatedAsNoHeaderAtAll()
    {
        MountShelf shelf = MountShelf.Parse("""{ "schemaVersion": -3, "mounts": [ { "id": 1, "patch": "2.0" } ] }""");

        Assert.Equal(MountShelf.LegacySchemaVersion, shelf.SchemaVersion);
        Assert.True(shelf.IsStale);
    }

    [Fact]
    public void ASnapshotFromANewerGeneratorIsNewerAndNotStale()
    {
        // The two mismatches are different facts: an older file is missing fields, a newer one has
        // fields this build ignores. Reporting either as the other hides which side to fix.
        MountShelf shelf = MountShelf.Parse("""{ "schemaVersion": 5, "mounts": [ { "id": 1, "patch": "2.0" } ] }""");

        Assert.True(shelf.IsNewer);
        Assert.False(shelf.IsStale);
    }

    [Fact]
    public void ASheetOnlySnapshotSaysSo()
    {
        MountShelf shelf = MountShelf.Parse("""
            { "schemaVersion": 4, "enrichment": "sheet-only", "mounts": [ { "id": 1, "patch": "" } ] }
            """);

        Assert.True(shelf.IsSheetOnly);
    }

    [Fact]
    public void DescribeNamesTheSourceTheDateAndTheVersion()
    {
        MountShelf shelf = MountShelf.Parse("""
            { "schemaVersion": 4, "generated": "2026-11-01", "source": "ffxivcollect.com", "enrichment": "full", "mounts": [] }
            """);

        Assert.Equal("ffxivcollect.com, 2026-11-01, schema v4", shelf.Describe());
    }

    [Fact]
    public void DescribeCallsOutAnOfflineBuildAndAnOldSnapshot()
    {
        MountShelf sheetOnly = MountShelf.Parse("""
            { "schemaVersion": 4, "generated": "2026-11-01", "source": "ffxivcollect.com", "enrichment": "sheet-only", "mounts": [] }
            """);
        MountShelf stale = MountShelf.Parse("""
            { "generated": "2026-01-01", "source": "ffxivcollect.com", "mounts": [] }
            """);

        Assert.Equal(
            "ffxivcollect.com, 2026-11-01, schema v4 (built offline: no obtain data refreshed)",
            sheetOnly.Describe());
        Assert.Equal(
            "ffxivcollect.com, 2026-01-01, schema v0 (older than this build reads: v4)",
            stale.Describe());
    }

    [Fact]
    public void DescribeAdmitsWhenTheFileSaysNothingAboutItself()
    {
        string text = MountShelf.Parse("""{ "mounts": [] }""").Describe();

        Assert.Equal("unknown source, schema v0 (older than this build reads: v4)", text);
    }

    [Fact]
    public void NonObjectRootIsRejected()
    {
        Assert.Throws<FormatException>(() => MountShelf.Parse("[]"));
    }

    [Fact]
    public void NonArrayMountsIsRejected()
    {
        Assert.Throws<FormatException>(() => MountShelf.Parse("""{ "mounts": {} }"""));
    }

    [Fact]
    public void ReadsTheSheetCrossCheckTheGeneratorRecorded()
    {
        // Seats and movement are sheet facts, so the file does not carry them per mount: this block is
        // the only evidence the site's copies were compared against the game at build time.
        MountShelf shelf = MountShelf.Parse("""
            {
              "schemaVersion": 4,
              "enrichment": "full",
              "sheetCrossCheck": {
                "sheet": "Mount",
                "fields": ["ExtraSeats", "IsAirborne"],
                "mounts": 356,
                "seats": 356,
                "movement": 356
              },
              "mounts": [ { "id": 1, "patch": "2.0" } ]
            }
            """);

        Assert.True(shelf.HasCrossCheck);
        MountShelfCrossCheck cross = shelf.CrossCheck!;
        Assert.Equal("Mount", cross.Sheet);
        Assert.Equal(new[] { "ExtraSeats", "IsAirborne" }, cross.Fields);
        Assert.Equal(356, cross.RowsChecked);
        Assert.True(cross.Agrees);
        Assert.True(cross.NamesSheetFacts);
        Assert.Equal(
            "356/356 seats, 356/356 movement (Mount.ExtraSeats, Mount.IsAirborne)",
            shelf.DescribeCrossCheck());
    }

    [Fact]
    public void ASnapshotWithoutACrossCheckSaysItWasNotChecked()
    {
        // Null is "no check ran", and the diagnostics line has to say that rather than imply a pass.
        MountShelf shelf = MountShelf.Parse("""
            { "schemaVersion": 4, "enrichment": "full", "sheetCrossCheck": null, "mounts": [ { "id": 1 } ] }
            """);

        Assert.False(shelf.HasCrossCheck);
        Assert.Null(shelf.CrossCheck);
        Assert.StartsWith("not recorded", shelf.DescribeCrossCheck(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACrossCheckWithNoCountsIsNotACheck()
    {
        // A block that names the fields but claims nothing covers no row: reading it as a pass would be
        // exactly the kind of half-understood shape N4 forbids.
        MountShelf shelf = MountShelf.Parse("""
            { "schemaVersion": 4, "sheetCrossCheck": { "fields": ["ExtraSeats", "IsAirborne"] }, "mounts": [] }
            """);

        Assert.False(shelf.HasCrossCheck);
    }

    [Fact]
    public void ACrossCheckThatDisagreedIsNotAnAgreement()
    {
        MountShelf shelf = MountShelf.Parse("""
            {
              "schemaVersion": 4,
              "sheetCrossCheck": { "sheet": "Mount", "fields": ["ExtraSeats", "IsAirborne"], "mounts": 356, "seats": 355, "movement": 356 },
              "mounts": []
            }
            """);

        Assert.True(shelf.HasCrossCheck);
        Assert.False(shelf.CrossCheck!.Agrees);
        Assert.Contains("DISAGREEMENT", shelf.DescribeCrossCheck(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACrossCheckOfOtherColumnsDoesNotNameTheSheetFacts()
    {
        MountShelf shelf = MountShelf.Parse("""
            {
              "schemaVersion": 4,
              "sheetCrossCheck": { "sheet": "Mount", "fields": ["Order"], "mounts": 10, "seats": 10, "movement": 10 },
              "mounts": []
            }
            """);

        Assert.False(shelf.CrossCheck!.NamesSheetFacts);
    }
}
