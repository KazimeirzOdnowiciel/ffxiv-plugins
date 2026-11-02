using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The table in the order the list opens in, with no header touched: the picture a sort click must leave.
/// </summary>
/// <remarks>
/// <para>
/// This scene and <see cref="SortClickReturnedPreview"/> are one measurement in two images: the same list,
/// once untouched and once through Name, Seats, Name, and they have to render the same. A sort orders rows and
/// nothing else - the columns keep the widths their cells were fitted to, the rows keep their height and their
/// pitch, and the header keeps all but its caret - so one moved column edge between the two is a different
/// table.
/// </para>
/// <para>
/// What this pair can and cannot show is worth stating, because the fix it documents had already escaped the
/// gate once. A preview process has no saved table settings, so every table in it is fitted from this scene's
/// cells for the first time and two sort orders converge on the same split - which is why the pair is stable
/// on the buggy build too. The per-order divergence lives in the settings ImGui persists per table id, and
/// that is in the client: its ini held one seven-column entry per order with different <c>Weight=</c> values
/// for the two stretch columns (3.0000/2.0000 for most, 4.0649/0.9351 for one, 2.2277/2.7723 for another,
/// with a 243 px Patch column in one and a 229 px Price column in another), and a click that re-laid the table
/// out under the pointer was what those entries add up to. So this pair holds the fixed behaviour, and the
/// live readback is the defect's evidence - the two claims are different, and this is the one an image can
/// make.
/// </para>
/// <para>
/// Twelve mounts of four different name lengths give the fit-content columns something to disagree about, and
/// the sample's own rows keep the cells that matter - a rare share with its mark, a full price, an owned dot -
/// in the picture.
/// </para>
/// <para>
/// Nothing here reads the game, the config or the clock, so the images hold from run to run.
/// </para>
/// </remarks>
public sealed class SortClickPreview : SortClickScene
{
    /// <summary>Also the golden's file name: <c>goldens/sortclick.png</c>.</summary>
    public override string Name => "sortclick";

    /// <inheritdoc/>
    protected override bool Sorts => false;
}

/// <summary>
/// The same table after Name, Seats, Name: it has to be the same picture, caret included.
/// </summary>
/// <remarks>
/// The clicks are the report's: the name column, then the seats column, then the name column again. The first
/// one asks for the other direction of the column the list is already sorted by and the third asks for this
/// direction back, so the list ends in the order it started in, and the frame that is captured is the one
/// <see cref="SortClickPreview"/> captures. Seats in between is the click that drew a second table before the
/// fix. The pointer is parked outside the window well before the frame is captured, so no hover ink, pressed
/// scale or tooltip is left in the picture, and the caret ends where it started - on Name, pointing up - which
/// is what the click rule (<see cref="TableSort.Observe"/>) has to keep true.
/// </remarks>
public sealed class SortClickReturnedPreview : SortClickScene
{
    /// <summary>Also the golden's file name: <c>goldens/sortclick-returned.png</c>.</summary>
    public override string Name => "sortclick-returned";

    /// <inheritdoc/>
    protected override bool Sorts => true;
}

/// <summary>
/// The book, the window, and the header-click script both sort-click scenes share.
/// </summary>
/// <remarks>
/// Frame numbers, not durations: the preview runs a fixed sixtieth of a second per frame, so a click on frame
/// 11 lands on frame 11 of every run and in every process - which is what lets two images be compared at all.
/// The clicks are far enough apart for each press and release pair to be its own click, and the last
/// forty-six frames are idle so every spring in the picture - the header's hover, the caret's turn - has
/// settled before it is taken. The two scenes end on the same frame for the same reason.
/// </remarks>
public abstract class SortClickScene : PreviewScene
{
    /// <summary>The one book both scenes draw.</summary>
    private static readonly IReadOnlyList<MountShelfItem> Book = SortClickBook.Build();

    private readonly MountbookView view = new();

    private readonly MountViewState state;

    /// <summary>The order the drawn rows were built for, so a click's new order is what the next frame draws.</summary>
    private MountOrder drawnOrder;

    /// <summary>Builds the state: the whole book, the table, the name order, one row selected.</summary>
    protected SortClickScene()
    {
        state = new MountViewState
        {
            Items = Query(MountOrder.Name),
            Expansions = MountBreakdown.ByExpansion(Book),
            RarestMissing = MountRarityInsight.Find(Book),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name).OwnershipCounts(Book, null),
            Prices = SortClickBook.Prices,
            Owned = SortClickBook.Owned,
            Total = Book.Count,
            SelectedId = Book[0].Id,
            ItemName = "Aithon Whistle",
            SourceOptions = MountbookPreview.SourceOptions(Book),
            AvailabilityOptions = MountbookPreview.AvailabilityOptions(Book),
            Order = MountOrder.Name,
            ViewMode = MountViewMode.Table,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
        };

        drawnOrder = state.Order;
    }

    /// <summary>Whether this scene plays the header clicks.</summary>
    protected abstract bool Sorts { get; }

    public override Look Look { get; } = MountLook.Dense(Look.Eorzea());

    /// <summary>The window the table is read in: wide enough that the name column is not the only thing.</summary>
    public override Vector2 WindowSize => new(1180f, 820f);

    /// <summary>
    /// The header clicks when <see cref="Sorts"/>, and the pointer parked off the window at the end either way
    /// so the captured frame holds no hover state.
    /// </summary>
    public override ReadOnlySpan<PreviewInput> Script => Sorts ? Clicks : Untouched;

    protected override void DrawScene()
    {
        // The window re-queries when the view reports a new order; the preview does the same here, so the
        // rows a click asks for are the rows the next frame draws. Without it the scenes would compare two
        // parked pictures of the same rows - true of the final frame either way, but not of the frames in
        // between, and the springs that settle into the final one see those.
        if (state.Order != drawnOrder)
        {
            state.Items = Query(state.Order);
            drawnOrder = state.Order;
        }

        view.Draw(state);
    }

    /// <summary>The book filtered and ordered the way the window would, prices included for the Price column.</summary>
    /// <param name="order">The order the rows are drawn in.</param>
    private static IReadOnlyList<MountShelfItem> Query(MountOrder order) =>
        new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, order).Apply(Book, prices: SortClickBook.Prices);

    /// <summary>Name, Seats, Name, then the pointer off the window and enough idle frames to settle.</summary>
    private static readonly PreviewInput[] Clicks =
    [
        new(2, PreviewInputKind.Move, SortClickBook.NameHeader),
        new(4, PreviewInputKind.Press, SortClickBook.NameHeader),
        new(9, PreviewInputKind.Move, SortClickBook.SeatsHeader),
        new(11, PreviewInputKind.Press, SortClickBook.SeatsHeader),
        new(16, PreviewInputKind.Move, SortClickBook.NameHeader),
        new(18, PreviewInputKind.Press, SortClickBook.NameHeader),
        new(24, PreviewInputKind.Move, SortClickBook.Away),

        // A last input on frame 70 is what makes the run that long: the preview stops on the script's last
        // frame, and every spring in the picture - the header's hover, the caret's turn - needs the quiet to
        // settle. Without it the captured frame is a moving one, and no two runs could be compared.
        new(70, PreviewInputKind.Move, SortClickBook.Away),
    ];

    /// <summary>The same pointer parking, with no header touched.</summary>
    private static readonly PreviewInput[] Untouched =
    [
        new(2, PreviewInputKind.Move, SortClickBook.NameHeader),
        new(24, PreviewInputKind.Move, SortClickBook.Away),
        new(70, PreviewInputKind.Move, SortClickBook.Away),
    ];
}

/// <summary>
/// The long book the two sort-click scenes draw, with the header positions their scripts click.
/// </summary>
/// <remarks>
/// The coordinates are the layout's, not the script's convenience: they are the header row of this
/// window's table, measured from a render of <see cref="SortClickPreview"/> - the same frame this pair
/// commits as its golden - so a change that moves the header is a diff in the image rather than a click that
/// lands on a row. The list scrolls with the wheel here exactly as it does in the client, and the header
/// scrolls away with it, so the clicks sit at the top of the list where a player's pointer can reach them.
/// </remarks>
internal static class SortClickBook
{
    /// <summary>The middle of the Name header cell, in logical units from the window's top left corner.</summary>
    internal static readonly Vector2 NameHeader = new(60f, 362f);

    /// <summary>The middle of the Seats header cell.</summary>
    internal static readonly Vector2 SeatsHeader = new(346f, 362f);

    /// <summary>Off the window, over the Preview's backdrop: nothing under the pointer, so no hover ink.</summary>
    internal static readonly Vector2 Away = new(-40f, -40f);

    private static readonly string[] Names =
    [
        "Aithon",
        "Astrope",
        "Auspicious Kamuy",
        "Coeurl",
        "Company Chocobo",
        "Genie of the Lamp",
        "Garlond GL-IS",
        "Juedi",
        "Midgardsormr",
        "Ronkan Emerald",
        "Unicorn",
        "Xanthos",
    ];

    private static readonly string[] Suffixes = [string.Empty, " II", " III", " IV"];

    private static readonly string[] PatchNumbers = ["2.0", "2.1", "2.5", "3.0", "3.5", "4.0", "4.4", "5.0", "6.1", "7.05"];

    private static readonly string[] Categories = ["Achievement", "Quest", "Trial", "FATE", "Premium", "V&C Dungeon"];

    /// <summary>The four lengths' worth of twelve: every name once per suffix, so the column's widest grows.</summary>
    internal static IReadOnlyList<MountShelfItem> Build()
    {
        var items = new List<MountShelfItem>(Names.Length * Suffixes.Length);

        foreach (string suffix in Suffixes)
        {
            for (var index = 0; index < Names.Length; index++)
            {
                int slot = (index * 7) + (suffix.Length * 3);
                items.Add(Item(
                    (uint)((index + 1) * 16) + (uint)(suffix.Length * 2),
                    Names[index] + suffix,
                    (uint)(index + 1),
                    (slot % 8) + 1,
                    PatchNumbers[slot % PatchNumbers.Length],
                    (slot % 3) == 0,
                    Categories[slot % Categories.Length]));
            }
        }

        return items;
    }

    /// <summary>The prices the table's Price column draws: every fourth row, so the column is neither full nor empty.</summary>
    internal static IReadOnlyDictionary<uint, int> Prices { get; } = BuildPrices();

    /// <summary>How many of the book's mounts the player has, counted rather than written down.</summary>
    internal static int Owned { get; } = BuildOwned();

    private static Dictionary<uint, int> BuildPrices()
    {
        var prices = new Dictionary<uint, int>();
        IReadOnlyList<MountShelfItem> book = Build();

        for (var index = 0; index < book.Count; index += 4)
        {
            prices[book[index].Id] = 24_000 * (index + 1);
        }

        return prices;
    }

    private static int BuildOwned()
    {
        var owned = 0;

        foreach (MountShelfItem item in Build())
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        return owned;
    }

    private static MountShelfItem Item(uint id, string name, uint iconId, int seats, string patch, bool owned, string category)
    {
        // A share per row, four of them under the rarity line's one percent, so the Owners column draws both
        // the plain numbers and the marked ones the previous card reworked.
        double share = (id % 24) / 2.0;
        var lines = new MountSourceLine[] { new(category, "Hand of the " + category, string.Empty, 0u) };

        return new MountShelfItem(
            id,
            name,
            iconId,
            64000u + iconId,
            seats,
            patch,
            owned,
            string.Empty,
            string.Empty,
            string.Empty,
            lines,
            new MountCommunity(share, true, true),
            Barding: name.StartsWith("Astrope", StringComparison.Ordinal),
            Flies: true,
            MountAvailability.Available);
    }
}
