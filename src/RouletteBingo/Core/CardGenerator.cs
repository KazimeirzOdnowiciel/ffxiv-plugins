using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RouletteBingo.Core;

public sealed class GeneratedCard
{
    public GeneratedCard(ulong seed, DutyKind kind, Suggestion[] cells)
    {
        this.Seed = seed;
        this.Kind = kind;
        this.Cells = cells;
    }

    public ulong Seed { get; }

    public DutyKind Kind { get; }

    /// <summary>Exactly 25 entries, index 12 is the free space.</summary>
    public Suggestion[] Cells { get; }
}

public static class CardGenerator
{
    public const int GridSize = 5;
    public const int CellCount = GridSize * GridSize;
    public const int CenterIndex = 12;

    private static readonly string[] FreeSpaces =
    {
        "The duty popped at all",
        "You are, in fact, inside a duty",
        "The queue timer lied to you",
        "Someone is here against their will",
        "This is a duty. Congratulations.",
        "You could be doing literally anything else",
    };

    /// <summary>
    /// Stable seed for a given duty + day + run number. Two people entering the same duty on the
    /// same day with the same run index get the same card, which is the whole point of card codes.
    /// </summary>
    public static ulong SeedFor(string dutyName, uint territoryId, DateTime utcNow, int runIndex)
    {
        var hash = 14695981039346656037UL;

        void Add(ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        foreach (var b in Encoding.UTF8.GetBytes(dutyName ?? string.Empty))
            Add(b);

        Add(territoryId);
        Add((ulong)(utcNow.Date.Ticks / TimeSpan.TicksPerDay));
        Add((ulong)runIndex);

        return hash == 0 ? 1UL : hash;
    }

    public static GeneratedCard Generate(SuggestionPool pool, ulong seed, DutyKind kind, bool cursedMode = false)
    {
        var rng = new SplitMix64(seed);
        var chosen = new List<Suggestion>(CellCount - 1);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (category, count) in GetQuotas(kind, cursedMode))
        {
            var candidates = pool.All
                .Where(s => s.Category == category && s.FitsDuty(kind) && !used.Contains(s.Id))
                .ToList();

            for (var i = 0; i < count && candidates.Count > 0; i++)
            {
                var pick = WeightedPick(candidates, ref rng);
                candidates.Remove(pick);
                used.Add(pick.Id);
                chosen.Add(pick);
            }
        }

        // Fill any shortfall with whatever is left that fits the duty.
        if (chosen.Count < CellCount - 1)
        {
            var spare = pool.All.Where(s => !used.Contains(s.Id) && s.FitsDuty(kind)).ToList();
            rng.Shuffle(spare);
            foreach (var square in spare)
            {
                if (chosen.Count >= CellCount - 1)
                    break;

                used.Add(square.Id);
                chosen.Add(square);
            }
        }

        // Last resort for tiny pools: ignore duty filtering entirely.
        if (chosen.Count < CellCount - 1)
        {
            var spare = pool.All.Where(s => !used.Contains(s.Id)).ToList();
            rng.Shuffle(spare);
            foreach (var square in spare)
            {
                if (chosen.Count >= CellCount - 1)
                    break;

                used.Add(square.Id);
                chosen.Add(square);
            }
        }

        if (chosen.Count != CellCount - 1)
            throw new InvalidOperationException($"The square pool is too small: needed {CellCount - 1} squares, got {chosen.Count}.");

        rng.Shuffle(chosen);

        var cells = new Suggestion[CellCount];
        var free = new Suggestion
        {
            Id = "free",
            Text = FreeSpaces[rng.NextInt(FreeSpaces.Length)],
            Category = BingoCategory.Free,
            Rarity = BingoRarity.Common,
        };

        var cursor = 0;
        for (var i = 0; i < CellCount; i++)
            cells[i] = i == CenterIndex ? free : chosen[cursor++];

        return new GeneratedCard(seed, kind, cells);
    }

    /// <summary>
    /// Ordered on purpose: the order of categories is part of the card's determinism, and card
    /// codes (and the web preview) replay the exact same board from a seed.
    /// </summary>
    private static (BingoCategory Category, int Count)[] GetQuotas(DutyKind kind, bool cursedMode)
    {
        if (cursedMode)
        {
            return new[]
            {
                (BingoCategory.Cursed, 12),
                (BingoCategory.Damage, 4),
                (BingoCategory.Social, 4),
                (BingoCategory.Tank, 2),
                (BingoCategory.Healer, 2),
            };
        }

        return kind switch
        {
            DutyKind.Alliance => new[]
            {
                (BingoCategory.Alliance, 5),
                (BingoCategory.Tank, 4),
                (BingoCategory.Healer, 4),
                (BingoCategory.Damage, 4),
                (BingoCategory.Social, 5),
                (BingoCategory.Cursed, 2),
            },
            DutyKind.Pvp => new[]
            {
                (BingoCategory.Pvp, 6),
                (BingoCategory.Social, 6),
                (BingoCategory.Damage, 5),
                (BingoCategory.Healer, 3),
                (BingoCategory.Tank, 2),
                (BingoCategory.Cursed, 2),
            },
            _ => new[]
            {
                (BingoCategory.Tank, 4),
                (BingoCategory.Healer, 4),
                (BingoCategory.Damage, 5),
                (BingoCategory.Social, 6),
                (BingoCategory.Loot, 2),
                (BingoCategory.Sprout, 2),
                (BingoCategory.Cursed, 1),
            },
        };
    }

    private static double Weight(BingoRarity rarity) => rarity switch
    {
        BingoRarity.Common => 10.0,
        BingoRarity.Uncommon => 6.0,
        BingoRarity.Rare => 3.0,
        _ => 1.5,
    };

    private static Suggestion WeightedPick(List<Suggestion> candidates, ref SplitMix64 rng)
    {
        var total = 0.0;
        foreach (var candidate in candidates)
            total += Weight(candidate.Rarity);

        var roll = rng.NextDouble() * total;
        foreach (var candidate in candidates)
        {
            roll -= Weight(candidate.Rarity);
            if (roll <= 0.0)
                return candidate;
        }

        return candidates[^1];
    }
}
