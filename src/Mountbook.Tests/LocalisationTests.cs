using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The string table and the guards around it: that the shipped English parses, that every key the interface
/// asks for exists, that no user-facing literal is left in the views, and that a language with a partial
/// table reads its own strings and falls back to English for the rest.
/// </summary>
/// <remarks>
/// The view and window sources are embedded into this assembly as text (see Mountbook.Tests.csproj), so the
/// "no hardcoded label" rule is a test rather than a review habit. The table itself is the shipped file: an
/// edit that moves a word breaks the tests that pin the wording, which is what makes the wording a decision
/// instead of a drift.
/// </remarks>
public sealed class LocalisationTests
{
    /// <summary>The resource prefix the view and window sources are embedded under.</summary>
    private const string SourcePrefix = "Mountbook.Tests.Sources.";

    /// <summary>A string literal, with the escapes left as written.</summary>
    private static readonly Regex Literal = new("(?<![A-Za-z0-9_@$])\"((?:[^\"\\\\\\n]|\\\\.)*)\"", RegexOptions.Compiled);

    /// <summary>A shape only table keys take: lower camel words, dot separated, no spaces.</summary>
    private static readonly Regex KeyShape = new("^[a-z][A-Za-z0-9]*(?:\\.[A-Za-z0-9]+)+$", RegexOptions.Compiled);

    /// <summary>
    /// Literals that are not interface text and never will be: the obtain dataset's file format name (a
    /// promise to other copies of the file, so it cannot follow a translation), and the name of the Look the
    /// settings window picks when the saved one is gone.
    /// </summary>
    private static readonly HashSet<string> NonText = new(StringComparer.Ordinal)
    {
        "mountbook.wanted",
        "eorzea",
    };

    [Fact]
    public void TheShippedTableParsesAndIsEnglish()
    {
        Assert.True(Loc.FallbackCount > 200, $"the English table is suspiciously small: {Loc.FallbackCount} keys");
        Assert.Equal(InterfaceLanguageRules.Fallback, Loc.Language);
        Assert.Equal(Loc.FallbackCount, Loc.Count);
    }

    [Fact]
    public void EveryStringInTheTableIsNonEmpty()
    {
        foreach (KeyValuePair<string, string> entry in Loc.Table(InterfaceLanguage.English))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"'{entry.Key}' is blank");
        }
    }

    [Fact]
    public void EveryKeyTheInterfaceAsksForIsInTheTable()
    {
        var missing = new List<string>();

        foreach ((string file, string text) in Sources())
        {
            foreach (string literal in LiteralsIn(text))
            {
                if (!KeyShape.IsMatch(literal) || NonText.Contains(literal) || literal.StartsWith("source.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Loc.Has(literal))
                {
                    missing.Add($"{file}: {literal}");
                }
            }
        }

        Assert.True(missing.Count == 0, "keys asked for and not in the table:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryKeyInTheTableIsAskedForOrIsASourceCategory()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach ((_, string text) in Sources())
        {
            foreach (string literal in LiteralsIn(text))
            {
                _ = used.Add(literal);
            }
        }

        var unused = new List<string>();

        foreach (string key in Loc.Table(InterfaceLanguage.English).Keys)
        {
            // source.* labels are looked up from the dataset's own category strings, and availability.<class>
            // labels from MountAvailabilityRules.Key, never as literals. The availability.hint.* lines are
            // literals and are checked like everything else.
            if (!used.Contains(key) && !key.StartsWith("source.", StringComparison.Ordinal) && !IsAvailabilityLabel(key))
            {
                unused.Add(key);
            }
        }

        Assert.True(unused.Count == 0, "keys nothing asks for:\n  " + string.Join("\n  ", unused));
    }

    /// <summary>
    /// True for a table key that is one availability class's own label - the keys
    /// <see cref="UiText.Availability"/> builds from <see cref="MountAvailabilityRules.Key"/> rather than from a
    /// literal, which is why the sweep above cannot see them.
    /// </summary>
    /// <param name="key">The table key to test.</param>
    private static bool IsAvailabilityLabel(string key)
    {
        foreach (MountAvailability availability in MountAvailabilityRules.Classes)
        {
            if (string.Equals(key, "availability." + MountAvailabilityRules.Key(availability), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void EverySourceCategoryInTheDatasetHasALabel()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mount-sources.json"));
        using JsonDocument document = JsonDocument.Parse(json);

        var categories = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement mount in document.RootElement.GetProperty("mounts").EnumerateArray())
        {
            if (!mount.TryGetProperty("sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement source in sources.EnumerateArray())
            {
                if (source.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String)
                {
                    _ = categories.Add(type.GetString() ?? string.Empty);
                }
            }
        }

        var unlabelled = new List<string>();

        foreach (string category in categories)
        {
            if (!Loc.Has("source." + UiText.SourceKey(category)))
            {
                unlabelled.Add(category);
            }
        }

        Assert.True(categories.Count > 20, "the dataset carries fewer categories than expected");
        Assert.True(unlabelled.Count == 0, "source categories with no label:\n  " + string.Join("\n  ", unlabelled));
    }

    [Fact]
    public void NoUserFacingLiteralIsLeftInTheViews()
    {
        var found = new List<string>();

        foreach ((string file, string text) in Sources("Views"))
        {
            foreach ((int line, string literal) in LineLiteralsIn(text))
            {
                if (!IsInterfaceText(literal))
                {
                    found.Add($"{file}:{line}: \"{literal}\"");
                }
            }
        }

        Assert.True(found.Count == 0, "hardcoded text in Views/:\n  " + string.Join("\n  ", found));
    }

    [Fact]
    public void TheWindowsKeepOnlyLogTextAndImGuiIdsAsLiterals()
    {
        var found = new List<string>();

        foreach ((string file, string text) in Sources("Windows"))
        {
            string[] lines = text.Split('\n');

            foreach ((int line, string literal) in LineLiteralsIn(text))
            {
                if (IsInterfaceText(literal)
                    || NonText.Contains(literal)
                    || lines[line - 1].Contains(".Log.", StringComparison.Ordinal)
                    || literal.Contains('{'))
                {
                    continue;
                }

                found.Add($"{file}:{line}: \"{literal}\"");
            }
        }

        Assert.True(found.Count == 0, "hardcoded text in Windows/:\n  " + string.Join("\n  ", found));
    }

    [Fact]
    public void ATranslationWinsOverEnglishAndAMissingKeyFallsBackToEnglish()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["detail.ride"] = "Ride",
            ["detail.want"] = "Want",
        };

        var japanese = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["detail.ride"] = "\u4E57\u308B",
        };

        var lookup = new StringLookup(japanese, english);

        Assert.Equal("\u4E57\u308B", lookup.T("detail.ride"));
        Assert.Equal("Want", lookup.T("detail.want"));
        Assert.Equal("detail.missing", lookup.T("detail.missing"));
        Assert.Equal("a default", lookup.TOr("detail.missing", "a default"));
        Assert.Equal("\u4E57\u308B", lookup.TOr("detail.ride", "a default"));
        Assert.Equal(1, lookup.Count);
        Assert.Equal(2, lookup.FallbackCount);
    }

    [Fact]
    public void AKeyNoTableHasReadsAsItselfRatherThanAsABlank()
    {
        Assert.Equal("detail.ride", StringLookup.Empty.T("detail.ride"));
        Assert.False(StringLookup.Empty.Has("detail.ride"));
        Assert.Equal(0, StringLookup.Empty.Count);
    }

    [Fact]
    public void FormattingUsesThePatternsPlaceholders()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["header.collected"] = "{0} of {1} collected",
        };

        var lookup = new StringLookup(table);

        Assert.Equal("9 of 12 collected", lookup.F("header.collected", 9, 12));
    }

    [Fact]
    public void EveryPatternsPlaceholdersAreContiguousAndFormattable()
    {
        var problems = new List<string>();

        foreach (KeyValuePair<string, string> entry in Loc.Table(InterfaceLanguage.English))
        {
            IReadOnlyList<int> placeholders = StringTable.Placeholders(entry.Value);

            for (var index = 0; index < placeholders.Count; index++)
            {
                if (placeholders[index] != index)
                {
                    problems.Add($"{entry.Key}: placeholders {string.Join(",", placeholders)} are not 0..n");
                    break;
                }
            }

            var arguments = new object?[placeholders.Count];

            for (var index = 0; index < arguments.Length; index++)
            {
                arguments[index] = "x";
            }

            try
            {
                _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, entry.Value, arguments);
            }
            catch (FormatException ex)
            {
                problems.Add($"{entry.Key}: {ex.Message}");
            }
        }

        Assert.True(problems.Count == 0, "patterns that do not format:\n  " + string.Join("\n  ", problems));
    }

    [Theory]
    [InlineData("Trial", "trial")]
    [InlineData("V&C Dungeon", "v-c-dungeon")]
    [InlineData("Cosmic Exploration", "cosmic-exploration")]
    [InlineData("Deep Dungeon", "deep-dungeon")]
    [InlineData("FATE", "fate")]
    public void SourceKeysArePredictable(string category, string expected) =>
        Assert.Equal(expected, UiText.SourceKey(category));

    /// <summary>
    /// The grid's section headings: the two sentinels <see cref="MountGroups"/> emits for an unrecorded patch or
    /// no obtain data, and the expansion names it sorts by. The mapping had no test at all while the sentinels
    /// are what a reader actually sees at the bottom of a grouped list, and they are the two headings a
    /// translation can drop without any count changing.
    /// </summary>
    [Theory]
    [InlineData(MountGroups.NoPatch, MountGrouping.Patch, "No patch recorded")]
    [InlineData(MountGroups.NoSource, MountGrouping.Source, "No source recorded")]
    [InlineData("Shadowbringers", MountGrouping.Expansion, "Shadowbringers")]
    [InlineData("Expansion unknown", MountGrouping.Expansion, "Expansion unknown")]
    [InlineData("V&C Dungeon", MountGrouping.Source, "V&C Dungeon")]
    public void GroupTitlesMapTheSentinelsAndTheExpansionNames(string title, MountGrouping grouping, string expected) =>
        Assert.Equal(expected, UiText.GroupTitle(title, grouping));

    [Fact]
    public void AHeadingNoEnumKnowsIsDrawnAsIs()
    {
        // The fallback matters: a dataset that gains an expansion must draw its name rather than an empty heading.
        Assert.Equal("Solution Nine", UiText.Expansion("Solution Nine"));
        Assert.Equal("Patch 9.0", UiText.GroupTitle("Patch 9.0", MountGrouping.None));
    }

    [Theory]
    [InlineData("ja", InterfaceLanguage.Japanese)]
    [InlineData("JA", InterfaceLanguage.Japanese)]
    [InlineData("de", InterfaceLanguage.German)]
    [InlineData("fr", InterfaceLanguage.French)]
    [InlineData("zh", InterfaceLanguage.ChineseSimplified)]
    [InlineData("zh-Hant", InterfaceLanguage.ChineseTraditional)]
    [InlineData("ko", InterfaceLanguage.Korean)]
    [InlineData("en", InterfaceLanguage.English)]
    [InlineData("xx", InterfaceLanguage.English)]
    [InlineData("", InterfaceLanguage.English)]
    public void DalamudUiLanguageCodesMapToATable(string code, InterfaceLanguage expected) =>
        Assert.Equal(expected, InterfaceLanguageRules.FromUiLanguage(code));

    [Fact]
    public void EveryLanguageHasATableCodeAndACodeMapsBack()
    {
        foreach (string code in InterfaceLanguageRules.Codes)
        {
            InterfaceLanguage? language = InterfaceLanguageRules.FromCode(code);

            Assert.NotNull(language);
            Assert.Equal(code, InterfaceLanguageRules.CodeOf(language!.Value));
        }
    }

    /// <summary>
    /// The one non-text literal the views may keep: the ellipsis run the settings pane measures with, which
    /// is ASCII on purpose (the headless preview's atlas has no U+2026) and is typography, not a word.
    /// </summary>
    private const string Ellipsis = "...";

    /// <summary>True for a literal the guard accepts: an ImGui id, a separator, or a table key.</summary>
    private static bool IsInterfaceText(string literal) =>
        literal.StartsWith("##", StringComparison.Ordinal)
        || literal.Trim().Length == 0
        || string.Equals(literal, Ellipsis, StringComparison.Ordinal)
        || Loc.Has(literal);

    /// <summary>The embedded view or window sources, as (file name, text).</summary>
    private static IEnumerable<(string File, string Text)> Sources(string area = "") =>
        SourcesWhere(name => name.StartsWith(SourcePrefix + (area.Length == 0 ? string.Empty : area + "."), StringComparison.Ordinal));

    private static IEnumerable<(string File, string Text)> SourcesWhere(Func<string, bool> predicate)
    {
        Assembly assembly = typeof(LocalisationTests).Assembly;

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!predicate(name))
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            string file = name[(name.LastIndexOf('.', name.Length - 4) + 1)..];
            yield return (file, reader.ReadToEnd());
        }
    }

    /// <summary>The string literals in a source, with comments removed so documentation is not flagged.</summary>
    private static IEnumerable<string> LiteralsIn(string source)
    {
        foreach ((_, string literal) in LineLiteralsIn(source))
        {
            yield return literal;
        }
    }

    /// <summary>The string literals in a source with the line each sits on, comments removed.</summary>
    private static IEnumerable<(int Line, string Literal)> LineLiteralsIn(string source)
    {
        string code = WithoutComments(source);
        string[] lines = source.Split('\n');

        foreach (Match match in Literal.Matches(code))
        {
            // The code keeps the line count (comments are replaced by their newlines), so an offset in one
            // is a line number in the other.
            int line = code.AsSpan(0, match.Index).Count('\n') + 1;
            _ = lines;
            yield return (line, match.Groups[1].Value);
        }
    }

    /// <summary>
    /// The source with comments blanked out, keeping every newline so offsets and line numbers survive.
    /// String and character literals are skipped whole, so a "//" inside a message is not a comment.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var text = source.ToCharArray();
        var index = 0;

        while (index < text.Length)
        {
            char character = text[index];

            if (character is '"' or '\'')
            {
                char quote = character;
                index++;

                while (index < text.Length)
                {
                    if (text[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (text[index] == quote)
                    {
                        index++;
                        break;
                    }

                    index++;
                }

                continue;
            }

            if (character == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    text[index++] = ' ';
                }

                continue;
            }

            if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                while (index < text.Length && !(text[index] == '*' && index + 1 < text.Length && text[index + 1] == '/'))
                {
                    if (text[index] != '\n')
                    {
                        text[index] = ' ';
                    }

                    index++;
                }

                index = Math.Min(index + 2, text.Length);
                continue;
            }

            index++;
        }

        return new string(text);
    }
}
