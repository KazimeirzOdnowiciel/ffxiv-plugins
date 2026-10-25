using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;

namespace GilLedger.Core;

/// <summary>
/// Labels a system message with a gil category and, when possible, the amount it mentions.
/// The amount here is advisory: the balance poller owns the real delta. These are hints.
/// </summary>
public sealed record ParsedHint(GilKind Kind, long Amount, string Text);

public static class ChatParser
{
    private static readonly Regex GilAmount = new(
        @"([0-9][0-9,\.]*)\s*gil",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedHint? Parse(XivChatType type, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsRelevant(type))
            return null;

        GilKind kind;

        if (text.Contains("sold", StringComparison.OrdinalIgnoreCase))
            kind = GilKind.Sale;
        else if (text.Contains("purchase", StringComparison.OrdinalIgnoreCase))
            kind = GilKind.Purchase;
        else if (text.Contains("tax", StringComparison.OrdinalIgnoreCase))
            kind = GilKind.ListingTax;
        else if (text.Contains("repair", StringComparison.OrdinalIgnoreCase))
            kind = GilKind.Repair;
        else if (text.Contains("entrust", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("venture", StringComparison.OrdinalIgnoreCase))
            kind = GilKind.Venture;
        else if (text.Contains("sell", StringComparison.OrdinalIgnoreCase))
            kind = GilKind.VendorSell;
        else
            return null;

        return new ParsedHint(kind, ExtractGil(text), text);
    }

    private static long ExtractGil(string text)
    {
        var matches = GilAmount.Matches(text);
        if (matches.Count == 0)
            return 0;

        return Money.ParseGil(matches[^1].Groups[1].Value);
    }

    /// <summary>Only the system-ish channels that carry gil transactions.</summary>
    private static bool IsRelevant(XivChatType type) => type is
        XivChatType.SystemMessage or
        XivChatType.RetainerSale or
        XivChatType.SystemError or
        XivChatType.Notice or
        XivChatType.GatheringSystemMessage;
}
