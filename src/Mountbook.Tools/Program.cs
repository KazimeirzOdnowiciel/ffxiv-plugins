using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mountbook.Services;

namespace Mountbook.Tools;

/// <summary>
/// The data-side helper for Mountbook. One verb today - <c>validate</c> - so a regenerated snapshot
/// can be checked by the same code the plugin and its tests use, from a shell script with no game and
/// no Dalamud. <c>tools/patch-day.ps1</c> is the caller; run this by hand only to reproduce a run.
/// </summary>
internal static class Program
{
    private const string Usage = """
        mountbook-tools - Mountbook data helpers (no Dalamud, no game, no network).

          mountbook-tools validate --candidate <mount-sources.json>
                                   [--baseline <mount-sources.json>]
                                   [--sheet-ids <file>]
                                   [--report <file.md>]
                                   [--allowed-shrink <n>] [--allowed-growth <n>] [--minimum <n>]
                                   [--max-bytes <n>]

          mountbook-tools classify --sources <mount-sources.json>
                                   [--map] [--counts]

        --map         print one "id,class" line per row, in ascending id: what the generator writes into
                      each row's "availability" field.
        --counts      print one "class,count" line per class that has rows: what the generator writes into
                      the header's "availability" block.
        Exit codes: 0 the rows were classified, 1 a row carries obtain lines the rules cannot place (nothing
        is printed), 2 bad usage or an unreadable file.

        --candidate   the freshly generated snapshot; the only required input.
        --baseline    the snapshot as committed before this run. Without it the row band has no
                      reference, "changed patches" cannot be reported and a lost source is invisible.
        --sheet-ids   the game's Mount sheet ids: one per line, or the mdlrip model map CSV
                      (out/mount-model-map.csv) unchanged. Without it an id is never checked against
                      the game, and the report says so.
        --report      also write the findings as markdown, for the run's evidence trail.
        --minimum     the row floor (default 300); --allowed-shrink/--allowed-growth widen the band
                      around the baseline (defaults 8 and 40).
        --max-bytes   the size budget the snapshot must stay under (default 122880 = 120 KB): the file
                      is embedded in the assembly and downloaded by every player.

        Exit codes: 0 the snapshot is valid, 1 it must not ship, 2 bad usage or an unreadable file.
        """;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (!string.Equals(args[0], "validate", StringComparison.Ordinal))
        {
            if (string.Equals(args[0], "classify", StringComparison.Ordinal))
            {
                return Classify(args);
            }

            Console.Error.WriteLine($"Unknown verb \"{args[0]}\".");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        return Validate(args);
    }

    /// <summary>
    /// Writes the availability classification for a snapshot's rows, so the generator never has to re-implement
    /// the rules in PowerShell: it asks for the map and writes it into the file (<see cref="MountAvailabilityRules"/>
    /// is the one implementation, and the validator re-derives the same answer from the shipped file).
    /// </summary>
    /// <remarks>
    /// Output is CSV on stdout, nothing on stderr unless something is wrong: <c>--map</c> gives
    /// <c>id,class</c> per row in ascending id, <c>--counts</c> gives <c>class,count</c> per class that has rows.
    /// Exit 1 when a row carries obtain lines the rules cannot place - the generator then writes nothing, because
    /// a snapshot with an unclassified mount would draw "No data" on a mount somebody could have classified.
    /// </remarks>
    private static int Classify(string[] args)
    {
        string? sources = null;
        var wantMap = false;
        var wantCounts = false;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--sources":
                    if (index + 1 >= args.Length)
                    {
                        return Fail("--sources needs a path.");
                    }

                    sources = args[++index];
                    break;

                case "--map":
                    wantMap = true;
                    break;

                case "--counts":
                    wantCounts = true;
                    break;

                default:
                    return Fail($"Unknown option \"{args[index]}\".");
            }
        }

        if (sources is null)
        {
            return Fail("classify needs --sources <mount-sources.json>.");
        }

        if (!wantMap && !wantCounts)
        {
            return Fail("classify needs at least one of --map or --counts.");
        }

        string json;
        try
        {
            json = File.ReadAllText(sources);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"Could not read the snapshot: {exception.Message}");
        }

        MountShelf shelf;
        try
        {
            shelf = MountShelf.Parse(json);
        }
        catch (FormatException exception)
        {
            return Fail($"The snapshot is not readable as Mountbook data: {exception.Message}");
        }

        var ids = new List<uint>(shelf.Count);

        foreach (uint id in shelf.Ids)
        {
            ids.Add(id);
        }

        ids.Sort();

        var unclassified = new List<uint>();
        var counts = new Dictionary<MountAvailability, int>();

        foreach (MountAvailability availability in MountAvailabilityRules.Classes)
        {
            counts[availability] = 0;
        }

        var builder = new StringBuilder();

        foreach (uint id in ids)
        {
            shelf.TryGet(id, out MountSources row);
            MountAvailability availability = MountAvailabilityRules.Classify(id, row.Lines);

            if (availability == MountAvailability.Unknown && row.Lines.Count > 0)
            {
                unclassified.Add(id);
            }

            counts[availability] = counts.GetValueOrDefault(availability) + 1;

            if (wantMap)
            {
                builder.Append(id.ToString(CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(MountAvailabilityRules.Key(availability))
                    .Append('\n');
            }
        }

        if (unclassified.Count > 0)
        {
            // Nothing on stdout: a generator that wrote a partial map would produce a file that half-ships.
            var listed = new StringBuilder();

            for (var index = 0; index < unclassified.Count && index < 10; index++)
            {
                if (index > 0)
                {
                    listed.Append(", ");
                }

                listed.Append(unclassified[index].ToString(CultureInfo.InvariantCulture));
            }

            Console.Error.WriteLine(
                $"{unclassified.Count} mount(s) carry obtain lines the availability rules cannot place, so nothing was classified: "
                + $"{listed}{(unclassified.Count > 10 ? ", ..." : string.Empty)}. "
                + "Teach MountAvailabilityRules what those lines are (a playable type, a PvP currency, or a curated id with its reason).");
            return 1;
        }

        if (wantMap)
        {
            Console.Write(builder.ToString());
        }

        if (wantCounts)
        {
            foreach (MountAvailability availability in MountAvailabilityRules.Classes)
            {
                int mounts = counts.GetValueOrDefault(availability);

                if (mounts > 0)
                {
                    Console.Write(
                        $"{MountAvailabilityRules.Key(availability)},{mounts.ToString(CultureInfo.InvariantCulture)}\n");
                }
            }
        }

        return 0;
    }

    private static int Validate(string[] args)
    {
        string? candidate = null;
        string? baseline = null;
        string? sheetIds = null;
        string? reportPath = null;
        var options = new MountSourcesValidationOptions();

        for (var index = 1; index < args.Length; index++)
        {
            string flag = args[index];

            // Values are always in the next argument: a path with an "=" or a leading "-" belongs to
            // the caller, not to this parser.
            if (index + 1 >= args.Length)
            {
                return Fail($"\"{flag}\" needs a value.");
            }

            string value = args[++index];

            switch (flag)
            {
                case "--candidate":
                    candidate = value;
                    break;
                case "--baseline":
                    baseline = value;
                    break;
                case "--sheet-ids":
                    sheetIds = value;
                    break;
                case "--report":
                    reportPath = value;
                    break;
                case "--allowed-shrink":
                    if (!TryCount(value, out int shrink))
                    {
                        return Fail("--allowed-shrink needs a whole number.");
                    }

                    options = options with { AllowedShrink = shrink };
                    break;
                case "--allowed-growth":
                    if (!TryCount(value, out int growth))
                    {
                        return Fail("--allowed-growth needs a whole number.");
                    }

                    options = options with { AllowedGrowth = growth };
                    break;
                case "--minimum":
                    if (!TryCount(value, out int minimum))
                    {
                        return Fail("--minimum needs a whole number.");
                    }

                    options = options with { MinimumCount = minimum };
                    break;
                case "--max-bytes":
                    if (!TryCount(value, out int maxBytes))
                    {
                        return Fail("--max-bytes needs a whole number.");
                    }

                    options = options with { MaxBytes = maxBytes };
                    break;
                default:
                    return Fail($"Unknown option \"{flag}\".");
            }
        }

        if (candidate is null)
        {
            return Fail("--candidate is required.");
        }

        string candidateJson;
        try
        {
            candidateJson = File.ReadAllText(candidate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"Could not read the candidate snapshot: {exception.Message}");
        }

        string? baselineJson = null;
        if (baseline is not null)
        {
            try
            {
                baselineJson = File.ReadAllText(baseline);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Fail($"Could not read the baseline snapshot: {exception.Message}");
            }
        }

        string? sheetIdsText = null;
        if (sheetIds is not null)
        {
            try
            {
                sheetIdsText = File.ReadAllText(sheetIds);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Fail($"Could not read the sheet ids: {exception.Message}");
            }
        }

        MountSourcesValidationReport report;
        try
        {
            // The size check is about the shipped file, so it uses the bytes on disk rather than the
            // length of the string the reader parsed.
            long candidateBytes = new FileInfo(candidate).Length;
            report = MountSourcesValidator.Validate(
                candidateJson,
                baselineJson,
                sheetIdsText,
                options with { CandidateBytes = candidateBytes });
        }
        catch (FormatException exception)
        {
            // A snapshot that cannot be read at all is the loudest failure there is; say which file.
            return Fail($"The snapshot is not readable as Mountbook data: {exception.Message}");
        }

        Console.Write(report.ToText());

        if (reportPath is not null)
        {
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, report.ToMarkdown(), new UTF8Encoding(false));
                Console.WriteLine($"Report written to {reportPath}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Fail($"Could not write the report: {exception.Message}");
            }
        }

        return report.IsValid ? 0 : 1;
    }

    private static bool TryCount(string value, out int count) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count >= 0;

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
