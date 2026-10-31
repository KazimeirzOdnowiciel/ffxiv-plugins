using System.Collections.Generic;
using System.Text.Json;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The wanted list as a shareable document: what an export writes, what an import keeps, and every
/// way a pasted file can be wrong. The names in the file are a comment, so only ids are trusted.
/// </summary>
public sealed class MountWantedListTests
{
    private static readonly List<uint> Known = [1u, 26u, 105u, 412u];

    [Fact]
    public void WriteNamesItsFormatAndVersion()
    {
        using JsonDocument document = JsonDocument.Parse(MountWantedList.Write([1u], _ => "Company Chocobo"));
        JsonElement root = document.RootElement;

        Assert.Equal("mountbook.wanted", root.GetProperty("format").GetString());
        Assert.Equal(MountWantedList.CurrentVersion, root.GetProperty("version").GetInt32());
    }

    [Fact]
    public void WriteSortsIdsAndKeepsTheNameBesideEachOne()
    {
        using JsonDocument document = JsonDocument.Parse(
            MountWantedList.Write([412u, 1u], id => id == 1u ? "Company Chocobo" : null));

        JsonElement.ArrayEnumerator mounts = document.RootElement.GetProperty("mounts").EnumerateArray();
        var ids = new List<uint>();

        foreach (JsonElement mount in mounts)
        {
            ids.Add(mount.GetProperty("id").GetUInt32());
        }

        Assert.Equal([1u, 412u], ids);

        // An id the book cannot name still exports: the id is what an import reads.
        Assert.Equal("Company Chocobo", document.RootElement.GetProperty("mounts")[0].GetProperty("name").GetString());
        Assert.False(document.RootElement.GetProperty("mounts")[1].TryGetProperty("name", out _));
    }

    [Fact]
    public void WriteListsADuplicateIdOnce()
    {
        using JsonDocument document = JsonDocument.Parse(MountWantedList.Write([26u, 26u, 1u], _ => null));

        Assert.Equal(2, document.RootElement.GetProperty("mounts").GetArrayLength());
    }

    [Fact]
    public void WriteAndParseRoundTrip()
    {
        string json = MountWantedList.Write([412u, 26u], _ => null);

        Assert.True(MountWantedList.TryParse(json, Known, out MountWantedImport result, out string error));
        Assert.Equal(string.Empty, error);
        Assert.Equal([26u, 412u], result.Wanted);
        Assert.Equal(0, result.Duplicates);
        Assert.Equal(0, result.Unknown);
        Assert.Equal(0, result.Invalid);
    }

    [Fact]
    public void ParseAcceptsABareArrayOfIds()
    {
        Assert.True(MountWantedList.TryParse("[26, 1]", Known, out MountWantedImport result, out _));
        Assert.Equal([1u, 26u], result.Wanted);
    }

    [Fact]
    public void ParseAcceptsABareArrayOfObjects()
    {
        Assert.True(MountWantedList.TryParse("[{\"id\":105}]", Known, out MountWantedImport result, out _));
        Assert.Equal([105u], result.Wanted);
    }

    [Fact]
    public void ParseTreatsAMissingVersionAsThisOne()
    {
        Assert.True(MountWantedList.TryParse("{\"mounts\":[26]}", Known, out MountWantedImport result, out _));
        Assert.Equal([26u], result.Wanted);
    }

    [Fact]
    public void ParseCountsAnIdListedTwiceAsADuplicate()
    {
        Assert.True(MountWantedList.TryParse(
            "{\"version\":1,\"mounts\":[26,26,{\"id\":26}]}",
            Known,
            out MountWantedImport result,
            out _));

        Assert.Equal([26u], result.Wanted);
        Assert.Equal(2, result.Duplicates);
    }

    [Fact]
    public void ParseDropsIdsThisBookHasNoMountFor()
    {
        // A friend on a newer patch exports a mount this build cannot draw: importing the id would
        // make the wanted count promise a card the list can never show.
        Assert.True(MountWantedList.TryParse("{\"mounts\":[26,9999]}", Known, out MountWantedImport result, out _));

        Assert.Equal([26u], result.Wanted);
        Assert.Equal(1, result.Unknown);
    }

    [Fact]
    public void ParseCountsEntriesWithNoUsableId()
    {
        Assert.True(MountWantedList.TryParse(
            "{\"mounts\":[26,\"x\",0,-5,{\"name\":\"no id\"}]}",
            Known,
            out MountWantedImport result,
            out _));

        Assert.Equal([26u], result.Wanted);
        Assert.Equal(4, result.Invalid);
    }

    [Fact]
    public void ParseRefusesAFileFromANewerBuild()
    {
        Assert.False(MountWantedList.TryParse(
            "{\"version\":9,\"mounts\":[26]}",
            Known,
            out MountWantedImport result,
            out string error));

        Assert.Contains("newer Mountbook", error);
        Assert.Empty(result.Wanted);
    }

    [Fact]
    public void ParseRefusesAVersionThatIsNotANumber()
    {
        Assert.False(MountWantedList.TryParse("{\"version\":\"one\",\"mounts\":[]}", Known, out _, out string error));
        Assert.Contains("must be a number", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseSaysWhenTheClipboardIsEmpty(string json)
    {
        Assert.False(MountWantedList.TryParse(json, Known, out _, out string error));
        Assert.Contains("empty", error);
    }

    [Fact]
    public void ParseSaysWhenTheTextIsNotJson()
    {
        Assert.False(MountWantedList.TryParse("Aithon\nAstrope", Known, out _, out string error));
        Assert.Contains("not JSON", error);
    }

    [Fact]
    public void ParseRefusesAJsonDocumentThatIsNotAWantedList()
    {
        Assert.False(MountWantedList.TryParse("\"just a string\"", Known, out _, out _));
        Assert.False(MountWantedList.TryParse("{\"format\":\"mountbook.wanted\"}", Known, out _, out _));
        Assert.False(MountWantedList.TryParse("{\"mounts\":26}", Known, out _, out _));
    }

    [Fact]
    public void ParseAcceptsAListWithNothingInIt()
    {
        Assert.True(MountWantedList.TryParse("{\"mounts\":[]}", Known, out MountWantedImport result, out _));
        Assert.Empty(result.Wanted);
    }

    [Fact]
    public void SummaryNamesTheLeftovers()
    {
        Assert.Equal("Imported 2 mounts.", MountWantedList.ImportSummary(2, 0, 0, 0));
        Assert.Equal("Imported 1 mount (1 was already wanted).", MountWantedList.ImportSummary(1, 1, 0, 0));
        Assert.Equal(
            "Imported 3 mounts (2 were already wanted, 1 is not in your book, 4 entries had no mount id).",
            MountWantedList.ImportSummary(3, 2, 1, 4));
        Assert.Equal("Nothing new to import (1 was already wanted).", MountWantedList.ImportSummary(0, 1, 0, 0));
    }
}
