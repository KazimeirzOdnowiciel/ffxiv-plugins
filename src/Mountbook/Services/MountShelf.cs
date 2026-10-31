using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mountbook.Services;

/// <summary>
/// The embedded obtain dataset, indexed by mount id. Parsing lives here rather than in the
/// wiring layer so it can be tested without the game; the JSON shape is the one emitted by
/// <c>scripts/generate-mount-sources.ps1</c>.
/// </summary>
public sealed class MountShelf
{
    private readonly Dictionary<uint, MountSources> entries;

    private MountShelf(Dictionary<uint, MountSources> entries)
    {
        this.entries = entries;
    }

    /// <summary>How many mounts the dataset carries.</summary>
    public int Count => entries.Count;

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
        if (!TryGetProperty(document.RootElement, "mounts", out JsonElement mounts))
        {
            return new MountShelf(entries);
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
                ReadCommunity(mount));
        }

        return new MountShelf(entries);
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
                ReadString(source, "type"),
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
