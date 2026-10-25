using System.Globalization;

namespace GilLedger.Core;

public static class Money
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Exact(long value) => value.ToString("N0", Invariant);

    public static string Compact(long value)
    {
        var absolute = System.Math.Abs(value);

        if (absolute >= 1_000_000_000)
            return (value / 1_000_000_000d).ToString("0.##", Invariant) + "B";

        if (absolute >= 1_000_000)
            return (value / 1_000_000d).ToString("0.##", Invariant) + "M";

        if (absolute >= 10_000)
            return (value / 1_000d).ToString("0.#", Invariant) + "k";

        return value.ToString("N0", Invariant);
    }

    public static string Signed(long value) =>
        value >= 0 ? "+" + Exact(value) : Exact(value);

    /// <summary>Reads a gil amount out of a formatted string ("1,234" / "1 234 gil").</summary>
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
