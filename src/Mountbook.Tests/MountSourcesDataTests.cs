using System;
using System.IO;
using System.Text.Json;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The committed dataset is a shipped artifact, so it is parsed here exactly as the plugin parses
/// it at runtime. A bad regeneration that drops ids, patches or source text fails this test.
/// </summary>
public sealed class MountSourcesDataTests
{
    private const string DatasetFileName = "mount-sources.json";

    [Fact]
    public void ShippedDatasetParsesAndCoversEveryRow()
    {
        string json = ReadDataset();
        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.Count > 300, $"Expected more than 300 mounts in the dataset, found {shelf.Count}.");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement mounts = document.RootElement.GetProperty("mounts");
        Assert.Equal(mounts.GetArrayLength(), shelf.Count);

        foreach (JsonElement mount in mounts.EnumerateArray())
        {
            uint id = mount.GetProperty("id").GetUInt32();
            Assert.True(id > 0, "A dataset row has no mount id.");

            string patch = mount.GetProperty("patch").GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(patch), $"Mount {id} has an empty patch.");

            Assert.True(shelf.TryGet(id, out MountSources sources), $"The parser dropped mount {id}.");
            Assert.Equal(patch, sources.Patch);

            foreach (MountSourceLine line in sources.Lines)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(line.Detail),
                    $"Mount {id} has a source line without text.");
            }

            foreach (JsonElement source in mount.GetProperty("sources").EnumerateArray())
            {
                string text = source.GetProperty("text").GetString() ?? string.Empty;
                Assert.False(
                    string.IsNullOrWhiteSpace(text),
                    $"Mount {id} has a source entry without text.");
            }
        }
    }

    [Fact]
    public void ShippedDatasetNamesItsSourceAndGenerationDate()
    {
        using JsonDocument document = JsonDocument.Parse(ReadDataset());

        Assert.Equal("ffxivcollect.com", document.RootElement.GetProperty("source").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("generated").GetString()));
    }

    [Fact]
    public void ShippedDatasetDeclaresTheSchemaVersionThisBuildReads()
    {
        // The reader and the generator are two programs; the number is what tells a stale snapshot
        // from a fresh one, so a file that does not carry it is a build failure.
        MountShelf shelf = MountShelf.Parse(ReadDataset());

        Assert.Equal(MountShelf.CurrentSchemaVersion, shelf.SchemaVersion);
        Assert.False(shelf.IsStale, $"The shipped snapshot is v{shelf.SchemaVersion}, stale for a v{MountShelf.CurrentSchemaVersion} build.");
        Assert.False(shelf.IsNewer, $"The shipped snapshot is v{shelf.SchemaVersion}, newer than this build reads (v{MountShelf.CurrentSchemaVersion}).");
        Assert.Equal(MountShelfHeader.FullEnrichment, shelf.Enrichment);
    }

    [Fact]
    public void ShippedDatasetCreditsTheVolunteerProjectItComesFrom()
    {
        MountShelf shelf = MountShelf.Parse(ReadDataset());

        Assert.True(shelf.HasAttribution, "The shipped snapshot carries no attribution or no attribution URL.");
        Assert.Contains("FFXIV Collect", shelf.Attribution, StringComparison.Ordinal);
        Assert.Equal("https://ffxivcollect.com", shelf.AttributionUrl);
    }

    [Fact]
    public void ShippedDatasetStaysInsideTheSizeBudget()
    {
        // 120 KB: the file is embedded in the assembly and downloaded by every player, and the API's
        // description copy is the field that would blow it (83 KB of metadata against 251 KB with it).
        long bytes = new FileInfo(Path.Combine(AppContext.BaseDirectory, DatasetFileName)).Length;

        Assert.True(bytes <= 122_880, $"The snapshot is {bytes} bytes, over the 120 KB budget.");
    }

    [Fact]
    public void ShippedDatasetRecordsTheSheetCrossCheckOfSeatsAndMovement()
    {
        // Seats and movement are deliberately not rows in the file: the game's Mount sheet answers both
        // for every mount, and a copy here could contradict a patched client. The cross-check block is
        // what proves the site's copies were compared against that sheet before this file shipped.
        MountShelf shelf = MountShelf.Parse(ReadDataset());

        Assert.True(shelf.HasCrossCheck, "The shipped snapshot records no sheet cross-check for seats/movement.");
        MountShelfCrossCheck cross = shelf.CrossCheck!;

        Assert.Equal("Mount", cross.Sheet);
        Assert.True(cross.NamesSheetFacts, $"The cross-check names [{string.Join(", ", cross.Fields)}].");
        Assert.Equal(shelf.Count, cross.RowsChecked);
        Assert.True(
            cross.Agrees,
            $"{cross.SeatsAgreed}/{cross.RowsChecked} seat counts and {cross.MovementAgreed}/{cross.RowsChecked} movement values agreed.");
    }

    [Fact]
    public void ShippedDatasetCarriesNoApiProse()
    {
        // Every one of these is the site's copy of text the game's own sheets carry; shipping it is
        // both a duplicate and three times the file.
        using JsonDocument document = JsonDocument.Parse(ReadDataset());

        foreach (JsonElement mount in document.RootElement.GetProperty("mounts").EnumerateArray())
        {
            foreach (string field in new[] { "description", "enhancedDescription", "enhanced_description", "tooltip" })
            {
                Assert.False(
                    mount.TryGetProperty(field, out _),
                    $"Mount {mount.GetProperty("id").GetUInt32()} carries \"{field}\", the API's own prose.");
            }
        }
    }

    private static string ReadDataset()
    {
        string path = Path.Combine(AppContext.BaseDirectory, DatasetFileName);
        Assert.True(File.Exists(path), $"The dataset was not copied next to the test binary: {path}");
        return File.ReadAllText(path);
    }
}
