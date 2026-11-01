using System;
using System.Collections.Generic;
using Mountbook.Services;

namespace Mountbook;

/// <summary>
/// The persisted shape of the plugin's settings, and the ladder that upgrades a file written by an older
/// build. Split out of <see cref="Configuration"/> so the shape is Dalamud-free and the migration is a
/// test: <c>Mountbook.Tests</c> compiles this file by path (T1), which is what makes "an old file still
/// means what it meant" a test rather than a review opinion.
/// </summary>
/// <remarks>
/// The other half (<c>Configuration.cs</c>) is the plugin-facing part: the <c>IPluginConfiguration</c>
/// implementation and the one save the ladder asks for. Nothing in this file may touch Dalamud, a service
/// or the clock - the tests read it with the same serializer Dalamud uses.
/// </remarks>
[Serializable]
public sealed partial class Configuration
{
    /// <summary>
    /// Bump whenever a shape change needs a migration in <see cref="Migrate"/>.
    /// </summary>
    /// <remarks>
    /// The ladder is one numbered step per persisted shape, in the order the shapes shipped: 1 the first
    /// file (browse state, the wanted list, the owned baseline), 2 the grind tracker, 3 the price watch,
    /// 4 the obtain-type taxonomy, 5 the saved views, 6 the availability filter and the "Farmable now" view
    /// the feature ships with. A file written before the ladder existed says <c>1</c> whatever it carried, so
    /// every step has to be safe to run on a file that already has its shape - which is why a step sweeps a
    /// collection instead of assuming it is absent.
    /// </remarks>
    public const int CurrentVersion = 6;

    /// <summary>The shape version this file was written with, from <see cref="CurrentVersion"/>'s ladder.</summary>
    public int Version { get; set; }

    /// <summary>Persisted so a reopened window comes back where the user left it.</summary>
    public bool MainWindowOpen { get; set; }

    /// <summary>
    /// True once the main window has drawn its first-run line, so the onboarding hint is shown exactly once.
    /// </summary>
    /// <remarks>
    /// Deliberately outside the version ladder: a missing <c>bool</c> keeps this initialiser, and <c>false</c>
    /// is the right reading for both a brand-new file and one written before the line existed - which is also
    /// what makes the hint reach a player upgrading from an older build, the one case where it is still news.
    /// </remarks>
    public bool FirstRunHintSeen { get; set; }

    /// <summary>What was typed into the search box when the window was last used.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>Which ownership chip was selected.</summary>
    public MountOwnership Ownership { get; set; }

    /// <summary>Which expansion the filter selected.</summary>
    public MountExpansion Expansion { get; set; }

    /// <summary>How the card grid was ordered.</summary>
    public MountOrder Order { get; set; }

    /// <summary>Whether the shelf draws artwork cards or the dense table.</summary>
    public MountViewMode ViewMode { get; set; }

    /// <summary>How the card grid gathers its mounts into sections.</summary>
    public MountGrouping GroupBy { get; set; }

    /// <summary>The mount whose detail pane was open, or zero for none.</summary>
    public uint SelectedMountId { get; set; }

    /// <summary>
    /// The player's hunting list: mount ids they have marked as wanted. Order is never read, so a
    /// set is the right shape; the card star and the detail button toggle membership.
    /// </summary>
    public HashSet<uint> WantedMounts { get; set; } = [];

    /// <summary>
    /// The saved obtain-type filter as the taxonomy spells it ("Trial", "PvP", ...), or empty for any. The
    /// spelling rather than the folded key: the file is something a human may read, and a value an older build
    /// wrote for one of its six labels is the same string the type's own name gives. Its reader folds it and only
    /// adopts a type the taxonomy knows, so a hand-edited "pvp" still selects PvP and a value naming no type reads
    /// as "any" instead of emptying the shelf.
    /// </summary>
    public string SavedSource { get; set; } = string.Empty;

    /// <summary>
    /// The saved availability filter as the class's own key ("available", "premium", ...), or empty for any.
    /// </summary>
    /// <remarks>
    /// Stored as the key rather than as the badge's label, for the same reason <see cref="SavedSource"/> stores
    /// the type's spelling: the string a translation produces must never decide what a filter selects. A value
    /// naming no class is cleared on load, which reads as "any" rather than emptying the shelf.
    /// </remarks>
    public string SavedAvailability { get; set; } = string.Empty;

    /// <summary>
    /// The player's named filter presets, in the order they want them: each one is a name and the five axes of
    /// the filter block, so one click puts the shelf back the way it was when they saved it.
    /// </summary>
    /// <remarks>
    /// A <see cref="List{T}"/> rather than a map keyed by name, because the order is the row's order and the
    /// player puts them there; the duplicate-name rule lives in <see cref="SavedViewRules"/> so the file, the
    /// dialog and the migration cannot disagree about what "the same name" means. An entry here is work the
    /// player typed, so the reset-to-defaults control counts it (ConfigResetImpact) rather than clearing it
    /// silently. It is the first list member since the wanted set - a wanted *id* has no order, a preset does.
    /// </remarks>
    public List<SavedView> SavedViews { get; set; } = [];

    /// <summary>
    /// The ids owned the last time the window looked, or null before the first reconcile. It exists so a
    /// mount that appears since then can be recognised as newly collected; without it every mount would
    /// look new on the first run.
    /// </summary>
    /// <remarks>
    /// Null is meaningful and must stay null until the first reconcile: an empty set says "the player
    /// owned nothing last time", which is what makes the first mount a fresh character collects show up
    /// as New. Normalising it to an empty set here would silently erase that case.
    /// </remarks>
    public HashSet<uint>? KnownOwnedIds { get; set; }

    /// <summary>
    /// Mounts first seen as collected, with the unix-second time they were first seen. Pruned to the ones
    /// from the last two weeks by <see cref="Services.MountRecent"/>.
    /// </summary>
    public Dictionary<uint, long> RecentCollected { get; set; } = [];

    /// <summary>
    /// The Glasswork Look the windows draw with: a built-in name ("eorzea", "aurora", ...) or the
    /// name of a theme JSON in the config folder's themes directory.
    /// </summary>
    public string LookName { get; set; } = "eorzea";

    /// <summary>
    /// What the player has banked toward each mount, keyed by mount id: the tokens counted, the target
    /// and their own note. Absent means never touched, which is why the pane draws no tracker for a
    /// mount that is not in here (and why the dictionary may stay empty).
    /// </summary>
    /// <remarks>
    /// A record is only written when it holds something: dragging a stepper back to zero removes its
    /// entry rather than leaving an empty one behind, so the file cannot fill with noise.
    /// </remarks>
    public Dictionary<uint, MountGrindRecord> Grinds { get; set; } = [];

    /// <summary>
    /// The mounts the player watches a price for, keyed by mount id: the price worth a toast, when the
    /// market was last read for it, and the price that read found.
    /// </summary>
    /// <remarks>
    /// Opt-in and empty by default, because an entry here is the only thing that makes the plugin poll a
    /// network service by itself: switching a watch off removes its record rather than zeroing a threshold,
    /// so "watching nothing" and "watching with no threshold" cannot both be persisted.
    /// </remarks>
    public Dictionary<uint, MountWatchRecord> Watches { get; set; } = [];

    /// <summary>How the wanted board orders its rows.</summary>
    public MountBoardSort BoardSort { get; set; }

    /// <summary>Persisted so the wanted board comes back open, like the main window.</summary>
    public bool BoardWindowOpen { get; set; }

    /// <summary>When on, the host restyles to the player's own game window theme.</summary>
    public bool FollowGameTheme { get; set; }

    /// <summary>
    /// Runs once after load, and normalises hand-editable fields on every load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never delete or rename a field here without bumping <see cref="CurrentVersion"/>: the user's
    /// JSON on disk is not inspectable from the plugin, so the only safe migration is to assign
    /// defaults for the new shape. The enum checks run at every version because a hand-edited file
    /// can hold an out-of-range number even when <see cref="Version"/> is current.
    /// </para>
    /// <para>
    /// Two layers on purpose. The null and enum guards below run on every load, because a file can be
    /// hand-edited at any version and a null collection is a crash on the first click. The ladder runs
    /// once, rewrites the collections, and is where a *shape* change lands - a record that gained a
    /// member, a collection that moved, a value whose spelling changed.
    /// </para>
    /// </remarks>
    public void Migrate()
    {
        SearchText ??= string.Empty;

        // A hand-edited or older file may have written null here; the toggle writes through the
        // set, so a null would throw on the first star click rather than at load. (Measured: a member
        // *missing* from the file keeps the initialiser above - Newtonsoft runs the constructor and then
        // fills only what the file names - so this guard is for an explicit null, and for a collection
        // the ladder has to rebuild.)
        WantedMounts ??= new HashSet<uint>();

        // KnownOwnedIds is deliberately not defaulted: null means "no baseline yet" (see its own
        // remarks), and only the first successful reconcile turns it into a set.
        SavedSource ??= string.Empty;
        SavedAvailability ??= string.Empty;
        RecentCollected ??= new Dictionary<uint, long>();
        Grinds ??= new Dictionary<uint, MountGrindRecord>();
        Watches ??= new Dictionary<uint, MountWatchRecord>();
        SavedViews ??= new List<SavedView>();

        // Every load, not only step 5 of the ladder: a file can claim the current version and still hold a
        // damaged list - a hand edit does not bump Version, and a null entry in it throws on the row's first
        // member read, which blanks the whole main window. The sweep is idempotent, so running it on a healthy
        // file costs one pass over at most eight entries.
        SavedViewRules.Normalise(SavedViews);

        if (string.IsNullOrWhiteSpace(LookName))
        {
            LookName = "eorzea";
        }

        Ownership = MountOwnershipRules.Normalize(Ownership);

        if (!Enum.IsDefined(Expansion))
        {
            Expansion = MountExpansion.All;
        }

        if (!Enum.IsDefined(Order))
        {
            Order = MountOrder.Name;
        }

        ViewMode = MountViewModeRules.Normalize(ViewMode);
        GroupBy = MountGroupingRules.Normalize(GroupBy);

        if (!Enum.IsDefined(BoardSort))
        {
            BoardSort = MountBoardSort.Patch;
        }

        if (Version >= CurrentVersion)
        {
            return;
        }

        // One step per persisted shape, in order. A file from before the ladder says 1 whatever it grew,
        // so each step sweeps rather than trusting the version it claims.
        while (Version < CurrentVersion)
        {
            Version++;
            UpgradeTo(Version);
        }

        // This write runs from the plugin constructor, so it must not be able to stop the plugin from
        // loading: a read-only or full config folder would turn a cosmetic version bump into "failed to
        // load". The plugin-facing half implements this and logs instead of throwing.
        OnMigrated();
    }

    /// <summary>
    /// Puts every persisted member back to the value a fresh install starts with, except the version: a
    /// reset file is the current shape, not one that still has to be upgraded.
    /// </summary>
    /// <remarks>
    /// The values come from a throwaway <see cref="Configuration"/> rather than from a second list of
    /// defaults, so a member added later cannot be forgotten here. The collections are cleared in place
    /// instead of replaced: a view state may still be holding the set it copied this frame, and a
    /// replaced instance would leave it drawing the list the player just cleared.
    /// </remarks>
    public void ResetToDefaults()
    {
        var fresh = new Configuration();

        Version = CurrentVersion;
        MainWindowOpen = fresh.MainWindowOpen;
        FirstRunHintSeen = fresh.FirstRunHintSeen;
        SearchText = fresh.SearchText;
        Ownership = fresh.Ownership;
        Expansion = fresh.Expansion;
        Order = fresh.Order;
        ViewMode = fresh.ViewMode;
        GroupBy = fresh.GroupBy;
        SelectedMountId = fresh.SelectedMountId;

        // Null rather than a fresh instance: null is "no baseline yet" and is a member a test pins.
        KnownOwnedIds = fresh.KnownOwnedIds;

        LookName = fresh.LookName;
        BoardSort = fresh.BoardSort;
        BoardWindowOpen = fresh.BoardWindowOpen;
        FollowGameTheme = fresh.FollowGameTheme;

        // Everything the player entered, including the wanted list this is not allowed to take silently.
        WantedMounts.Clear();
        RecentCollected.Clear();
        Grinds.Clear();
        Watches.Clear();
        SavedViews.Clear();
        SavedSource = fresh.SavedSource;
        SavedAvailability = fresh.SavedAvailability;
    }

    /// <summary>
    /// The one thing this half needs from the plugin: persist the upgraded file.
    /// </summary>
    /// <remarks>
    /// A partial method, so this file stays Dalamud-free and the test build compiles a ladder with no
    /// save at all. The body lives in <c>Configuration.cs</c>, which is the half that knows Dalamud.
    /// </remarks>
    partial void OnMigrated();

    /// <summary>
    /// Applies step <paramref name="version"/> of the ladder: the shape change that arrived with it.
    /// </summary>
    /// <param name="version">The version being stepped to, never below 2.</param>
    private void UpgradeTo(int version)
    {
        switch (version)
        {
            // 1 is the first file: nothing to upgrade to, it is the shape the others are written against.
            case 2:
                // The grind tracker. A record is only worth drawing when it holds a count, a target or a
                // note, and the pane and the board both write through MountGrind, so a hand-edited entry
                // that holds nothing (or a null the deserialiser left in the map) is dropped here rather
                // than being carried forever.
                SweepGrinds();
                break;

            case 3:
                // The price watch. A record with no threshold is how the pane switches a watch off, and the
                // feature never persists one - so an entry like that came from a hand edit, and keeping it
                // would make "watching nothing" and "watching with no threshold" both persistable.
                SweepWatches();
                break;

            case 4:
                // The obtain types became a taxonomy, and the saved filter is now one type's own spelling.
                // Canonicalising it means the file a human reads matches the combo, and an older build's
                // label ("pvp") stops being re-folded on every load; a value that names no type is cleared,
                // which the main window already refuses to adopt (MountSourceKinds.Parse + IsKnown).
                CanonicaliseSavedSource();
                break;

            case 5:
                // The saved views. A preset is a name and five enum values, and the enums are the only part a
                // hand edit or a newer build can damage - a value outside the enum would make the chip's filter
                // select nothing, which reads as "the preset is broken". The sweep also drops a blank name (a
                // chip with no text is not a control), a repeated one (two chips reading the same) and anything
                // past SavedViewRules.MaxViews.
                SavedViewRules.Normalise(SavedViews);
                break;

            case 6:
                // The availability filter is persisted, and the "Farmable now" view ships with it. The seed is a
                // step rather than a property initialiser on purpose: a file at Version 6 that holds an empty
                // list is a player who deleted the view, and re-seeding it on every load would put the chip back
                // in front of them forever. Canonicalising the filter follows the same rule as SavedSource - a
                // word this build does not know is cleared to "any" instead of emptying the shelf.
                CanonicaliseSavedAvailability();
                SeedFarmableView();
                break;

            default:
                break;
        }
    }

    /// <summary>Drops grind records that hold nothing, and clamps the counts a hand edit can make negative.</summary>
    private void SweepGrinds()
    {
        if (Grinds.Count == 0)
        {
            return;
        }

        var dead = new List<uint>();

        foreach (KeyValuePair<uint, MountGrindRecord> entry in Grinds)
        {
            if (!MountGrind.IsTracked(entry.Value))
            {
                dead.Add(entry.Key);
                continue;
            }

            entry.Value.Have = MountGrind.Have(entry.Value);
            entry.Value.Note = string.IsNullOrWhiteSpace(entry.Value.Note) ? null : entry.Value.Note;
        }

        foreach (uint id in dead)
        {
            Grinds.Remove(id);
        }
    }

    /// <summary>Drops watch records with no threshold, and clamps the timestamps a hand edit can make negative.</summary>
    private void SweepWatches()
    {
        if (Watches.Count == 0)
        {
            return;
        }

        var dead = new List<uint>();

        foreach (KeyValuePair<uint, MountWatchRecord> entry in Watches)
        {
            if (!MountWatch.IsWatching(entry.Value))
            {
                dead.Add(entry.Key);
                continue;
            }

            entry.Value.CheckedAt = Math.Max(entry.Value.CheckedAt, 0L);
            entry.Value.LastSeen = Math.Max(entry.Value.LastSeen, 0);
        }

        foreach (uint id in dead)
        {
            Watches.Remove(id);
        }
    }

    /// <summary>Rewrites the saved source filter as the taxonomy spells it, or clears a value naming no type.</summary>
    private void CanonicaliseSavedSource()
    {
        if (string.IsNullOrWhiteSpace(SavedSource))
        {
            SavedSource = string.Empty;
            return;
        }

        MountSourceKind kind = MountSourceKinds.Parse(SavedSource);

        SavedSource = kind.IsKnown ? kind.Name : string.Empty;
    }

    /// <summary>Rewrites the saved availability filter as the class's key, or clears a value naming no class.</summary>
    private void CanonicaliseSavedAvailability()
    {
        SavedAvailability = MountAvailabilityRules.TryParseKey(SavedAvailability, out MountAvailability availability)
            ? MountAvailabilityRules.Key(availability)
            : string.Empty;
    }

    /// <summary>
    /// Adds the "Farmable now" view the feature ships with, once per file: the shelf's own answer to the question
    /// collectors ask, as one click instead of a typed query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The preset's content lives in <see cref="Services.SavedViewRules.FarmableView"/> - the query
    /// <c>avail:available</c> over the mounts the player does not own - because the rules about presets are there
    /// (see <see cref="SavedView"/> for why a preset carries a query and not the filter combo).
    /// </para>
    /// <para>
    /// Never overwrites: a player who already has a view under that name keeps theirs. Running only as a ladder
    /// step is what makes a file at <see cref="CurrentVersion"/> holding an empty list a player who deleted the
    /// view, rather than a list the plugin keeps refilling.
    /// </para>
    /// </remarks>
    private void SeedFarmableView()
    {
        if (SavedViews.Count >= SavedViewRules.MaxViews)
        {
            return;
        }

        SavedView seeded = SavedViewRules.FarmableView();

        if (seeded.Name.Length == 0)
        {
            return;
        }

        foreach (SavedView view in SavedViews)
        {
            if (view is not null && string.Equals(SavedViewRules.Clean(view.Name), seeded.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        SavedViews.Add(seeded);
    }
}
