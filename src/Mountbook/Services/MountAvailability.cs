using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// Whether a mount can be obtained today, as a class rather than a sentence.
/// </summary>
/// <remarks>
/// <para>
/// This is the answer to the question a collector actually asks ("what can I go and farm right now"), and it
/// is a <em>class</em> because the answer has to be filterable and countable: <see cref="Available"/> is
/// obtainable now, and the other four say why not. The value travels in the snapshot
/// (<c>availability</c> on every row) instead of being worked out while drawing, so the detail badge, the
/// filter, the counts and the saved "Farmable now" view all read one field and cannot disagree - and so a
/// rule change is a regeneration, not a rebuild of the pane's logic.
/// </para>
/// <para>
/// <see cref="Unknown"/> is a real value, not a failure mode to hide: it is what a mount with no obtain data
/// reads as (the game has rows the site has never listed), and the patch-day validator refuses a snapshot
/// that writes it for a row the rules could have classified.
/// </para>
/// </remarks>
public enum MountAvailability
{
    /// <summary>Nothing is known: the mount carries no obtain data, so no class can be derived.</summary>
    Unknown = 0,

    /// <summary>Obtainable now, by playing the game: a trial, a raid, a quest, a currency, an event that is up.</summary>
    Available,

    /// <summary>An event reward, out of season: it comes back when the event does.</summary>
    Seasonal,

    /// <summary>
    /// A PvP season or series reward. The window it was handed out in has closed, and the shops do not stock
    /// it - a mount that is also sold for Wolf Marks or Trophy Crystals is <see cref="Available"/> instead.
    /// </summary>
    Ranked,

    /// <summary>Bought with real money: the online store, a collector's edition, a paid promotion.</summary>
    Premium,

    /// <summary>Retired: no route was ever reissued, and curation is the only thing that can know it.</summary>
    Legacy,
}

/// <summary>
/// The colour family a class is drawn in, so the four ways of being unobtainable read as four different
/// things instead of four words in one grey.
/// </summary>
/// <remarks>
/// The tone is decided here rather than in the view because "the four non-Available classes are visibly
/// distinct" is a claim a test can hold (a map that collapses two classes onto one tone is the bug), while
/// the view's half - one Look colour per tone - is a line of code a reviewer reads. The view may not invent
/// a tone of its own: <see cref="MountAvailabilityRules.ToneOf"/> is the only source of one.
/// </remarks>
public enum MountAvailabilityTone
{
    /// <summary>No class: the badge is drawn plain, and nothing claims a colour it does not have.</summary>
    None = 0,

    /// <summary>Get it now. The positive colour.</summary>
    Positive,

    /// <summary>Time-gated: the event that grants it is out of season.</summary>
    Informative,

    /// <summary>A window that has closed: a retired season or series reward.</summary>
    Caution,

    /// <summary>Money, not play: the store or a paid edition.</summary>
    Brand,

    /// <summary>Gone: retired content, never reissued.</summary>
    Negative,
}

/// <summary>One class and how many mounts fall in it: the shape the filter's entries and the counts draw.</summary>
/// <param name="Class">The class being counted.</param>
/// <param name="Mounts">How many mounts of the shelf carry it.</param>
public sealed record MountAvailabilityCount(MountAvailability Class, int Mounts);

/// <summary>
/// The classification itself: what class a mount's obtain lines put it in, the one spelling of each class, and
/// the counts the interface shows.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1), and deliberately the <em>only</em> implementation: the generator writes the
/// field through <c>mountbook-tools classify</c>, the validator re-derives it to prove the file was not
/// hand-edited, the tests pin every type, and the plugin's views never classify anything - they read
/// <see cref="MountSources.Availability"/> that travelled with the row (T7).
/// </para>
/// <para>
/// The rules are a table over the obtain taxonomy plus a very small curated list. Anything the table cannot
/// answer stays <see cref="MountAvailability.Unknown"/>, which is a hard failure at patch time rather than a
/// guess: a new obtain type, or a row filed as <c>Other</c>, has to be classified by a person who looked at
/// what it actually is.
/// </para>
/// </remarks>
public static class MountAvailabilityRules
{
    /// <summary>
    /// The classes in the order the interface draws them: the one you can act on first, then the four reasons
    /// you cannot, then the rows nothing is known about.
    /// </summary>
    public static readonly IReadOnlyList<MountAvailability> Classes =
    [
        MountAvailability.Available,
        MountAvailability.Seasonal,
        MountAvailability.Ranked,
        MountAvailability.Premium,
        MountAvailability.Legacy,
        MountAvailability.Unknown,
    ];

    /// <summary>
    /// The obtain types that are obtainable today, folded to <see cref="MountSourceKinds.Key"/>.
    /// </summary>
    /// <remarks>
    /// Written out rather than "every type except the four special ones", because the default for a type this
    /// build has never seen must be <see cref="MountAvailability.Unknown"/>: an unclassified row is caught at
    /// patch time, whereas a wall of <c>default: Available</c> would quietly promise a new type is farmable.
    /// </remarks>
    private static readonly HashSet<string> PlayableTypes = new(StringComparer.Ordinal)
    {
        "achievement",
        "trial",
        "quest",
        "cosmic-exploration",
        "wondrous-tails",
        "tribal",
        "raid",
        "occult-crescent",
        "island-sanctuary",
        "gold-saucer",
        "v-c-dungeon",
        "deep-dungeon",
        "skybuilders",
        "fate",
        "treasure-hunt",
        "hunts",
        "purchase",
        "chaotic-raid",
        "bozja",
        "eureka",
        "gathering",
        "crafting",
        "dungeon",
        "voyages",
    };

    /// <summary>
    /// The currencies a PvP mount can simply be bought with. A <c>PvP</c> line naming one of these is a route
    /// that is open now, which is what makes those mounts <see cref="MountAvailability.Available"/> even
    /// though the taxonomy files them under PvP; a <c>PvP</c> line naming a season or a series is
    /// <see cref="MountAvailability.Ranked"/>, because that window is what closed.
    /// </summary>
    private static readonly string[] PvpCurrencies = ["Wolf Marks", "Trophy Crystals"];

    /// <summary>
    /// The few mounts whose class the taxonomy cannot see, each with the reason it is here. Curation is by id
    /// and it wins over every type rule: an entry is a decision somebody took after reading the row.
    /// </summary>
    /// <remarks>
    /// Two entries today, both 1.x rewards: the taxonomy files them as <c>Other</c>, which the rules leave
    /// <see cref="MountAvailability.Unknown"/> on purpose, and the text names a campaign that ended with the
    /// servers. Everything else the game has retired is a <c>PvP</c> season (already
    /// <see cref="MountAvailability.Ranked"/>) or an event (already <see cref="MountAvailability.Seasonal"/>).
    /// </remarks>
    private static readonly Dictionary<uint, (MountAvailability Class, string Why)> Curated = new()
    {
        [4] = (MountAvailability.Legacy, "At level 30 or above, speak with the Wandering Minstrel (FFXIV 1.0): the 1.x servers are gone."),
        [5] = (MountAvailability.Legacy, "Legacy Campaign: a 1.x account reward that was never reissued."),
    };

    /// <summary>Every curated id, in ascending order, so a test can pin that each one is in the snapshot.</summary>
    public static IReadOnlyCollection<uint> CuratedIds => Curated.Keys;

    /// <summary>
    /// The class <paramref name="id"/>'s obtain lines put it in, curation first and otherwise the most
    /// available route it has.
    /// </summary>
    /// <remarks>
    /// The precedence is "answer the player's question first": a mount with a route that is open today is
    /// <see cref="MountAvailability.Available"/> even if another of its lines is closed (a former PvP season
    /// reward that the crystal vendor stocks now), and a mount that is both an old event reward and in the
    /// store is <see cref="MountAvailability.Premium"/> because the store is a route that exists today. Only
    /// when nothing is open does the reason it is not decide the class.
    /// </remarks>
    /// <param name="id">The mount id, for the curated list.</param>
    /// <param name="lines">The mount's obtain lines; empty for a mount the dataset does not list.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    public static MountAvailability Classify(uint id, IReadOnlyList<MountSourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (Curated.TryGetValue(id, out (MountAvailability Class, string Why) curated))
        {
            return curated.Class;
        }

        var seen = MountAvailability.Unknown;

        foreach (MountSourceLine line in lines)
        {
            MountAvailability lineClass = LineClass(line);

            // The precedence, applied in one pass: a class earlier than what has been seen wins outright, and
            // nothing later can take it back.
            if (Rank(lineClass) < Rank(seen))
            {
                seen = lineClass;
            }
        }

        return seen;
    }

    /// <summary>The class one obtain line implies on its own, ignoring the mount's other lines.</summary>
    /// <param name="line">The line to read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is null.</exception>
    public static MountAvailability LineClass(MountSourceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string key = line.Kind.Key;

        if (key == "premium")
        {
            return MountAvailability.Premium;
        }

        if (key == "event")
        {
            return MountAvailability.Seasonal;
        }

        if (key == "pvp")
        {
            foreach (string currency in PvpCurrencies)
            {
                if (line.Detail.Contains(currency, StringComparison.OrdinalIgnoreCase))
                {
                    return MountAvailability.Available;
                }
            }

            return MountAvailability.Ranked;
        }

        return PlayableTypes.Contains(key) ? MountAvailability.Available : MountAvailability.Unknown;
    }

    /// <summary>The colour family a class is drawn in. The four unobtainable classes are four different tones.</summary>
    /// <param name="availability">The class.</param>
    public static MountAvailabilityTone ToneOf(MountAvailability availability) => availability switch
    {
        MountAvailability.Available => MountAvailabilityTone.Positive,
        MountAvailability.Seasonal => MountAvailabilityTone.Informative,
        MountAvailability.Ranked => MountAvailabilityTone.Caution,
        MountAvailability.Premium => MountAvailabilityTone.Brand,
        MountAvailability.Legacy => MountAvailabilityTone.Negative,
        _ => MountAvailabilityTone.None,
    };

    /// <summary>
    /// The class's one spelling: lower case, hyphen-folded, and the same string in the snapshot, in the
    /// saved filter and in the search grammar, so no two of them can drift apart.
    /// </summary>
    /// <param name="availability">The class.</param>
    public static string Key(MountAvailability availability) => availability switch
    {
        MountAvailability.Available => "available",
        MountAvailability.Seasonal => "seasonal",
        MountAvailability.Ranked => "ranked",
        MountAvailability.Premium => "premium",
        MountAvailability.Legacy => "legacy",
        _ => "unknown",
    };

    /// <summary>
    /// The search term that selects this class: <c>avail:available</c>. The one spelling of the term, so the
    /// seeded "Farmable now" view, the tooltip that teaches it and a test all write the same string.
    /// </summary>
    /// <param name="availability">The class.</param>
    public static string SearchTerm(MountAvailability availability) => "avail:" + Key(availability);

    /// <summary>
    /// Reads a class from its spelling, case and surrounding whitespace folded. A value naming no class reads
    /// as <see cref="MountAvailability.Unknown"/>: a snapshot field or a saved filter is data, and a word this
    /// build does not know must not be guessed at.
    /// </summary>
    /// <param name="text">The spelling, or null.</param>
    public static MountAvailability Parse(string? text) =>
        TryParseKey(text, out MountAvailability availability) ? availability : MountAvailability.Unknown;

    /// <summary>
    /// Reads a class from a whole key, case and surrounding whitespace folded, and reports whether the text
    /// named one at all. <see cref="Parse"/> and this differ in what they say about a word this build does not
    /// know: the snapshot field must still yield a value (Unknown, which is also a class), while a saved filter
    /// has to be able to tell "the player chose Unknown" from "the file holds rubbish" so the second can be
    /// cleared instead of filtering the shelf down to the unclassified rows.
    /// </summary>
    /// <param name="text">The spelling, or null.</param>
    /// <param name="availability">The class the text names, or <see cref="MountAvailability.Unknown"/>.</param>
    public static bool TryParseKey(string? text, out MountAvailability availability)
    {
        availability = MountAvailability.Unknown;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string key = MountSourceKinds.Key(text.Trim());

        foreach (MountAvailability candidate in Classes)
        {
            if (string.Equals(Key(candidate), key, StringComparison.Ordinal))
            {
                availability = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a class from what a player typed after <c>avail:</c>: a whole key (<c>available</c>,
    /// <c>ranked</c>) or the beginning of one (<c>season</c>, <c>leg</c>). Every key starts with a different
    /// letter, so a single character is enough - but a value that begins two classes would still refuse
    /// rather than guess.
    /// </summary>
    /// <param name="value">The text after the colon.</param>
    /// <param name="availability">The class the text names.</param>
    /// <returns>False when it names none, or names more than one by being empty.</returns>
    public static bool TryParseOperator(string? value, out MountAvailability availability)
    {
        availability = MountAvailability.Unknown;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string key = MountSourceKinds.Key(value.Trim());

        if (key.Length == 0)
        {
            return false;
        }

        MountAvailability? prefix = null;

        foreach (MountAvailability candidate in Classes)
        {
            string candidateKey = Key(candidate);

            if (string.Equals(candidateKey, key, StringComparison.Ordinal))
            {
                availability = candidate;
                return true;
            }

            if (candidateKey.StartsWith(key, StringComparison.Ordinal))
            {
                if (prefix is not null)
                {
                    return false;
                }

                prefix = candidate;
            }
        }

        if (prefix is null)
        {
            return false;
        }

        availability = prefix.Value;
        return true;
    }

    /// <summary>
    /// How many mounts fall in each class, in <see cref="Classes"/> order, over <paramref name="items"/>.
    /// Only the classes the shelf actually carries are listed, so the filter never offers an entry that can
    /// select nothing - and <see cref="MountAvailability.Unknown"/> appears only when something is unknown.
    /// </summary>
    /// <param name="items">The whole shelf, not the filtered list: a count that moved with the filter would be
    /// counting itself.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static IReadOnlyList<MountAvailabilityCount> Tally(IReadOnlyList<MountShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var counts = new int[Classes.Count];

        foreach (MountShelfItem item in items)
        {
            int index = IndexOf(item.Availability);

            if (index >= 0)
            {
                counts[index]++;
            }
        }

        var tally = new List<MountAvailabilityCount>(counts.Length);

        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] > 0)
            {
                tally.Add(new MountAvailabilityCount(Classes[index], counts[index]));
            }
        }

        return tally;
    }

    /// <summary>How many mounts of <paramref name="items"/> are in <paramref name="availability"/>.</summary>
    /// <param name="items">The shelf to count.</param>
    /// <param name="availability">The class to count.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static int CountOf(IReadOnlyList<MountShelfItem> items, MountAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(items);

        var count = 0;

        foreach (MountShelfItem item in items)
        {
            if (item.Availability == availability)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Why a class says what it says, for the detail badge's tooltip: one sentence a player can act on, and
    /// the count of mounts that share it.
    /// </summary>
    /// <param name="availability">The class.</param>
    /// <param name="mounts">How many mounts of the shelf are in the class.</param>
    /// <param name="total">How many mounts the shelf holds.</param>
    public static string Explain(MountAvailability availability, int mounts, int total) => availability switch
    {
        MountAvailability.Available => Loc.F("availability.hint.available", mounts, total),
        MountAvailability.Seasonal => Loc.F("availability.hint.seasonal", mounts, total),
        MountAvailability.Ranked => Loc.F("availability.hint.ranked", mounts, total),
        MountAvailability.Premium => Loc.F("availability.hint.premium", mounts, total),
        MountAvailability.Legacy => Loc.F("availability.hint.legacy", mounts, total),
        _ => Loc.F("availability.hint.unknown", mounts, total),
    };

    /// <summary>The class's position in <see cref="Classes"/>, or -1 for a value outside the enum.</summary>
    /// <param name="availability">The class.</param>
    private static int IndexOf(MountAvailability availability)
    {
        for (var index = 0; index < Classes.Count; index++)
        {
            if (Classes[index] == availability)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Where a class sits in the precedence: lower wins. Curated classes are not ranked (they never compete,
    /// see <see cref="Classify"/>), and <see cref="MountAvailability.Unknown"/> is last so any class a line
    /// could name beats it.
    /// </summary>
    /// <param name="availability">The class.</param>
    private static int Rank(MountAvailability availability) => availability switch
    {
        MountAvailability.Available => 0,
        MountAvailability.Premium => 1,
        MountAvailability.Seasonal => 2,
        MountAvailability.Ranked => 3,
        MountAvailability.Legacy => 4,
        _ => 5,
    };
}
