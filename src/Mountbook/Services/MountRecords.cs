using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// One "how to obtain" line for a mount: the category the game files it under, the human
/// readable detail, and the related record the source points at when it has one.
/// </summary>
public sealed record MountSourceLine(string Category, string Detail, string RelatedKind, uint RelatedId);

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
public sealed record MountSources(
    string Patch,
    uint ItemId,
    IReadOnlyList<MountSourceLine> Lines,
    MountCommunity? Community = null);

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
    bool Flies = false);
