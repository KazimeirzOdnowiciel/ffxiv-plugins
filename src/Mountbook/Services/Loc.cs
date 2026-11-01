using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Mountbook.Services;

/// <summary>
/// The one way a user-facing string gets into the interface: <see cref="T"/> for a label,
/// <see cref="F"/> for a sentence with numbers in it.
/// </summary>
/// <remarks>
/// <para>
/// Static, and installed once by the plugin, because the views draw from a state object and a Look and
/// nothing else (T6, T7): threading a string service through every draw call would carry a dependency into
/// the render path to reach a value that cannot change between frames. The lookup itself is
/// <see cref="StringLookup"/> - pure, and what a test holds when it wants a language other than the one the
/// plugin installed, without disturbing it.
/// </para>
/// <para>
/// The tables are embedded resources of the assembly that holds this class, found by the
/// <c>.Strings.&lt;code&gt;.json</c> suffix: the plugin's assembly carries them in the shipped build, and the
/// Dalamud-free test project embeds the same files so what it pins is the shipped English. Nothing is read
/// from disk at run time, and adding a language is adding a file.
/// </para>
/// <para>
/// Not thread-safe, and it does not need to be: Dalamud draws on one thread, and the only writer is the
/// plugin's construction, before the first frame.
/// </para>
/// </remarks>
public static class Loc
{
    /// <summary>
    /// The part of an embedded resource's name between the assembly root and the table's file name, e.g.
    /// <c>Mountbook.Data.Strings.en.json</c>.
    /// </summary>
    private const string ResourceMarker = ".Strings.";

    private static readonly Dictionary<InterfaceLanguage, IReadOnlyDictionary<string, string>> Installed = [];

    /// <summary>Guards the one-time scan. The draw path only ever reads the flag beside it.</summary>
    private static readonly object Gate = new();

    /// <summary>
    /// True once the scan ran to completion. Volatile because the readers are on the draw path and the
    /// setter is the first caller: a reader must never see this true with <see cref="lookup"/> still empty,
    /// which is what made one test read a key name instead of its text while another ran the scan.
    /// </summary>
    private static volatile bool scanned;

    private static StringLookup lookup = StringLookup.Empty;

    /// <summary>The language the interface is drawn in.</summary>
    public static InterfaceLanguage Language { get; private set; } = InterfaceLanguageRules.Fallback;

    /// <summary>How many strings the active language defines; a translation's progress reads from this.</summary>
    public static int Count
    {
        get
        {
            Ensure();
            return lookup.Count;
        }
    }

    /// <summary>How many strings the fallback language defines: every key the plugin knows.</summary>
    public static int FallbackCount
    {
        get
        {
            Ensure();
            return lookup.FallbackCount;
        }
    }

    /// <summary>
    /// The table installed for <paramref name="language"/>, or an empty one. Read-only, for tests and for
    /// the diagnostics dump.
    /// </summary>
    /// <param name="language">The language to read.</param>
    public static IReadOnlyDictionary<string, string> Table(InterfaceLanguage language) =>
        Installed.TryGetValue(language, out IReadOnlyDictionary<string, string>? table)
            ? table
            : new Dictionary<string, string>();

    /// <summary>
    /// Registers a table for a language without selecting it. <see cref="Scan"/> calls this once per embedded
    /// file; a test calls it with a fixture.
    /// </summary>
    /// <param name="language">The table's language.</param>
    /// <param name="table">The key/value pairs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    public static void Install(InterfaceLanguage language, IReadOnlyDictionary<string, string> table)
    {
        ArgumentNullException.ThrowIfNull(table);

        Installed[language] = table;

        if (language == Language)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Selects the language the interface draws in, reading the tables from this assembly's embedded
    /// resources when they have not been installed. A language with no table reads in English.
    /// </summary>
    /// <param name="language">The player's interface language.</param>
    public static void Use(InterfaceLanguage language)
    {
        Scan();

        Language = language;
        Rebuild();
    }

    /// <summary>
    /// Reads every embedded <c>*Strings.&lt;code&gt;.json</c> resource of the assembly that holds this class
    /// and installs it. Runs once; a table that does not parse is skipped rather than thrown, because this is
    /// also the path a draw call takes.
    /// </summary>
    /// <param name="onTableFailure">Where a skipped table is reported, with the resource name. Without it a
    /// broken translation disappears with no evidence at all (N4), which is what the plugin's load path passes
    /// a logger for; the headless previews and the tests pass nothing.</param>
    /// <returns>How many tables were installed.</returns>
    public static int Scan(Action<string, Exception>? onTableFailure = null)
    {
        lock (Gate)
        {
            return ScanOnce(onTableFailure);
        }
    }

    /// <summary>The body of <see cref="Scan"/>, called with <see cref="Gate"/> held.</summary>
    /// <param name="onTableFailure">Where a skipped table is reported; may be null.</param>
    private static int ScanOnce(Action<string, Exception>? onTableFailure)
    {
        if (scanned)
        {
            return Installed.Count;
        }

        Assembly assembly = typeof(Loc).Assembly;

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(StringTable.FileExtension, StringComparison.Ordinal)
                || !name.Contains(ResourceMarker, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream? stream = assembly.GetManifestResourceStream(name);

            if (stream is null)
            {
                continue;
            }

            try
            {
                (InterfaceLanguage language, IReadOnlyDictionary<string, string> strings) = StringTable.Read(stream, name);
                Installed[language] = strings;
            }
            catch (FormatException ex)
            {
                // A broken table must not take the window down, and it must not vanish either: the caller gets
                // the resource name so the log can say which translation was skipped (N4).
                onTableFailure?.Invoke(name, ex);
            }
        }

        Rebuild();

        // Last, inside the lock: until the lookup points at the tables, nothing may believe the scan is done.
        scanned = true;
        return Installed.Count;
    }

    /// <summary>
    /// The text for <paramref name="key"/> in the active language, falling back to English and then to the
    /// key itself.
    /// </summary>
    /// <param name="key">The table key, e.g. <c>detail.ride</c>.</param>
    /// <returns>The text to draw.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public static string T(string key)
    {
        Ensure();
        return lookup.T(key);
    }

    /// <summary>
    /// The text for <paramref name="key"/>, or <paramref name="otherwise"/> when no language defines it.
    /// For values that are data rather than interface text, such as a source category the dataset names.
    /// </summary>
    /// <param name="key">The table key.</param>
    /// <param name="otherwise">What to draw when the table has no such key.</param>
    public static string TOr(string key, string otherwise)
    {
        Ensure();
        return lookup.TOr(key, otherwise);
    }

    /// <summary>
    /// The text for <paramref name="key"/> with its <c>{0}</c>-style placeholders filled. The arguments are
    /// formatted with the current culture, so a player's own digits and separators are used.
    /// </summary>
    /// <param name="key">The table key.</param>
    /// <param name="arguments">The values for the placeholders, in order.</param>
    /// <returns>The formatted sentence.</returns>
    /// <exception cref="FormatException">The pattern is not a valid composite format.</exception>
    public static string F(string key, params object?[] arguments)
    {
        Ensure();
        return lookup.F(key, arguments);
    }

    /// <summary>True when the active or fallback table defines <paramref name="key"/>.</summary>
    /// <param name="key">The table key.</param>
    public static bool Has(string key)
    {
        Ensure();
        return lookup.Has(key);
    }

    /// <summary>
    /// Drops every installed table and returns to the pre-scan state. For tests only: the plugin installs
    /// once and never uninstalls.
    /// </summary>
    public static void Reset()
    {
        Installed.Clear();
        Language = InterfaceLanguageRules.Fallback;
        scanned = false;
        lookup = StringLookup.Empty;
    }

    /// <summary>
    /// Reads the embedded tables on first use, so a caller that never installs anything - the headless
    /// previews - still draws English rather than key names. One boolean test on the lookup path.
    /// </summary>
    private static void Ensure()
    {
        if (!scanned)
        {
            Scan();
        }
    }

    /// <summary>
    /// Points <see cref="lookup"/> at the selected language's table with English behind it. Called by
    /// <see cref="Scan"/>, <see cref="Use"/> and <see cref="Install"/>, so no caller can leave the two out of
    /// step.
    /// </summary>
    private static void Rebuild()
    {
        IReadOnlyDictionary<string, string> english = Installed.TryGetValue(InterfaceLanguageRules.Fallback, out IReadOnlyDictionary<string, string>? fallback)
            ? fallback
            : new Dictionary<string, string>();

        IReadOnlyDictionary<string, string> table = Installed.TryGetValue(Language, out IReadOnlyDictionary<string, string>? active)
            ? active
            : english;

        lookup = table.Count == 0 && english.Count == 0
            ? StringLookup.Empty
            : new StringLookup(table, english);
    }
}
