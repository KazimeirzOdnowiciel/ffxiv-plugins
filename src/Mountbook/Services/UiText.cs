using System;

namespace Mountbook.Services;

/// <summary>
/// Display text for values the rest of the plugin treats as data: an expansion's name, a source category
/// from the obtain dataset, the heading of a group.
/// </summary>
/// <remarks>
/// <para>
/// These are not table lookups by key, because the code that carries them around compares and sorts by the
/// same string: <see cref="MountGroups"/> buckets its sections by title, <see cref="MountBreakdown.FilterOf"/>
/// finds a filter by an expansion's name, and the source filter's stored value is the dataset's own
/// category. Translating the value itself would translate the identity with it. So the value stays the
/// key, and this is the one place it becomes text.
/// </para>
/// <para>
/// A category the table has not been translated for reads as itself, which is right for a dataset value and
/// is what keeps a new category in the obtain data from drawing a raw key.
/// </para>
/// </remarks>
public static class UiText
{
    /// <summary>
    /// The sentinel the source combo's first entry carries: no category filter. It is a value, not a
    /// label - <see cref="Source"/> gives it its text.
    /// </summary>
    public const string AnySource = "Any";

    /// <summary>
    /// The heading text for a group in the card grid.
    /// </summary>
    /// <param name="title">The title <see cref="MountGroups"/> produced.</param>
    /// <param name="grouping">How the list was grouped, which says what the title means.</param>
    /// <returns>The text to draw.</returns>
    public static string GroupTitle(string title, MountGrouping grouping)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (string.Equals(title, MountGroups.NoPatch, StringComparison.Ordinal))
        {
            return Loc.TOr("group.noPatch", title);
        }

        if (string.Equals(title, MountGroups.NoSource, StringComparison.Ordinal))
        {
            return Loc.TOr("group.noSource", title);
        }

        return grouping switch
        {
            MountGrouping.Expansion => Expansion(title),
            MountGrouping.Source => Source(title),
            _ => title,
        };
    }

    /// <summary>
    /// The name of an expansion, from the English name the code sorts by.
    /// </summary>
    /// <param name="name">e.g. <c>Heavensward</c>, <c>Other</c> or <c>Expansion unknown</c>.</param>
    /// <returns>The localised name, or the name itself when it is not one of the known ones.</returns>
    public static string Expansion(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name switch
        {
            "A Realm Reborn" => Loc.TOr("expansion.arr", name),
            "Heavensward" => Loc.TOr("expansion.hw", name),
            "Stormblood" => Loc.TOr("expansion.sb", name),
            "Shadowbringers" => Loc.TOr("expansion.shb", name),
            "Endwalker" => Loc.TOr("expansion.ew", name),
            "Dawntrail" => Loc.TOr("expansion.dt", name),
            "Other" => Loc.TOr("expansion.other", name),
            "Expansion unknown" => Loc.TOr("expansion.unknown", name),
            _ => name,
        };
    }

    /// <summary>
    /// The abbreviation the header's progress strip draws for an expansion.
    /// </summary>
    /// <param name="shortName">e.g. <c>ARR</c>, <c>HW</c>, <c>ShB</c>.</param>
    /// <returns>The localised abbreviation, or the abbreviation itself when it is not one of the known ones.</returns>
    public static string ExpansionShort(string shortName)
    {
        ArgumentNullException.ThrowIfNull(shortName);

        return shortName switch
        {
            "ARR" => Loc.TOr("expansionShort.arr", shortName),
            "HW" => Loc.TOr("expansionShort.hw", shortName),
            "StB" => Loc.TOr("expansionShort.sb", shortName),
            "ShB" => Loc.TOr("expansionShort.shb", shortName),
            "EW" => Loc.TOr("expansionShort.ew", shortName),
            "DT" => Loc.TOr("expansionShort.dt", shortName),
            "\u2014" => Loc.TOr("expansionShort.other", shortName),
            _ => shortName,
        };
    }

    /// <summary>
    /// The label for a source category from the obtain dataset.
    /// </summary>
    /// <param name="category">The dataset's category, e.g. <c>V&amp;C Dungeon</c>.</param>
    /// <returns>The localised label, or the category itself when the table has no label for it.</returns>
    public static string Source(string category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return category.Length == 0 || string.Equals(category, AnySource, StringComparison.Ordinal)
            ? Loc.TOr("source.any", category)
            : Loc.TOr("source." + SourceKey(category), category);
    }

    /// <summary>
    /// The table-key suffix for a source category: lower case, with every run of characters that is not a
    /// letter or a digit folded to one hyphen. <c>V&amp;C Dungeon</c> becomes <c>v-c-dungeon</c>, so a key
    /// never carries JSON-hostile punctuation and a category added to the dataset gets a predictable one.
    /// </summary>
    /// <param name="category">The dataset's category.</param>
    /// <returns>The key suffix, without the <c>source.</c> prefix.</returns>
    public static string SourceKey(string category)
    {
        ArgumentNullException.ThrowIfNull(category);

        var slug = new char[category.Length];
        var length = 0;
        var gap = false;

        foreach (char character in category)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (gap && length > 0)
                {
                    slug[length++] = '-';
                }

                slug[length++] = char.ToLowerInvariant(character);
                gap = false;
            }
            else
            {
                gap = true;
            }
        }

        return new string(slug, 0, length);
    }
}
