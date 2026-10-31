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
    public void NonObjectRootIsRejected()
    {
        Assert.Throws<FormatException>(() => MountShelf.Parse("[]"));
    }

    [Fact]
    public void NonArrayMountsIsRejected()
    {
        Assert.Throws<FormatException>(() => MountShelf.Parse("""{ "mounts": {} }"""));
    }
}
