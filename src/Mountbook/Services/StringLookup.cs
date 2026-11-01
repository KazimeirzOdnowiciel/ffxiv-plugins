using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>
/// One language's strings, with the English table behind them: the pure half of <see cref="Loc"/>, and the
/// thing a test can hold without touching the global the plugin installed.
/// </summary>
/// <remarks>
/// The chain is the table, then the fallback, then the key itself. The last step is deliberate: a key that
/// reached a player draws something they can quote in a bug report, instead of a blank or a throw on the
/// draw path. A key the code asks for and no table defines is a test failure, so it does not reach anyone.
/// </remarks>
public sealed class StringLookup
{
    private static readonly Dictionary<string, string> None = [];

    private readonly IReadOnlyDictionary<string, string> table;

    private readonly IReadOnlyDictionary<string, string> fallback;

    /// <summary>
    /// A lookup over <paramref name="table"/>, falling back to <paramref name="fallback"/> and then to the
    /// key.
    /// </summary>
    /// <param name="table">The language being drawn.</param>
    /// <param name="fallback">The table to read when <paramref name="table"/> has no such key, usually English.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    public StringLookup(IReadOnlyDictionary<string, string> table, IReadOnlyDictionary<string, string>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        this.table = table;
        this.fallback = fallback ?? None;
    }

    /// <summary>A lookup with nothing installed: every key reads as itself.</summary>
    public static StringLookup Empty { get; } = new(None);

    /// <summary>How many strings the table itself defines.</summary>
    public int Count => table.Count;

    /// <summary>How many strings the fallback defines: every key the plugin knows.</summary>
    public int FallbackCount => fallback.Count;

    /// <summary>The text for <paramref name="key"/>, falling back to English and then to the key.</summary>
    /// <param name="key">The table key, e.g. <c>detail.ride</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public string T(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (table.TryGetValue(key, out string? text))
        {
            return text;
        }

        return fallback.TryGetValue(key, out string? english) ? english : key;
    }

    /// <summary>
    /// The text for <paramref name="key"/>, or <paramref name="otherwise"/> when neither table defines it.
    /// For values that are data rather than interface text, such as a source category the dataset names.
    /// </summary>
    /// <param name="key">The table key.</param>
    /// <param name="otherwise">What to draw when no table has the key.</param>
    public string TOr(string key, string otherwise)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (table.TryGetValue(key, out string? text))
        {
            return text;
        }

        return fallback.TryGetValue(key, out string? english) ? english : otherwise;
    }

    /// <summary>
    /// The text for <paramref name="key"/> with its <c>{0}</c>-style placeholders filled, formatted with the
    /// current culture so a player's own digits and separators are used.
    /// </summary>
    /// <param name="key">The table key.</param>
    /// <param name="arguments">The values for the placeholders, in order.</param>
    /// <exception cref="FormatException">The pattern is not a valid composite format.</exception>
    public string F(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(key), arguments);

    /// <summary>True when either table defines <paramref name="key"/>.</summary>
    /// <param name="key">The table key.</param>
    public bool Has(string key) =>
        !string.IsNullOrEmpty(key) && (table.ContainsKey(key) || fallback.ContainsKey(key));
}
