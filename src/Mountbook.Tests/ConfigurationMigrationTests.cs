using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mountbook.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The settings file's shape and the ladder that upgrades it: a file written by an older build has to mean
/// what it meant, and a damaged one has to arrive as something the plugin can draw.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are real settings files, not strings: <c>Fixtures/config-v1-pre-grind.json</c>,
/// <c>config-v1-grind.json</c> and <c>config-v1-watch.json</c> are the member lists of the three commits that
/// each added a persisted collection (26e6ba2, 6fa6035, 75afaba), written the way Dalamud writes them
/// (<c>$type</c>, indented, PascalCase). <c>config-v1-handedited.json</c> is the same era's file damaged the
/// way a hand edit damages one: null collections, dead records, out-of-range enums, a filter spelled by hand.
/// </para>
/// <para>
/// They are read with Newtonsoft's <c>JsonConvert.DeserializeObject&lt;Configuration&gt;</c>, which is what the
/// shipped loader does: <c>PluginConfigurations.LoadForType&lt;T&gt;</c> builds
/// <c>JsonConvert.DeserializeObject&lt;T&gt;(text, new JsonSerializerSettings())</c> plus one converter that
/// only applies to Dalamud's own assembly, so a Mountbook type is read with TypeNameHandling.None and the
/// file's <c>$type</c> is ignored. Measured against Dalamud 15.0.3.9 with the real loader and the real
/// Configuration type; docs/config-migration.md has the run.
/// </para>
/// </remarks>
public sealed class ConfigurationMigrationTests
{
    /// <summary>A file with every collection explicitly null, which is what a careless hand edit produces.</summary>
    private const string NullCollections = """
    {
      "Version": 1,
      "SearchText": null,
      "WantedMounts": null,
      "SavedSource": null,
      "RecentCollected": null,
      "Grinds": null,
      "Watches": null
    }
    """;

    [Fact]
    public void AFileFromBeforeTheGrindTrackerKeepsEveryFieldItCarried()
    {
        Configuration config = Load("config-v1-pre-grind.json");

        config.Migrate();

        Assert.Equal(Configuration.CurrentVersion, config.Version);
        Assert.True(config.MainWindowOpen);
        Assert.Equal("trial", config.SearchText);
        Assert.Equal(MountOwnership.Unowned, config.Ownership);
        Assert.Equal(MountExpansion.Stormblood, config.Expansion);
        Assert.Equal(MountOrder.NewestFirst, config.Order);
        Assert.Equal(MountViewMode.Table, config.ViewMode);
        Assert.Equal(MountGrouping.Patch, config.GroupBy);
        Assert.Equal(373u, config.SelectedMountId);
        Assert.Equal(new HashSet<uint> { 192, 82 }, config.WantedMounts);
        Assert.Equal("Trial", config.SavedSource);
        Assert.Equal(new HashSet<uint> { 1, 6, 9 }, config.KnownOwnedIds);
        Assert.Equal(1730000000L, config.RecentCollected[192]);
        Assert.Equal("aurora", config.LookName);
        Assert.True(config.FollowGameTheme);

        // The two shapes this file predates arrive empty rather than null.
        Assert.Empty(config.Grinds);
        Assert.Empty(config.Watches);

        // Members that did not exist when the file was written keep their declared default.
        Assert.Equal(MountBoardSort.Patch, config.BoardSort);
        Assert.False(config.BoardWindowOpen);
    }

    [Fact]
    public void AFileFromTheGrindEraKeepsItsRecordsAndGainsNoWatches()
    {
        Configuration config = Load("config-v1-grind.json");

        config.Migrate();

        Assert.Equal(Configuration.CurrentVersion, config.Version);
        Assert.Equal(MountOwnership.Wanted, config.Ownership);
        Assert.Equal(MountGrouping.Expansion, config.GroupBy);
        Assert.Equal("wondrous", config.SearchText);
        Assert.Equal(new HashSet<uint> { 192, 82, 300 }, config.WantedMounts);
        Assert.Equal("Achievement", config.SavedSource);
        Assert.Equal(MountBoardSort.Price, config.BoardSort);
        Assert.True(config.BoardWindowOpen);

        // The two records the player entered survive the sweep with every member intact: a record whose target
        // is zero is how a track rides on the shop price, not an empty record.
        Assert.Equal(2, config.Grinds.Count);

        MountGrindRecord few = config.Grinds[82];
        Assert.Equal(300, few.Have);
        Assert.Equal(1200, few.Want);
        Assert.Equal("shared fate runs", few.Note);
        Assert.Equal(1731111111L, few.UpdatedAt);

        MountGrindRecord priceOnly = config.Grinds[300];
        Assert.Equal(450, priceOnly.Have);
        Assert.Equal(0, priceOnly.Want);
        Assert.Null(priceOnly.Note);

        Assert.Empty(config.Watches);
    }

    [Fact]
    public void AFileFromTheWatchEraKeepsEveryFieldItCarried()
    {
        Configuration config = Load("config-v1-watch.json");

        config.Migrate();

        Assert.Equal(Configuration.CurrentVersion, config.Version);
        Assert.Equal(MountOrder.RarestFirst, config.Order);
        Assert.Equal(MountExpansion.Shadowbringers, config.Expansion);
        Assert.Equal("Gold Saucer", config.SavedSource);
        Assert.Equal(new HashSet<uint> { 1, 6, 9, 15, 18 }, config.KnownOwnedIds);
        Assert.Equal(1730000000L, config.RecentCollected[192]);
        Assert.Equal(1731111111L, config.RecentCollected[82]);

        Assert.Equal(2, config.Watches.Count);

        MountWatchRecord crossed = config.Watches[82];
        Assert.Equal(250000, crossed.Threshold);
        Assert.Equal(1731200000L, crossed.CheckedAt);
        Assert.Equal(0, crossed.LastSeen);
        Assert.True(crossed.Alerted);

        MountWatchRecord watching = config.Watches[192];
        Assert.Equal(900000, watching.Threshold);
        Assert.Equal(1731200500L, watching.CheckedAt);
        Assert.Equal(875000, watching.LastSeen);
        Assert.False(watching.Alerted);

        Assert.Equal(2, config.Grinds.Count);
    }

    [Fact]
    public void AFileWrittenBeforeTheLadderIsBroughtUpToTheCurrentVersion()
    {
        Configuration config = Load("config-v1-pre-grind.json");

        Assert.Equal(1, config.Version);

        config.Migrate();

        Assert.Equal(Configuration.CurrentVersion, config.Version);
    }

    [Fact]
    public void AVersionFromANewerBuildIsLeftAlone()
    {
        Configuration config = JsonConvert.DeserializeObject<Configuration>(
            """{ "Version": 99, "WantedMounts": [ 1 ], "SavedSource": "pvp" }""")!;

        config.Migrate();

        // Forwards compatible on purpose: a newer build knows shapes this one does not, so the ladder must not
        // walk it backwards - but the always-on guards still run, which is why the spelling is untouched here
        // and the collection is not null.
        Assert.Equal(99, config.Version);
        Assert.Equal("pvp", config.SavedSource);
        Assert.NotNull(config.WantedMounts);
        Assert.NotNull(config.Grinds);
    }

    [Fact]
    public void AMissingCollectionKeepsItsInitialiserRatherThanArrivingNull()
    {
        // Measured, not assumed: the old comment in Configuration said Dalamud leaves a missing collection null.
        // It does not - the deserialiser runs the constructor (so the member initialisers run) and then fills
        // only the members the file names. The null guards are for an explicit null, which is the next test.
        Configuration config = Load("config-v1-pre-grind.json");

        Assert.NotNull(config.Grinds);
        Assert.NotNull(config.Watches);
        Assert.NotNull(config.RecentCollected);
        Assert.NotNull(config.WantedMounts);
    }

    [Fact]
    public void AnExplicitNullCollectionIsRepaired()
    {
        Configuration config = JsonConvert.DeserializeObject<Configuration>(NullCollections)!;

        Assert.Null(config.Grinds);
        Assert.Null(config.Watches);

        config.Migrate();

        Assert.NotNull(config.Grinds);
        Assert.NotNull(config.Watches);
        Assert.NotNull(config.RecentCollected);
        Assert.NotNull(config.WantedMounts);
        Assert.Equal(string.Empty, config.SearchText);
        Assert.Equal(string.Empty, config.SavedSource);

        // Null is still meaningful for the owned baseline: an empty set says "owned nothing", null says "never
        // looked", and only the first reconcile may turn one into the other.
        Assert.Null(config.KnownOwnedIds);
    }

    [Fact]
    public void TheGrindStepDropsRecordsThatHoldNothing()
    {
        Configuration config = Load("config-v1-handedited.json");

        Assert.Equal(3, config.Grinds.Count);
        Assert.Null(config.Grinds[5]);

        config.Migrate();

        // A null entry and a record with a negative count, no target and a whitespace note are both noise: the
        // pane and the board only ever draw a record MountGrind.IsTracked accepts.
        Assert.Single(config.Grinds);
        Assert.True(config.Grinds.ContainsKey(9));

        MountGrindRecord kept = config.Grinds[9];
        Assert.Equal(300, kept.Have);
        Assert.Equal("saved 300", kept.Note);
        Assert.Equal(1730000000L, kept.UpdatedAt);
    }

    [Fact]
    public void TheWatchStepDropsRecordsWithNoThreshold()
    {
        Configuration config = Load("config-v1-handedited.json");

        Assert.Equal(3, config.Watches.Count);

        config.Migrate();

        // "No threshold" is how the pane switches a watch off, and the feature never persists that: keeping one
        // would make "watching nothing" and "watching with no threshold" both persistable.
        Assert.Single(config.Watches);

        MountWatchRecord kept = config.Watches[13];
        Assert.Equal(500000, kept.Threshold);

        // A hand-edited negative timestamp reads as "never", not as a moment in 1969.
        Assert.Equal(0L, kept.CheckedAt);
        Assert.Equal(0, kept.LastSeen);
        Assert.True(kept.Alerted);
    }

    [Fact]
    public void TheSourceStepRewritesTheSavedFilterAsTheTaxonomySpellsIt()
    {
        Configuration config = Load("config-v1-handedited.json");

        Assert.Equal("pvp ", config.SavedSource);

        config.Migrate();

        Assert.Equal("PvP", config.SavedSource);
    }

    [Fact]
    public void TheSourceStepClearsAValueThatNamesNoType()
    {
        Configuration config = JsonConvert.DeserializeObject<Configuration>(
            """{ "Version": 1, "SavedSource": "beast tribe" }""")!;

        config.Migrate();

        // The main window refuses to adopt a type the taxonomy does not know (an unknown value would empty the
        // shelf for a frame), so the file should not keep carrying one.
        Assert.Equal(string.Empty, config.SavedSource);
    }

    [Fact]
    public void TheGuardsRepairADamagedFileWhoseVersionClaimsToBeCurrent()
    {
        // Every guard runs on a load, not only on a version step: a hand edit does not bump Version, so a file
        // claiming to be current is exactly where an "only migrate when behind" ladder would skip the work.
        Configuration config = JsonConvert.DeserializeObject<Configuration>(
            """{ "Version": 4, "Ownership": 99, "Order": 77, "Expansion": -3, "ViewMode": 42, "GroupBy": -1, "LookName": " ", "BoardSort": 9 }""")!;

        config.Migrate();

        Assert.Equal(MountOwnership.All, config.Ownership);
        Assert.Equal(MountOrder.Name, config.Order);
        Assert.Equal(MountExpansion.All, config.Expansion);
        Assert.Equal(MountViewMode.Grid, config.ViewMode);
        Assert.Equal(MountGrouping.None, config.GroupBy);
        Assert.Equal("eorzea", config.LookName);
        Assert.Equal(MountBoardSort.Patch, config.BoardSort);
    }

    [Fact]
    public void ResetToDefaultsPutsEveryMemberBack()
    {
        Configuration config = Load("config-v1-watch.json");
        config.Migrate();

        config.ResetToDefaults();

        var fresh = new Configuration();

        Assert.Equal(Configuration.CurrentVersion, config.Version);
        Assert.Equal(fresh.MainWindowOpen, config.MainWindowOpen);
        Assert.Equal(fresh.SearchText, config.SearchText);
        Assert.Equal(fresh.Ownership, config.Ownership);
        Assert.Equal(fresh.Expansion, config.Expansion);
        Assert.Equal(fresh.Order, config.Order);
        Assert.Equal(fresh.ViewMode, config.ViewMode);
        Assert.Equal(fresh.GroupBy, config.GroupBy);
        Assert.Equal(fresh.SelectedMountId, config.SelectedMountId);
        Assert.Equal(fresh.SavedSource, config.SavedSource);
        Assert.Equal(fresh.LookName, config.LookName);
        Assert.Equal(fresh.BoardSort, config.BoardSort);
        Assert.Equal(fresh.BoardWindowOpen, config.BoardWindowOpen);
        Assert.Equal(fresh.FollowGameTheme, config.FollowGameTheme);

        Assert.Empty(config.WantedMounts);
        Assert.Empty(config.RecentCollected);
        Assert.Empty(config.Grinds);
        Assert.Empty(config.Watches);
        Assert.Null(config.KnownOwnedIds);
    }

    [Fact]
    public void ResetToDefaultsKeepsTheCollectionsTheWindowsAreHolding()
    {
        // The main window copies the wanted set into its view state once a frame; replacing the instance would
        // leave that frame drawing the list the player just cleared.
        Configuration config = Load("config-v1-watch.json");
        config.Migrate();

        HashSet<uint> wanted = config.WantedMounts;
        Dictionary<uint, MountGrindRecord> grinds = config.Grinds;

        config.ResetToDefaults();

        Assert.Same(wanted, config.WantedMounts);
        Assert.Same(grinds, config.Grinds);
        Assert.Empty(wanted);
        Assert.Empty(grinds);
    }

    [Fact]
    public void EveryMemberAFixtureCarriesIsStillAMemberOfTheShape()
    {
        // The fixtures are the acceptance for "an old file still means what it meant", so a renamed or removed
        // member has to fail here rather than read as a default and quietly pass the migration tests above.
        // The files are Dalamud-shaped, so the root carries Newtonsoft's type metadata, which is not a member.
        var members = typeof(Configuration)
            .GetProperties()
            .Select(property => property.Name)
            .Append("$type")
            .ToHashSet(StringComparer.Ordinal);

        // Every member either record type carries, plus the type metadata a Dalamud-written file has.
        var recordMembers = typeof(MountGrindRecord)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(MountWatchRecord).GetProperties().Select(property => property.Name))
            .Append("$type")
            .ToHashSet(StringComparer.Ordinal);

        foreach (string fixture in new[] { "config-v1-pre-grind.json", "config-v1-grind.json", "config-v1-watch.json", "config-v1-handedited.json" })
        {
            JObject root = JObject.Parse(File.ReadAllText(FixturePath(fixture)));

            foreach (JProperty property in root.Properties())
            {
                Assert.True(members.Contains(property.Name), $"{fixture} carries '{property.Name}', which the shape no longer has");
            }
        }

        JObject damaged = JObject.Parse(File.ReadAllText(FixturePath("config-v1-handedited.json")));

        foreach (JObject record in new[] { "Grinds", "Watches" }
                     .Select(name => damaged[name])
                     .OfType<JObject>()
                     .SelectMany(map => map.Properties())
                     .Select(entry => entry.Value)
                     .OfType<JObject>())
        {
            foreach (JProperty property in record.Properties())
            {
                Assert.True(recordMembers.Contains(property.Name), $"a fixture record carries '{property.Name}', which no record type has");
            }
        }
    }

    /// <summary>Reads a fixture the way the plugin's own load path reads the real file.</summary>
    /// <param name="fixture">A file name under <c>Fixtures/</c>.</param>
    private static Configuration Load(string fixture)
    {
        string json = File.ReadAllText(FixturePath(fixture));

        return JsonConvert.DeserializeObject<Configuration>(json)
            ?? throw new InvalidOperationException($"'{fixture}' deserialised to null");
    }

    private static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
}
