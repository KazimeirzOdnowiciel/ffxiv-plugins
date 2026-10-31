using System.Collections.Generic;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>
/// Everything <see cref="MountbookView"/> draws, and everything it reports back.
/// </summary>
/// <remarks>
/// The view reads and writes only this object, never a Dalamud service, the config or the wall clock,
/// which is what lets the preview project draw the same view with fixed sample data and compare the
/// result against a golden image (CONTRIBUTING T6, T7).
/// <para>
/// <see cref="Items"/> is the list to draw, already filtered and ordered by the caller: the window
/// runs <c>MountQuery.Apply</c> with the fields this view writes back, and the view never sorts or
/// filters (a per-frame sort of every mount would allocate for no visible gain).
/// </para>
/// </remarks>
public sealed class MountViewState
{
    /// <summary>The mounts to draw, in draw order. The caller filters and sorts; the view does not.</summary>
    public IReadOnlyList<MountShelfItem> Items { get; set; } = [];

    /// <summary>icon id or art id -> ImGui texture handle. Empty in the preview: the view then
    /// draws a themed placeholder tile, which is what keeps the golden runnable without the game.</summary>
    public IReadOnlyDictionary<uint, nint> Textures { get; set; } = new Dictionary<uint, nint>();

    /// <summary>The pixel size behind each handle, used to cover-crop the hero artwork. Empty in the
    /// preview, where the view falls back to a square assumption.</summary>
    public IReadOnlyDictionary<uint, System.Numerics.Vector2> TextureSizes { get; set; } =
        new Dictionary<uint, System.Numerics.Vector2>();

    /// <summary>The mount whose detail pane is shown, or null for the "pick a mount" state.</summary>
    public uint? SelectedId { get; set; }

    /// <summary>What the user typed into the search field.</summary>
    public string Search { get; set; } = string.Empty;

    /// <summary>Which ownership filter chip is selected.</summary>
    public MountOwnership Ownership { get; set; }

    /// <summary>Which expansion the filter selects.</summary>
    public MountExpansion Expansion { get; set; }

    /// <summary>How the caller should order <see cref="Items"/>.</summary>
    public MountOrder Order { get; set; }

    /// <summary>Whether the shelf draws cards or a dense table; the caller persists the choice.</summary>
    public MountViewMode ViewMode { get; set; }

    /// <summary>
    /// How the card grid splits the list into labelled sections. Presentation only: the grouped rows are
    /// the same <see cref="Items"/> in the same order, so this never re-queries the list.
    /// </summary>
    public MountGrouping GroupBy { get; set; }

    /// <summary>
    /// True once the table has drawn a frame since it was opened. Glasswork's table restores the sort
    /// spec it last had - ImGui keeps it per table id, across sessions - so the first frame after the
    /// table appears reports a sort change that nobody clicked, and honouring it silently replaced the
    /// player's order (choosing "Cheapest" and opening the table reverted the list to "Newest first").
    /// The caller clears this whenever the view mode changes, because that is exactly when the table is
    /// re-created and the stale spec comes back.
    /// </summary>
    public bool TableSortReady { get; set; }

    /// <summary>
    /// Cheapest known market price per mount id, keyed by mount (not item) id because the list it
    /// orders is a list of mounts. Empty until the prices have been read; a missing id is "unknown",
    /// which sorts last and draws no price.
    /// </summary>
    public IReadOnlyDictionary<uint, int> Prices { get; set; } = new Dictionary<uint, int>();

    /// <summary>
    /// The token price of the selected mount with the player's progress ("12,000 Seafarer's Cowries - you
    /// have 3,400, 8,600 to go"), or empty when the mount costs no tokens.
    /// </summary>
    public string CostText { get; set; } = string.Empty;

    /// <summary>How many mounts the player owns, over every mount in the game (not over <see cref="Items"/>).</summary>
    public int Owned { get; set; }

    /// <summary>How many mounts the game has in total.</summary>
    public int Total { get; set; }

    /// <summary>
    /// How many mounts each ownership chip would show, in <see cref="MountOwnership"/> order. Empty
    /// until the window has run a query.
    /// </summary>
    public IReadOnlyList<int> OwnershipCounts { get; set; } = [];

    /// <summary>Per-expansion owned/total, in release order, for the header strip.</summary>
    public IReadOnlyList<ExpansionProgress> Expansions { get; set; } = [];

    /// <summary>
    /// The player's hunting list, read-only from the view's side. The window owns the set and
    /// applies <see cref="ToggleWantedId"/> after the draw; the view only asks whether an id is in
    /// it and how many are on it.
    /// </summary>
    public IReadOnlySet<uint> WantedIds { get; set; } = new HashSet<uint>();

    /// <summary>
    /// The mount whose wanted membership the user toggled on this frame, or null. A one-frame pulse
    /// like <see cref="RideRequested"/>: the window applies it after the draw and clears it.
    /// </summary>
    public uint? ToggleWantedId { get; set; }

    /// <summary>
    /// Why "Ride" cannot act, set by the window. Null, empty or whitespace means it can; anything
    /// else is drawn beside the disabled button. The view already disables the button for a mount
    /// the player does not own, so this is only for window-level reasons (combat, casting, and so on).
    /// </summary>
    public string? RideDisabledReason { get; set; }

    /// <summary>
    /// Why "Link whistle" cannot act, set by the window: the item data carries no whistle item id, so
    /// only the window knows whether there is one to link. Null, empty or whitespace means it can.
    /// </summary>
    public string? LinkDisabledReason { get; set; }

    /// <summary>True on the frame "Ride" was clicked while enabled. A one-frame pulse: read it after <see cref="MountbookView.Draw"/>.</summary>
    public bool RideRequested { get; set; }

    /// <summary>True on the frame "Link whistle" was clicked while enabled. A one-frame pulse.</summary>
    public bool LinkRequested { get; set; }

    /// <summary>True when the window should open the game's own Mount Guide for this mount.</summary>
    public bool GuideRequested { get; set; }

    /// <summary>
    /// Clears every one-frame request. The view calls this at the start of each draw, so a request set during a
    /// draw survives exactly until the window reads it after that draw.
    /// </summary>
    /// <remarks>
    /// This exists because the list used to live inline in the view, and a flag added to this file without a
    /// matching line there opened a browser tab every frame for the wiki action. Adding a request property means
    /// adding one line here - the compiler cannot check it, so keep the list complete.
    /// </remarks>
    public void ClearRequests()
    {
        MarketCheckRequested = false;
        RideRequested = false;
        LinkRequested = false;
        GuideRequested = false;
        WikiRequested = false;
        ToggleWantedId = null;
        ClearFiltersRequested = false;
        SettingsRequested = false;
        RefreshRequested = false;
        ThemesRequested = false;
        CopyMissingRequested = false;
        SourceLinkRequested = null;
    }

    /// <summary>True when the window should open the selected mount's wiki page in a browser.</summary>
    public bool WikiRequested { get; set; }

    /// <summary>The display name of the selected mount, used for the wiki page.</summary>
    public string? WikiName { get; set; }

    /// <summary>
    /// The URL of a "How to obtain" row the player clicked, or null. A full URL rather than a name,
    /// because the row already knows which page it stands for (<see cref="MountSourceLinks"/>) and the
    /// window should not have to work it out again.
    /// </summary>
    public string? SourceLinkRequested { get; set; }

    /// <summary>Why the Mount Guide button must be disabled, or null when it may act.</summary>
    public string? GuideDisabledReason { get; set; }

    /// <summary>True on the frame a value control finished an edit.</summary>
    public bool Committed { get; set; }

    /// <summary>True when the header's settings action was clicked.</summary>
    public bool SettingsRequested { get; set; }

    /// <summary>True when the header's refresh action was clicked.</summary>
    public bool RefreshRequested { get; set; }

    /// <summary>True when the header's copy-missing action was clicked.</summary>
    public bool CopyMissingRequested { get; set; }

    /// <summary>True when the header's themes action was clicked.</summary>
    public bool ThemesRequested { get; set; }

    /// <summary>The acquire-method filter ("Trial", "PvP", ...), or empty for any.</summary>
    public string SourceFilter { get; set; } = string.Empty;

    /// <summary>The labels for the source combo: "Any" followed by every category on the shelf.</summary>
    public string[] SourceLabels { get; set; } = ["Any"];

    /// <summary>The whistle item's name for the selected mount, or null when it has none.</summary>
    public string? ItemName { get; set; }

    /// <summary>Mounts collected since the plugin loaded; drawn with a "New" marker.</summary>
    public IReadOnlySet<uint> RecentIds { get; set; } = new HashSet<uint>();

    /// <summary>True when the player asked the empty state to drop the search and the filters.</summary>
    public bool ClearFiltersRequested { get; set; }

    /// <summary>
    /// The market line under the wiki button, already formatted: a price, "nobody is selling this",
    /// "could not read the market", or empty when nothing has been asked yet.
    /// </summary>
    public string MarketPriceText { get; set; } = string.Empty;

    /// <summary>True when this mount records a tradeable whistle, so a price can be asked for at all.</summary>
    public bool MarketCanCheck { get; set; }

    /// <summary>True while a price request for the selected mount is out; the button says so.</summary>
    public bool MarketCheckInFlight { get; set; }

    /// <summary>True when the player asked for the selected mount's market price.</summary>
    public bool MarketCheckRequested { get; set; }

    /// <summary>
    /// Bumped by the window every time a Look is applied. Every cached text measurement in the view -
    /// header action widths, the counts line, the expansion labels, the card captions - is measured with
    /// the Look's type face, so a theme with a different face invalidates all of them; without this the
    /// old advance widths survived a theme switch and the chrome drifted off the card edge.
    /// </summary>
    public int MeasurementGeneration { get; set; }
}
