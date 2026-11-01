using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// The filter's counts, per type, as the shipped snapshot gives them: how many <em>mounts</em> carry the type,
    /// and, in the comment, how many source <em>lines</em> carry it. The two numbers differ wherever a mount has
    /// two lines of one type (Premium is 73 lines over 67 mounts), and a filter counts mounts - so this table is
    /// what "the counts match the dataset" means, and the line counts are kept beside it to keep the two apart.
    /// </summary>
    private static readonly (string Type, int Mounts, int Lines)[] Expected =
    [
        ("Premium", 67, 73),
        ("Achievement", 50, 50),
        ("Trial", 44, 48),
        ("Quest", 23, 25),
        ("PvP", 21, 24),
        ("Cosmic Exploration", 19, 23),
        ("Event", 22, 22),
        ("Wondrous Tails", 12, 21),
        ("Tribal", 21, 21),
        ("Raid", 14, 14),
        ("Occult Crescent", 9, 13),
        ("Island Sanctuary", 12, 13),
        ("Gold Saucer", 9, 9),
        ("V&C Dungeon", 5, 9),
        ("Deep Dungeon", 7, 9),
        ("Skybuilders", 8, 9),
        ("FATE", 6, 6),
        ("Treasure Hunt", 5, 5),
        ("Hunts", 5, 5),
        ("Purchase", 5, 5),
        ("Chaotic Raid", 2, 4),
        ("Bozja", 4, 4),
        ("Eureka", 3, 3),
        ("Gathering", 2, 2),
        ("Crafting", 2, 2),
        ("Other", 2, 2),
        ("Dungeon", 1, 1),
        ("Voyages", 1, 1),
    ];

    /// <summary>
    /// The taxonomy side of the shipped snapshot: every row carries at least one type, and every type is one the
    /// plugin knows. A row the filter cannot see, or a type it cannot name, fails here rather than in a player's
    /// filter.
    /// </summary>
    [Fact]
    public void ShippedDatasetTypesEveryMountFromTheKnownTaxonomy()
    {
        MountShelf shelf = MountShelf.Parse(ReadDataset());
        var typeless = new List<uint>();
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (uint id in shelf.Ids)
        {
            Assert.True(shelf.TryGet(id, out MountSources sources), $"The parser dropped mount {id}.");

            if (!sources.Lines.Any(line => !line.Kind.IsAny))
            {
                typeless.Add(id);
            }

            foreach (MountSourceLine line in sources.Lines)
            {
                if (!line.Kind.IsAny && !line.Kind.IsKnown)
                {
                    unknown.Add(line.Kind.Name);
                }
            }
        }

        Assert.True(typeless.Count == 0, $"{typeless.Count} mount(s) carry no obtain type: {string.Join(", ", typeless)}.");
        Assert.True(unknown.Count == 0, $"Type(s) outside the taxonomy: {string.Join(", ", unknown)}.");
    }

    /// <summary>
    /// The acceptance line for the taxonomy: the counts the filter shows are the dataset's own, per type, and the
    /// number beside a type in the combo is exactly how many mounts choosing it lists.
    /// </summary>
    [Fact]
    public void TheFiltersCountsMatchTheDataset()
    {
        MountShelf snapshot = MountShelf.Parse(ReadDataset());
        IReadOnlyList<MountShelfItem> shelf = ShippedShelf();
        IReadOnlyList<MountSourceCount> tally = MountSourceKinds.Tally(shelf);

        foreach ((string type, int mounts, int lines) in Expected)
        {
            MountSourceKind kind = MountSourceKinds.Parse(type);

            Assert.True(kind.IsKnown, $"\"{type}\" is not in the taxonomy.");

            // Three numbers that have to be one: what filtering by the type lists, what the combo counts, and what
            // the committed snapshot holds.
            Assert.Equal(mounts, Filter(kind).Apply(shelf).Count);
            Assert.Equal(mounts, tally.Single(count => count.Kind == kind).Mounts);
            Assert.Equal(lines, CountLines(snapshot, kind));
        }
    }

    private static int CountLines(MountShelf snapshot, MountSourceKind kind)
    {
        var lines = 0;

        foreach (uint id in snapshot.Ids)
        {
            snapshot.TryGet(id, out MountSources sources);
            lines += sources.Lines.Count(line => line.Kind == kind);
        }

        return lines;
    }

    /// <summary>
    /// The combo's counts and the filter it labels cannot drift: for every type the snapshot carries, the number
    /// in the label is the number of mounts choosing it returns.
    /// </summary>
    [Fact]
    public void EveryTallyCountIsWhatItsFilterSelects()
    {
        IReadOnlyList<MountShelfItem> shelf = ShippedShelf();
        IReadOnlyList<MountSourceCount> tally = MountSourceKinds.Tally(shelf);

        Assert.Equal(28, tally.Count);
        Assert.All(tally, count => Assert.Equal(count.Mounts, Filter(count.Kind).Apply(shelf).Count));

        // Every mount has a type, so the per-type counts add up to the rows plus the extra types of the mounts
        // that carry two - and never to fewer than one per mount.
        Assert.True(tally.Sum(count => count.Mounts) >= shelf.Count);
    }

    /// <summary>
    /// The snapshot, read as the filter reads it: only the source lines matter here, so the rows are built with
    /// no name, patch or flags - what is under test is the taxonomy, not the sheet.
    /// </summary>
    private static IReadOnlyList<MountShelfItem> ShippedShelf()
    {
        MountShelf shelf = MountShelf.Parse(ReadDataset());
        var ids = new List<uint>(shelf.Ids);
        ids.Sort();

        var items = new List<MountShelfItem>(ids.Count);

        foreach (uint id in ids)
        {
            shelf.TryGet(id, out MountSources sources);
            items.Add(new MountShelfItem(id, $"Mount {id}", 0u, 0u, 1, sources.Patch, false, string.Empty, string.Empty, string.Empty, sources.Lines));
        }

        return items;
    }

    private static MountQuery Filter(MountSourceKind kind) =>
        new(null, MountOwnership.All, MountExpansion.All, MountOrder.Name) { Source = kind };

    private static string ReadDataset()
    {
        string path = Path.Combine(AppContext.BaseDirectory, DatasetFileName);
        Assert.True(File.Exists(path), $"The dataset was not copied next to the test binary: {path}");
        return File.ReadAllText(path);
    }
}
