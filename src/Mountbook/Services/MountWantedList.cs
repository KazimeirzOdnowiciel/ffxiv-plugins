using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Mountbook.Services;

/// <summary>
/// What a pasted wanted list turned into, and what had to be left behind doing it.
/// </summary>
/// <param name="Wanted">The ids to add, ascending and without duplicates.</param>
/// <param name="Duplicates">Entries naming an id the file had already listed.</param>
/// <param name="Unknown">Ids this book has no mount for, so they were not imported.</param>
/// <param name="Invalid">Entries that carried no usable id at all.</param>
public sealed record MountWantedImport(IReadOnlyList<uint> Wanted, int Duplicates, int Unknown, int Invalid);

/// <summary>
/// The wanted list as a shareable document: a versioned JSON object of mount ids, with the mount's
/// name kept beside each id so a person can read the file without the plugin.
/// </summary>
/// <remarks>
/// The ids are the truth and the names are a comment: a name is written when the caller can resolve
/// one and never read back, so a stale or translated name cannot bring in the wrong mount. Kept
/// Dalamud-free and pure (T1) — the settings window decides when to touch the clipboard.
/// </remarks>
public static class MountWantedList
{
    /// <summary>The version this build writes; a file from a newer version is refused rather than guessed at.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The <c>format</c> member, so a pasted theme or diagnostics block is rejected as a whole file.</summary>
    private const string FormatName = "mountbook.wanted";

    /// <summary>
    /// The wanted list as JSON, ids ascending so two exports of the same list are byte-identical.
    /// </summary>
    /// <param name="ids">The wanted mount ids.</param>
    /// <param name="nameOf">Resolves a mount's display name, or null when the book does not carry it.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static string Write(IReadOnlyList<uint> ids, Func<uint, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(nameOf);

        var ordered = new List<uint>(ids);
        ordered.Sort();

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", FormatName);
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteStartArray("mounts");

            uint previous = 0;

            foreach (uint id in ordered)
            {
                // A set that carries the same id twice (a hand-edited config) must not become a file
                // that lists it twice.
                if (id == previous)
                {
                    continue;
                }

                previous = id;

                writer.WriteStartObject();
                writer.WriteNumber("id", id);

                if (nameOf(id) is { Length: > 0 } name)
                {
                    writer.WriteString("name", name);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a wanted list, keeping only the ids <paramref name="knownIds"/> carries.
    /// </summary>
    /// <param name="json">The pasted text.</param>
    /// <param name="knownIds">Every id the catalog knows, used to drop the rest.</param>
    /// <param name="result">The ids to add and what was left behind; empty when this returns false.</param>
    /// <param name="error">A sentence for the player when this returns false.</param>
    /// <returns>True when the text was a wanted list this build understands.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="knownIds"/> is null.</exception>
    public static bool TryParse(
        string? json,
        IReadOnlyCollection<uint> knownIds,
        out MountWantedImport result,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(knownIds);

        result = new MountWantedImport([], 0, 0, 0);

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The clipboard is empty.";

            return false;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException)
        {
            error = "That is not JSON.";

            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                error = "A wanted list is a JSON object with a \"mounts\" array, or an array of mount ids.";

                return false;
            }

            // A bare array is what a hand-written list looks like; the object form is what we write.
            JsonElement mounts = root;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetMember(root, "version", out JsonElement version))
                {
                    if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number))
                    {
                        error = "\"version\" must be a number.";

                        return false;
                    }

                    if (number > CurrentVersion)
                    {
                        error = string.Concat(
                            "This list was written by a newer Mountbook (version ",
                            number.ToString(CultureInfo.InvariantCulture),
                            ").");

                        return false;
                    }
                }

                if (!TryGetMember(root, "mounts", out mounts))
                {
                    error = "A wanted list is a JSON object with a \"mounts\" array.";

                    return false;
                }
            }

            if (mounts.ValueKind != JsonValueKind.Array)
            {
                error = "\"mounts\" must be an array.";

                return false;
            }

            var wanted = new List<uint>();
            var seen = new HashSet<uint>();
            var duplicates = 0;
            var unknown = 0;
            var invalid = 0;

            foreach (JsonElement entry in mounts.EnumerateArray())
            {
                if (!TryReadId(entry, out uint id))
                {
                    invalid++;

                    continue;
                }

                if (!seen.Add(id))
                {
                    duplicates++;

                    continue;
                }

                // A mount this book has no row for cannot be shown, so importing it would make the
                // wanted count promise something the list cannot deliver.
                if (!Contains(knownIds, id))
                {
                    unknown++;

                    continue;
                }

                wanted.Add(id);
            }

            wanted.Sort();
            result = new MountWantedImport(wanted, duplicates, unknown, invalid);
            error = string.Empty;

            return true;
        }
    }

    /// <summary>One line for the toast after an import, with the leftovers named honestly.</summary>
    /// <param name="imported">How many ids were added to the wanted list.</param>
    /// <param name="already">How many were already on the player's list.</param>
    /// <param name="unknown">How many ids this book does not carry.</param>
    /// <param name="invalid">How many entries carried no usable id.</param>
    public static string ImportSummary(int imported, int already, int unknown, int invalid)
    {
        var builder = new StringBuilder();

        builder.Append(imported == 0
            ? "Nothing new to import"
            : string.Concat("Imported ", Count(imported, "mount", "mounts")));

        var notes = new List<string>(3);

        if (already > 0)
        {
            notes.Add(string.Concat(Count(already, "was", "were"), " already wanted"));
        }

        if (unknown > 0)
        {
            notes.Add(string.Concat(Count(unknown, "is not", "are not"), " in your book"));
        }

        if (invalid > 0)
        {
            notes.Add(string.Concat(Count(invalid, "entry", "entries"), " had no mount id"));
        }

        if (notes.Count > 0)
        {
            builder.Append(" (").Append(string.Join(", ", notes)).Append(')');
        }

        builder.Append('.');

        return builder.ToString();
    }

    /// <summary>"1 mount" / "12 mounts", for the counts a summary prints.</summary>
    private static string Count(int number, string singular, string plural) =>
        string.Concat(
            number.ToString(CultureInfo.InvariantCulture),
            " ",
            number == 1 ? singular : plural);

    /// <summary>
    /// The id one entry stands for: a number on its own, or an object's <c>id</c> member. A zero id is
    /// not a mount, so it reads as no id rather than as "the first row".
    /// </summary>
    private static bool TryReadId(JsonElement entry, out uint id)
    {
        id = 0;

        if (entry.ValueKind == JsonValueKind.Number)
        {
            return entry.TryGetUInt32(out id) && id != 0;
        }

        if (entry.ValueKind == JsonValueKind.Object
            && TryGetMember(entry, "id", out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetUInt32(out id)
            && id != 0)
        {
            return true;
        }

        id = 0;

        return false;
    }

    private static bool Contains(IReadOnlyCollection<uint> ids, uint id)
    {
        // List<uint> is the common argument and the catalog's items are ordered by id, but a set is
        // what the wanted list itself is: check the cheap case first and fall back to a scan.
        if (ids is HashSet<uint> set)
        {
            return set.Contains(id);
        }

        foreach (uint known in ids)
        {
            if (known == id)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetMember(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;

                return true;
            }
        }

        value = default;

        return false;
    }
}
