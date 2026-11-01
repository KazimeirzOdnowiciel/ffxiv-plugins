using System;
using System.Collections.Generic;
using System.Text;

namespace Mountbook.Services;

/// <summary>
/// One obtain type: the kind of thing a mount is earned by, in the community taxonomy the obtain dataset
/// uses (Premium, Achievement, Trial, ...).
/// </summary>
/// <remarks>
/// <para>
/// This is the type the plugin filters and groups by - never the text a label shows. The display text is
/// <see cref="UiText.Source(MountSourceKind)"/>, which is looked up from the table by <see cref="Key"/>, so a
/// translation cannot change what a filter selects and a filter cannot be broken by a renamed label.
/// </para>
/// <para>
/// A type this build does not know still yields a value: the dataset's own spelling is kept, so a snapshot
/// written for a newer patch filters and groups by the new type instead of dumping it into \"Other\" or
/// dropping it. <see cref="IsKnown"/> is what a caller checks to report it - the patch-day validator refuses
/// to ship such a snapshot, which is where a new type has to be adopted deliberately.
/// </para>
/// </remarks>
public sealed record MountSourceKind
{
    internal MountSourceKind(string key, string name, int rank)
    {
        Key = key;
        Name = name;
        Rank = rank;
    }

    /// <summary>
    /// The folded identity: lower case, with every run of characters that is not a letter or a digit folded
    /// to one hyphen (<c>V&amp;C Dungeon</c> becomes <c>v-c-dungeon</c>). Two spellings of one type therefore
    /// compare equal, and the same string is the localisation table's key suffix.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The dataset's own spelling (<c>V&amp;C Dungeon</c>), which is also what a saved filter stores and what
    /// a type this build does not know is displayed as. Empty for <see cref="MountSourceKinds.Any"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Position in the taxonomy, the largest group first. <see cref="MountSourceKinds.Unranked"/> for a type
    /// this build does not know, so an unrecognised one sorts after every known one instead of into the middle
    /// of the list by its spelling.
    /// </summary>
    public int Rank { get; }

    /// <summary>True for the "no filter" value: it names no type at all.</summary>
    public bool IsAny => Key.Length == 0;

    /// <summary>False for a type the dataset carries and this build has never been taught.</summary>
    public bool IsKnown => Rank != MountSourceKinds.Unranked;

    /// <summary>
    /// Two kinds are the same type when their keys are. The spelling is how one of them happened to be written:
    /// a known type always parses to the table's own entry, but a type outside the taxonomy is built from
    /// whatever spelling reached the reader, and a saved filter written <c>brand-new-type</c> has to select the
    /// mounts the dataset spells <c>Brand New Type</c>.
    /// </summary>
    /// <param name="other">The kind to compare with.</param>
    public bool Equals(MountSourceKind? other) =>
        other is not null && string.Equals(Key, other.Key, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    /// <summary>The type's localised label, falling back to its own spelling.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// The obtain taxonomy: the 28 types the community dataset classifies the game's mounts into, the folding
/// rule that gives each one a stable key, and the reader the search grammar's <c>source:</c> term uses.
/// </summary>
/// <remarks>
/// <para>
/// The list below is the site's own classification and its order - largest group first - because that is the
/// order the combo and the grid's source headings draw in: a player asking "what is left in Cosmic
/// Exploration" should not have to hunt for it alphabetically. Adding a type is an edit here and a row in the
/// strings table; the snapshot's own types are checked against this list at patch time
/// (<c>source-type-unknown</c>), so a type that appears upstream cannot reach a player unnoticed.
/// </para>
/// <para>
/// Dalamud-free and pure (T1): the filter, the grouping, the search grammar and the validator all read this
/// one table.
/// </para>
/// </remarks>
public static class MountSourceKinds
{
    /// <summary>
    /// The rank a type outside the taxonomy carries: after every known type, because an unknown one is not
    /// evidence about where it belongs.
    /// </summary>
    public const int Unranked = int.MaxValue;

    /// <summary>
    /// The site's own spelling of each type, in its own order. The array index is the type's rank, so the
    /// combo and the source headings follow the taxonomy without a second table to keep in step.
    /// </summary>
    private static readonly string[] Names =
    [
        "Premium",
        "Achievement",
        "Trial",
        "Quest",
        "PvP",
        "Cosmic Exploration",
        "Event",
        "Wondrous Tails",
        "Tribal",
        "Raid",
        "Occult Crescent",
        "Island Sanctuary",
        "Gold Saucer",
        "V&C Dungeon",
        "Deep Dungeon",
        "Skybuilders",
        "FATE",
        "Treasure Hunt",
        "Hunts",
        "Purchase",
        "Chaotic Raid",
        "Bozja",
        "Eureka",
        "Gathering",
        "Crafting",
        "Other",
        "Dungeon",
        "Voyages",
    ];

    private static readonly MountSourceKind[] Ordered = Build();

    /// <summary>The "no filter" value: the combo's first entry, and what a source line with no type reads as.</summary>
    public static MountSourceKind Any { get; } = new(string.Empty, string.Empty, Unranked);

    /// <summary>Every known type, in taxonomy order (largest group first).</summary>
    public static IReadOnlyList<MountSourceKind> Taxonomy => Ordered;

    /// <summary>
    /// Reads one type from the dataset's spelling. Surrounding whitespace, case and punctuation are folded,
    /// so <c>pvp</c>, <c>PvP</c> and <c>PVP</c> are one type; a value naming nothing is
    /// <see cref="Any"/>, and anything else is kept as an unknown kind rather than thrown away.
    /// </summary>
    /// <param name="type">The dataset's <c>type</c>, or null.</param>
    public static MountSourceKind Parse(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return Any;
        }

        string name = type.Trim();
        string key = Key(name);

        if (key.Length == 0)
        {
            return Any;
        }

        foreach (MountSourceKind known in Ordered)
        {
            if (string.Equals(known.Key, key, StringComparison.Ordinal))
            {
                return known;
            }
        }

        return new MountSourceKind(key, name, Unranked);
    }

    /// <summary>
    /// Reads a type from what a player typed after <c>source:</c>: an exact key (<c>pvp</c>, <c>trial</c>,
    /// <c>v-c-dungeon</c>) or an unambiguous beginning of one (<c>trib</c>, <c>cosmic</c>, <c>sky</c>), so a
    /// type whose name carries a space is still reachable from a search box that splits on spaces.
    /// </summary>
    /// <param name="value">The text after the colon.</param>
    /// <param name="kind">The type the text names.</param>
    /// <returns>False when it names none, or begins more than one: the term then stays free text, exactly
    /// like an unreadable value of any other operator.</returns>
    public static bool TryParseOperator(string? value, out MountSourceKind kind)
    {
        kind = Any;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string key = Key(value.Trim());

        if (key.Length == 0)
        {
            return false;
        }

        MountSourceKind? prefix = null;

        foreach (MountSourceKind known in Ordered)
        {
            if (string.Equals(known.Key, key, StringComparison.Ordinal))
            {
                kind = known;
                return true;
            }

            if (known.Key.StartsWith(key, StringComparison.Ordinal))
            {
                // Two types begin with the same text ("p" is Premium, Purchase and PvP): a guess would filter
                // to a type nobody asked for, so the term refuses to be a constraint at all.
                if (prefix is not null)
                {
                    return false;
                }

                prefix = known;
            }
        }

        if (prefix is null)
        {
            return false;
        }

        kind = prefix;
        return true;
    }

    /// <summary>
    /// The folded identity of a type's spelling: lower case, every run of characters that is not a letter or
    /// a digit folded to one hyphen. This is the one fold, shared by the type's key, the strings table's key
    /// suffix and the search grammar.
    /// </summary>
    /// <param name="name">A type's spelling, e.g. <c>V&amp;C Dungeon</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static string Key(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var slug = new StringBuilder(name.Length);
        var gap = false;

        foreach (char character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (gap && slug.Length > 0)
                {
                    slug.Append('-');
                }

                slug.Append(char.ToLowerInvariant(character));
                gap = false;
            }
            else
            {
                gap = true;
            }
        }

        return slug.ToString();
    }

    /// <summary>
    /// The types <paramref name="item"/> carries, each once, in the dataset's own order. A mount with two
    /// lines of one type is one type, which is what makes the counts add up to mounts rather than to lines.
    /// </summary>
    /// <param name="item">The mount to read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static IReadOnlyList<MountSourceKind> Of(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var kinds = new List<MountSourceKind>(item.Sources.Count);

        foreach (MountSourceLine line in item.Sources)
        {
            if (line.Kind.IsAny || kinds.Contains(line.Kind))
            {
                continue;
            }

            kinds.Add(line.Kind);
        }

        return kinds;
    }

    /// <summary>
    /// How many mounts each type would show, over <paramref name="items"/>, in taxonomy order. Only the types
    /// the shelf actually carries are listed, so the combo never offers an empty filter.
    /// </summary>
    /// <param name="items">The whole shelf, not the filtered list: a count that moved when the filter changed
    /// would be counting itself.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static IReadOnlyList<MountSourceCount> Tally(IReadOnlyList<MountShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var counts = new Dictionary<MountSourceKind, int>();

        foreach (MountShelfItem item in items)
        {
            foreach (MountSourceKind kind in Of(item))
            {
                counts.TryGetValue(kind, out int mounts);
                counts[kind] = mounts + 1;
            }
        }

        var tally = new List<MountSourceCount>(counts.Count);

        foreach (KeyValuePair<MountSourceKind, int> entry in counts)
        {
            tally.Add(new MountSourceCount(entry.Key, entry.Value));
        }

        tally.Sort(CompareCounts);
        return tally;
    }

    /// <summary>Taxonomy order, then by name: an unknown type never lands between two known ones.</summary>
    private static int CompareCounts(MountSourceCount left, MountSourceCount right)
    {
        int byRank = left.Kind.Rank.CompareTo(right.Kind.Rank);

        if (byRank != 0)
        {
            return byRank;
        }

        int byName = StringComparer.OrdinalIgnoreCase.Compare(left.Kind.Name, right.Kind.Name);
        return byName != 0 ? byName : StringComparer.Ordinal.Compare(left.Kind.Name, right.Kind.Name);
    }

    private static MountSourceKind[] Build()
    {
        var ordered = new MountSourceKind[Names.Length];

        for (var rank = 0; rank < Names.Length; rank++)
        {
            ordered[rank] = new MountSourceKind(Key(Names[rank]), Names[rank], rank);
        }

        return ordered;
    }
}

/// <summary>
/// One row of the filter's counts: how many mounts carry <paramref name="Kind"/>, or - for
/// <see cref="MountSourceKinds.Any"/> - how many mounts there are to carry any type at all.
/// </summary>
/// <param name="Kind">The type being counted.</param>
/// <param name="Mounts">How many mounts the filter would show for it.</param>
public sealed record MountSourceCount(MountSourceKind Kind, int Mounts);
