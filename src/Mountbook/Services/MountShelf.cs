using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mountbook.Services;

/// <summary>
/// The embedded obtain dataset, indexed by mount id. Parsing lives here rather than in the
/// wiring layer so it can be tested without the game; the JSON shape is the one emitted by
/// <c>src/Mountbook/tools/generate-mount-sources.ps1</c>.
/// </summary>
/// <remarks>
/// <para>
/// The document carries a header as well as the rows: <c>schemaVersion</c> (the shape the
/// generator wrote), <c>generated</c> (the date), <c>enrichment</c> ("full" when ffxivcollect
/// answered, "sheet-only" when the snapshot was built offline), and the <c>attribution</c> the
/// plugin shows wherever the data is credited. Reading the version is what lets a snapshot written
/// by a different generator be <em>detected</em> - a shape this reader does not know is reported
/// once instead of being half-understood (N4).
/// </para>
/// <para>
/// The header is optional as far as parsing goes: a snapshot with no <c>schemaVersion</c> (the
/// first shipped shape) still yields its rows, with <see cref="SchemaVersion"/> zero and
/// <see cref="IsStale"/> true, because losing every obtain line is worse than reading an old one.
/// </para>
/// </remarks>
public sealed class MountShelf
{
    /// <summary>
    /// The shape this reader understands; the generator writes the same number. Kept in the reader
    /// rather than derived from the file so a snapshot cannot declare its own compatibility.
    /// </summary>
    /// <remarks>
    /// v2 added the header (provenance, attribution, size budget), v3 the sheet cross-check, v4 the per-row
    /// <c>availability</c> class and the header block that counts it - and stopped writing
    /// <c>relatedType</c>/<c>relatedId</c> on a source line that has neither, which the reader already reads as
    /// empty and zero, and which is what kept the field inside the size budget.
    /// </remarks>
    public const int CurrentSchemaVersion = 4;

    /// <summary>The snapshot that ships without a header, i.e. before <see cref="CurrentSchemaVersion"/>.</summary>
    public const int LegacySchemaVersion = 0;

    private readonly Dictionary<uint, MountSources> entries;

    private MountShelf(Dictionary<uint, MountSources> entries, MountShelfHeader header)
    {
        this.entries = entries;
        Header = header;
    }

    /// <summary>How many mounts the dataset carries.</summary>
    public int Count => entries.Count;

    /// <summary>The document's own description of itself: version, date, provenance and enrichment.</summary>
    public MountShelfHeader Header { get; }

    /// <summary>The shape the snapshot was written in; zero for a file with no <c>schemaVersion</c>.</summary>
    public int SchemaVersion => Header.SchemaVersion;

    /// <summary>The date the generator ran, or empty when the file does not say.</summary>
    public string Generated => Header.Generated;

    /// <summary>The site the enrichment came from, or empty when the file does not say.</summary>
    public string Source => Header.Source;

    /// <summary>Whether the snapshot was built with the site (<c>full</c>) or from the sheets alone.</summary>
    public string Enrichment => Header.Enrichment;

    /// <summary>The credit line to show beside the data, or empty when the file carries none.</summary>
    public string Attribution => Header.Attribution;

    /// <summary>The URL the credit line points at, or empty when the file carries none.</summary>
    public string AttributionUrl => Header.AttributionUrl;

    /// <summary>
    /// The build-time sheet comparison recorded in the header, or null when the snapshot does not
    /// record one (built offline, or by a run with no sheet facts). Null is "not checked", which is why
    /// no caller may treat it as "checked and fine".
    /// </summary>
    public MountShelfCrossCheck? CrossCheck => Header.CrossCheck;

    /// <summary>True when the snapshot records a sheet cross-check that actually ran.</summary>
    public bool HasCrossCheck => Header.HasCrossCheck;

    /// <summary>
    /// The generator's own count per availability class, from the header, in
    /// <see cref="MountAvailabilityRules.Classes"/> order. Empty when the file records none, which the
    /// validator reports: the counts are the evidence the classification ran, and this reader never derives
    /// them, because a count worked out here would agree with the rows by construction.
    /// </summary>
    public IReadOnlyList<MountAvailabilityCount> AvailabilityCounts => Header.Availability ?? [];

    /// <summary>True when the snapshot says how many mounts it put in each class.</summary>
    public bool HasAvailabilityCounts => Header.Availability is { Count: > 0 };

    /// <summary>True when every mount in this snapshot came from the game sheets, not the site.</summary>
    public bool IsSheetOnly => string.Equals(Enrichment, MountShelfHeader.SheetOnlyEnrichment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the snapshot was written by an older generator than this build reads: it parses,
    /// but a field added since may be missing, so its absence is a fact to report, not a surprise.
    /// </summary>
    public bool IsStale => SchemaVersion < CurrentSchemaVersion;

    /// <summary>
    /// True when the snapshot was written by a newer generator than this build reads. The rows are
    /// still read (a wrong "How to obtain" list is worse than none), but the mismatch is an error
    /// in the log: the plugin and the data it ships with were built from different versions.
    /// </summary>
    public bool IsNewer => SchemaVersion > CurrentSchemaVersion;

    /// <summary>True when the snapshot names the volunteer project it came from and links to it.</summary>
    public bool HasAttribution =>
        !string.IsNullOrWhiteSpace(Header.Attribution) && !string.IsNullOrWhiteSpace(Header.AttributionUrl);

    /// <summary>
    /// One line describing the data for the settings window and the diagnostics block, e.g.
    /// <c>ffxivcollect.com, 2026-11-01, schema v2</c>, with the two honest exceptions called out:
    /// a snapshot built offline, and one written by another generator version.
    /// </summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(string.IsNullOrWhiteSpace(Source) ? "unknown source" : Source);

        if (!string.IsNullOrWhiteSpace(Generated))
        {
            text.Append(", ").Append(Generated);
        }

        text.Append(", schema v").Append(SchemaVersion.ToString(CultureInfo.InvariantCulture));

        if (IsSheetOnly)
        {
            text.Append(" (built offline: no obtain data refreshed)");
        }

        if (IsStale)
        {
            text.Append(" (older than this build reads: v")
                .Append(CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture))
                .Append(')');
        }
        else if (IsNewer)
        {
            text.Append(" (newer than this build reads: v")
                .Append(CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture))
                .Append(')');
        }

        return text.ToString();
    }

    /// <summary>
    /// One line for the diagnostics block about the build-time sheet check of the two facts the snapshot
    /// does not carry: <c>356/356 seats, 356/356 movement (Mount.ExtraSeats, Mount.IsAirborne)</c>, or the
    /// honest alternative when no check was recorded.
    /// </summary>
    public string DescribeCrossCheck()
    {
        return CrossCheck?.Describe()
            ?? "not recorded: seats and movement were not checked against the Mount sheet";
    }

    /// <summary>
    /// Every mount id the dataset carries, in no particular order. Exposed so a caller that has to
    /// enumerate the shelf (the patch-day validator) does not keep a second copy of the ids it parsed.
    /// </summary>
    public IReadOnlyCollection<uint> Ids => entries.Keys;

    /// <summary>
    /// Parses the shipped dataset. Property names are matched case-insensitively; a missing
    /// <c>sources</c> array is an empty list, a null <c>relatedType</c> becomes
    /// <see cref="string.Empty"/>, and a null or absent <c>relatedId</c> becomes zero.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="FormatException">The document is not a JSON object, or <c>mounts</c> is present but not an array.</exception>
    public static MountShelf Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The mount source data must be a JSON object.");
        }

        var entries = new Dictionary<uint, MountSources>();
        MountShelfHeader header = ReadHeader(document.RootElement);
        if (!TryGetProperty(document.RootElement, "mounts", out JsonElement mounts))
        {
            return new MountShelf(entries, header);
        }

        if (mounts.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The mount source data's \"mounts\" member must be an array.");
        }

        foreach (JsonElement mount in mounts.EnumerateArray())
        {
            if (mount.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            uint id = ReadUInt32(mount, "id");
            if (id == 0)
            {
                continue;
            }

            entries[id] = new MountSources(
                ReadString(mount, "patch"),
                ReadUInt32(mount, "itemId"),
                ReadSources(mount),
                ReadCommunity(mount),
                // Folded here, once per row: the field is the class the generator wrote, and a word this build
                // does not know reads as Unknown rather than being guessed at (the validator is what refuses it).
                MountAvailabilityRules.Parse(ReadString(mount, "availability")));
        }

        return new MountShelf(entries, header);
    }

    /// <summary>
    /// Reads the document header. Every member is optional: an unknown or missing
    /// <c>schemaVersion</c> becomes zero (the shape that shipped without one), and the strings fall
    /// back to empty rather than null so no caller has to null-check a credit line.
    /// </summary>
    private static MountShelfHeader ReadHeader(JsonElement root)
    {
        int schemaVersion = ReadInt32(root, "schemaVersion");

        if (schemaVersion < 0)
        {
            schemaVersion = MountShelf.LegacySchemaVersion;
        }

        return new MountShelfHeader(
            schemaVersion,
            ReadString(root, "generated"),
            ReadString(root, "source"),
            ReadString(root, "enrichment"),
            ReadString(root, "attribution"),
            ReadString(root, "attributionUrl"),
            ReadCrossCheck(root),
            ReadAvailabilityBlock(root));
    }

    /// <summary>
    /// Reads the <c>availability</c> block: how many mounts the generator put in each class. A member naming
    /// no class is skipped rather than turned into <see cref="MountAvailability.Unknown"/>, because the block
    /// is a count of classified rows and an unknown key is evidence of a generator this build does not know,
    /// which the validator then reports as a count mismatch instead of silently agreeing.
    /// </summary>
    private static IReadOnlyList<MountAvailabilityCount> ReadAvailabilityBlock(JsonElement root)
    {
        if (!TryGetProperty(root, "availability", out JsonElement block) || block.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var counts = new List<MountAvailabilityCount>();

        foreach (MountAvailability availability in MountAvailabilityRules.Classes)
        {
            string key = MountAvailabilityRules.Key(availability);

            if (!TryGetProperty(block, key, out JsonElement value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out int mounts)
                || mounts < 0)
            {
                continue;
            }

            counts.Add(new MountAvailabilityCount(availability, mounts));
        }

        return counts;
    }

    /// <summary>
    /// Reads the <c>sheetCrossCheck</c> block: which sheet columns the build compared the site's seats and
    /// movement against, and how many rows agreed. A missing block, a null one, or one with no usable
    /// counts reads as null - "no check ran" - never as a pass, because the counts are the evidence and a
    /// module that guessed them would be worse than no evidence at all.
    /// </summary>
    private static MountShelfCrossCheck? ReadCrossCheck(JsonElement root)
    {
        if (!TryGetProperty(root, "sheetCrossCheck", out JsonElement cross) || cross.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        int rows = ReadInt32(cross, "mounts");
        if (rows <= 0)
        {
            return null;
        }

        var fields = new List<string>();
        if (TryGetProperty(cross, "fields", out JsonElement fieldValues) && fieldValues.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement field in fieldValues.EnumerateArray())
            {
                if (field.ValueKind == JsonValueKind.String && field.GetString() is { Length: > 0 } name)
                {
                    fields.Add(name);
                }
            }
        }

        return new MountShelfCrossCheck(
            ReadString(cross, "sheet"),
            fields,
            rows,
            ReadInt32(cross, "seats"),
            ReadInt32(cross, "movement"));
    }

    private static int ReadInt32(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number)
            ? number
            : 0;
    }

    /// <summary>Looks one mount's obtain data up by id.</summary>
    public bool TryGet(uint id, out MountSources value)
    {
        if (entries.TryGetValue(id, out MountSources? found))
        {
            value = found;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>
    /// Reads the community facts beside one mount: ffxivcollect's <c>owned</c> percentage (a number
    /// such as <c>0.3</c>, written without the percent sign), and its <c>tradeable</c> /
    /// <c>customMusic</c> flags. A missing or non-numeric member stays null rather than becoming
    /// zero or false, and a mount with none of the three is null instead of an empty object, so the
    /// view can leave the line out entirely.
    /// </summary>
    private static MountCommunity? ReadCommunity(JsonElement mount)
    {
        double? owned =
            TryGetProperty(mount, "owned", out JsonElement ownedValue)
            && ownedValue.ValueKind == JsonValueKind.Number
            && ownedValue.TryGetDouble(out double percent)
                ? percent
                : null;

        bool? tradeable = ReadBool(mount, "tradeable");
        bool? customMusic = ReadBool(mount, "customMusic");

        return owned is null && tradeable is null && customMusic is null
            ? null
            : new MountCommunity(owned, tradeable, customMusic);
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static List<MountSourceLine> ReadSources(JsonElement mount)
    {
        var lines = new List<MountSourceLine>();
        if (!TryGetProperty(mount, "sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }

        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string detail = ReadString(source, "text");

            // Whitespace-only text would draw a blank "How to obtain" row. The generator drops those,
            // so this is the reader agreeing with the writer rather than a second opinion.
            if (string.IsNullOrWhiteSpace(detail))
            {
                continue;
            }

            lines.Add(new MountSourceLine(
                // Folded here, once per line: the filter and the grouping read the type, and folding it per
                // frame would allocate a key for every line on every frame.
                MountSourceKinds.Parse(ReadString(source, "type")),
                detail,
                ReadString(source, "relatedType"),
                ReadUInt32(source, "relatedId")));
        }

        return lines;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
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

    private static string ReadString(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static uint ReadUInt32(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetUInt32(out uint number)
            ? number
            : 0;
    }
}
