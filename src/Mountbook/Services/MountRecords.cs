using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// One "how to obtain" line for a mount: the type the game files it under, the human
/// readable detail, and the related record the source points at when it has one.
/// </summary>
/// <param name="Kind">The obtain type this line belongs to, read once from the dataset's <c>type</c>.</param>
/// <param name="Detail">The line's own text, e.g. the quest or achievement it names.</param>
/// <param name="RelatedKind">The record kind the line points at, or empty.</param>
/// <param name="RelatedId">The record id the line points at, or zero.</param>
public sealed record MountSourceLine(MountSourceKind Kind, string Detail, string RelatedKind, uint RelatedId)
{
    /// <summary>
    /// Builds a line from the dataset's own spelling, folding it to the type. For callers that hold the text
    /// rather than the type - the shelf's reader parses the type itself, and a test or a preview writes the
    /// spelling it means.
    /// </summary>
    /// <param name="category">The dataset's <c>type</c>, e.g. <c>V&amp;C Dungeon</c>.</param>
    /// <param name="detail">The line's own text.</param>
    /// <param name="relatedKind">The record kind the line points at, or empty.</param>
    /// <param name="relatedId">The record id the line points at, or zero.</param>
    public MountSourceLine(string category, string detail, string relatedKind, uint relatedId)
        : this(MountSourceKinds.Parse(category), detail, relatedKind, relatedId)
    {
    }

    /// <summary>
    /// The type's own spelling, e.g. <c>V&amp;C Dungeon</c>: what the obtain rows and the table's Source
    /// column draw. The filter and the grouping read <see cref="Kind"/>, never this, so a display string
    /// cannot decide what a filter selects.
    /// </summary>
    public string Category => Kind.Name;
}

/// <summary>
/// What the wider player base reports about one mount, from the embedded dataset: the share of
/// collectors who own it, and whether it can be sold or brings its own music. Every member is
/// nullable on purpose - the dataset writes <c>null</c> where ffxivcollect omits a field, and
/// "unknown" must never be drawn as "no" (a wrong tradeable badge is worse than no badge).
/// </summary>
/// <param name="OwnedPercent">The percentage of collectors who own it (ffxivcollect's <c>owned</c>), or null.</param>
/// <param name="Tradeable">Whether the mount can be traded, or null when unrecorded.</param>
/// <param name="CustomMusic">Whether the mount brings its own music, or null when unrecorded.</param>
public sealed record MountCommunity(double? OwnedPercent = null, bool? Tradeable = null, bool? CustomMusic = null);

/// <summary>
/// The obtain data for one mount id: the patch it arrived in, the item that grants it (zero when
/// it is not an item), every source line the dataset carries, and the community facts beside them.
/// </summary>
/// <param name="Availability">
/// Whether this mount can be obtained today, as the snapshot states it. It is read from the file rather than
/// derived here: the classification is data the generator wrote and the validator re-derives, so the badge,
/// the filter and the counts all read one field (see <see cref="MountAvailabilityRules"/>). Defaults to
/// <see cref="MountAvailability.Unknown"/>, which is also what a snapshot written by an older generator says.
/// </param>
public sealed record MountSources(
    string Patch,
    uint ItemId,
    IReadOnlyList<MountSourceLine> Lines,
    MountCommunity? Community = null,
    MountAvailability Availability = MountAvailability.Unknown);

/// <summary>
/// Everything the window needs to draw one mount row. Built by the wiring layer from Lumina and
/// the embedded dataset; the view reads only this.
/// </summary>
public sealed record MountShelfItem(
    uint Id,
    string Name,
    uint IconId,
    uint ArtId,
    int Seats,
    string Patch,
    bool Owned,
    string Description,
    string EnhancedDescription,
    string Tooltip,
    IReadOnlyList<MountSourceLine> Sources,
    MountCommunity? Community = null,
    bool Barding = false,
    bool Flies = false,
    MountAvailability Availability = MountAvailability.Unknown);
