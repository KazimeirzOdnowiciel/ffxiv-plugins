using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork.Backend;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>
/// Draws the main window's body from <see cref="MountViewState"/> and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// No Dalamud service is touched here, no configuration, and no wall clock: the window gathers the
/// mounts and the player's progress into the state first, and the headless preview draws this same
/// view with fixed sample data (CONTRIBUTING T6, T7). Anything that differs between two runs - the
/// player's collection, the game's data - is already a value in the state.
/// </para>
/// <para>
/// The body sits in a <c>Gw.BeginCard()</c> panel rather than straight on the window, which is what
/// makes it readable both over the glass in game and over the preview's backdrop stand-in. Colour is
/// never painted by hand: every widget and every highlight takes its ink from the Look (N9). The two
/// columns and the scrolling card grid use plain ImGui children - layout only. Glasswork does ship
/// scrolling components (<c>VirtualList</c>, surface <c>Scroll</c>, <c>Gw.BeginSplit</c>), but they
/// impose their own item model; the grid here is a plain child, and every control inside it is still
/// a <c>Gw</c> widget.
/// </para>
/// </remarks>
public sealed partial class MountbookView
{
    /// <summary>The bottom inset <c>Gw.EndCard()</c> consumes below its content, measured at UI scale 1.</summary>
    private const float CardBottomInset = 16f;


    /// <summary>One card's width: the 64 px tile plus room for a readable caption.</summary>
    private const float CellWidth = 120f;

    /// <summary>Fitted captions and their measured width, keyed by mount and cell width.</summary>
    private static readonly System.Collections.Generic.Dictionary<(uint Id, int Width), Caption> Captions = [];

    /// <summary>The share of the card the filter and card column takes; the detail pane takes the rest.</summary>
    private const float LeftColumnShare = 0.62f;

    /// <summary>The icon tile edge inside a card, in logical units.</summary>
    private const float IconSize = 64f;

    /// <summary>The hero artwork's height in the detail pane, in logical units.</summary>
    private const float HeroHeight = 168f;

    /// <summary>
    /// Labels for <c>Gw.Segmented</c>, in <see cref="MountOwnership"/> order. Static arrays, because
    /// the widgets re-read them every frame and the draw path must not allocate.
    /// </summary>
    private static readonly string[] OwnershipNames = { "All", "Owned", "Unowned", "Wanted" };

    /// <summary>Cached chip labels with their counts; rebuilt only when the counts change.</summary>
    private readonly string[] ownershipLabels = (string[])OwnershipNames.Clone();

    /// <summary>Cached strip labels, one per expansion, rebuilt when a count moves.</summary>
    /// <summary>How many <see cref="PushControlRoom"/> clips are open, so a pop never underflows.</summary>
    private static int roomPushes;

    private string[] expansionLabels = [];

    /// <summary>The owned/total pair each cached strip label was built from.</summary>
    private int[,] expansionCounts = new int[0, 2];
    private readonly int[] ownershipCounts = { -1, -1, -1, -1 };


    /// <summary>Labels for the expansion <c>Gw.ComboBox</c>, in <see cref="MountExpansion"/> order.</summary>
    private static readonly string[] ExpansionLabels =
    {
        "All",
        "A Realm Reborn",
        "Heavensward",
        "Stormblood",
        "Shadowbringers",
        "Endwalker",
        "Dawntrail",
        "Unknown",
    };

    /// <summary>The two ways the shelf can draw the same list.</summary>
    private static readonly string[] ViewLabels = ["Cards", "Table"];

    /// <summary>Labels for the "Group by" <c>Gw.ComboBox</c>, in <see cref="MountGrouping"/> order.</summary>
    private static readonly string[] GroupLabels = ["No grouping", "Expansion", "Patch", "Source"];

    /// <summary>Labels for the order <c>Gw.ComboBox</c>, in <see cref="MountOrder"/> order.</summary>
    private static readonly string[] OrderLabels =
    {
        "Name",
        "Oldest first",
        "Most seats",
        "Owned first",
        "Newest first",
        "Rarest first",
        "Cheapest",
        // The one order that is a verb: it exists so the top of the list is a mount nobody would have
        // picked, and "Surprise me" says that where "Random" would read as a sort key.
        "Surprise me",
    };

    /// <summary>The Look generation the cached measurements were taken at.</summary>
    private int measuredGeneration = int.MinValue;

    /// <summary>
    /// Drops every cached text measurement in the view. Each one - the header action widths, the counts
    /// line, the expansion labels, the card captions - was measured with the Look's type face, so after a
    /// theme with a different face the advance widths no longer match what is drawn: the chrome drifts
    /// off the card edge and captions wrap or ellipsise against the previous font.
    /// </summary>
    private void InvalidateMeasurements()
    {
        actionWidths = [];
        actionWidthsScale = -1f;

        countedOwned = -1;
        countedTotal = -1;
        countedWanted = -1;

        filteredOwned = -1;
        filteredShown = -1;

        expansionLabels = [];
        expansionWidths = [];
        expansionCounts = new int[0, 2];

        Array.Fill(ownershipCounts, -1);

        // Static: the caption cache is shared by every view instance (and by the previews).
        Captions.Clear();
    }

    private int filteredOwned = -1;

    private int filteredShown = -1;

    private string filterText = string.Empty;

    private int countedOwned = -1;
    private int countedTotal = -1;
    private string countText = string.Empty;
    private string percentText = string.Empty;
    private string remainingText = string.Empty;

    private int countedWanted = -1;
    private string wantedText = string.Empty;

    /// <summary>
    /// Draws the whole body from <paramref name="state"/>, and reports clicks back into it.
    /// </summary>
    /// <remarks>
    /// <see cref="MountViewState.RideRequested"/>, <see cref="MountViewState.LinkRequested"/>,
    /// <see cref="MountViewState.GuideRequested"/> and <see cref="MountViewState.ToggleWantedId"/>
    /// are cleared at the start of every draw, so they are true (or set) for exactly one frame after
    /// a click. The caller must read them after this call, never cache them.
    /// </remarks>
    /// <summary>
    /// Grants a hover's worth of room around the current window's clip rectangle.
    /// </summary>
    /// <remarks>
    /// Glasswork scales a hovered control up around its centre (<c>Look.HoverLift</c>), so a control whose
    /// edge coincides with a clip rectangle - a combo filling a column, a right-aligned header action, an
    /// action row at the bottom of its footer - paints a few pixels past it and loses its cap. The caller
    /// pairs this with <see cref="PopControlRoom"/> around the controls it protects. Nothing moves; the
    /// clip is simply allowed to be a little larger than the window.
    /// </remarks>
    private static void PushControlRoom()
    {
        var pad = 12f * Frame.Scale;

        if (pad <= 0f)
        {
            return;
        }

        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        roomPushes++;

        ImGui.GetWindowDrawList().PushClipRect(min - new Vector2(pad, pad), max + new Vector2(pad, pad), false);
    }

    /// <summary>Closes the clip <see cref="PushControlRoom"/> opened, if it opened one.</summary>
    private static void PopControlRoom()
    {
        if (roomPushes <= 0)
        {
            return;
        }

        roomPushes--;
        ImGui.GetWindowDrawList().PopClipRect();
    }

    public void Draw(MountViewState state)
    {
        // Every cached measurement below is taken with the Look's type face (T6/T7), and a theme switch
        // can swap that face under us. The window bumps the generation whenever it applies a Look, so
        // the caches are dropped here, once, instead of being trusted across a theme change.
        if (state.MeasurementGeneration != measuredGeneration)
        {
            measuredGeneration = state.MeasurementGeneration;
            InvalidateMeasurements();
        }

        // One list, kept next to the properties it resets: the window reads these after Draw returns.
        state.ClearRequests();

        if (Gw.BeginCard())
        {
            DrawHeader(state);

            Gw.Divider();

            DrawColumns(state);
        }

        Gw.EndCard();

        state.Committed = Gw.Committed;
    }

    private void DrawColumns(MountViewState state)
    {
        var available = ImGui.GetContentRegionAvail();
        var gap = Frame.Look.SpaceLarge * Frame.Scale;
        // Gw.EndCard() reserves a bottom inset of its own (measured 16 logical px at scale 1);
        // sizing the columns to the raw available height overruns the window by that much and gives
        // the window a scrollbar.
        var height = MathF.Max(available.Y - (CardBottomInset * Frame.Scale), 1f);
        var leftWidth = MathF.Max((available.X * LeftColumnShare) - gap, 1f);

        DrawFilterColumn(state, leftWidth, height);

        ImGui.SameLine(0f, gap);

        DrawDetailColumn(state, height, MathF.Max(available.X - (leftWidth + gap), 1f));
    }

    /// <summary>
    /// Tints the stock ImGui scrollbar from the Look. Glasswork only styles scrollbars inside its own
    /// table and surface components, so a plain child keeps ImGui's default grey bar; the colours here
    /// are still Look tokens (N9), just pushed where the library has no opinion.
    /// </summary>
    private static void PushScrollbarInk()
    {
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Frame.Look.Background.WithAlpha(0f).ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Frame.Look.Accent.WithAlpha(0.28f).ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Frame.Look.Accent.WithAlpha(0.48f).ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Frame.Look.Accent.WithAlpha(0.68f).ToVector4());
    }

    private static void PopScrollbarInk() => ImGui.PopStyleColor(4);

    /// <summary>
    /// The position of <see cref="MountViewState.SelectedId"/> in the drawn list, or -1 when nothing
    /// is selected or the selection was filtered out of the list.
    /// </summary>
    private static int SelectedIndex(MountViewState state)
    {
        if (state.SelectedId is not { } id)
        {
            return -1;
        }

        for (var index = 0; index < state.Items.Count; index++)
        {
            if (state.Items[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>A caption wrapped to at most two lines, with each line's measured width.</summary>
    private readonly record struct Caption(string First, string Second, float FirstWidth, float SecondWidth);

    /// <summary>
    /// How many rows the four action buttons need at this width: one when all four fit, two when
    /// the original three fit but the wanted button has to move to its own line, four when even the
    /// three have to stack.
    /// </summary>
    /// <remarks>
    /// The budgets are deliberate constants, not label arithmetic: a <c>Gw.Button</c> is wider than
    /// its label (Look.SpaceLarge padding on each side plus the gutter), so a measured sum came out
    /// too optimistic. 360 logical px is what Ride / Link whistle / Mount Guide need together; the
    /// shorter Want/Unwant button adds its own width and gutter, taking the single-row budget to 470.
    /// The two-row band between them keeps the actions in the fixed footer - only the real
    /// one-per-row fallback below 360 has to move them into the scrolling body.
    /// </remarks>
    private static int ActionRowCount(float available)
    {
        var scale = Frame.Scale;
        if (available >= (470f * scale))
        {
            return 1;
        }

        return available >= (360f * scale) ? 2 : 4;
    }

    private string RefreshFilterCounts(MountViewState state)
    {
        var owned = 0;
        foreach (MountShelfItem item in state.Items)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        if (owned == filteredOwned && state.Items.Count == filteredShown)
        {
            return filterText;
        }

        filteredOwned = owned;
        filteredShown = state.Items.Count;
        filterText = string.Concat(
            FormatInt(owned),
            " of ",
            FormatInt(state.Items.Count),
            " shown collected (",
            FormatInt(MountProgress.Percent(owned, state.Items.Count)),
            "%)");
        return filterText;
    }

    private static MountShelfItem? Selected(MountViewState state)
    {
        var index = SelectedIndex(state);
        return index < 0 ? null : state.Items[index];
    }

    private static nint TextureFor(MountViewState state, uint id) =>
        state.Textures.TryGetValue(id, out var handle) ? handle : (nint)0;

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static float Fraction(int owned, int total) =>
        total <= 0 ? 0f : Math.Clamp((float)owned / total, 0f, 1f);
}
