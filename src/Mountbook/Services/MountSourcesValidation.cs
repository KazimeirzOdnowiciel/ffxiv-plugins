using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mountbook.Services;

/// <summary>
/// How serious one finding from <see cref="MountSourcesValidator" /> is.
/// </summary>
public enum MountSourcesIssueSeverity
{
    /// <summary>The snapshot must not ship: fix the source or the generator first.</summary>
    Error,

    /// <summary>Legitimate, but a human has to read and accept it before the file is committed.</summary>
    Warning,

    /// <summary>Context for the report: a change that is expected and needs no decision.</summary>
    Note,
}

/// <summary>
/// One finding about a candidate snapshot: a severity, a stable code the runbook can point at, and a
/// sentence a person can act on.
/// </summary>
/// <param name="Severity">Whether this blocks the snapshot.</param>
/// <param name="Code">A stable machine-readable name, e.g. <c>row-count</c>.</param>
/// <param name="Message">What was found, with the ids or values that show it.</param>
public sealed record MountSourcesIssue(MountSourcesIssueSeverity Severity, string Code, string Message);

/// <summary>
/// The band a regenerated snapshot is allowed to land in, and how many ids a finding lists before it
/// summarises the rest.
/// </summary>
/// <remarks>
/// A patch adds a handful of mounts and never removes one, so a drop of more than
/// <see cref="AllowedShrink" /> rows is a partial fetch rather than a patch. The growth side is wider
/// because a patch can add a dozen mounts at once, but it still has a ceiling: a jump of more than
/// <see cref="AllowedGrowth" /> rows means the endpoint started returning a different table, which is a
/// decision, not a regeneration.
/// </remarks>
public sealed record MountSourcesValidationOptions
{
    /// <summary>How many rows fewer than the committed snapshot still counts as a patch-day change.</summary>
    public int AllowedShrink { get; init; } = 8;

    /// <summary>How many rows more than the committed snapshot still counts as a patch-day change.</summary>
    public int AllowedGrowth { get; init; } = 40;

    /// <summary>The floor for any snapshot, so a first-ever run (no baseline) is still checked.</summary>
    public int MinimumCount { get; init; } = 300;

    /// <summary>How many ids one finding lists before it says "and N more".</summary>
    public int MaxListedIds { get; init; } = 10;

    /// <summary>
    /// The largest snapshot that may ship: 120 KB. The file is embedded in the assembly and
    /// downloaded by every player, so the budget is enforced here rather than rediscovered when the
    /// package grows - metadata measures 83 KB and the API's description copy would take it to
    /// 251 KB, which is the trade this number protects.
    /// </summary>
    public int MaxBytes { get; init; } = 122_880;

    /// <summary>
    /// The candidate file's size on disk, when the caller knows it. Null means "measure the text",
    /// which is the same number for the UTF-8 the generator writes (no BOM).
    /// </summary>
    public long? CandidateBytes { get; init; }
}

/// <summary>
/// The outcome of one validation run: the three row counts it compared, and every finding in the order
/// it is meant to be read (errors, then warnings, then notes).
/// </summary>
public sealed record MountSourcesValidationReport(
    int CandidateCount,
    int BaselineCount,
    int SheetCount,
    long CandidateBytes,
    IReadOnlyList<MountSourcesIssue> Issues)
{
    /// <summary>True when the snapshot may be committed: no finding is an error.</summary>
    public bool IsValid => ErrorCount == 0;

    /// <summary>How many findings block the snapshot.</summary>
    public int ErrorCount => CountOf(MountSourcesIssueSeverity.Error);

    /// <summary>How many findings a human has to read and accept.</summary>
    public int WarningCount => CountOf(MountSourcesIssueSeverity.Warning);

    /// <summary>
    /// The report as plain text, for a console or a log. ASCII only, one finding per line, so it reads
    /// the same pasted into a terminal, a commit message or a chat.
    /// </summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append("Mount sources validation: ").Append(IsValid ? "PASSED" : "FAILED");
        text.Append(" (").Append(CountText(ErrorCount, "error")).Append(", ");
        text.Append(CountText(WarningCount, "warning")).Append(", ");
        text.Append(CountText(CountOf(MountSourcesIssueSeverity.Note), "note")).Append(")").Append(Environment.NewLine);
        text.Append("  candidate ").Append(CandidateCount).Append(" rows");
        text.Append(" (").Append(Kilobytes(CandidateBytes)).Append(')');
        text.Append(" | baseline ").Append(BaselineCount > 0 ? BaselineCount.ToString(CultureInfo.InvariantCulture) : "none");
        text.Append(" | sheet ").Append(SheetCount > 0 ? SheetCount.ToString(CultureInfo.InvariantCulture) : "not supplied");
        text.Append(Environment.NewLine);

        foreach (MountSourcesIssue issue in Issues)
        {
            text.Append("  ").Append(issue.Severity.ToString().ToUpperInvariant().PadRight(8));
            text.Append(issue.Code.PadRight(20)).Append(issue.Message).Append(Environment.NewLine);
        }

        return text.ToString();
    }

    /// <summary>
    /// The same report as a markdown document, for the file the patch-day script leaves behind as the
    /// evidence that a regeneration was checked.
    /// </summary>
    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.Append("# Mount sources validation - ").Append(IsValid ? "passed" : "FAILED").Append('\n').Append('\n');
        text.Append("| | |\n| --- | --- |\n");
        text.Append("| Candidate rows | ").Append(CandidateCount).Append(" |\n");
        text.Append("| Candidate size | ").Append(Kilobytes(CandidateBytes)).Append(" |\n");
        text.Append("| Committed rows | ").Append(BaselineCount > 0 ? BaselineCount.ToString(CultureInfo.InvariantCulture) : "none").Append(" |\n");
        text.Append("| Mount sheet ids | ").Append(SheetCount > 0 ? SheetCount.ToString(CultureInfo.InvariantCulture) : "not supplied").Append(" |\n");
        text.Append("| Errors | ").Append(ErrorCount).Append(" |\n");
        text.Append("| Warnings | ").Append(WarningCount).Append(" |\n").Append('\n');
        text.Append("## Findings\n\n");

        if (Issues.Count == 0)
        {
            text.Append("Nothing to report: every check passed.\n");
        }

        foreach (MountSourcesIssue issue in Issues)
        {
            text.Append("- **").Append(issue.Severity.ToString().ToUpperInvariant()).Append("** `");
            text.Append(issue.Code).Append("` - ").Append(issue.Message).Append('\n');
        }

        return text.ToString();
    }

    private int CountOf(MountSourcesIssueSeverity severity)
    {
        var count = 0;
        foreach (MountSourcesIssue issue in Issues)
        {
            if (issue.Severity == severity)
            {
                count++;
            }
        }

        return count;
    }

    private static string CountText(int count, string noun) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");

    /// <summary>Bytes as "108.4 KB (110,738 bytes)", the two numbers a budget conversation needs.</summary>
    private static string Kilobytes(long bytes) =>
        (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB (" + bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes)";
}

/// <summary>
/// Checks a regenerated obtain snapshot before it replaces the committed one: the row count has to stay
/// inside a band, every id has to be a Mount sheet row, no mount may lose its source lines, and every
/// patch value has to be one the plugin can map to an expansion.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is against two references, and each one answers a question the file alone cannot:
/// the <em>baseline</em> (the snapshot as committed before this run) says what changed, and the
/// <em>sheet ids</em> (a list of the game's Mount sheet rows, which the mdlrip model map already is)
/// says whether an id is a mount at all. Both are optional so the checks that need them can be skipped
/// explicitly rather than silently: a missing sheet is reported as a note, never as a pass.
/// </para>
/// <para>
/// What it deliberately does not do: judge whether the site's data is <em>right</em>. It fails on a
/// snapshot that cannot be described honestly - a partial fetch, an id that is not a mount, a mount that
/// lost its sources, a patch the expansion map cannot read - and leaves the "is this correction real?"
/// question to the human reading the warnings.
/// </para>
/// <para>
/// Kept Dalamud-free (T1): it reads the same JSON the plugin embeds and the same expansion map the
/// filter uses, so a snapshot that passes here is a snapshot the reader understands.
/// </para>
/// </remarks>
public static class MountSourcesValidator
{
    /// <summary>An id appears twice, so the loader keeps one row and silently drops the other.</summary>
    public const string DuplicateIdCode = "duplicate-mount-id";

    /// <summary>A row has no usable id, so the loader drops it.</summary>
    public const string InvalidIdCode = "invalid-mount-id";

    /// <summary>The row count left the band the committed snapshot allows.</summary>
    public const string RowCountCode = "row-count";

    /// <summary>An id is not a row in the game's Mount sheet.</summary>
    public const string UnknownIdCode = "unknown-mount-id";

    /// <summary>The committed snapshot had a mount this one does not.</summary>
    public const string MountDisappearedCode = "mount-disappeared";

    /// <summary>A mount that had source lines now has none.</summary>
    public const string SourcesLostCode = "sources-lost";

    /// <summary>A mount has no patch value at all.</summary>
    public const string PatchMissingCode = "patch-missing";

    /// <summary>A patch value maps to no expansion the plugin knows.</summary>
    public const string PatchNotInSheetCode = "patch-not-in-sheet";

    /// <summary>An existing mount's patch value moved.</summary>
    public const string PatchChangedCode = "patch-changed";

    /// <summary>A patch label nobody has seen before: either a new patch or a typo.</summary>
    public const string PatchAddedCode = "patch-added";

    /// <summary>The snapshot carries mounts the committed one did not.</summary>
    public const string MountAddedCode = "mount-added";

    /// <summary>Community ownership percentages moved: the site recounts daily.</summary>
    public const string OwnershipDriftCode = "ownership-drift";

    /// <summary>The snapshot declares no schema version, so a stale file cannot be told from a fresh one.</summary>
    public const string SchemaMissingCode = "schema-missing";

    /// <summary>The snapshot's schema version is not the one this build reads.</summary>
    public const string SchemaVersionCode = "schema-version";

    /// <summary>The snapshot does not credit the volunteer project it came from.</summary>
    public const string AttributionMissingCode = "attribution-missing";

    /// <summary>The snapshot is bigger than the size budget it ships under.</summary>
    public const string SizeBudgetCode = "size-budget";

    /// <summary>A row carries the API's own description copy, which the game's sheets already provide.</summary>
    public const string DescriptionCopyCode = "description-copy";

    /// <summary>A mount carries no obtain type at all, so no source filter and no source heading can place it.</summary>
    public const string SourceTypeMissingCode = "source-type-missing";

    /// <summary>An obtain line carries a type this build has never been taught.</summary>
    public const string SourceTypeUnknownCode = "source-type-unknown";

    /// <summary>The snapshot was built offline, so nothing in it was refreshed from the site.</summary>
    public const string SheetOnlyCode = "enrichment-sheet-only";

    /// <summary>The snapshot records no comparison of the site's seats/movement against the Mount sheet.</summary>
    public const string CrossCheckMissingCode = "crosscheck-missing";

    /// <summary>The recorded comparison did not cover every row, or did not agree on every row.</summary>
    public const string CrossCheckDriftCode = "crosscheck-drift";

    /// <summary>The recorded comparison names sheet columns that are not the seats/movement ones.</summary>
    public const string CrossCheckFieldsCode = "crosscheck-fields";

    /// <summary>No sheet ids were supplied, so ids were checked for shape only.</summary>
    public const string SheetSkippedCode = "sheet-skipped";

    /// <summary>
    /// Validates <paramref name="candidateJson" /> against the committed snapshot and, when given, the
    /// sheet ids.
    /// </summary>
    /// <param name="candidateJson">The freshly generated snapshot.</param>
    /// <param name="baselineJson">The snapshot as committed before this run, or null for a first run.</param>
    /// <param name="sheetIdsText">One mount id per line, or a CSV whose first column is the id (see <see cref="ParseSheetIds" />).</param>
    /// <param name="options">The band and listing limits; defaults to <see cref="MountSourcesValidationOptions" />.</param>
    /// <returns>Every finding, ordered errors first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidateJson" /> is null.</exception>
    /// <exception cref="FormatException">A document is not the snapshot shape, or the sheet ids carry no id.</exception>
    public static MountSourcesValidationReport Validate(
        string candidateJson,
        string? baselineJson = null,
        string? sheetIdsText = null,
        MountSourcesValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(candidateJson);
        options ??= new MountSourcesValidationOptions();

        MountShelf candidate = MountShelf.Parse(candidateJson);
        MountShelf? baseline = baselineJson is null ? null : MountShelf.Parse(baselineJson);
        HashSet<uint>? sheet = sheetIdsText is null ? null : ParseSheetIds(sheetIdsText);
        MountIdScan scan = ScanIds(candidateJson);

        var issues = new List<MountSourcesIssue>();
        long candidateBytes = options.CandidateBytes ?? Encoding.UTF8.GetByteCount(candidateJson);

        CheckHeader(candidate, candidateJson, candidateBytes, issues, options);
        CheckCrossCheck(candidate, issues);
        CheckIds(scan, issues, options);
        CheckRowCount(candidate.Count, baseline?.Count ?? 0, candidate.IsSheetOnly, issues, options);
        CheckSheet(candidate, sheet, issues, options);
        CheckDisappeared(candidate, baseline, issues, options);
        CheckSources(candidate, baseline, issues, options);
        CheckSourceTypes(candidate, issues, options);
        CheckPatches(candidate, baseline, issues, options);
        CheckCommunity(candidate, baseline, issues);

        return new MountSourcesValidationReport(
            candidate.Count,
            baseline?.Count ?? 0,
            sheet?.Count ?? 0,
            candidateBytes,
            Order(issues));
    }

    /// <summary>
    /// Checks what the document says about itself: the schema version it was written in, the credit
    /// line for the volunteer project the data comes from, the size it has to fit in, and whether it
    /// carries the API's description copy (which the game's own sheets already provide, and which is
    /// what would blow the budget). A snapshot built offline is a warning, not an error: it is a
    /// deliberate fallback, but it means nothing in it was refreshed.
    /// </summary>
    private static void CheckHeader(
        MountShelf candidate,
        string candidateJson,
        long candidateBytes,
        List<MountSourcesIssue> issues,
        MountSourcesValidationOptions options)
    {
        if (candidate.SchemaVersion == MountShelf.LegacySchemaVersion)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                SchemaMissingCode,
                $"The snapshot declares no schemaVersion, so this build cannot tell a stale file from a fresh one (it reads v{MountShelf.CurrentSchemaVersion}). Regenerate it with tools/generate-mount-sources.ps1."));
        }
        else if (candidate.SchemaVersion != MountShelf.CurrentSchemaVersion)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                SchemaVersionCode,
                $"The snapshot is schema v{candidate.SchemaVersion}, but this build reads v{MountShelf.CurrentSchemaVersion}. Regenerate it with the generator from this tree, or the fields added since will be silently missing."));
        }

        if (!candidate.HasAttribution)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                AttributionMissingCode,
                "The snapshot carries no attribution (attribution + attributionUrl), and the obtain data is a volunteer community project's work: it has to be credited where it is shown, in the plugin description and in the notices."));
        }

        if (candidateBytes > options.MaxBytes)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                SizeBudgetCode,
                $"The snapshot is {candidateBytes} bytes, over the {options.MaxBytes}-byte budget. The file is embedded in the assembly and downloaded by every player: drop a field (the API's description/tooltip copy is the usual offender at +168 KB) rather than raising the budget."));
        }

        int withDescription = CountDescriptionCopy(candidateJson);
        if (withDescription > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                DescriptionCopyCode,
                $"{withDescription} mount(s) carry the API's own description/enhancedDescription/tooltip copy. The plugin reads that text from the game's MountTransient sheet, so the copy is both a duplicate and the reason the file would leave the size budget."));
        }

        if (candidate.IsSheetOnly)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Warning,
                SheetOnlyCode,
                $"The snapshot says it was built offline (enrichment \"{MountShelfHeader.SheetOnlyEnrichment}\"): {candidate.Count} mounts keep whatever the committed file carried and nothing was refreshed from ffxivcollect. Ship it only when the site is unreachable and the alternative is no snapshot at all."));
        }
    }

    /// <summary>
    /// Checks the record of the one comparison the snapshot cannot make itself: the site's seat counts and
    /// Airborne/Terrestrial values against the game's <c>Mount</c> sheet. The file does not carry those two
    /// facts (the sheet answers them for every row, and a copy could contradict a patched client), so this
    /// block is the only evidence the check ran - and a check that covered fewer rows than the file carries,
    /// or that agreed on fewer than it covered, must not ship.
    /// </summary>
    /// <remarks>
    /// A snapshot built offline has nothing to compare and is skipped: <see cref="CheckHeader" />'s
    /// <c>enrichment-sheet-only</c> warning already says nothing was refreshed. Every other snapshot that
    /// records no block gets a warning rather than an error, because a machine without the game can still
    /// regenerate the data - it just cannot verify it, and the report has to say so.
    /// </remarks>
    private static void CheckCrossCheck(MountShelf candidate, List<MountSourcesIssue> issues)
    {
        if (candidate.IsSheetOnly)
        {
            return;
        }

        MountShelfCrossCheck? cross = candidate.CrossCheck;
        if (cross is null)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Warning,
                CrossCheckMissingCode,
                $"The snapshot records no sheet cross-check, so the site's seat counts and Airborne/Terrestrial values were never compared against the game's Mount sheet for {candidate.Count} mount(s). They are the two facts the file deliberately does not carry. Regenerate with the sheet facts (`mdlrip --facts`, then tools/generate-mount-sources.ps1) so this is recorded."));
            return;
        }

        if (!cross.NamesSheetFacts)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                CrossCheckFieldsCode,
                $"The recorded cross-check names the sheet columns [{string.Join(", ", cross.Fields)}], but seats come from Mount.{MountShelfCrossCheck.SeatsField} (+1) and movement from Mount.{MountShelfCrossCheck.MovementField}. A comparison of other columns says nothing about these two facts."));
        }

        if (cross.RowsChecked < candidate.Count)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                CrossCheckDriftCode,
                $"The recorded cross-check covers {cross.RowsChecked} of the snapshot's {candidate.Count} mount(s): the rest were never compared against the game's Mount sheet. Regenerate with the full sheet facts."));
        }

        if (!cross.Agrees)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                CrossCheckDriftCode,
                $"The site and the game's Mount sheet disagree: {cross.SeatsAgreed}/{cross.RowsChecked} seat counts and {cross.MovementAgreed}/{cross.RowsChecked} movement values agreed. The generator refuses to write such a snapshot, so either this file was edited by hand or the sheet facts come from another game build. Regenerate both before shipping."));
        }
    }

    /// <summary>
    /// Counts the rows carrying a member the plugin never reads because the game's sheets provide it.
    /// </summary>
    private static int CountDescriptionCopy(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("mounts", out JsonElement mounts)
            || mounts.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (JsonElement mount in mounts.EnumerateArray())
        {
            if (mount.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (mount.TryGetProperty("description", out _)
                || mount.TryGetProperty("enhancedDescription", out _)
                || mount.TryGetProperty("enhanced_description", out _)
                || mount.TryGetProperty("tooltip", out _))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Reads sheet ids from text: one per line, or a CSV whose first column is <c>mount_id</c> - the
    /// mdlrip model map (<c>out/mount-model-map.csv</c>) drops in unchanged, header and all. Blank lines
    /// and <c>#</c> comments are skipped.
    /// </summary>
    /// <returns>The sheet's row ids, never empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text" /> is null.</exception>
    /// <exception cref="FormatException">A line is not an id, or no id was found at all.</exception>
    public static HashSet<uint> ParseSheetIds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ids = new HashSet<uint>();
        var first = true;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int comma = line.IndexOf(',');
            string head = (comma < 0 ? line : line[..comma]).Trim().Trim('"');

            if (!uint.TryParse(head, NumberStyles.None, CultureInfo.InvariantCulture, out uint id))
            {
                // One non-numeric first field is the header row of a CSV; anything else is a broken file.
                if (first)
                {
                    first = false;
                    continue;
                }

                throw new FormatException($"The sheet ids carry a line that is not a mount id: \"{line}\"");
            }

            first = false;
            if (id != 0)
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            throw new FormatException("The sheet ids carry no mount id at all; an empty sheet would report every mount as unknown.");
        }

        return ids;
    }

    private static void CheckIds(MountIdScan scan, List<MountSourcesIssue> issues, MountSourcesValidationOptions options)
    {
        var duplicates = new List<uint>();
        var seen = new HashSet<uint>();
        var unusable = scan.RowCount - scan.Ids.Count;

        foreach (uint id in scan.Ids)
        {
            if (id == 0)
            {
                unusable++;
                continue;
            }

            if (!seen.Add(id) && !duplicates.Contains(id))
            {
                duplicates.Add(id);
            }
        }

        if (unusable > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                InvalidIdCode,
                $"{unusable} row(s) carry no usable id, and the loader drops those mounts silently."));
        }

        if (duplicates.Count > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                DuplicateIdCode,
                $"{duplicates.Count} id(s) appear more than once, so the loader keeps only the last row: {ListIds(duplicates, options)}."));
        }
    }

    private static void CheckRowCount(
        int candidateCount,
        int baselineCount,
        bool sheetOnly,
        List<MountSourcesIssue> issues,
        MountSourcesValidationOptions options)
    {
        int lowest = Math.Max(options.MinimumCount, baselineCount > 0 ? baselineCount - options.AllowedShrink : 0);
        int highest = baselineCount > 0 ? baselineCount + options.AllowedGrowth : int.MaxValue;

        // A sheet-only snapshot is measured against the game's Mount sheet, not against the committed
        // file: it may legitimately carry every sheet row (the disaster-recovery shape) or exactly the
        // committed rows (the carry-over shape), and CheckSheet/CheckDisappeared already prove that
        // every id is a mount and that none was dropped. The band would only fire on the first shape.
        if (sheetOnly)
        {
            lowest = options.MinimumCount;
            highest = int.MaxValue;
        }

        if (candidateCount < lowest)
        {
            string band = baselineCount > 0
                ? $"the band is {lowest}-{highest} (committed {baselineCount}, floor {options.MinimumCount})"
                : $"at least {lowest} are expected";
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                RowCountCode,
                $"The snapshot has {candidateCount} mounts but {band}. A patch never removes a mount, so this is a partial fetch or a changed endpoint, not a patch."));
        }
        else if (candidateCount > highest)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                RowCountCode,
                $"The snapshot has {candidateCount} mounts, {candidateCount - baselineCount} more than the committed {baselineCount} (band {lowest}-{highest}). A jump that large means the source started returning a different table."));
        }
    }

    private static void CheckSheet(
        MountShelf candidate,
        HashSet<uint>? sheet,
        List<MountSourcesIssue> issues,
        MountSourcesValidationOptions options)
    {
        if (sheet is null)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Note,
                SheetSkippedCode,
                "No sheet ids were supplied, so mount ids were checked for shape only, not against the game's Mount sheet."));
            return;
        }

        var unknown = new List<uint>();
        var mentioned = 0;

        foreach (uint id in sheet)
        {
            if (candidate.TryGet(id, out _))
            {
                mentioned++;
            }
        }

        foreach (uint id in SortedIds(candidate))
        {
            if (!sheet.Contains(id))
            {
                unknown.Add(id);
            }
        }

        if (unknown.Count > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                UnknownIdCode,
                $"{unknown.Count} id(s) are not rows in the game's Mount sheet, so the site and the game disagree about what a mount is: {ListIds(unknown, options)}."));
        }

        issues.Add(new MountSourcesIssue(
            MountSourcesIssueSeverity.Note,
            "sheet-coverage",
            $"{sheet.Count - mentioned} of the sheet's {sheet.Count} rows have no obtain data (mounts the site does not list as available, plus its unnamed placeholder rows)."));
    }

    private static void CheckDisappeared(
        MountShelf candidate,
        MountShelf? baseline,
        List<MountSourcesIssue> issues,
        MountSourcesValidationOptions options)
    {
        if (baseline is null)
        {
            return;
        }

        var gone = new List<uint>();
        foreach (uint id in SortedIds(baseline))
        {
            if (!candidate.TryGet(id, out _))
            {
                gone.Add(id);
            }
        }

        if (gone.Count > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                MountDisappearedCode,
                $"{gone.Count} mount(s) the committed snapshot had are missing here: {ListIds(gone, options)}. The game does not remove mounts, so this is a fetch or generator problem."));
        }
    }

    private static void CheckSources(
        MountShelf candidate,
        MountShelf? baseline,
        List<MountSourcesIssue> issues,
        MountSourcesValidationOptions options)
    {
        if (baseline is null)
        {
            return;
        }

        var lost = new List<uint>();
        foreach (uint id in SortedIds(baseline))
        {
            if (!baseline.TryGet(id, out MountSources before) || before.Lines.Count == 0)
            {
                continue;
            }

            // A mount that vanished entirely is reported once, by CheckDisappeared.
            if (candidate.TryGet(id, out MountSources after) && after.Lines.Count == 0)
            {
                lost.Add(id);
            }
        }

        if (lost.Count > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                SourcesLostCode,
                $"{lost.Count} mount(s) lost every source line they had: {ListIds(lost, options)}. An empty \"How to obtain\" list is worse than a stale one, so check the site's records for these before shipping."));
        }
    }

    /// <summary>
    /// Checks the taxonomy side of the obtain data: every mount that has obtain lines has to carry at least one
    /// type, and every type has to be one this build knows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter, the grouping and the counts are all keyed by the type, so a row whose lines carry none is
    /// invisible to each of them at once - which reads as a broken filter rather than as a gap in the data. An
    /// unknown type is refused rather than accepted quietly for the same reason the patch map is: a new type is a
    /// decision (its own label, its own rank, its own section in the grid), and a snapshot must not ship one that
    /// nobody has made. Both are warnings for a sheet-only snapshot, which by construction has no refreshed types.
    /// </para>
    /// <para>
    /// A mount with no obtain lines at all is not this check's business: the site not listing a mount yet is a
    /// fact about the site, and <see cref="CheckSources"/> is what fails a mount that *lost* the lines it had.
    /// </para>
    /// </remarks>
    private static void CheckSourceTypes(
        MountShelf candidate,
        List<MountSourcesIssue> issues,
        MountSourcesValidationOptions options)
    {
        var typeless = new List<uint>();
        var unknown = new List<string>();
        var seenUnknown = new HashSet<string>(StringComparer.Ordinal);

        foreach (uint id in SortedIds(candidate))
        {
            candidate.TryGet(id, out MountSources sources);
            var typed = false;

            foreach (MountSourceLine line in sources.Lines)
            {
                if (line.Kind.IsAny)
                {
                    continue;
                }

                typed = true;

                if (!line.Kind.IsKnown && seenUnknown.Add(line.Kind.Name))
                {
                    unknown.Add($"{line.Kind.Name} (mount {id})");
                }
            }

            if (sources.Lines.Count > 0 && !typed)
            {
                typeless.Add(id);
            }
        }

        if (typeless.Count > 0)
        {
            string message =
                $"{typeless.Count} mount(s) have obtain lines but no type on any of them, so the source filter, its "
                + $"counts and the source grouping cannot place them: {ListIds(typeless, options)}. The source data "
                + "classifies every obtain line it writes; a line without a type is a broken fetch, not a patch.";

            issues.Add(candidate.IsSheetOnly
                ? new MountSourcesIssue(MountSourcesIssueSeverity.Warning, SourceTypeMissingCode, message)
                : new MountSourcesIssue(MountSourcesIssueSeverity.Error, SourceTypeMissingCode, message));
        }

        if (unknown.Count > 0)
        {
            unknown.Sort(StringComparer.Ordinal);
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                SourceTypeUnknownCode,
                $"{unknown.Count} obtain type(s) are not in the taxonomy this build knows: {ListText(unknown, options)}. "
                + "Add each one to MountSourceKinds.Names (its rank decides where it sorts) and its label to the strings "
                + "table, deliberately - until then those mounts would land in an unlabelled section."));
        }
    }

    private static void CheckPatches(
        MountShelf candidate,
        MountShelf? baseline,
        List<MountSourcesIssue> issues,
        MountSourcesValidationOptions options)
    {
        var patchless = new List<uint>();
        var unmapped = new List<string>();

        foreach (uint id in SortedIds(candidate))
        {
            candidate.TryGet(id, out MountSources sources);
            string patch = sources.Patch.Trim();

            if (patch.Length == 0)
            {
                patchless.Add(id);
            }
            else if (MountBreakdown.ExpansionForPatch(patch) is null)
            {
                unmapped.Add($"{id} (\"{patch}\")");
            }
        }

        if (patchless.Count > 0)
        {
            // A sheet-only snapshot has no patch values by construction; the single warning in
            // CheckHeader already told the reader that nothing was refreshed, so this is not a second
            // error about the same fact.
            issues.Add(candidate.IsSheetOnly
                ? new MountSourcesIssue(
                    MountSourcesIssueSeverity.Warning,
                    PatchMissingCode,
                    $"{patchless.Count} mount(s) carry no patch because the snapshot was built offline: {ListIds(patchless, options)}.")
                : new MountSourcesIssue(
                    MountSourcesIssueSeverity.Error,
                    PatchMissingCode,
                    $"{patchless.Count} mount(s) carry no patch, so the patch filter and the expansion strip cannot place them: {ListIds(patchless, options)}."));
        }

        if (unmapped.Count > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Error,
                PatchNotInSheetCode,
                $"{unmapped.Count} patch value(s) map to no expansion the plugin ships, so those mounts would land in \"Other\": {ListText(unmapped, options)}. Add the expansion to MountBreakdown.Expansions (and MountExpansion) deliberately, or fix the value."));
        }

        if (baseline is null)
        {
            return;
        }

        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (uint id in SortedIds(baseline))
        {
            baseline.TryGet(id, out MountSources before);
            labels.Add(before.Patch.Trim());
        }

        var changed = new List<string>();
        var added = new List<uint>();
        var newLabels = new List<string>();
        var seenLabels = new HashSet<string>(StringComparer.Ordinal);

        foreach (uint id in SortedIds(candidate))
        {
            candidate.TryGet(id, out MountSources after);
            string patch = after.Patch.Trim();

            if (patch.Length > 0 && seenLabels.Add(patch) && !labels.Contains(patch))
            {
                newLabels.Add(patch);
            }

            if (!baseline.TryGet(id, out MountSources before))
            {
                added.Add(id);
                continue;
            }

            string previous = before.Patch.Trim();
            if (!string.Equals(previous, patch, StringComparison.Ordinal))
            {
                changed.Add($"{id}: \"{previous}\" -> \"{patch}\"");
            }
        }

        if (changed.Count > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Warning,
                PatchChangedCode,
                $"{changed.Count} mount(s) moved patch: {ListText(changed, options)}. Only these may differ on a regeneration - read them, then say so in the changelog."));
        }

        if (newLabels.Count > 0)
        {
            newLabels.Sort(StringComparer.Ordinal);
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Note,
                PatchAddedCode,
                $"Patch label(s) the committed snapshot did not have: {string.Join(", ", newLabels)}."));
        }

        if (added.Count > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Note,
                MountAddedCode,
                $"{added.Count} new mount(s): {ListIds(added, options)}."));
        }
    }

    private static void CheckCommunity(
        MountShelf candidate,
        MountShelf? baseline,
        List<MountSourcesIssue> issues)
    {
        if (baseline is null)
        {
            return;
        }

        var drifted = 0;
        foreach (uint id in SortedIds(candidate))
        {
            if (!baseline.TryGet(id, out MountSources before) || !candidate.TryGet(id, out MountSources after))
            {
                continue;
            }

            if (before.Community?.OwnedPercent != after.Community?.OwnedPercent)
            {
                drifted++;
            }
        }

        if (drifted > 0)
        {
            issues.Add(new MountSourcesIssue(
                MountSourcesIssueSeverity.Note,
                OwnershipDriftCode,
                $"{drifted} mount(s) had their community ownership percentage move. The site recounts daily, so that alone is not a data change - it is why a regeneration's diff is never empty."));
        }
    }

    private static List<MountSourcesIssue> Order(List<MountSourcesIssue> issues)
    {
        var ordered = new List<MountSourcesIssue>(issues.Count);
        foreach (MountSourcesIssueSeverity severity in new[]
        {
            MountSourcesIssueSeverity.Error,
            MountSourcesIssueSeverity.Warning,
            MountSourcesIssueSeverity.Note,
        })
        {
            foreach (MountSourcesIssue issue in issues)
            {
                if (issue.Severity == severity)
                {
                    ordered.Add(issue);
                }
            }
        }

        return ordered;
    }

    private static List<uint> SortedIds(MountShelf shelf)
    {
        // The order belongs in one place: the report reads ids ascending whatever order the snapshot
        // happened to write them in (a dictionary's key order is not stable across runs).
        var ids = new List<uint>(shelf.Ids);
        ids.Sort();
        return ids;
    }

    private static string ListIds(IReadOnlyList<uint> ids, MountSourcesValidationOptions options)
    {
        var listed = new List<string>();
        int shown = Math.Min(ids.Count, Math.Max(0, options.MaxListedIds));

        for (var index = 0; index < shown; index++)
        {
            listed.Add(ids[index].ToString(CultureInfo.InvariantCulture));
        }

        return shown == ids.Count
            ? string.Join(", ", listed)
            : string.Join(", ", listed) + $", and {ids.Count - shown} more";
    }

    private static string ListText(IReadOnlyList<string> values, MountSourcesValidationOptions options)
    {
        int shown = Math.Min(values.Count, Math.Max(0, options.MaxListedIds));
        var listed = new List<string>(shown);

        for (var index = 0; index < shown; index++)
        {
            listed.Add(values[index]);
        }

        return shown == values.Count
            ? string.Join(", ", listed)
            : string.Join(", ", listed) + $", and {values.Count - shown} more";
    }

    private static MountIdScan ScanIds(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var ids = new List<uint>();
        var rows = 0;

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("mounts", out JsonElement mounts)
            || mounts.ValueKind != JsonValueKind.Array)
        {
            return new MountIdScan(rows, ids);
        }

        foreach (JsonElement mount in mounts.EnumerateArray())
        {
            if (mount.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rows++;

            if (mount.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.Number
                && id.TryGetUInt32(out uint value))
            {
                ids.Add(value);
            }
        }

        return new MountIdScan(rows, ids);
    }

    private sealed record MountIdScan(int RowCount, IReadOnlyList<uint> Ids);
}
