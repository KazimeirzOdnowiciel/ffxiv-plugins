using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>
/// The wanted board: the mounts the player starred and does not own, one row each, with where they come
/// from and how far the grind has got.
/// </summary>
/// <remarks>
/// Split from its window for the same reason as the shelf: the headless preview can draw it against a
/// golden (T6), and the view never reads a service, the config or the clock (T7). Filtering and ordering
/// are <see cref="WantedBoard.Build"/>'s job; this class only draws.
/// </remarks>
public sealed class WantedBoardView
{
    /// <summary>
    /// The two sort labels, read once: the interface language is fixed for the session (<see cref="Loc.Use"/>
    /// runs in the plugin's constructor), so there is nothing to invalidate and the draw path allocates none.
    /// </summary>
    private static string[]? sortLabels;

    private static string[] SortLabels => sortLabels ??=
    [
        Loc.T("board.sort.patch"),
        Loc.T("board.sort.price"),
    ];

    public void Draw(BoardViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // The body the window gave us, read before the card opens: a card is content-sized, so a board of
        // two rows would leave the bottom half of the window as bare glass. The filler at the end of the
        // rows stretches the panel to this height, and taking it from the window (not from the card's own
        // auto-sized region) is what makes the card the same height on every frame.
        var body = ImGui.GetContentRegionAvail().Y;

        if (Gw.BeginCard())
        {
            Gw.Heading(Loc.T("window.board"));
            DrawSort(state);
            DrawRows(state, body);
        }

        Gw.EndCard();
    }

    /// <summary>
    /// The order control, on the line under the window's title. A segmented control rather than a table
    /// header: the board has two orders and no columns, and a header would promise sorting on every line.
    /// </summary>
    private static void DrawSort(BoardViewState state)
    {
        Gw.Text(Loc.T("board.sort"), TextRole.Footnote);
        ImGui.SameLine(0f, Frame.Look.SpaceSmall * Frame.Scale);
        ImGui.SetNextItemWidth(-1f);

        var sort = (int)state.Sort;

        if (Gw.Segmented(ref sort, SortLabels))
        {
            state.Sort = (MountBoardSort)sort;
        }

        Gw.Divider();
    }

    private static void DrawRows(BoardViewState state, float body)
    {
        if (state.Rows.Count == 0)
        {
            Gw.Text(Loc.T("board.empty"), TextRole.Footnote);
        }
        else
        {
            foreach (BoardRow row in state.Rows)
            {
                DrawRow(row, state.NowUnix);
            }
        }

        // One filler item stretches the panel to the body the window gave it, like the shelf's grid does. The
        // card's padding is taken off both ends, and what the content already used is measured from the card
        // child's own cursor, so the filler is the same height on every frame rather than a pixel off.
        var padding = Frame.Look.SpaceLarge * 2f * Frame.Scale;
        var rest = body - padding - ImGui.GetCursorPosY();

        if (rest > 0f)
        {
            ImGui.Dummy(new Vector2(1f, rest));
        }
    }

    private static void DrawRow(BoardRow row, long now)
    {
        Gw.Text(row.Name, TextRole.Subheadline);

        // The first source line is the one line a to-do row can carry: it is what the player has to go and
        // do, and the full list is one click away in the shelf.
        if (row.Source.Length > 0)
        {
            var width = ImGui.GetContentRegionAvail().X;
            Gw.Text(Fit(row.Source, width, TextRole.Footnote), TextRole.Footnote);
        }

        Gw.Text(WantedBoard.ProgressText(row), TextRole.Caption);

        // A bar under "no token price recorded" would be a bar for nothing: it only draws when there is a
        // target to fill it against.
        if (row.Want > 0)
        {
            Gw.ProgressBar(row.Fraction, 0f, Frame.Look.Accent);

            // Only while hovered: the label goes through the string table, and it was built for every row of
            // the board on every frame whether the pointer was anywhere near it or not.
            if (ImGui.IsItemHovered())
            {
                Gw.TooltipFor(true, MountGrind.Label(row.Have, row.Want));
            }
        }

        Gw.Text(Facts(row, now), TextRole.Caption);
        Gw.Divider();
    }

    /// <summary>The row's quiet line: the patch, the cheapest known price, and when the count last moved.</summary>
    private static string Facts(BoardRow row, long now) =>
        Loc.F(
            "board.row.facts",
            WantedBoard.PatchText(row),
            WantedBoard.PriceText(row),
            MountGrind.Updated(row.UpdatedAt, now));

    /// <summary>
    /// Trims <paramref name="text"/> with an ellipsis so it fits <paramref name="width"/>, measured with
    /// Glasswork's own text paint (ImGui's measurement is narrower and cut text just before the glyph).
    /// </summary>
    /// <remarks>
    /// The rule is <see cref="TextFit.Fit"/>, not a copy: this file and <see cref="ConfigView"/> held two
    /// byte-identical versions of it. ASCII dots are passed in because the headless preview's atlas has no
    /// U+2026, so the golden would show a "?" where the game shows an ellipsis.
    /// </remarks>
    /// <param name="text">The string to fit.</param>
    /// <param name="width">The pixel width it has to fit in.</param>
    /// <param name="role">The role it draws in, which is also the role it is measured in.</param>
    private static string Fit(string text, float width, TextRole role)
    {
        Look look = Frame.Look;
        var scale = Frame.Scale;

        return TextFit.Fit(text, width, candidate => TextPaint.Measure(candidate, role, in look, scale).X, EllipsisDots);
    }

    /// <summary>What a trimmed line ends in here: ASCII, because the headless preview's atlas has no U+2026.</summary>
    private const string EllipsisDots = "...";
}
