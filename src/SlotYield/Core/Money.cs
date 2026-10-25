using System;
using System.Globalization;

namespace SlotYield.Core;

public static class Money
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Compact gil for table cells: 12.3M, 845k, 9,999.</summary>
    public static string Gil(decimal value)
    {
        var absolute = Math.Abs(value);

        if (absolute >= 1_000_000_000m)
            return (value / 1_000_000_000m).ToString("0.##", Invariant) + "B";

        if (absolute >= 1_000_000m)
            return (value / 1_000_000m).ToString("0.##", Invariant) + "M";

        if (absolute >= 10_000m)
            return (value / 1_000m).ToString("0.#", Invariant) + "k";

        return value.ToString("N0", Invariant);
    }

    public static string Exact(decimal value) => value.ToString("N0", Invariant);

    public static string Percent(double fraction) => (fraction * 100).ToString("0.#", Invariant) + "%";

    public static string Days(double days)
    {
        if (double.IsInfinity(days) || days <= 0)
            return "-";

        if (days < 1)
            return "<1d";

        return days.ToString("0.#", Invariant) + "d";
    }

    public static string Units(double units) => units.ToString("0.00", Invariant);

    /// <summary>Reads a gil amount out of a formatted UI string ("1,234" / "1 234").</summary>
    public static long ParseGil(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        long value = 0;
        var sawDigit = false;

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
                continue;

            value = (value * 10) + (character - '0');
            sawDigit = true;

            if (value > 999_999_999)
                return 999_999_999;
        }

        return sawDigit ? value : 0;
    }
}
