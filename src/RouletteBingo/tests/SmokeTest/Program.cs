using System.Reflection;

// Loads the *built plugin assembly* and exercises the core through reflection.
// This catches packaging mistakes that the unit tests cannot see, mainly a missing
// or misnamed embedded resource.

var dllPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Release", "RouletteBingo.dll");

dllPath = Path.GetFullPath(dllPath);
if (!File.Exists(dllPath))
{
    Console.Error.WriteLine($"Plugin assembly not found: {dllPath}");
    return 1;
}

Console.WriteLine($"loading {dllPath}");
var assembly = Assembly.LoadFrom(dllPath);
Console.WriteLine($"  {assembly.FullName}");

var poolType = assembly.GetType("RouletteBingo.Core.SuggestionPool", throwOnError: true)!;
var pool = poolType.GetMethod("LoadEmbedded", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
var all = (System.Collections.IEnumerable)poolType.GetProperty("All")!.GetValue(pool)!;
var squares = all.Cast<object>().ToList();
Console.WriteLine($"  embedded pool: {squares.Count} squares");

if (squares.Count < 100)
{
    Console.Error.WriteLine("  FAIL: embedded pool looks wrong");
    return 1;
}

var first = squares[0];
var idProp = first.GetType().GetProperty("Id")!;
var textProp = first.GetType().GetProperty("Text")!;
Console.WriteLine($"  sample: {idProp.GetValue(first)} - {textProp.GetValue(first)}");

// Generate a card and print its report, end to end.
var generator = assembly.GetType("RouletteBingo.Core.CardGenerator", throwOnError: true)!;
var generate = generator.GetMethod("Generate", BindingFlags.Public | BindingFlags.Static)!;
var kindType = assembly.GetType("RouletteBingo.Core.DutyKind", throwOnError: true)!;
var dungeon = Enum.Parse(kindType, "Dungeon");
var generated = generate.Invoke(null, new object?[] { pool, 0xDEADBEEFUL, dungeon, false })!;

var cardType = assembly.GetType("RouletteBingo.Core.BingoCard", throwOnError: true)!;
var cells = (Array)generated.GetType().GetProperty("Cells")!.GetValue(generated)!;
var card = Activator.CreateInstance(cardType, 0xDEADBEEFUL, dungeon, "The Tam-Tara Deepcroft", cells, DateTime.UtcNow.AddMinutes(-7))!;

var toggle = cardType.GetMethod("Toggle")!;
foreach (var index in new[] { 0, 1, 2, 3, 4 })
    toggle.Invoke(card, new object?[] { index, DateTime.UtcNow });

var report = (string)cardType.GetMethod("ToReport")!.Invoke(card, new object?[] { null })!;
Console.WriteLine();
Console.WriteLine(report);

var ok = report.Contains("ROULETTE BINGO") && report.Contains("Card code: RB1.") && report.Contains("Row 1");
Console.WriteLine();
Console.WriteLine(ok ? "SMOKE TEST PASSED" : "SMOKE TEST FAILED");
return ok ? 0 : 1;
