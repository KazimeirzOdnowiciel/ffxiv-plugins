using System;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>
/// A price written in a "How to obtain" line: how many tokens, and which ones.
/// </summary>
/// <param name="Amount">The number of tokens the mount costs.</param>
/// <param name="Currency">The token's name as the dataset spells it ("Seafarer's Cowries").</param>
/// <param name="Note">
/// Anything the line added in parentheses, kept verbatim because it is usually the requirement behind the
/// price ("Sanctuary Rank 10"): dropping it would turn "12,000 Seafarer's Cowries" into a promise the
/// player cannot meet yet.
/// </param>
public sealed record MountCost(int Amount, string Currency, string Note);

/// <summary>
/// Reads the token prices out of the dataset's source lines, and says how far along the player is.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1): the count comes from the caller, which is the only part that needs the game.
/// </para>
/// <para>
/// The dataset writes a purchase as a line that is nothing but a number and a token name - 51 of its 423
/// lines - and everything else as prose. That is the rule used here: a line that starts with a count and
/// parses cleanly is a price, and a line with any other shape is not. Being strict matters more than
/// catching every case, because a wrong "you have enough" is worse than no progress at all.
/// </para>
/// </remarks>
public static class MountCosts
{
    /// <summary>
    /// Reads <paramref name="text"/> as a token price, or returns null when the line is prose.
    /// </summary>
    /// <param name="text">A source line's detail text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static MountCost? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ReadOnlySpan<char> span = text.AsSpan().Trim();

        if (span.IsEmpty || !char.IsAsciiDigit(span[0]))
        {
            return null;
        }

        var digits = 0;

        while (digits < span.Length && (char.IsAsciiDigit(span[digits]) || span[digits] == ','))
        {
            digits++;
        }

        if (!int.TryParse(span[..digits], NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int amount)
            || amount <= 0)
        {
            return null;
        }

        if (digits >= span.Length)
        {
            return null;
        }

        ReadOnlySpan<char> rest = span[digits..].TrimStart();

        if (rest.IsEmpty)
        {
            return null;
        }

        var currency = rest.ToString().Trim();
        var note = string.Empty;

        // "12,000 Seafarer's Cowries (Sanctuary Rank 10)": the parenthetical is a requirement, not part of
        // the token's name, and the name is what the item lookup is keyed on.
        int open = currency.IndexOf('(', StringComparison.Ordinal);

        if (open > 0 && currency.EndsWith(')'))
        {
            note = currency[(open + 1)..^1].Trim();
            currency = currency[..open].Trim();
        }

        return currency.Length == 0 ? null : new MountCost(amount, currency, note);
    }

    /// <summary>
    /// One line for the detail pane: the price, and the progress when it is known.
    /// </summary>
    /// <param name="cost">The parsed price.</param>
    /// <param name="held">How many tokens the player has, or null when that cannot be counted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cost"/> is null.</exception>
    public static string Describe(MountCost cost, int? held)
    {
        ArgumentNullException.ThrowIfNull(cost);

        var wanted = cost.Amount.ToString("N0", CultureInfo.InvariantCulture);

        if (held is not { } have)
        {
            return string.Concat(wanted, " ", cost.Currency);
        }

        var mine = have.ToString("N0", CultureInfo.InvariantCulture);

        if (have >= cost.Amount)
        {
            return string.Concat(wanted, " ", cost.Currency, " - you have ", mine, ", enough to buy it");
        }

        var missing = (cost.Amount - have).ToString("N0", CultureInfo.InvariantCulture);

        return string.Concat(wanted, " ", cost.Currency, " - you have ", mine, ", ", missing, " to go");
    }
}
