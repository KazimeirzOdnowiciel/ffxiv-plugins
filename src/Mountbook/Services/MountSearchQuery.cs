using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>
/// The search box's grammar. Plain words still match name, lore, tooltip, patch, expansion and source,
/// and a term written <c>key:value</c> narrows the list by a fact instead: <c>tradeable:y</c>,
/// <c>music:n</c>, <c>barding:y</c>, <c>flying:y</c>, <c>seats:&gt;=2</c>, <c>patch:&gt;=6.1</c>,
/// <c>owned:&lt;1</c>.
/// </summary>
/// <remarks>
/// <para>
/// A term only becomes a constraint when its key is known <em>and</em> its value parses. Anything else is
/// left in the free text, so "Trial: Byakko" and a typo both behave exactly as they did before the
/// operators existed. A constraint never matches a mount whose fact is unrecorded: "I do not know" is not
/// "no", and it is certainly not "yes".
/// </para>
/// <para>
/// <c>owned:</c> compares the share of collectors who own the mount, so <c>owned:&lt;1</c> reads "rarer
/// than 1% of collectors" - the same number the detail pane shows.
/// </para>
/// </remarks>
public sealed class MountSearchQuery
{
    private static readonly MountSearchQuery MatchEverything = new(string.Empty, []);

    private readonly string text;

    private readonly List<Constraint> constraints;

    private MountSearchQuery(string text, List<Constraint> constraints)
    {
        this.text = text;
        this.constraints = constraints;
    }

    /// <summary>Whether the mount satisfies every constraint and matches the remaining free text.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public bool Matches(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (Constraint constraint in constraints)
        {
            if (!constraint.Matches(item))
            {
                return false;
            }
        }

        return text.Length == 0 || MatchesText(item, text);
    }

    /// <summary>Parses a search string; an empty or whitespace-only one matches everything.</summary>
    /// <param name="search">What the player typed.</param>
    public static MountSearchQuery Parse(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return MatchEverything;
        }

        var constraints = new List<Constraint>();
        var words = new List<string>();

        foreach (string token in search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseTerm(token, out Constraint constraint))
            {
                constraints.Add(constraint);
            }
            else
            {
                words.Add(token);
            }
        }

        return new MountSearchQuery(string.Join(' ', words), constraints);
    }

    private static bool TryParseTerm(string token, out Constraint constraint)
    {
        constraint = default;

        int colon = token.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 0 || colon == token.Length - 1)
        {
            return false;
        }

        string key = token[..colon].ToLowerInvariant();
        string value = token[(colon + 1)..];

        switch (key)
        {
            case "tradeable":
            case "trade":
                return TryParseFlag(value, out bool tradeable)
                    && Set(out constraint, Fact.Tradeable, Comparison.Equal, tradeable ? 1d : 0d);

            case "music":
                return TryParseFlag(value, out bool music)
                    && Set(out constraint, Fact.CustomMusic, Comparison.Equal, music ? 1d : 0d);

            case "seats":
                return TryParseComparison(value, out Comparison seatsOp, out double seats)
                    && Set(out constraint, Fact.Seats, seatsOp, seats);

            case "patch":
                return TryParseComparison(value, out Comparison patchOp, out string patchText)
                    && MountExpansionRules.PatchValue(patchText) is { } patch
                    && Set(out constraint, Fact.Patch, patchOp, (double)patch);

            case "barding":
                return TryParseFlag(value, out bool barding)
                    && Set(out constraint, Fact.Barding, Comparison.Equal, barding ? 1d : 0d);

            case "flying":
            case "fly":
                return TryParseFlag(value, out bool flying)
                    && Set(out constraint, Fact.Flying, Comparison.Equal, flying ? 1d : 0d);

            case "owned":
            case "rarity":
                return TryParseComparison(value.TrimEnd('%'), out Comparison ownedOp, out double owned)
                    && Set(out constraint, Fact.Owned, ownedOp, owned);

            default:
                // An unknown key is free text: "Trial: Byakko" must keep working as a source search.
                return false;
        }
    }

    private static bool Set(out Constraint constraint, Fact fact, Comparison comparison, double value)
    {
        constraint = new Constraint(fact, comparison, value);
        return true;
    }

    private static bool TryParseFlag(string value, out bool flag)
    {
        switch (value.ToLowerInvariant())
        {
            case "y":
            case "yes":
            case "true":
            case "1":
            case "on":
                flag = true;
                return true;

            case "n":
            case "no":
            case "false":
            case "0":
            case "off":
                flag = false;
                return true;

            default:
                flag = false;
                return false;
        }
    }

    /// <summary>Splits an optional comparison prefix off a numeric operand.</summary>
    private static bool TryParseComparison(string value, out Comparison comparison, out string operand)
    {
        comparison = Comparison.Equal;
        operand = value;

        foreach ((string prefix, Comparison kind) in Prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                comparison = kind;
                operand = value[prefix.Length..];
                break;
            }
        }

        return operand.Length > 0;
    }

    private static bool TryParseComparison(string value, out Comparison comparison, out double operand)
    {
        operand = 0d;

        return TryParseComparison(value, out comparison, out string text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out operand);
    }

    private static readonly (string Prefix, Comparison Kind)[] Prefixes =
    [
        (">=", Comparison.AtLeast),
        ("<=", Comparison.AtMost),
        (">", Comparison.MoreThan),
        ("<", Comparison.LessThan),
        ("=", Comparison.Equal),
    ];

    /// <summary>The pre-operator matching: a case-insensitive substring over every visible fact.</summary>
    private static bool MatchesText(MountShelfItem item, string search)
    {
        if (item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.EnhancedDescription.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Tooltip.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // "endwalker", "EW" and "6.1" all narrow the list the way a player would expect: the patch is
        // visible on every card, and the expansion is the filter everyone thinks in.
        if (item.Patch.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MountBreakdown.ExpansionForPatch(item.Patch) is { } expansion
            && (expansion.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || expansion.ShortName.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (MountSourceLine source in item.Sources)
        {
            if (source.Detail.Contains(search, StringComparison.OrdinalIgnoreCase)
                || source.Category.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What a term compares against.</summary>
    private enum Fact
    {
        Tradeable,
        CustomMusic,
        Seats,
        Patch,
        Owned,
        Barding,
        Flying,
    }

    private enum Comparison
    {
        Equal,
        AtLeast,
        AtMost,
        MoreThan,
        LessThan,
    }

    /// <summary>One parsed <c>key:value</c> term.</summary>
    private readonly record struct Constraint(Fact Fact, Comparison Comparison, double Value)
    {
        /// <summary>False when the mount does not carry the fact at all, whatever the comparison.</summary>
        public bool Matches(MountShelfItem item)
        {
            return FactOf(item, Fact) is { } actual && Holds(actual);
        }

        private bool Holds(double actual) => Comparison switch
        {
            Comparison.AtLeast => actual >= Value,
            Comparison.AtMost => actual <= Value,
            Comparison.MoreThan => actual > Value,
            Comparison.LessThan => actual < Value,
            _ => actual.Equals(Value),
        };

        /// <summary>The fact as a number, or null when the dataset does not record it.</summary>
        private static double? FactOf(MountShelfItem item, Fact fact) => fact switch
        {
            Fact.Tradeable => item.Community?.Tradeable is { } tradeable ? (tradeable ? 1d : 0d) : null,
            Fact.CustomMusic => item.Community?.CustomMusic is { } music ? (music ? 1d : 0d) : null,
            Fact.Seats => item.Seats,
            Fact.Patch => MountExpansionRules.PatchValue(item.Patch) is { } patch ? (double)patch : null,
            Fact.Owned => item.Community?.OwnedPercent,
            // The sheet always answers these two, so they are never "unrecorded": no barding slot means
            // no, and a mount that cannot fly is not "unknown".
            Fact.Barding => item.Barding ? 1d : 0d,
            Fact.Flying => item.Flies ? 1d : 0d,
            _ => null,
        };
    }
}
