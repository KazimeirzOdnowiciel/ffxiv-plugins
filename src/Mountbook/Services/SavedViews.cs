using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// One named filter preset: what the player had typed into the search box and selected in the four controls
/// beside it, kept under a name they chose.
/// </summary>
/// <remarks>
/// <para>
/// Name plus five axes - the query, the ownership chip, the expansion, the order and the grouping - is the
/// whole of what the shelf's filter block holds, so restoring one is assigning five fields and nothing else.
/// The obtain-type combo is deliberately not a member: the search grammar already says it (<c>source:pvp</c>,
/// <c>source:event</c>, <c>source:trial</c>), and two homes for one axis would let a preset's combo and its
/// own query disagree. The queries the roadmap names - "farmable now", "PvP only", "event mounts" - are the
/// grammar's, which is exactly why the feature is a saved query rather than a fifth kind of filter.
/// </para>
/// <para>
/// Plain settable members rather than a positional record: this is a persisted shape written and read by
/// Newtonsoft through Dalamud's loader, and a member added later has to keep the meaning of a file that does
/// not carry it (see docs/config-migration.md).
/// </para>
/// </remarks>
public sealed class SavedView
{
    /// <summary>The name the player gave it. Never blank in a file this build wrote; a blank one is dropped.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The search text, grammar included, exactly as the field held it.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Which ownership chip was selected when it was saved.</summary>
    public MountOwnership Ownership { get; set; }

    /// <summary>Which expansion the filter selected when it was saved.</summary>
    public MountExpansion Expansion { get; set; }

    /// <summary>How the list was ordered when it was saved.</summary>
    public MountOrder Order { get; set; }

    /// <summary>How the grid was grouped when it was saved.</summary>
    public MountGrouping GroupBy { get; set; }
}

/// <summary>
/// The saved views' rules: what "save the current filter" captures, which chip is the applied one, and what a
/// rename or a delete is allowed to do - pure and Dalamud-free (T1), so the row, the dialog and the migration
/// step all go through one implementation and the acceptance is a test rather than a click.
/// </summary>
/// <remarks>
/// The list is the player's work in the same sense the wanted list is, which decides three things here: the
/// order is insertion order and is never sorted, a name is compared case-insensitively so "PvP only" and
/// "pvp only" cannot both exist, and nothing is ever silently renamed or reordered - a duplicate is dropped
/// by <see cref="Normalise"/> exactly as a map would drop a duplicate key.
/// </remarks>
public static class SavedViewRules
{
    /// <summary>
    /// How many views a file may hold. The row that draws them is one line of the filter block, which is the
    /// space the layout work bought back; past this many the dialog is a list to scroll rather than a set of
    /// shortcuts, and the feature stops being faster than typing the query.
    /// </summary>
    public const int MaxViews = 8;

    /// <summary>
    /// The longest name kept, in characters. A chip is drawn with the name in it, so a sentence-length name
    /// would push every other chip off the row the moment one was saved; the rest is dropped rather than
    /// truncated at draw time, so the file and the chip say the same thing.
    /// </summary>
    public const int MaxNameLength = 28;

    /// <summary>
    /// The name to store for what was typed: trimmed, with runs of whitespace folded to one space, cut to
    /// <see cref="MaxNameLength"/>, and empty when there is nothing left.
    /// </summary>
    /// <remarks>
    /// A name is a chip label and a key, so the folding happens once, on the way in, rather than at every
    /// comparison: "Farmable  now " and "farmable now" are the same view, and the row draws the name the file
    /// holds.
    /// </remarks>
    /// <param name="name">What the field held, or null.</param>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var cleaned = new System.Text.StringBuilder(Math.Min(name.Length, MaxNameLength));

        foreach (string word in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (cleaned.Length > 0)
            {
                _ = cleaned.Append(' ');
            }

            _ = cleaned.Append(word);
        }

        return cleaned.Length > MaxNameLength ? cleaned.ToString(0, MaxNameLength).TrimEnd() : cleaned.ToString();
    }

    /// <summary>
    /// The preset the current filter would be saved as: the name as <see cref="Clean"/> keeps it and the five
    /// axes copied from the fields the filter block writes.
    /// </summary>
    /// <param name="name">What the player typed.</param>
    /// <param name="query">The search text.</param>
    /// <param name="ownership">The ownership chip.</param>
    /// <param name="expansion">The expansion filter.</param>
    /// <param name="order">The order.</param>
    /// <param name="grouping">The grouping.</param>
    public static SavedView Capture(
        string? name,
        string? query,
        MountOwnership ownership,
        MountExpansion expansion,
        MountOrder order,
        MountGrouping grouping) =>
        new()
        {
            Name = Clean(name),
            Query = query ?? string.Empty,
            Ownership = ownership,
            Expansion = expansion,
            Order = order,
            GroupBy = grouping,
        };

    /// <summary>True when <paramref name="view"/> is exactly the filter the given fields describe.</summary>
    /// <remarks>
    /// Every axis matters, because this is what decides which chip draws as the applied one: a comparison on
    /// the name or on the query alone would light a chip up while the list it describes was ordered another
    /// way. The search text is compared ordinally - the field stores the grammar's own characters, and a
    /// culture-aware comparison here would call two different queries equal.
    /// </remarks>
    /// <param name="view">The preset to test.</param>
    /// <param name="query">The search text in the box.</param>
    /// <param name="ownership">The selected ownership chip.</param>
    /// <param name="expansion">The selected expansion.</param>
    /// <param name="order">The selected order.</param>
    /// <param name="grouping">The selected grouping.</param>
    public static bool Matches(
        SavedView view,
        string? query,
        MountOwnership ownership,
        MountExpansion expansion,
        MountOrder order,
        MountGrouping grouping) =>
        view is not null
        && view.Ownership == ownership
        && view.Expansion == expansion
        && view.Order == order
        && view.GroupBy == grouping
        && string.Equals(view.Query ?? string.Empty, query ?? string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// The index of the preset the given fields describe, or -1 when the current filter is not one of them -
    /// which is the case a chip row must draw as "nothing applied".
    /// </summary>
    /// <param name="views">The saved views, in draw order.</param>
    /// <param name="query">The search text in the box.</param>
    /// <param name="ownership">The selected ownership chip.</param>
    /// <param name="expansion">The selected expansion.</param>
    /// <param name="order">The selected order.</param>
    /// <param name="grouping">The selected grouping.</param>
    public static int MatchIndex(
        IReadOnlyList<SavedView>? views,
        string? query,
        MountOwnership ownership,
        MountExpansion expansion,
        MountOrder order,
        MountGrouping grouping)
    {
        if (views is null)
        {
            return -1;
        }

        for (var index = 0; index < views.Count; index++)
        {
            if (Matches(views[index], query, ownership, expansion, order, grouping))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Stores <paramref name="view"/>, replacing the entry of the same name in place, and returns whether it
    /// was stored and where.
    /// </summary>
    /// <remarks>
    /// Saving under a name that is already taken is how a preset is updated: the chip keeps its position - a
    /// row that reordered itself under the pointer is one the player has to re-read - and the five axes are
    /// overwritten. Refused when the name is blank (a chip with no text is not a control) and when the list is
    /// full of other names (<see cref="MaxViews"/>).
    /// </remarks>
    /// <param name="views">The list to write into.</param>
    /// <param name="view">The preset to store.</param>
    /// <param name="index">Where it landed, or -1 when it was refused.</param>
    public static bool TrySave(List<SavedView> views, SavedView view, out int index)
    {
        index = -1;

        if (views is null || view is null || view.Name.Length == 0)
        {
            return false;
        }

        for (var existing = 0; existing < views.Count; existing++)
        {
            if (views[existing] is { } entry && SameName(entry.Name, view.Name))
            {
                entry.Query = view.Query;
                entry.Ownership = view.Ownership;
                entry.Expansion = view.Expansion;
                entry.Order = view.Order;
                entry.GroupBy = view.GroupBy;
                index = existing;
                return true;
            }
        }

        if (views.Count >= MaxViews)
        {
            return false;
        }

        views.Add(view);
        index = views.Count - 1;
        return true;
    }

    /// <summary>Why a rename was refused, so the window can say which one it was instead of a dead button.</summary>
    public enum RenameResult
    {
        /// <summary>The entry now has the new name.</summary>
        Renamed,

        /// <summary>Nothing to rename: no such index, or a blank name.</summary>
        Refused,

        /// <summary>Another entry already has that name, so the two chips would read the same.</summary>
        NameTaken,
    }

    /// <summary>Renames the entry at <paramref name="index"/>, keeping its position and its five axes.</summary>
    /// <param name="views">The list to rename in.</param>
    /// <param name="index">The entry to rename.</param>
    /// <param name="name">The new name, as typed.</param>
    public static RenameResult Rename(List<SavedView> views, int index, string? name)
    {
        if (views is null || index < 0 || index >= views.Count || views[index] is not { } entry)
        {
            return RenameResult.Refused;
        }

        string cleaned = Clean(name);

        if (cleaned.Length == 0)
        {
            return RenameResult.Refused;
        }

        for (var other = 0; other < views.Count; other++)
        {
            if (other != index && views[other] is { } candidate && SameName(candidate.Name, cleaned))
            {
                return RenameResult.NameTaken;
            }
        }

        entry.Name = cleaned;
        return RenameResult.Renamed;
    }

    /// <summary>Removes the entry at <paramref name="index"/>, and reports whether there was one.</summary>
    /// <param name="views">The list to remove from.</param>
    /// <param name="index">The entry to remove.</param>
    public static bool Delete(List<SavedView> views, int index)
    {
        if (views is null || index < 0 || index >= views.Count)
        {
            return false;
        }

        views.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Brings a list read from disk into a state this build can draw: no null entries, no blank or repeated
    /// names, no enum value outside the ones that exist, and no more than <see cref="MaxViews"/> entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written for step 5 of the config ladder, and it sweeps instead of trusting the version, for the reason
    /// every step there does: a hand-edited file does not bump <c>Version</c>, so the file most likely to hold
    /// damage is the one claiming to be current. It runs in the plugin's constructor, so it must not throw - a
    /// null entry in the list is exactly the case that does, and the null check comes before any member read.
    /// </para>
    /// <para>
    /// A repeated name drops the later entry rather than being made unique: the axes it described are still
    /// reachable by typing its query, and inventing a name ("PvP only (2)") is a rename the player did not ask
    /// for. Order is otherwise kept as written - it is the row's order.
    /// </para>
    /// </remarks>
    /// <param name="views">The list to normalise, in place.</param>
    public static void Normalise(List<SavedView> views)
    {
        if (views is null || views.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dead = new List<int>();

        for (var index = 0; index < views.Count; index++)
        {
            if (views[index] is not { } view)
            {
                dead.Add(index);
                continue;
            }

            view.Name = Clean(view.Name);
            view.Query ??= string.Empty;
            view.Ownership = MountOwnershipRules.Normalize(view.Ownership);

            if (!Enum.IsDefined(view.Expansion))
            {
                view.Expansion = MountExpansion.All;
            }

            if (!Enum.IsDefined(view.Order))
            {
                view.Order = MountOrder.Name;
            }

            view.GroupBy = MountGroupingRules.Normalize(view.GroupBy);

            if (view.Name.Length == 0 || !seen.Add(view.Name) || seen.Count > MaxViews)
            {
                dead.Add(index);
            }
        }

        for (var index = dead.Count - 1; index >= 0; index--)
        {
            views.RemoveAt(dead[index]);
        }
    }

    /// <summary>True when two names are the same view's name: the comparison the file and the row both use.</summary>
    /// <param name="left">One name.</param>
    /// <param name="right">The other.</param>
    private static bool SameName(string? left, string? right) =>
        string.Equals(Clean(left), Clean(right), StringComparison.OrdinalIgnoreCase);
}
