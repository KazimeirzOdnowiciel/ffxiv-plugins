using System.Text;
using System.Text.Json;
using RouletteBingo.Core;

// Fixture mode: dump cards for the browser preview to reproduce byte-for-byte.
if (args.Length >= 2 && args[0] == "--fixtures")
{
    var fixturePool = SuggestionPool.FromFile(Path.Combine(AppContext.BaseDirectory, "squares.json"));
    var cases = new List<object>();

    foreach (var seed in new ulong[] { 1UL, 42UL, 0xDEADBEEFUL, 123456789UL, 987654321UL })
    {
        foreach (var kind in new[] { DutyKind.Dungeon, DutyKind.EightMan, DutyKind.Alliance, DutyKind.Pvp })
        {
            var generated = CardGenerator.Generate(fixturePool, seed, kind);
            cases.Add(new
            {
                seed = seed.ToString(),
                kind = kind.ToString(),
                cursed = false,
                ids = generated.Cells.Select(c => c.Id).ToArray(),
                free = generated.Cells[12].Text,
                code = ShareCode.Encode(seed, 0x1A5C3u, kind),
            });
        }
    }

    var chaosTest = CardGenerator.Generate(fixturePool, 424242UL, DutyKind.Dungeon, cursedMode: true);
    cases.Add(new
    {
        seed = "424242",
        kind = "Dungeon",
        cursed = true,
        ids = chaosTest.Cells.Select(c => c.Id).ToArray(),
        free = chaosTest.Cells[12].Text,
        code = ShareCode.Encode(424242UL, 0x1A5C3u, DutyKind.Dungeon),
    });

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllText(args[1], JsonSerializer.Serialize(new { cards = cases }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {cases.Count} fixtures to {args[1]}");
    return 0;
}

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void Section(string name) => Console.WriteLine($"\n== {name}");

// ---------------------------------------------------------------- pool ----
Section("pool");
var jsonPath = Path.Combine(AppContext.BaseDirectory, "squares.json");
var pool = SuggestionPool.FromFile(jsonPath);
var all = pool.All;

Console.WriteLine($"squares: {all.Count}");

Check(all.Count >= 140, $"expected at least 140 squares, got {all.Count}");
Check(all.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count, "duplicate square ids");
Check(all.Select(s => s.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count, "duplicate square text");
Check(all.All(s => s.Text.Length <= 78), "a square text is longer than 78 chars: " + all.OrderByDescending(s => s.Text.Length).First().Text);
Check(all.All(s => s.Text.Trim() == s.Text && s.Text.Length > 0), "a square text has stray whitespace");

var nonAscii = all.FirstOrDefault(s => s.Text.Any(c => c >= 128));
Check(nonAscii is null, $"non-ascii character in square text: {nonAscii?.Text}");

foreach (var category in Enum.GetValues<BingoCategory>().Where(c => c != BingoCategory.Free))
    Console.WriteLine($"  {category,-9} {all.Count(s => s.Category == category),3}");

foreach (var rarity in Enum.GetValues<BingoRarity>())
    Console.WriteLine($"  {rarity,-9} {all.Count(s => s.Rarity == rarity),3}");

var validTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dungeon", "eightman", "alliance", "pvp" };
foreach (var square in all.Where(s => s.Tags.Any(t => !validTags.Contains(t))))
    failures.Add($"square '{square.Id}' has an unknown tag: {string.Join(",", square.Tags)}");

var knownDetectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "chat.owave", "chat.gg", "chat.first", "chat.where", "chat.sorry", "chat.ty", "chat.lag",
    "chat.question", "death.tank", "death.healer", "death.drg", "death.self", "vuln.3",
    "duty.wipe", "silence",
};

foreach (var square in all.Where(s => s.Auto is not null && !knownDetectors.Contains(s.Auto!)))
    failures.Add($"square '{square.Id}' uses unknown detector '{square.Auto}'");

// Every category must be deep enough to fill its largest quota.
foreach (var kind in Enum.GetValues<DutyKind>())
{
    foreach (var category in Enum.GetValues<BingoCategory>())
    {
        var available = all.Count(s => s.Category == category && s.FitsDuty(kind));
        if (category is BingoCategory.Tank or BingoCategory.Healer && available < 12)
            failures.Add($"{category} has only {available} squares that fit {kind}");
    }
}

// ---------------------------------------------------------- generation ----
Section("generation");

const int cards = 4000;
var idsBySeed = new Dictionary<(ulong Seed, DutyKind Kind), string>();
var appearances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var cursedCounts = new List<int>();
var centerTexts = new HashSet<string>();

foreach (var kind in new[] { DutyKind.Dungeon, DutyKind.EightMan, DutyKind.Alliance, DutyKind.Pvp })
{
    for (var i = 0; i < cards / 4; i++)
    {
        var seed = CardGenerator.SeedFor("The Tam-Tara Deepcroft", 1036, new DateTime(2026, 10, 22), i);
        var generated = CardGenerator.Generate(pool, seed, kind);

        Check(generated.Cells.Length == 25, "card does not have 25 cells");
        Check(generated.Cells[12].Category == BingoCategory.Free, "centre is not the free space");
        centerTexts.Add(generated.Cells[12].Text);

        var nonFree = generated.Cells.Where((_, idx) => idx != 12).ToList();
        Check(nonFree.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 24, "duplicate square on card");
        Check(nonFree.All(s => s.FitsDuty(kind)), $"a square does not fit duty {kind}");

        cursedCounts.Add(nonFree.Count(s => s.Category == BingoCategory.Cursed));
        foreach (var square in nonFree)
            appearances[square.Id] = appearances.GetValueOrDefault(square.Id) + 1;

        if (i == 0)
        {
            var signature = string.Join(",", generated.Cells.Select(c => c.Id));
            var key = (seed, kind);
            if (idsBySeed.TryGetValue(key, out var previous))
                Check(previous == signature, $"generation is not deterministic for the same seed/kind ({kind})");
            else
                idsBySeed[key] = signature;
        }
    }
}

Console.WriteLine($"  cards generated: 4000 across 4 duty kinds");
Console.WriteLine($"  distinct squares used: {appearances.Count}/{all.Count}");
Console.WriteLine($"  cursed squares per card: min {cursedCounts.Min()}, max {cursedCounts.Max()}");
Check(cursedCounts.Max() <= 3, "too many cursed squares on a normal card");
Check(appearances.Count >= all.Count * 0.85, $"only {appearances.Count} of {all.Count} squares ever appeared - generation is too narrow");

var never = all.Where(s => s.Category != BingoCategory.Free && !appearances.ContainsKey(s.Id)).Select(s => s.Id).ToList();
if (never.Count > 0)
    Console.WriteLine($"  never drawn: {string.Join(", ", never)}");

// Determinism of the whole pipeline across two separate loads.
var generatedA = CardGenerator.Generate(SuggestionPool.FromFile(jsonPath), 0xDEADBEEF, DutyKind.Dungeon);
var generatedB = CardGenerator.Generate(pool, 0xDEADBEEF, DutyKind.Dungeon);
Check(generatedA.Cells.Select(c => c.Id).SequenceEqual(generatedB.Cells.Select(c => c.Id)), "pipeline is not deterministic");

// Chaos mode.
var chaos = CardGenerator.Generate(pool, 42, DutyKind.Dungeon, cursedMode: true);
Check(chaos.Cells.Count(c => c.Category == BingoCategory.Cursed) >= 8, "cursed mode does not produce enough cursed squares");

// ---------------------------------------------------------------- card ----
Section("card");

var seedForCard = 1234567UL;
var generatedCard = CardGenerator.Generate(pool, seedForCard, DutyKind.Dungeon);
var card = new BingoCard(seedForCard, DutyKind.Dungeon, "The Tam-Tara Deepcroft", generatedCard.Cells, DateTime.UtcNow.AddMinutes(-6));
var now = DateTime.UtcNow;

Check(card.MarkedCount == 1, "free space should start marked");
Check(!card.HasBingo, "a fresh card should not have a bingo");

// Complete row 0 (indices 0..4), skipping the free centre.
for (var i = 0; i < 5; i++)
    card.Toggle(i, now);

Check(card.HasBingo, "marking a full row should produce a bingo");
Check(card.CompletedLines.Count == 1 && card.CompletedLines[0].Label == "Row 1", "expected exactly Row 1");
Check(card.FirstBingoUtc is not null, "first bingo time was not recorded");

// Unmark and re-mark: line disappears.
card.Toggle(2, now);
Check(!card.HasBingo, "unmarking a square should remove the bingo");
card.Toggle(2, now);
Check(card.HasBingo, "re-marking should restore the bingo");

// Column 0 needs 0,5,10,15,20.
foreach (var i in new[] { 5, 10, 15, 20 })
    card.Toggle(i, now);
Check(card.CompletedLines.Any(l => l.Label == "Column 1"), "column bingo not detected");

// Mask round-trip.
var mask = card.MarksMask;
var clone = new BingoCard(seedForCard, DutyKind.Dungeon, "The Tam-Tara Deepcroft", generatedCard.Cells, DateTime.UtcNow.AddMinutes(-6));
clone.ApplyMarks(mask);
Check(clone.MarkedCount == card.MarkedCount, "mask round-trip lost marks");
Check(clone.HasBingo, "mask round-trip lost the bingo");

// Auto-marking only touches squares with that detector.
var autoSeed = 999UL;
var autoCardGen = CardGenerator.Generate(pool, autoSeed, DutyKind.Dungeon);
var autoCard = new BingoCard(autoSeed, DutyKind.Dungeon, "Test", autoCardGen.Cells, DateTime.UtcNow);
var autoSquares = autoCardGen.Cells.Select((c, i) => (c, i)).Where(t => t.c.Auto == "duty.wipe").Select(t => t.i).ToList();
var hit = autoCard.AutoMark("duty.wipe", now);
Check(hit.SequenceEqual(autoSquares), "auto-mark hit the wrong squares");
Check(hit.All(i => autoCard.IsMarked(i) && autoCard.IsAuto(i)), "auto-mark did not flag the squares as automatic");
Check(autoCard.AutoMark("duty.wipe", now).Count == 0, "auto-mark fired twice for the same detector");

var report = card.ToReport(now);
Check(report.Contains("ROULETTE BINGO"), "report missing header");
Check(report.Contains("Card code: RB1."), "report missing card code");
Check(report.Split('\n').Length > 30, "report looks too short");
Console.WriteLine("\n--- sample report ---");
Console.WriteLine(report);

// ----------------------------------------------------------- share code ----
Section("share code");

var rng = new SplitMix64(7);
for (var i = 0; i < 500; i++)
{
    var seed = rng.Next();
    var marks = (uint)(rng.Next() & 0x1FFFFFF);
    var kind = (DutyKind)(rng.Next() % 5);
    var code = ShareCode.Encode(seed, marks, kind);
    Check(code.StartsWith("RB1."), "bad prefix");

    if (!ShareCode.TryDecode(code, out var seed2, out var marks2, out var kind2))
    {
        failures.Add("could not decode its own share code");
        break;
    }

    if (seed2 != seed || marks2 != marks || kind2 != kind)
    {
        failures.Add($"share code round-trip mismatch for {code}");
        break;
    }
}

Check(!ShareCode.TryDecode("garbage", out _, out _, out _), "decoded garbage");
Check(!ShareCode.TryDecode("RB1.!!!!!!!!", out _, out _, out _), "decoded an invalid payload");
Check(!ShareCode.TryDecode(string.Empty, out _, out _, out _), "decoded an empty string");
Check(ShareCode.TryDecode("  rb1." + ShareCode.Encode(5, 5, DutyKind.Pvp)[4..] + "  ", out _, out var m2, out var k2) && m2 == 5 && k2 == DutyKind.Pvp, "whitespace/case handling failed");

// ---------------------------------------------------------------- done ----
Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("ALL CHECKS PASSED");
    return 0;
}

Console.WriteLine($"{failures.Count} FAILURE(S):");
foreach (var failure in failures.Distinct())
    Console.WriteLine($"  - {failure}");
return 1;
