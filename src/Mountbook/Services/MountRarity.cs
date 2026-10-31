using System;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>
/// The one-line reading of how many collectors own a mount.
/// </summary>
/// <remarks>
/// Pure and Dalamud-free so it can be tested without the game (T1). ffxivcollect reports its
/// <c>owned</c> field rounded to one decimal, which makes zero a special case: it does not mean
/// "nobody owns this", it means "fewer than the rounding step", and printing "0%" would claim
/// something the data cannot support.
/// </remarks>
public static class MountRarity
{
    /// <summary>The step ffxivcollect rounds its ownership percentage to.</summary>
    public const double RoundingStep = 0.1;

    /// <summary>
    /// "Owned by 2.4% of collectors", the "fewer than" wording for a rounded zero, the "every
    /// collector" wording at the top of the range, or an empty string when the rarity is unknown -
    /// in which case the caller draws nothing at all rather than a placeholder.
    /// </summary>
    /// <param name="ownedPercent">The share of collectors who own the mount, or null when unrecorded.</param>
    public static string Describe(double? ownedPercent)
    {
        if (ownedPercent is not { } percent || double.IsNaN(percent))
        {
            return string.Empty;
        }

        var rounded = Math.Round(percent, 1, MidpointRounding.AwayFromZero);

        if (rounded <= 0d)
        {
            return string.Concat("Fewer than ", Format(RoundingStep), "% of collectors own this");
        }

        // 99.96 and up rounds to 100, and "owned by 100% of collectors" would be a claim the site
        // never made; the honest reading of a number that rounds to the top is the top.
        if (rounded >= 100d)
        {
            return "Every collector owns this";
        }

        return string.Concat("Owned by ", Format(rounded), "% of collectors");
    }

    /// <summary>The percentage the way the site writes it: one decimal at most, no trailing ".0".</summary>
    /// <param name="percent">The percentage to format.</param>
    public static string Format(double percent) =>
        Math.Round(percent, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// The same fact without the leading clause ("5.2% of collectors"), for the narrow pane where the
    /// full sentence does not fit: dropping words reads better than an ellipsis in the middle of a number.
    /// </summary>
    /// <param name="ownedPercent">The share of collectors who own the mount, or null when unrecorded.</param>
    public static string DescribeCompact(double? ownedPercent)
    {
        if (ownedPercent is not { } percent || double.IsNaN(percent))
        {
            return string.Empty;
        }

        var rounded = Math.Round(percent, 1, MidpointRounding.AwayFromZero);

        if (rounded <= 0d)
        {
            return string.Concat("under ", Format(RoundingStep), "% of collectors");
        }

        if (rounded >= 100d)
        {
            return "every collector";
        }

        return string.Concat(Format(rounded), "% of collectors");
    }
}
