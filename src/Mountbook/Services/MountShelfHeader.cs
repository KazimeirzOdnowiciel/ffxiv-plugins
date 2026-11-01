using System;

namespace Mountbook.Services;

/// <summary>
/// The header of an obtain snapshot: what wrote it, when, how, and who has to be credited for it.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the rows alone cannot answer "is this file current?". A snapshot carries the
/// shape it was written in (<see cref="SchemaVersion"/>), the date the generator ran
/// (<see cref="Generated"/>), whether the third-party site answered (<see cref="Enrichment"/>), and
/// the credit line for the volunteer project the data comes from (<see cref="Attribution"/> and
/// <see cref="AttributionUrl"/>). The plugin reads all of it: the version decides whether the file
/// is stale or newer than the reader, and the attribution is what gets shown beside the data.
/// </para>
/// <para>
/// Every member has a harmless default, because the first shipped snapshot had no header at all -
/// it is read as version zero, i.e. stale, rather than refused.
/// </para>
/// </remarks>
/// <param name="SchemaVersion">The snapshot shape, zero when the file does not say.</param>
/// <param name="Generated">The generator's date (yyyy-MM-dd), empty when absent.</param>
/// <param name="Source">The site the enrichment came from, empty when absent.</param>
/// <param name="Enrichment">"full" or "sheet-only"; empty when absent.</param>
/// <param name="Attribution">The credit line, empty when absent.</param>
/// <param name="AttributionUrl">The credit link, empty when absent.</param>
/// <param name="CrossCheck">
/// The build-time comparison of the site's seats and movement against the game's <c>Mount</c> sheet, or
/// null when the run could not make one (offline, or no sheet facts on that machine).
/// </param>
public sealed record MountShelfHeader(
    int SchemaVersion,
    string Generated,
    string Source,
    string Enrichment,
    string Attribution,
    string AttributionUrl,
    MountShelfCrossCheck? CrossCheck = null)
{
    /// <summary>The enrichment word for a snapshot the site answered: every row may carry data.</summary>
    public const string FullEnrichment = "full";

    /// <summary>
    /// The enrichment word for a snapshot built from the game sheets alone: the rows are whatever
    /// the committed file carried, and nothing was refreshed. The plugin says so instead of drawing
    /// a stale "How to obtain" list as if it were current.
    /// </summary>
    public const string SheetOnlyEnrichment = "sheet-only";

    /// <summary>True when the snapshot records a sheet cross-check that actually ran.</summary>
    public bool HasCrossCheck => CrossCheck is not null;
}
