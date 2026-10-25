using System.Reflection;

// Loads the *built* plugin assembly and runs the optimizer through reflection, so packaging
// mistakes (missing types, broken references) show up outside the game.

var dllPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Release", "SlotYield.dll");

dllPath = Path.GetFullPath(dllPath);
if (!File.Exists(dllPath))
{
    Console.Error.WriteLine($"plugin assembly not found: {dllPath}");
    return 1;
}

Console.WriteLine($"loading {dllPath}");
var assembly = Assembly.LoadFrom(dllPath);
Console.WriteLine($"  {assembly.FullName}");

// The plugin references Dalamud; resolve it from the local install so reflection can see types.
var hooks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "addon", "Hooks");
var dalamud = Directory.Exists(hooks)
    ? Directory.GetFiles(hooks, "Dalamud.dll", SearchOption.AllDirectories).FirstOrDefault()
    : null;

if (dalamud is not null)
{
    var dalamudDir = Path.GetDirectoryName(dalamud)!;
    AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
    {
        var candidate = Path.Combine(dalamudDir, new AssemblyName(e.Name).Name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    };
    Console.WriteLine($"  resolving Dalamud from {dalamudDir}");
}

var candidateType = assembly.GetType("SlotYield.Core.PortfolioCandidate", throwOnError: true)!;
var optimizerType = assembly.GetType("SlotYield.Core.PortfolioOptimizer", throwOnError: true)!;
var qualityType = assembly.GetType("SlotYield.Core.Quality", throwOnError: true)!;
var nq = Enum.Parse(qualityType, "Nq");

object MakeCandidate(uint id, decimal capital, decimal perDay) =>
    Activator.CreateInstance(candidateType, id, nq, capital, perDay)!;

var listType = typeof(List<>).MakeGenericType(candidateType);
var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
list.Add(MakeCandidate(1, 1_000_000m, 10_000m));
list.Add(MakeCandidate(2, 500_000m, 8_000m));
list.Add(MakeCandidate(3, 2_000_000m, 30_000m));
list.Add(MakeCandidate(4, 100_000m, 1_000m));

var optimize = optimizerType.GetMethod("Optimize", BindingFlags.Public | BindingFlags.Static)!;
var plan = optimize.Invoke(null, new object?[] { list, 2, 3_000_000m })!;
var planType = plan.GetType();
var gilPerDay = (decimal)planType.GetProperty("GilPerDay")!.GetValue(plan)!;
var capital = (decimal)planType.GetProperty("Capital")!.GetValue(plan)!;
var picks = (System.Collections.IEnumerable)planType.GetProperty("Picks")!.GetValue(plan)!;
var pickCount = picks.Cast<object>().Count();

Console.WriteLine($"  plan: {pickCount} slots, {gilPerDay:N0} gil/day, {capital:N0} capital");
var ok = pickCount == 2 && gilPerDay == 40_000m && capital <= 3_000_000m;

if (!ok)
    Console.Error.WriteLine("  FAIL: expected 2 slots, 40,000 gil/day, within budget");

// The plugin type itself must exist and implement IDalamudPlugin.
Console.WriteLine("  types: " + string.Join(", ", assembly.GetTypes().Where(t => t.Name.Contains("Plugin") || t.Name == "MainWindow").Select(t => t.FullName)));
var pluginType = assembly.GetType("SlotYield.Plugin", throwOnError: false);
var isPlugin = pluginType is not null && pluginType.GetInterfaces().Any(i => i.Name == "IDalamudPlugin");
Console.WriteLine($"  plugin entry point: {(pluginType?.FullName ?? "missing")} (IDalamudPlugin: {isPlugin})");
ok &= isPlugin;

Console.WriteLine();
Console.WriteLine(ok ? "SMOKE TEST PASSED" : "SMOKE TEST FAILED");
return ok ? 0 : 1;
