using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RouletteBingo.Core;

/// <summary>
/// The full square pool, loaded from Data/squares.json.
/// </summary>
public sealed class SuggestionPool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private SuggestionPool(IReadOnlyList<Suggestion> all)
    {
        this.All = all;
    }

    public IReadOnlyList<Suggestion> All { get; }

    public static SuggestionPool FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<PoolFile>(json, JsonOptions)
                   ?? throw new InvalidDataException("squares.json was empty.");

        if (file.Squares is null || file.Squares.Count == 0)
            throw new InvalidDataException("squares.json contains no squares.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var square in file.Squares)
        {
            if (string.IsNullOrWhiteSpace(square.Id))
                throw new InvalidDataException("A square is missing its id.");

            if (!seen.Add(square.Id))
                throw new InvalidDataException($"Duplicate square id '{square.Id}'.");

            if (string.IsNullOrWhiteSpace(square.Text))
                throw new InvalidDataException($"Square '{square.Id}' has no text.");
        }

        return new SuggestionPool(file.Squares);
    }

    public static SuggestionPool FromFile(string path) => FromJson(File.ReadAllText(path));

    public static SuggestionPool LoadEmbedded()
    {
        var assembly = typeof(SuggestionPool).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("squares.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Embedded squares.json was not found in the plugin assembly.");

        using var stream = assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidDataException($"Could not open embedded resource '{name}'.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    private sealed class PoolFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("squares")]
        public List<Suggestion>? Squares { get; set; }
    }
}
