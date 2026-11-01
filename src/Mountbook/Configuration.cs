using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Mountbook.Services;

namespace Mountbook;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Bump whenever a shape change needs a migration in <see cref="Migrate"/>.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; }

    /// <summary>Persisted so a reopened window comes back where the user left it.</summary>
    public bool MainWindowOpen { get; set; }

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
    /// Never delete or rename a field here without bumping <see cref="CurrentVersion"/>: the user's
    /// JSON on disk is not inspectable from the plugin, so the only safe migration is to assign
    /// defaults for the new shape. The enum checks run at every version because a hand-edited file
    /// can hold an out-of-range number even when <see cref="Version"/> is current.
    /// </remarks>
    public void Migrate()
    {
        SearchText ??= string.Empty;

        // A hand-edited or older file may have written null here; the toggle writes through the
        // set, so a null would throw on the first star click rather than at load.
        WantedMounts ??= new HashSet<uint>();

        // KnownOwnedIds is deliberately not defaulted: null means "no baseline yet" (see its own
        // remarks), and only the first successful reconcile turns it into a set.
        SavedSource ??= string.Empty;
        RecentCollected ??= new Dictionary<uint, long>();

        // A file written before the grind tracker existed has no member for it at all, and Dalamud's
        // deserialiser leaves a missing collection null rather than running the initialiser.
        Grinds ??= new Dictionary<uint, MountGrindRecord>();

        // Same reasoning for the price watches: a 0.4.0 config file has no member for them.
        Watches ??= new Dictionary<uint, MountWatchRecord>();

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

        Version = CurrentVersion;

        // This write runs from the plugin constructor, so it must not be able to stop the plugin from
        // loading: a read-only or full config folder would turn a cosmetic version bump into "failed to
        // load". Every other write in the plugin goes through SaveConfigSafe for the same reason.
        try
        {
            Save();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Saving the migrated configuration failed.");
        }
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
