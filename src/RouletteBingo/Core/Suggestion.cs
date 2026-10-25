using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace RouletteBingo.Core;

public enum BingoCategory
{
    Free,
    Tank,
    Healer,
    Damage,
    Social,
    Loot,
    Sprout,
    Cursed,
    Pvp,
    Alliance,
}

public enum BingoRarity
{
    Common,
    Uncommon,
    Rare,
    Cursed,
}

public enum DutyKind
{
    Other,
    Dungeon,
    EightMan,
    Alliance,
    Pvp,
}

/// <summary>
/// One square of the card. The pool lives in Data/squares.json and is embedded into the assembly.
/// </summary>
public sealed class Suggestion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("cat")]
    public BingoCategory Category { get; set; }

    [JsonPropertyName("rarity")]
    public BingoRarity Rarity { get; set; }

    /// <summary>Empty means "fits any duty".</summary>
    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>Optional auto-detector id, see Detection/Detectors.cs.</summary>
    [JsonPropertyName("auto")]
    public string? Auto { get; set; }

    public bool FitsDuty(DutyKind kind)
    {
        if (this.Tags.Length == 0)
            return true;

        var tag = TagFor(kind);
        return tag.Length != 0 && this.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
    }

    public static string TagFor(DutyKind kind) => kind switch
    {
        DutyKind.Dungeon => "dungeon",
        DutyKind.EightMan => "eightman",
        DutyKind.Alliance => "alliance",
        DutyKind.Pvp => "pvp",
        _ => string.Empty,
    };

    public static string CategoryName(BingoCategory category) => category switch
    {
        BingoCategory.Free => "Free",
        BingoCategory.Tank => "Tank",
        BingoCategory.Healer => "Healer",
        BingoCategory.Damage => "Damage",
        BingoCategory.Social => "Party",
        BingoCategory.Loot => "Loot",
        BingoCategory.Sprout => "Sprout",
        BingoCategory.Cursed => "Cursed",
        BingoCategory.Pvp => "PvP",
        BingoCategory.Alliance => "Alliance",
        _ => "Other",
    };
}
