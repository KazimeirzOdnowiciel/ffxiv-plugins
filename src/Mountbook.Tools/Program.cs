using System;
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

        --candidate   the freshly generated snapshot; the only required input.
        --baseline    the snapshot as committed before this run. Without it the row band has no
                      reference, "changed patches" cannot be reported and a lost source is invisible.
        --sheet-ids   the game's Mount sheet ids: one per line, or the mdlrip model map CSV
                      (out/mount-model-map.csv) unchanged. Without it an id is never checked against
                      the game, and the report says so.
        --report      also write the findings as markdown, for the run's evidence trail.
        --minimum     the row floor (default 300); --allowed-shrink/--allowed-growth widen the band
                      around the baseline (defaults 8 and 40).

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
            Console.Error.WriteLine($"Unknown verb \"{args[0]}\".");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        return Validate(args);
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
            report = MountSourcesValidator.Validate(candidateJson, baselineJson, sheetIdsText, options);
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
