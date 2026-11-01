using System;
using System.Collections.Generic;
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
        Gw.Heading(Loc.T("detail.none.title"));

        ImGui.SetCursorScreenPos(new Vector2(left, top + (look.Type.LineHeight(TextRole.Title) * scale) + (look.SpaceMedium * scale)));
        Gw.Text(Loc.T("detail.none.hint"), TextRole.Footnote);
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

        // Only the one-per-row fallback lives in the scrolling body - the case where even the three older
        // actions do not fit the footer together. The one- and two-row layouts keep the actions in a fixed
        // footer: hiding the primary action below a scroll is worse than shrinking the body by a row.
        var stacked = actionRows > 2;

        // One row keeps a fixed, always-visible footer. When the buttons really stack there is no
        // honest way to reserve their height (a Gw.Button is taller than ImGui's frame metrics, and
        // the last one kept being clipped), so they are drawn at the end of the scrolling body
        // instead: always reachable, never cut in half.
        // Even when the whole row must stack, Ride keeps a fixed footer: the primary action hidden
        // below the fold was the worst part of the narrow layout. Only the secondary actions scroll.
        var footerRows = stacked ? 1 : actionRows;

        // A Gw.Button is taller than Look.ControlHeight: Gw reserves its item as
        // Max(drawn height, Look.MinHit), so at the default metrics the row is 44 px while 34 px was
        // reserved. Pairing that 34 with a SpaceSmall fudge left the row two pixels short, which the column
        // reported as scrollMax.Y 2 in the live window - invisible until a scrollbar appears under a footer
        // that has nothing to scroll. The line is reserved only when there IS a reason: an owned mount with
        // live actions draws nothing there, and paying 20-27 px of body for an empty strip pushed the first
        // "How to obtain" row out of view (main.png showed it sliced by the footer).
        var reasonLine = ActionReason(state, item) is null ? 0f : ImGui.GetTextLineHeight();
        var rowHeight = MathF.Max(Frame.Look.ControlHeight, Frame.Look.MinHit) * Frame.Scale;
        var footer = LayoutMeasure.DetailFooter(rowHeight, footerRows, style.ItemSpacing.Y, reasonLine);
        var bodyHeight = LayoutMeasure.BodyHeight(ImGui.GetContentRegionAvail().Y, footer);

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
                    Gw.Text(FitRole(stackedReason, paneWidth, Frame.Scale, TextRole.Footnote), TextRole.Footnote);
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

    /// <summary>
    /// The two-column header: the portrait in a ~40% column with the title, the rarity line, the state badges
    /// and the sheet facts to its right.
    /// </summary>
    /// <remarks>
    /// A two-column header was tried once and reverted, and the reason is worth keeping: that attempt put the
    /// 168px hero beside a title column that had 46px left, so the pane read as a portrait with a sliver of
    /// text and a band of dead space beside it. The form that works is the inverse - a 96-112px portrait
    /// beside a column that keeps at least 170px - so <see cref="LayoutMeasure.DetailHeader"/> gates it on the
    /// pane width and this method can assume the column is there.
    /// <para>
    /// The sheet-only facts (patch, seats, barding, can fly) leave the badge row for one fitted footnote line:
    /// they are the same for every mount of their kind and nobody picks a mount by them, while a badge row
    /// costs the width the title needs. The state and community badges stay badges, because they change.
    /// </para>
    /// </remarks>
    private static void DrawDetailHeaderColumns(MountViewState state, MountShelfItem item, float artWidth)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;
        var plate = look.SpaceSmall * scale;
        var gutter = look.SpaceLarge * scale;
        var gap = ImGui.GetStyle().ItemSpacing.Y;

        var origin = ImGui.GetCursorScreenPos();
        var paneWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        var hero = HeroFit(state, item, artWidth, artWidth);
        var boxPx = artWidth * scale;

        // The mat sits inside the pane's left edge, so the image starts one small space in and is centred in
        // the art box; the text column starts after the mat and one large space of gutter.
        var artTop = new Vector2(
            origin.X + plate + MathF.Max((boxPx - (hero.X * scale)) * 0.5f, 0f),
            origin.Y + plate);

        var artBottom = DrawHeroArt(state, item, artTop, hero);
        var columnX = origin.X + (plate * 2f) + boxPx + gutter;
        var columnWidth = MathF.Max((origin.X + paneWidth) - columnX, 1f);

        ImGui.SetCursorScreenPos(new Vector2(columnX, artTop.Y));
        DrawTitle(item);

        var bottom = ImGui.GetItemRectMax().Y;
        var next = bottom + gap;

        ImGui.SetCursorScreenPos(new Vector2(columnX, next));

        if (DrawRarityLine(state, item))
        {
            bottom = ImGui.GetItemRectMax().Y;
            next = bottom + gap;
        }

        bottom = DrawColumnBadges(
            DetailBadges(state, item, sheetFacts: false),
            columnX,
            next,
            columnWidth,
            look.SpaceSmall * scale);

        var facts = SheetFactLine(item);

        if (facts.Length > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(columnX, bottom + gap));
            Gw.Text(FitRole(facts, columnWidth, scale, TextRole.Footnote), TextRole.Footnote);
            bottom = ImGui.GetItemRectMax().Y;
        }

        // Whichever column is taller decides where the rest of the pane starts: the artwork when the title is
        // short, the text when the badges wrap.
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(artBottom, bottom) + gap));
    }

    /// <summary>
    /// The badges placed a row at a time inside a rectangle, returning the bottom of the last row. The stacked
    /// header lets ImGui's line flow wrap the row (<see cref="DrawBadgeRow"/>); here the row lives beside the
    /// artwork, where that flow would put a wrapped badge under the portrait at the window's left margin.
    /// </summary>
    private static float DrawColumnBadges(
        DetailBadge[] badges,
        float x,
        float y,
        float width,
        float spacing)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;
        var rowTop = y;
        var rowBottom = y;
        var cursorX = x;

        for (var index = 0; index < badges.Length; index++)
        {
            if (badges[index].Text is not { } label)
            {
                continue;
            }

            var badgeWidth = BadgeWidth(label, look, scale);

            // The first badge always starts the row, however narrow the column is: a badge that is wider
            // than the column has to overflow rather than loop back onto an empty line of its own.
            if (cursorX > x && cursorX + badgeWidth > x + width)
            {
                cursorX = x;
                rowTop = rowBottom + spacing;
                rowBottom = rowTop;
            }

            ImGui.SetCursorScreenPos(new Vector2(cursorX, rowTop));
            DrawBadge(badges[index]);
            rowBottom = MathF.Max(rowBottom, ImGui.GetItemRectMax().Y);
            cursorX += badgeWidth + spacing;
        }

        return rowBottom;
    }

    /// <summary>
    /// The mount's name at the cursor, fitted to the width left on the line. In the stacked header that width is
    /// the pane - at the narrow window size a long name ran past the edge, which the 1040px golden could never
    /// show - and in the two-column header it is the title column, where the same call is what stops a long
    /// name from being clipped by the artwork beside it.
    /// </summary>
    private static void DrawTitle(MountShelfItem item)
    {
        var titleWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        Gw.Text(FitRole(item.Name, titleWidth, Frame.Scale, TextRole.Title), TextRole.Title);
    }

    /// <summary>
    /// The one number a collector cannot get in game, on the line at the cursor: an unrecorded rarity draws
    /// nothing rather than a placeholder line, and the return value says whether anything was drawn so a
    /// caller tracking its own vertical position does not read a stale item rect.
    /// </summary>
    private static bool DrawRarityLine(MountViewState state, MountShelfItem item)
    {
        var rarity = MountRarity.Describe(item.Community?.OwnedPercent);

        if (rarity.Length == 0)
        {
            return false;
        }

        // The full sentence does not fit the narrow pane (measured: it ran past the card and was
        // cut mid-word in the narrow golden), so the clause is dropped before the number is
        // ellipsised, and the fitted result is only a last resort.
        var scale = Frame.Scale;
        var paneWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);

        if (TextWidth(rarity, TextRole.Footnote, scale) > paneWidth)
        {
            rarity = MountRarity.DescribeCompact(item.Community!.OwnedPercent);
        }

        Gw.Text(FitRole(rarity, paneWidth, scale, TextRole.Footnote), TextRole.Footnote);
        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("detail.rarity.tooltip"));
        return true;
    }

    /// <summary>
    /// The artwork fitted inside a box: at the texture's own aspect ratio, never stretched and never cropped,
    /// with a square assumed while the window has not resolved the texture yet.
    /// </summary>
    private static Vector2 HeroFit(MountViewState state, MountShelfItem item, float boxWidth, float boxHeight)
    {
        var source = state.TextureSizes.TryGetValue(item.ArtId, out Vector2 size) && size.X > 0f && size.Y > 0f
            ? size
            : new Vector2(1f, 1f);

        var aspect = source.X / source.Y;
        var width = boxWidth;
        var height = width / aspect;

        if (height > boxHeight)
        {
            height = boxHeight;
            width = height * aspect;
        }

        return new Vector2(width, height);
    }

    /// <summary>
    /// Draws the artwork at <paramref name="topLeft"/> and returns the bottom of its mat. The mat is the Look's
    /// own raised surface at reduced alpha: without it a portrait floats on the card surface and the pane has
    /// no frame of reference.
    /// </summary>
    /// <param name="topLeft">The image's top-left corner, in screen pixels; the mat extends one small space outside it.</param>
    /// <param name="size">The image's drawn size, in logical units.</param>
    private static float DrawHeroArt(MountViewState state, MountShelfItem item, Vector2 topLeft, Vector2 size)
    {
        var scale = Frame.Scale;
        var plate = Frame.Look.SpaceSmall * scale;
        var drawn = size * scale;

        ImGui.GetWindowDrawList().AddRectFilled(
            topLeft - new Vector2(plate, plate),
            topLeft + drawn + new Vector2(plate, plate),
            // Frame.Look, not the Theme facade: Theme is bound by the windows, so in the headless
            // preview it is unbound and the plate vanished (the golden hash did not even change).
            ImGui.GetColorU32(PlateColor(Frame.Look.TertiaryBackground)),
            (Frame.Look.RadiusMedium * scale) + plate);

        ImGui.SetCursorScreenPos(topLeft);
        Gw.Image(TextureFor(state, item.ArtId), size, radius: Frame.Look.RadiusMedium);

        return topLeft.Y + drawn.Y + plate;
    }

    /// <summary>
    /// The share of the detail pane the artwork column takes, before it is clamped to the art bounds.
    /// Together with <see cref="HeaderChrome"/> it puts the column at roughly 40% of the pane.
    /// </summary>
    private const float HeaderArtShare = 0.32f;

    /// <summary>The artwork box's width bounds in the two-column header, in logical units.</summary>
    private const float HeaderArtMin = 96f;

    /// <summary>
    /// The largest the artwork box grows, in logical units. Past this the portrait starts taking room the title
    /// column needs on a pane that is already giving it 40%, which is the trade this header exists to avoid.
    /// </summary>
    private const float HeaderArtMax = 112f;

    /// <summary>
    /// The narrowest title column the two-column header may draw into, in logical units. Below it the header
    /// reads as a portrait with a sliver of text beside it, which is exactly the shape the reverted attempt
    /// had; <see cref="LayoutMeasure.DetailTwoColumnMinWidth"/> is the pane width that guarantees it, with room
    /// left over for the longest names.
    /// </summary>
    private const float TitleColumnMin = 170f;

    /// <summary>
    /// What the artwork column takes beyond the image: a small space of mat on each side and one large space of
    /// gutter before the text column. Logical units, so it is added to logical widths and scaled once.
    /// </summary>
    private static float HeaderChrome => (Frame.Look.SpaceSmall * 2f) + Frame.Look.SpaceLarge;

    /// <summary>
    /// The largest share of the body the hero portrait may take. Above it the first "How to obtain" row lands
    /// under the fixed footer, which is the one row the pane exists to show.
    /// </summary>
    private const float HeroShare = 0.35f;

    /// <summary>
    /// Below this body height the portrait is not drawn at all: at the minimum window size the pane cannot
    /// hold a portrait, a title, two badge rows and one source row, and the picture is the only element there
    /// that carries no information.
    /// </summary>
    private const float HeroMinBody = 340f;

    private static void DrawDetailBody(MountViewState state, MountShelfItem item)
    {
        var width = MathF.Max(ImGui.GetContentRegionAvail().X / Frame.Scale, 1f);

        // The body height is honest: the child is sized from the window's own geometry
        // (LayoutMeasure.ColumnHeight). Before that fix this region reported 661-715 inside a 660-tall window,
        // so a cap taken from it could never bite.
        var bodyAvailHeight = MathF.Max(ImGui.GetContentRegionAvail().Y / Frame.Scale, 1f);
        var showHero = bodyAvailHeight >= HeroMinBody;

        // The header has two forms and the pane's width picks between them. Stacked is the original: a
        // full-width portrait band with the title, the badges and the rarity line under it. Two-column puts
        // a small portrait in a ~40% column with that same block beside it, which is where the body comes
        // from - roughly 90px of the scroll, i.e. the first "How to obtain" row at the default window size.
        // The measured title is pixels and the rule's arguments are logical, so it is divided here: the one
        // place the two unit systems meet in this file.
        var header = LayoutMeasure.DetailHeader(
            width,
            HeaderArtShare,
            HeaderArtMin,
            HeaderArtMax,
            HeaderChrome,
            TitleColumnMin,
            TextWidth(item.Name, TextRole.Title, Frame.Scale) / Frame.Scale);

        if (showHero && header.TwoColumn)
        {
            DrawDetailHeaderColumns(state, item, header.ArtWidth);
        }
        else
        {
            if (showHero)
            {
                // The artwork is portrait (roughly square): fitted inside the hero box at its own aspect and
                // centred, never stretched to the pane width and never cropped to a banner slice. The leftover
                // width stays card surface, which reads as a portrait rather than as a broken image.
                var hero = HeroFit(state, item, width, MathF.Min(HeroHeight, bodyAvailHeight * HeroShare));
                var heroPx = hero * Frame.Scale;
                var heroBoxPx = width * Frame.Scale;

                // Gw.Image takes logical units, but the cursor, the mat rect on the draw list and the
                // centring offset are all in pixels. Mixing the two only looked right at 100% UI scale:
                // at 150% the mat stayed at its logical size while the portrait grew half again, and the
                // pair drifted left.
                ImGui.SetCursorScreenPos(
                    ImGui.GetCursorScreenPos() + new Vector2(MathF.Max((heroBoxPx - heroPx.X) * 0.5f, 0f), 0f));

                DrawHeroArt(state, item, ImGui.GetCursorScreenPos(), hero);
            }

            DrawTitle(item);
            DrawBadges(state, item);
            DrawRarityLine(state, item);
        }

        // The price is the one fact a player asks the market for, not the book: the request costs a
        // network round trip, so it is a button and never an automatic fetch on selection.
        // The wiki link shares that line when both fit: measured, not assumed, because the pair used to clip
        // to "Ch" in the narrow pane and the fix at the time was to stack them, which cost a whole row for a
        // 360px pane that now has room.
        var priceLabel = state.MarketCheckInFlight ? Loc.T("detail.price.checking") : Loc.T("detail.price.check");
        var wikiLabel = Loc.T("detail.wiki.open");
        var priceWidth = SmallButtonWidth(priceLabel, Frame.Look, Frame.Scale);
        var wikiWidth = SmallButtonWidth(wikiLabel, Frame.Look, Frame.Scale);
        var actionGap = Frame.Look.SpaceSmall * Frame.Scale;
        var paneAvail = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        var wikiBesidePrice = state.MarketCanCheck && (priceWidth + actionGap + wikiWidth) <= paneAvail;

        if (state.MarketCanCheck)
        {
            var pressed = state.MarketCheckInFlight
                ? Gw.Button(Loc.T("detail.price.checking"), ButtonStyle.Plain, ButtonSize.Small)
                : Gw.Button(Loc.T("detail.price.check"), ButtonStyle.Plain, ButtonSize.Small);

            if (pressed)
            {
                state.MarketCheckRequested = true;
            }

            // One tooltip, not two on the same item: the price when there is one, the explanation of what the
            // button asks for when there is not. Two TooltipFor calls on one item drew both boxes at once
            // whenever a price was known - and the second replaced the first, so the explanation was dead text.
            Gw.TooltipFor(
                ImGui.IsItemHovered(),
                state.MarketPriceText.Length > 0 ? state.MarketPriceText : Loc.T("detail.price.tooltip"));

            if (wikiBesidePrice)
            {
                ImGui.SameLine(0f, actionGap);
                DrawWikiButton(state);
            }
        }

        if (state.MarketPriceText.Length > 0)
        {
            var marketWidth = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
            Gw.Text(FitRole(state.MarketPriceText, marketWidth, Frame.Scale, TextRole.Footnote), TextRole.Footnote);
        }

        // Under the badges, above the scroll: the book explains how to start, the wiki explains the whole
        // fight, and a scrolling pane would hide the link right where the source text is. Drawn before the
        // watch so it stays beside the price button whose row it could not fit on.
        if (!wikiBesidePrice)
        {
            DrawWikiButton(state);
        }

        // The watch is about a price, so it goes under the price line - and only for a mount the market can
        // answer for: a watch on an untradeable whistle would be a poll that can never find a listing. Its own
        // row, not the price row: three small buttons up there left the wiki link wrapping in one scene and
        // not in the other.
        if (state.MarketCanCheck)
        {
            DrawWatch(state, LayoutMeasure.WatchRowFits(width));
        }

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

        DrawBadgeRow(DetailBadges(state, item, sheetFacts: true), spacing, paneRight);
    }

    /// <summary>
    /// The detail pane's badges in draw order, written into <see cref="BadgeBuffer"/>. <paramref name="sheetFacts"/>
    /// false is the two-column header, where the sheet-only pills move onto <see cref="SheetFactLine"/>; the
    /// entries stay in the array - and in their own slots - so the order cannot drift between the two forms. A
    /// shared buffer rather than a fresh array: this runs every frame, and nine tuples of a string and a colour
    /// is nine allocations a frame for values that are already interned strings.
    /// </summary>
    private static DetailBadge[] DetailBadges(MountViewState state, MountShelfItem item, bool sheetFacts)
    {
        // The availability class leads the row: it is the answer to "can I get this", and it is the only badge
        // whose colour carries a meaning (the four unobtainable classes are four different tones).
        BadgeBuffer[0] = new DetailBadge(
            UiText.Availability(item.Availability),
            AvailabilityTint(item.Availability),
            AvailabilityHint(state, item.Availability));

        // A null patch is unrecorded, like every other flag: a "Patch unknown" pill is a placeholder,
        // and on the locked golden it was the badge that pushed "Flies" onto a line of its own.
        BadgeBuffer[1] = new DetailBadge(sheetFacts && !string.IsNullOrEmpty(item.Patch) ? item.Patch : null);
        BadgeBuffer[2] = new DetailBadge(sheetFacts ? SeatsText(item.Seats) : null);
        // "Unowned", not "Not collected": the chip has to read the same as the ownership filter and
        // the table column, or the same fact looks like two different things in one window.
        BadgeBuffer[3] = new DetailBadge(item.Owned ? Loc.T("badge.collected") : Loc.T("badge.unowned"));
        // Only a recorded "yes" earns a badge: a null flag is unrecorded, and a "No tradeable"
        // badge would be noise on the 200-odd mounts that simply cannot be sold.
        BadgeBuffer[4] = new DetailBadge(item.Community?.Tradeable == true ? Loc.T("badge.tradeable") : null);
        BadgeBuffer[5] = new DetailBadge(item.Community?.CustomMusic == true ? Loc.T("badge.ownMusic") : null);
        // Barding is a sheet fact, not a dataset one: a chocobo's gear slots are recorded, so a
        // missing badge here really means "this mount wears nothing".
        BadgeBuffer[6] = new DetailBadge(sheetFacts && item.Barding ? Loc.T("badge.barding") : null);
        // A sheet flag, like Barding: absent really means "cannot fly", which is the useful
        // fact when a mount is the last thing between you and a hunt mark.
        BadgeBuffer[7] = new DetailBadge(sheetFacts && item.Flies ? Loc.T("badge.flies") : null);
        // The wanted state belongs in the header too: it is the one piece of the mount's state the player
        // set themselves, and the card marker is easy to miss at a glance.
        BadgeBuffer[8] = new DetailBadge(
            state.WantedIds.Contains(item.Id) ? Loc.T("badge.wanted") : null,
            Frame.Look.Accent);
        BadgeBuffer[9] = new DetailBadge(
            state.RecentIds.Contains(item.Id) ? Loc.T("badge.new") : null,
            Frame.Look.Accent);

        return BadgeBuffer;
    }

    /// <summary>One slot per badge <see cref="DetailBadges"/> writes, refilled in place every frame.</summary>
    private static readonly DetailBadge[] BadgeBuffer = new DetailBadge[10];

    /// <summary>
    /// One badge: its label, the colour it is drawn in when it has one, and the tooltip it explains itself with.
    /// </summary>
    /// <param name="Text">The label, or null for a slot this mount has nothing to say in.</param>
    /// <param name="Tint">The colour, or null for the plain badge.</param>
    /// <param name="Hint">The tooltip, or null for none.</param>
    private readonly record struct DetailBadge(string? Text, Color? Tint = null, string? Hint = null);

    /// <summary>
    /// The colour a class is drawn in: the Look's own tokens, one per tone, so the four unobtainable classes are
    /// four visibly different badges without any colour being invented here.
    /// </summary>
    /// <remarks>
    /// This is the whole of the view's share of the classification: it turns a tone into a theme colour and
    /// never decides which class a mount is in, which is what keeps a theme change from changing an answer.
    /// </remarks>
    /// <param name="availability">The class on the row.</param>
    private static Color? AvailabilityTint(MountAvailability availability) =>
        MountAvailabilityRules.ToneOf(availability) switch
        {
            MountAvailabilityTone.Positive => Frame.Look.Success,
            MountAvailabilityTone.Informative => Frame.Look.Info,
            MountAvailabilityTone.Caution => Frame.Look.Warning,
            MountAvailabilityTone.Brand => Frame.Look.Accent,
            MountAvailabilityTone.Negative => Frame.Look.Destructive,
            _ => null,
        };

    /// <summary>
    /// The badge's tooltip: what the class means and how many of the shelf's mounts share it, which is where the
    /// per-class counts are shown (the filter's entries carry them too).
    /// </summary>
    /// <param name="state">The state whose availability counts to read.</param>
    /// <param name="availability">The class on the row.</param>
    private static string AvailabilityHint(MountViewState state, MountAvailability availability)
    {
        IReadOnlyList<MountAvailabilityCount> options = state.AvailabilityOptions;
        var mounts = 0;
        var total = 0;

        for (var index = 0; index < options.Count; index++)
        {
            total += options[index].Mounts;

            if (options[index].Class == availability)
            {
                mounts = options[index].Mounts;
            }
        }

        return MountAvailabilityRules.Explain(availability, mounts, total);
    }

    /// <summary>The seat sentence per seat count: four possible values, formatted once instead of every frame.</summary>
    private static readonly System.Collections.Generic.Dictionary<int, string> SeatsBadges = [];

    /// <summary>
    /// The sheet-only facts as one line - patch, seats, barding, can fly - for the two-column header. Cached by
    /// the facts themselves because the draw path must not build strings, and two mounts of the same kind share
    /// the answer: the labels are fixed for the session, like every other static label array here.
    /// </summary>
    private static string SheetFactLine(MountShelfItem item)
    {
        var key = (Patch: item.Patch ?? string.Empty, item.Seats, item.Barding, item.Flies);

        if (SheetFactLines.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var parts = new System.Collections.Generic.List<string>(4);

        if (item.Patch is { Length: > 0 } patch)
        {
            parts.Add(patch);
        }

        if (SeatsText(item.Seats) is { } seats)
        {
            parts.Add(seats);
        }

        if (item.Barding)
        {
            parts.Add(Loc.T("badge.barding"));
        }

        if (item.Flies)
        {
            parts.Add(Loc.T("badge.flies"));
        }

        // The separator is a string-table entry, and ASCII on purpose: the headless atlas a golden renders with
        // has no middle dot, so one would draw as a placeholder in every golden of this line.
        var line = string.Join(Loc.T("detail.facts.separator"), parts);
        Remember(SheetFactLines, key, line);
        return line;
    }

    /// <summary>Draws one badge, tinted when the caller asked for it, and its tooltip when it has one.</summary>
    private static void DrawBadge(DetailBadge badge)
    {
        if (badge.Tint is { } tint)
        {
            Gw.Badge(badge.Text!, tint);
        }
        else
        {
            Gw.Badge(badge.Text!);
        }

        // A hand-rolled hit test: Gw.Badge reserves its box the way Gw.Text does, so IsItemHovered() is not a
        // reliable answer - the window-ownership half is the same rule every other hand-rolled test here uses.
        if (badge.Hint is { } hint
            && ImGui.IsWindowHovered()
            && ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
        {
            Gw.TooltipFor(true, hint);
        }
    }

    /// <summary>
    /// The label style and padding <c>Gw.Badge</c> really uses: the caption role at semibold, one small
    /// space each side, and never narrower than it is tall - a one-character badge is a circle.
    /// Measuring in the footnote role (larger) with medium padding over-estimated every badge, so the
    /// wrap check pushed a badge onto a second line while the first still had room for it.
    /// </summary>
    private static readonly TextStyle BadgeStyle = new(TextRole.Caption, FontWeight.Semibold);

    /// <summary>
    /// The width <c>Gw.Badge</c> draws a label at: the caption role at semibold, one small space of padding each
    /// side, and never narrower than its own height, so a one-character badge is a circle.
    /// </summary>
    private static float BadgeWidth(string label, Look look, float scale)
        => MathF.Max(
            TextPaint.LineHeight(BadgeStyle, in look, scale) + (look.SpaceExtraSmall * scale * 2f),
            TextWidth(label, BadgeStyle, scale) + (look.SpaceSmall * scale * 2f));

    /// <summary>
    /// Draws the badges, starting a new line when the next one would cross <paramref name="paneRight"/>.
    /// Without the wrap the last badge was cut mid-word in the narrow pane (`Not collec…` in a golden).
    /// </summary>
    private static void DrawBadgeRow(
        DetailBadge[] badges,
        float spacing,
        float paneRight)
    {
        var scale = Frame.Scale;
        var look = Frame.Look;
        var first = true;

        for (var index = 0; index < badges.Length; index++)
        {
            if (badges[index].Text is not { } label)
            {
                continue;
            }

            var width = BadgeWidth(label, look, scale);

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

            DrawBadge(badges[index]);

            first = false;
        }
    }

    /// <summary>
    /// A dim label and its value on one line, the value ellipsised only when it genuinely cannot fit. The
    /// label is drawn first and the remaining width is measured from there, so the pair can never overflow the
    /// pane; text is never wrapped, because a wrapped value silently costs the second line this exists to save.
    /// </summary>
    private static void DrawLabelledValue(ImU8String label, string value)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;
        Gw.Text(label, TextRole.Footnote);
        ImGui.SameLine(0f, look.SpaceSmall * scale);
        var width = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        Gw.Text(FitRole(value, width, scale, TextRole.Body), TextRole.Body);
    }

    private static void DrawSources(MountViewState state, MountShelfItem item)
    {
        if (Gw.BeginSection(Loc.T("detail.obtain.section")))
        {
            // The whistle is what a player actually buys or earns; the dataset carries only its id, the
            // wait layer resolves the name.
            if (!string.IsNullOrEmpty(state.ItemName))
            {
                // One line, not two: "Whistle" over its value cost 20px for a single word and this pane has
                // three label/value pairs fighting for the same space. The value is fitted, never wrapped, so
                // a long whistle name cannot push the pair onto a second line by accident.
                DrawLabelledValue(Loc.T("detail.whistle"), state.ItemName);
            }

            if (item.Sources.Count == 0)
            {
                Gw.Text(Loc.T("detail.obtain.none"), TextRole.Footnote);
            }

            // The price first: a token mount's question is "how much is left", and it belongs above the
            // line that names the shop rather than under it.
            if (state.CostText.Length > 0)
            {
                // Wrapped, not fitted: the number at the end ("8,600 to go") is the reason the line is
                // there, and a one-line Gw.Text ran off the pane edge in the sources golden.
                Gw.TextWrapped(state.CostText, TextRole.Body);
            }

            // Directly under the price, because the two answer the same question from two sides: the
            // price line counts the bags, this one counts what the player has banked on purpose.
            if (state.GrindVisible)
            {
                DrawGrind(state);
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

                // Only while hovered: this sentence goes through the string table, and it used to be formatted
                // for every source row of the selected mount on every frame.
                if (ImGui.IsItemHovered())
                {
                    Gw.TooltipFor(true, Loc.F("detail.source.tooltip", source.Detail));
                }
            }

            // The credit belongs at the end of the list it is about, not only in the settings window:
            // these lines are a volunteer project's work, and a row is read far from any About pane.
            // Drawn only when there is something to credit, so an unenriched mount does not carry a
            // footnote about data it does not have.
            if (item.Sources.Count > 0 && state.DatasetSource.Length > 0)
            {
                Gw.Text(
                    FitRole(
                        CreditLine(state.DatasetSource),
                        ImGui.GetContentRegionAvail().X,
                        Frame.Scale,
                        TextRole.Footnote),
                    TextRole.Footnote);
            }
        }

        Gw.EndSection();
    }

    /// <summary>
    /// The credit sentence for <paramref name="source"/>, formatted once per dataset rather than once a frame:
    /// the text is fixed for the session and it is one of the two formatted strings the detail pane used to
    /// rebuild on every draw.
    /// </summary>
    /// <param name="source">The site the snapshot credits, e.g. <c>ffxivcollect.com</c>.</param>
    private static string CreditLine(string source)
    {
        if (Credits.TryGetValue(source, out string? line))
        {
            return line;
        }

        line = Loc.F("detail.obtain.credit", source);
        Remember(Credits, source, line);
        return line;
    }

    /// <summary>The credit line per dataset source; one entry per install in practice.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> Credits = [];

    /// <summary>
    /// The tracker: a bar, the count that fills it, the two steppers that move the count, and the player's
    /// own note. One row of chrome, drawn only for a mount that costs tokens or already has a record.
    /// </summary>
    /// <remarks>
    /// The count is two steppers rather than a text field because banking a token is one click after a duty,
    /// and typing a number into a field loses that click to the keyboard. A fixed item width keeps the pair
    /// inside the narrowest detail pane the window allows.
    /// <para>
    /// Every control writes the state, and a change raises <see cref="MountViewState.GrindChanged"/>: the
    /// window persists the record once after the draw, so a typed note is one save rather than one per key.
    /// </para>
    /// </remarks>
    private static void DrawGrind(MountViewState state)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;

        // The bar first: the fraction answers "how far off am I" without arithmetic, and the count below
        // says exactly what it is drawn from.
        Gw.ProgressBar(MountGrind.Fraction(state.GrindHave, state.GrindWant), 0f, look.Accent);
        Gw.TooltipFor(
            ImGui.IsItemHovered(),
            MountGrind.Remaining(state.GrindHave, state.GrindWant));

        var have = state.GrindHave;
        var want = state.GrindWant;
        var gap = look.SpaceMedium * scale;

        Gw.Text(MountGrind.Label(have, want), TextRole.Footnote);

        // Two labelled steppers rather than two anonymous ones: with the ids hidden they read as the same
        // control twice, and the labels are short enough that the pair fits the narrowest detail pane.
        if (Gw.Stepper(ref have, 1, 0, MaxGrindTokens, Loc.T("grind.have")))
        {
            state.GrindChanged = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("grind.have.tooltip"));

        ImGui.SameLine(0f, gap);

        if (Gw.Stepper(ref want, 1, 0, MaxGrindTokens, Loc.T("grind.want")))
        {
            state.GrindChanged = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("grind.want.tooltip"));

        state.GrindHave = have;
        state.GrindWant = want;

        var note = state.GrindNote;
        _ = Gw.TextField(ref note, hint: Loc.T("grind.note.hint"), maxLength: MaxNoteLength);

        // The field is the truth while it is focused, or the frame that follows a keystroke would restore
        // the saved value and eat the letter. It is persisted when it is left - one config write per
        // note rather than one per key, and the note survives an alt-tab mid-word.
        state.GrindNote = note;

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            state.GrindChanged = true;
        }
    }

    /// <summary>
    /// The largest count the steppers allow. Not a game limit: a hand-edited config can hold more, and the
    /// interface must draw it rather than clamp it away, this only bounds what the arrows can reach.
    /// </summary>
    private const int MaxGrindTokens = 99_999;

    /// <summary>
    /// The price watch: the toggle, the threshold it alerts at, and the line saying what the last read found.
    /// </summary>
    /// <remarks>
    /// Opt-in, and drawn only where a price can be asked for at all. The toggle is a plain small button
    /// because switching a watch on is the same kind of decision as asking for one price; the view only
    /// reports the press, because starting a watch needs the price already on screen, which is a market read
    /// the view is not allowed to make (T7). One stepper rather than a text field, because a threshold is
    /// nudged in round numbers and typing one hands the pane to the keyboard. Every control writes the state
    /// and raises <see cref="MountViewState.WatchChanged"/>, so the window persists the record once after the
    /// draw.
    /// </remarks>
    /// <param name="state">The state to read and write.</param>
    /// <param name="shareRow">True when the pane is wide enough for the toggle and the stepper to share a row.</param>
    private static void DrawWatch(MountViewState state, bool shareRow)
    {
        var label = state.Watching ? Loc.T("detail.watch.stop") : Loc.T("detail.watch.add");

        if (Gw.Button(label, ButtonStyle.Plain, ButtonSize.Small))
        {
            state.ToggleWatchRequested = true;
        }

        Gw.TooltipFor(
            ImGui.IsItemHovered(),
            Loc.T(state.Watching ? "detail.watch.stop.tooltip" : "detail.watch.add.tooltip"));

        if (!state.Watching)
        {
            return;
        }

        if (shareRow)
        {
            ImGui.SameLine(0f, Frame.Look.SpaceSmall * Frame.Scale);
        }

        var threshold = state.WatchThreshold;

        if (Gw.Stepper(
            ref threshold,
            MountWatch.ThresholdStep,
            MountWatch.MinThreshold,
            MountWatch.MaxThreshold,
            Loc.T("detail.watch.threshold")))
        {
            state.WatchThreshold = threshold;
            state.WatchChanged = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("detail.watch.threshold.tooltip"));

        if (state.WatchText.Length > 0)
        {
            var width = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
            Gw.Text(FitRole(state.WatchText, width, Frame.Scale, TextRole.Footnote), TextRole.Footnote);
        }
    }

    /// <summary>Long enough for a sentence about where the player stands, short enough to stay one line.</summary>
    private const int MaxNoteLength = 120;

    /// <summary>
    /// The wiki link. Drawn either beside the price check (when the two fit on a line) or on its own line, so
    /// the button and its tooltip exist in exactly one place.
    /// </summary>
    private static void DrawWikiButton(MountViewState state)
    {
        if (Gw.Button(Loc.T("detail.wiki.open"), ButtonStyle.Plain, ButtonSize.Small))
        {
            state.WikiRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("detail.wiki.tooltip"));
    }

    /// <summary>
    /// The single line the footer draws under the buttons: why the first blocked action is blocked. Computed
    /// before the footer is sized so a mount with nothing blocked does not reserve the line, and shared with
    /// the drawing code so the reservation and the text can never disagree.
    /// </summary>
    private static string? ActionReason(MountViewState state, MountShelfItem item)
        => RideReason(state, item) ?? Reason(state.LinkDisabledReason) ?? Reason(state.GuideDisabledReason);

    /// <summary>The Ride button's disabled reason, or null when the mount can be summoned.</summary>
    private static string? RideReason(MountViewState state, MountShelfItem item)
        => item.Owned ? Reason(state.RideDisabledReason) : Loc.T("detail.reason.unowned");

    /// <summary>
    /// Draws the Ride button, the one action that must never hide behind a scroll, and returns why it is
    /// disabled (null when it is live). The reason is *returned*, not drawn here: text ends the ImGui row, and
    /// drawing it here made the next button start a new line inside a footer that reserved one.
    /// </summary>
    private static string? DrawRideAction(MountViewState state, MountShelfItem item)
    {
        var rideReason = RideReason(state, item);

        ImGui.BeginDisabled(rideReason is not null);

        if (Gw.Button(Loc.T("detail.ride"), ButtonStyle.Prominent))
        {
            state.RideRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("detail.ride.tooltip"));

        ImGui.EndDisabled();

        return rideReason;
    }

    /// <summary>Link whistle, Mount Guide and the wanted toggle, in the given number of rows.</summary>
    private static string? DrawSecondaryActions(MountViewState state, MountShelfItem item, int rows)
    {
        var spacing = Frame.Look.SpaceSmall * Frame.Scale;
        var linkReason = Reason(state.LinkDisabledReason);

        ImGui.BeginDisabled(linkReason is not null);

        if (Gw.Button(Loc.T("detail.linkWhistle"), ButtonStyle.Plain, ButtonSize.Small))
        {
            state.LinkRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("detail.link.tooltip"));

        ImGui.EndDisabled();

        if (rows == 1)
        {
            ImGui.SameLine(0f, spacing);
        }

        // The guide is not a 3D viewport, whatever the old note here said: its 192x192 preview is the
        // same flat artwork this pane already draws, and it was pixel-identical across four minutes in
        // the live client. What the guide still adds is the game's own list with the Ride action and
        // the favourite/roulette controls, which is why the button stays.
        var guideReason = Reason(state.GuideDisabledReason);

        ImGui.BeginDisabled(guideReason is not null);

        if (Gw.Button(Loc.T("detail.mountGuide"), ButtonStyle.Plain, ButtonSize.Small))
        {
            state.GuideRequested = true;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("detail.guide.tooltip"));

        ImGui.EndDisabled();

        // In the two-row band the second line pairs Mount Guide with the wanted toggle; with only
        // "rows == 1" here the toggle became a third line that the two-row footer could not fit, and
        // it was clipped (the wanted golden caught it).
        if (rows <= 2)
        {
            ImGui.SameLine(0f, spacing);
        }

        var wanted = state.WantedIds.Contains(item.Id);

        if (Gw.Button(wanted ? Loc.T("detail.unwant") : Loc.T("detail.want"), ButtonStyle.Plain, ButtonSize.Small))
        {
            state.ToggleWantedId = item.Id;
        }

        Gw.TooltipFor(ImGui.IsItemHovered(), wanted ? Loc.T("detail.want.remove.hint") : Loc.T("detail.want.add.hint"));

        return linkReason ?? guideReason;
    }

    /// <summary>
    /// The full action block, for the layouts that fit it in the fixed footer: "Ride" is disabled for a mount
    /// the player does not own, whatever the window says, and the three older actions are disabled while the
    /// window reports a reason. Only an enabled button reports a click, so the window never has to
    /// second-guess a request it cannot honour. "Want"/"Unwant" is always live: it only toggles the hunting
    /// list. The reason line under the row is exactly <see cref="ActionReason"/>'s answer, which is also what
    /// the footer reserves space for.
    /// </summary>
    /// <param name="rows">
    /// <see cref="ActionRowCount"/>'s answer: 1 all four share a line, 2 the three older actions share one and
    /// the wanted button drops to the next, 4 one per line.
    /// </param>
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
                Gw.Text(FitRole(reason, paneWidth, Frame.Scale, TextRole.Footnote), TextRole.Footnote);
            }
        }
        finally
        {
            PopControlRoom();
        }
    }

    /// <summary>
    /// Fits a label in the callout role, remembering the answer per width: the card caption wrap measures at the
    /// role it draws, which is what keeps a wrapped line from being cut by the tile's clip rect. A caller that
    /// draws something else passes its own role through <see cref="FitRole"/>.
    /// </summary>
    private static string Fit(string name, float width, float scale)
        => FitTo(name, width, TextRole.Callout, scale);

    /// <summary>Same fit as <see cref="Fit"/>, for a different text role (the title is larger).</summary>
    private static string FitRole(string name, float width, float scale, TextRole role)
        => FitTo(name, width, role, scale);

    /// <summary>
    /// The rule lives in <see cref="TextFit"/>; this is the cache in front of it. Fitting a title walks it a
    /// character at a time with a fresh concat per step, which is far too much to repeat every frame, and the
    /// width it was fitted to barely moves - so the answer is keyed by the floored width and reused until the
    /// pane is a whole pixel narrower or wider.
    /// </summary>
    private static string FitTo(string name, float width, TextStyle style, float scale)
    {
        if (TextWidth(name, style, scale) <= width)
        {
            return name;
        }

        var key = (name, style, (int)MathF.Round(scale * 1000f), (int)MathF.Floor(width));

        if (FittedText.TryGetValue(key, out var fitted))
        {
            return fitted;
        }

        fitted = TextFit.Fit(name, width, text => TextWidth(text, style, scale));
        Remember(FittedText, key, fitted);
        return fitted;
    }

    /// <summary>The artwork mat: the Look's raised surface at 45% of its own alpha.</summary>
    private static Vector4 PlateColor(Color color)
    {
        var faded = color.WithAlpha(color.A * 0.45f);
        return faded.ToVector4();
    }

    /// <summary>
    /// "1 seat" / "4 seats", or null when the sheet records no seat count: an "Unknown seats" pill is a
    /// placeholder rather than a fact, and it was one of the badges that wrapped the row. Formatted once per
    /// distinct count: the badge row is rebuilt every frame, and the sentence goes through the string table.
    /// </summary>
    private static string? SeatsText(int seats)
    {
        if (seats <= 0)
        {
            return null;
        }

        if (SeatsBadges.TryGetValue(seats, out string? cached))
        {
            return cached;
        }

        string text = seats == 1 ? Loc.T("badge.seats.one") : Loc.F("badge.seats.many", FormatInt(seats));
        Remember(SeatsBadges, seats, text);
        return text;
    }

    private static string? Reason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason;
}
