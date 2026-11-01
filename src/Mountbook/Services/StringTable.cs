using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Mountbook.Services;

/// <summary>
/// One language's user-facing strings: the key/value table the views, the windows and the services that
/// write prose all ask instead of carrying literals.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1), and it never loads anything by itself: the table arrives as a JSON file
/// the caller was already able to read, so the tests can hand it a fixture and the plugin can hand it an
/// embedded resource. The file's shape is deliberately the plainest thing that works -
/// <c>{ "language": "en", "strings": { "key": "text" } }</c> - because the next maintainer is a
/// translator with a text editor, not a compiler.
/// </para>
/// <para>
/// A table is keyed by <see cref="InterfaceLanguage"/> and never by the Dalamud enum: this file is
/// compiled into the Dalamud-free test project, where that enum does not exist.
/// </para>
/// </remarks>
public static class StringTable
{
    /// <summary>The member that names the language, e.g. <c>"ja"</c>.</summary>
    public const string LanguageMember = "language";

    /// <summary>The member that holds the key/value pairs.</summary>
    public const string StringsMember = "strings";

    /// <summary>The file suffix every language table uses, e.g. <c>en.json</c>.</summary>
    public const string FileExtension = ".json";

    /// <summary>
    /// Reads a table from JSON text.
    /// </summary>
    /// <param name="json">The table's text.</param>
    /// <param name="source">A name for the table in error messages, usually its file name.</param>
    /// <returns>The language the table declares and its strings.</returns>
    /// <exception cref="FormatException">The text is not a table: not JSON, no strings member, a non-text value, or a repeated key.</exception>
    public static (InterfaceLanguage Language, IReadOnlyDictionary<string, string> Strings) Parse(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{source} is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"{source} must be a JSON object with a \"{StringsMember}\" member.");
            }

            string? code = root.TryGetProperty(LanguageMember, out JsonElement language) && language.ValueKind == JsonValueKind.String
                ? language.GetString()
                : null;

            if (code is null || InterfaceLanguageRules.FromCode(code) is not { } resolved)
            {
                throw new FormatException(
                    $"{source} must declare \"{LanguageMember}\" as one of {string.Join(", ", InterfaceLanguageRules.Codes)}.");
            }

            if (!root.TryGetProperty(StringsMember, out JsonElement strings) || strings.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"{source} must have a \"{StringsMember}\" object of key/value pairs.");
            }

            var table = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (JsonProperty property in strings.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException($"{source}: the string '{property.Name}' must be text, not {property.Value.ValueKind}.");
                }

                if (!table.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
                {
                    throw new FormatException($"{source}: the key '{property.Name}' appears twice.");
                }
            }

            return (resolved, table);
        }
    }

    /// <summary>Reads a table from a stream, for an embedded resource.</summary>
    /// <param name="stream">The resource stream.</param>
    /// <param name="source">A name for the table in error messages.</param>
    /// <returns>The language the table declares and its strings.</returns>
    /// <exception cref="FormatException">The stream does not hold a valid table.</exception>
    public static (InterfaceLanguage Language, IReadOnlyDictionary<string, string> Strings) Read(Stream stream, string source)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), source);
    }

    /// <summary>
    /// Reads a table from a file on disk, for a fixture or a drop-in translation.
    /// </summary>
    /// <param name="path">The JSON file.</param>
    /// <returns>The language the table declares and its strings.</returns>
    /// <exception cref="FormatException">The file does not hold a valid table.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static (InterfaceLanguage Language, IReadOnlyDictionary<string, string> Strings) Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllText(path), Path.GetFileName(path));
    }

    /// <summary>
    /// The placeholders a pattern uses, in the order they appear, e.g. <c>{0}</c>.
    /// </summary>
    /// <param name="pattern">A table value such as <c>"{0} of {1} collected"</c>.</param>
    /// <returns>The distinct indexes the pattern names, ascending.</returns>
    /// <remarks>Used by the tests that check a translation keeps the placeholders its English pattern has.</remarks>
    public static IReadOnlyList<int> Placeholders(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var found = new SortedSet<int>(Comparer<int>.Default);
        var index = 0;

        while (index < pattern.Length)
        {
            var open = pattern.IndexOf('{', index);

            if (open < 0 || open + 1 >= pattern.Length)
            {
                break;
            }

            // "{{" is a literal brace, so it is not a placeholder.
            if (pattern[open + 1] == '{')
            {
                index = open + 2;
                continue;
            }

            var close = pattern.IndexOf('}', open + 1);

            if (close < 0)
            {
                break;
            }

            if (int.TryParse(pattern.AsSpan(open + 1, close - open - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                _ = found.Add(number);
            }

            index = close + 1;
        }

        return [.. found];
    }
}
