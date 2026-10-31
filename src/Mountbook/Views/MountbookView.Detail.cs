using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>Part of <see cref="MountbookView"/>: detail drawing.</summary>
public sealed partial class MountbookView
{
    /// <summary>
    /// The detail pane with nothing selected: a centred heading and one hint line, vertically centred
    /// in the space the card gave the column.
    /// </summary>
    private static void DrawDetailPlaceholder(float height)
    {
        var scale = Frame.Scale;
        var look = Frame.Look;
        var lineHeight = look.Type.LineHeight(TextRole.Footnote) * scale;
        var blockHeight = (look.Type.LineHeight(TextRole.Title) * scale) + lineHeight + (look.SpaceMedium * scale);
        var left = ImGui.GetCursorScreenPos().X;
        var top = ImGui.GetCursorScreenPos().Y + MathF.Max((height - blockHeight) * 0.45f, 0f);

        ImGui.SetCursorScreenPos(new Vector2(left, top));
        Gw.Heading("No mount selected"u8);

        ImGui.SetCursorScreenPos(new Vector2(left, top + (look.Type.LineHeight(TextRole.Title) * scale) + (look.SpaceMedium * scale)));
        Gw.Text("Select a card to see its details."u8, TextRole.Footnote);
    }

    private static void DrawDetailColumn(MountViewState state, float height, float width)
    {
        if (Selected(state) is not { } item)
        {
            DrawDetailPlaceholder(height);
            return;
        }

        // The whole column lives in one child, actions included. Without it, ImGui puts anything
        // drawn after the body child back on the window's next line - at the left margin under the
        // mount grid - which is exactly where the action row used to disappear to.
        if (!ImGui.BeginChild("##detailColumn"u8, new Vector2(0f, height)))
        {
            ImGui.EndChild();
            return;
        }

        // Reserve the footer from the metrics ImGui itself uses for a button row and a text line,
        // not from Look tokens: the token-based sum under-reserved the row by tens of pixels. The
        // row count matters too: at the narrow window size the buttons stack, and reserving one row
        // clipped the second.
        var style = ImGui.GetStyle();
        var actionRows = ActionRowCount(width);

        // Only the one-per-row fallback below 360 logical px lives in the scrolling body. The one-
        // and two-row layouts keep the actions in a fixed footer: hiding the primary action below a
        // scroll is worse than shrinking the body by a row.
        var stacked = actionRows > 2;

        // One row keeps a fixed, always-visible footer. When the buttons really stack there is no
        // honest way to reserve their height (a Gw.Button is taller than ImGui's frame metrics, and
        // the last one kept being clipped), so they are drawn at the end of the scrolling body
        // instead: always reachable, never cut in half.
        // Even when the whole row must stack, Ride keeps a fixed footer: the primary action hidden
        // below the fold was the worst part of the narrow layout. Only the secondary actions scroll.
        var footerRows = stacked ? 1 : actionRows;

        // A Gw.Button is taller than Look.ControlHeight, so reserving exactly ControlHeight per row left the
        // last line (the disabled-reason text) under the clip - the locked golden showed the reason missing.
        var rowHeight = (Frame.Look.ControlHeight + Frame.Look.SpaceSmall) * Frame.Scale;
        var footer = (rowHeight * footerRows)
            + (style.ItemSpacing.Y * (footerRows + 1))
            + ImGui.GetTextLineHeight();
        var bodyHeight = MathF.Max(ImGui.GetContentRegionAvail().Y - footer, 1f);

        PushScrollbarInk();

        if (ImGui.BeginChild("##detail"u8, new Vector2(0f, bodyHeight)))
        {
            DrawDetailBody(state, item);

            if (stacked)
            {
                ImGui.Dummy(new Vector2(0f, Frame.Look.SpaceMedium * Frame.Scale));
                DrawSecondaryActions(state, item, actionRows);
            }

            // Without this the body's last line was sliced in half by the clip rectangle. The fade
            // also signals that there is more text, which the plain cut never did.
            Gw.EdgeFade();
        }

        ImGui.EndChild();

        PopScrollbarInk();

        if (stacked)
        {
            // The primary action keeps its footer; its reason shares the line the footer reserved for it.
            PushControlRoom();

            try
            {
                var stackedReason = DrawRideAction(state, item);

                if (stackedReason is not null)
                {
                    var paneWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
                    Gw.Text(Fit(stackedReason, paneWidth, Frame.Scale), TextRole.Footnote);
                }
            }
            finally
            {
                PopControlRoom();
            }
        }
        else
        {
            DrawActions(state, item, actionRows);
        }

        ImGui.EndChild();
    }

    private static void DrawDetailBody(MountViewState state, MountShelfItem item)
    {
        var width = MathF.Max(ImGui.GetContentRegionAvail().X / Frame.Scale, 1f);

        // The artwork is portrait (roughly square), so it is fitted inside the hero box at its own
        // aspect and centred, never stretched to the pane width and never cropped to a banner slice.
        // The leftover width stays card surface, which reads as a portrait rather than a broken image.
        var heroBoxWidth = width;
        var heroBoxHeight = HeroHeight;
        var source = state.TextureSizes.TryGetValue(item.ArtId, out Vector2 size) && size.X > 0f && size.Y > 0f
            ? size
            : new Vector2(1f, 1f);

        var sourceAspect = source.X / source.Y;
        var heroWidth = heroBoxWidth;
        var heroHeight = heroWidth / sourceAspect;

        if (heroHeight > heroBoxHeight)
        {
            heroHeight = heroBoxHeight;
            heroWidth = heroHeight * sourceAspect;
        }

        // Gw.Image takes logical units, but the cursor, the mat rect on the draw list and the centring
        // offset are all in pixels. Mixing the two only looked right at 100% UI scale: at 150% the mat
        // stayed at its logical size while the portrait grew half again, and the pair drifted left.
        var scalePx = Frame.Scale;
        var heroPx = new Vector2(heroWidth, heroHeight) * scalePx;
        var heroBoxPx = heroBoxWidth * scalePx;

        // Artwork first, then the title block, both centred in the pane. A two-column header was
        // tried here (portrait left, name and badges beside it) and reverted: two visual reviews
        // called it unbalanced, a band of dead space beside a tall portrait, with no gain over this.
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max((heroBoxPx - heroPx.X) * 0.5f, 0f));

        // A plate behind the artwork: without it a portrait floats on the card surface and the pane
        // has no frame of reference. PanelRaised is the Look's own raised surface, at reduced alpha so
        // it reads as a mat, not as another panel.
        var platePad = Frame.Look.SpaceSmall * scalePx;
        var artAt = ImGui.GetCursorScreenPos();
        var plateRadius = (Frame.Look.RadiusMedium * scalePx) + platePad;

        ImGui.GetWindowDrawList().AddRectFilled(
            artAt - new Vector2(platePad, platePad),
            artAt + heroPx + new Vector2(platePad, platePad),
            // Frame.Look, not the Theme facade: Theme is bound by the windows, so in the headless
            // preview it is unbound and the plate vanished (the golden hash did not even change).
            ImGui.GetColorU32(PlateColor(Frame.Look.TertiaryBackground)),
            plateRadius);

        Gw.Image(TextureFor(state, item.ArtId), new Vector2(heroWidth, heroHeight), radius: Frame.Look.RadiusMedium);

        // The title is measured to the pane: at the narrow window size a long name ran past the
        // edge, which the 1040px golden could never show.
        var titleWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        Gw.Text(FitRole(item.Name, titleWidth, Frame.Scale, TextRole.Title), TextRole.Title);

        DrawBadges(state, item);

        // The one number a collector cannot get in game. Under the badges and above the wiki link,
        // because it is the first thing a rare mount is judged by; an unrecorded rarity draws nothing
        // rather than a placeholder line.
        var rarity = MountRarity.Describe(item.Community?.OwnedPercent);

        if (rarity.Length > 0)
        {
            // The full sentence does not fit the narrow pane (measured: it ran past the card and was
            // cut mid-word in the narrow golden), so the clause is dropped before the number is
            // ellipsised, and the fitted result is only a last resort.
            var look = Frame.Look;
            var scale = Frame.Scale;
            var paneWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);

            if (TextPaint.Measure(rarity, TextRole.Footnote, in look, scale).X > paneWidth)
            {
                rarity = MountRarity.DescribeCompact(item.Community!.OwnedPercent);
            }

            Gw.Text(FitRole(rarity, paneWidth, scale, TextRole.Footnote), TextRole.Footnote);
            Gw.TooltipFor(ImGui.IsItemHovered(), "Share of the ffxivcollect player base that owns this mount");
        }

        // The price is the one fact a player asks the market for, not the book: the request costs a
        // network round trip, so it is a button and never an automatic fetch on selection.
        if (state.MarketCanCheck)
        {
            // Its own line: beside the wiki button the label was clipped to "Ch" in the narrow pane,
            // and a truncated button is worse than a taller pane.
            var pressed = state.MarketCheckInFlight
                ? Gw.Button("Checking"u8, ButtonStyle.Plain, ButtonSize.Small)
                : Gw.Button("Check price"u8, ButtonStyle.Plain, ButtonSize.Small);

            if (pressed)
            {
                state.MarketCheckRequested = true;
            }

            Gw.TooltipFor(
                ImGui.IsItemHovered(),
                "Ask Universalis for the cheapest listing of this mount's whistle on your world");

            if (state.MarketPriceText.Length > 0)
            {
                Gw.TooltipFor(ImGui.IsItemHovered(), state.MarketPriceText);
            }
        }

        if (state.MarketPriceText.Length > 0)
        {
            var marketWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
            Gw.Text(FitRole(state.MarketPriceText, marketWidth, Frame.Scale, TextRole.Footnote), TextRole.Footnote);
        }

        // Under the badges, above the scroll: the book explains how to start, the wiki explains the whole
        // fight, and a scrolling pane would hide the link right where the source text is.
        if (Gw.Button("Open the wiki page"u8, ButtonStyle.Plain, ButtonSize.Small))
        {
            state.WikiRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), "Open the Gamer Escape page for this mount in a browser");

        // Obtain data before the flavour text: "where do I get it" is the question the pane exists to
        // answer, and below the portrait plus a paragraph of lore it used to start at the fold.
        DrawSources(state, item);

        var flavour = string.IsNullOrEmpty(item.EnhancedDescription) ? item.Description : item.EnhancedDescription;

        if (!string.IsNullOrEmpty(flavour))
        {
            Gw.TextWrapped(flavour, TextRole.Body);
        }

        if (!string.IsNullOrEmpty(item.Tooltip))
        {
            Gw.TextWrapped(item.Tooltip, TextRole.Footnote);
        }
    }

    private static void DrawBadges(MountViewState state, MountShelfItem item)
    {
        var spacing = Frame.Look.SpaceSmall * Frame.Scale;
        var paneRight = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;

        // The wanted state belongs in the header too: it is the one piece of the mount's state the player
        // set themselves, and the card marker is easy to miss at a glance.
        DrawBadgeRow(
            [
                (string.IsNullOrEmpty(item.Patch) ? "Patch unknown" : item.Patch, null),
                (SeatsText(item.Seats), null),
                (item.Owned ? "Collected" : "Not collected", null),
                // Only a recorded "yes" earns a badge: a null flag is unrecorded, and a "No tradeable"
                // badge would be noise on the 200-odd mounts that simply cannot be sold.
                (item.Community?.Tradeable == true ? "Tradeable" : null, null),
                (item.Community?.CustomMusic == true ? "Own music" : null, null),
                // Barding is a sheet fact, not a dataset one: a chocobo's gear slots are recorded, so a
                // missing badge here really means "this mount wears nothing".
                (item.Barding ? "Barding" : null, null),
                // A sheet flag, like Barding: absent really means "cannot fly", which is the useful
                // fact when a mount is the last thing between you and a hunt mark.
                (item.Flies ? "Flies" : null, null),
                (state.WantedIds.Contains(item.Id) ? "Wanted" : null, Frame.Look.Accent),
                (state.RecentIds.Contains(item.Id) ? "New" : null, Frame.Look.Accent),
            ],
            spacing,
            paneRight);
    }

    /// <summary>
    /// The label style and padding <c>Gw.Badge</c> really uses: the caption role at semibold, one small
    /// space each side, and never narrower than it is tall - a one-character badge is a circle.
    /// Measuring in the footnote role (larger) with medium padding over-estimated every badge, so the
    /// wrap check pushed a badge onto a second line while the first still had room for it.
    /// </summary>
    private static readonly TextStyle BadgeStyle = new(TextRole.Caption, FontWeight.Semibold);

    /// <summary>
    /// Draws the badges, starting a new line when the next one would cross <paramref name="paneRight"/>.
    /// Without the wrap the last badge was cut mid-word in the narrow pane (`Not collec…` in a golden).
    /// </summary>
    private static void DrawBadgeRow(
        (string? Text, Color? Tint)[] badges,
        float spacing,
        float paneRight)
    {
        var scale = Frame.Scale;
        var look = Frame.Look;
        var first = true;
        var badgePadding = look.SpaceSmall * scale;
        var badgeHeight = TextPaint.LineHeight(BadgeStyle, in look, scale) + (look.SpaceExtraSmall * scale * 2f);

        for (var index = 0; index < badges.Length; index++)
        {
            if (badges[index].Text is not { } label)
            {
                continue;
            }

            var width = MathF.Max(
                badgeHeight,
                TextPaint.Measure(label, BadgeStyle, in look, scale).X + (badgePadding * 2f));

            if (!first)
            {
                if (ImGui.GetItemRectMax().X + spacing + width > paneRight)
                {
                    // No SameLine: the badge starts a fresh line.
                }
                else
                {
                    ImGui.SameLine(0f, spacing);
                }
            }

            if (badges[index].Tint is { } tint)
            {
                Gw.Badge(label, tint);
            }
            else
            {
                Gw.Badge(label);
            }

            first = false;
        }
    }

    private static void DrawSources(MountViewState state, MountShelfItem item)
    {
        if (Gw.BeginSection("How to obtain"u8))
        {
            // The whistle is what a player actually buys or earns; the dataset carries only its id, the
            // wait layer resolves the name.
            if (!string.IsNullOrEmpty(state.ItemName))
            {
                // A plain label and value, not a row: the row layout right-aligns the value and truncated a
                // one-line description to "that grants?" in this narrow pane.
                Gw.Text("Whistle"u8, TextRole.Footnote);
                Gw.Text(state.ItemName, TextRole.Body);
            }

            if (item.Sources.Count == 0)
            {
                Gw.Text("No source recorded for this mount yet."u8, TextRole.Footnote);
            }

            // The price first: a token mount's question is "how much is left", and it belongs above the
            // line that names the shop rather than under it.
            if (state.CostText.Length > 0)
            {
                // Wrapped, not fitted: the number at the end ("8,600 to go") is the reason the line is
                // there, and a one-line Gw.Text ran off the pane edge in the sources golden.
                Gw.TextWrapped(state.CostText, TextRole.Body);
            }

            for (var index = 0; index < item.Sources.Count; index++)
            {
                var source = item.Sources[index];
                string? link = MountSourceLinks.Url(source);

                // A row that names a record is a row the player can go and read about; a row that is a
                // sentence stays a sentence. The chevron is the difference, so a tappable row is never
                // drawn around text with nowhere to go.
                if (link is null)
                {
                    Gw.BeginRow(source.Category, source.Detail);
                    Gw.EndRow();
                    continue;
                }

                if (Gw.Row(source.Category, source.Detail, chevron: true))
                {
                    state.SourceLinkRequested = link;
                }

                Gw.TooltipFor(
                    ImGui.IsItemHovered(),
                    string.Concat("Open the Gamer Escape page for ", source.Detail, " in a browser"));
            }
;
        }

        Gw.EndSection();
    }

    /// <summary>
    /// The action row. "Ride" is disabled for a mount the player does not own, whatever the window
    /// says; "Ride", "Link whistle" and "Mount Guide" are disabled while the window reports a
    /// reason. Only an enabled button reports a click, so the window never has to second-guess a
    /// request it cannot honour. "Want"/"Unwant" is always live: it only toggles the hunting list.
    /// </summary>
    /// <remarks>
    /// <paramref name="rows"/> is <see cref="ActionRowCount"/>'s answer: 1 all four share a line,
    /// 2 the three older actions share one and the wanted button drops to the next, 4 one per line.
    /// </remarks>
    /// <summary>
    /// Draws the Ride button, the one action that must never hide behind a scroll, and returns why it is
    /// disabled (null when it is live). The reason is *returned*, not drawn here: text ends the ImGui row, and
    /// drawing it here made the next button start a new line inside a footer that reserved one.
    /// </summary>
    private static string? DrawRideAction(MountViewState state, MountShelfItem item)
    {
        var rideReason = item.Owned ? Reason(state.RideDisabledReason) : "Not collected.";

        ImGui.BeginDisabled(rideReason is not null);

        if (Gw.Button("Ride"u8, ButtonStyle.Prominent))
        {
            state.RideRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), "Summon this mount");

        ImGui.EndDisabled();

        return rideReason;
    }

    /// <summary>Link whistle, Mount Guide and the wanted toggle, in the given number of rows.</summary>
    private static string? DrawSecondaryActions(MountViewState state, MountShelfItem item, int rows)
    {
        var spacing = Frame.Look.SpaceSmall * Frame.Scale;
        var linkReason = Reason(state.LinkDisabledReason);

        ImGui.BeginDisabled(linkReason is not null);

        if (Gw.Button("Link whistle"u8, ButtonStyle.Default))
        {
            state.LinkRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), "Post the whistle as a chat item link");

        ImGui.EndDisabled();

        if (rows == 1)
        {
            ImGui.SameLine(0f, spacing);
        }

        // The game's Mount Guide is the only place a mount is drawn as a real 3D model, so this is
        // the honest "see it in 3D" button: it hands the player to the native view.
        var guideReason = Reason(state.GuideDisabledReason);

        ImGui.BeginDisabled(guideReason is not null);

        if (Gw.Button("Mount Guide"u8, ButtonStyle.Default))
        {
            state.GuideRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), "Open the game's Mount Guide");

        ImGui.EndDisabled();

        // In the two-row band the second line pairs Mount Guide with the wanted toggle; with only
        // "rows == 1" here the toggle became a third line that the two-row footer could not fit, and
        // it was clipped (the wanted golden caught it).
        if (rows <= 2)
        {
            ImGui.SameLine(0f, spacing);
        }

        var wanted = state.WantedIds.Contains(item.Id);

        if (Gw.Button(wanted ? "Unwant"u8 : "Want"u8, ButtonStyle.Default))
        {
            state.ToggleWantedId = item.Id;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), wanted ? "Remove from your wanted list" : "Add to your wanted list");

        return linkReason ?? guideReason;
    }

    /// <summary>The full action block, for the layouts that fit it in the fixed footer.</summary>
    private static void DrawActions(MountViewState state, MountShelfItem item, int rows)
    {
        // The row sits on the column's bottom edge, where a hovered button would grow past the clip.
        PushControlRoom();

        try
        {
            var rideReason = DrawRideAction(state, item);

            if (rows <= 2)
            {
                ImGui.SameLine(0f, Frame.Look.SpaceSmall * Frame.Scale);
            }

            var blocked = DrawSecondaryActions(state, item, rows);

            // One line under the whole row, which is what the footer reserves for it.
            var reason = rideReason ?? blocked;

            if (reason is not null)
            {
                var paneWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
                Gw.Text(Fit(reason, paneWidth, Frame.Scale), TextRole.Footnote);
            }
        }
        finally
        {
            PopControlRoom();
        }
    }

    private static string Fit(string name, float width, float scale)
    {
        if (CaptionWidth(name, scale) <= width)
        {
            return name;
        }

        const string Ellipsis = "\u2026";
        int length = name.Length;
        while (length > 1 && CaptionWidth(string.Concat(name.AsSpan(0, length), Ellipsis), scale) > width)
        {
            length--;
        }

        return string.Concat(name.AsSpan(0, length).TrimEnd(), Ellipsis);
    }

    /// <summary>Same fit as <see cref="Fit"/>, for a different text role (the title is larger).</summary>
    private static string FitRole(string name, float width, float scale, TextRole role)
    {
        var look = Frame.Look;
        if (TextPaint.Measure(name, role, in look, scale).X <= width)
        {
            return name;
        }

        const string Ellipsis = "\u2026";
        int length = name.Length;
        while (length > 1
            && TextPaint.Measure(string.Concat(name.AsSpan(0, length), Ellipsis), role, in look, scale).X > width)
        {
            length--;
        }

        return string.Concat(name.AsSpan(0, length).TrimEnd(), Ellipsis);
    }

    /// <summary>The artwork mat: the Look's raised surface at 45% of its own alpha.</summary>
    private static Vector4 PlateColor(Color color)
    {
        var faded = color.WithAlpha(color.A * 0.45f);
        return faded.ToVector4();
    }

    private static string SeatsText(int seats)
    {
        if (seats <= 0)
        {
            return "Unknown seats";
        }

        return seats == 1 ? "1 seat" : string.Concat(FormatInt(seats), " seats");
    }

    private static string? Reason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason;
}
