using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The wanted board's rows: which mounts are on it, what each row says, and the two orders.
/// </summary>
/// <remarks>
/// The board is the one view where a wrong filter is expensive: it is a to-do list, so a mount the player
/// owns (done) or never starred (not asked for) has no business on it, and a row without its source line
/// answers nothing. Prices arrive through a delegate, so these tests also pin that an unpriced mount sorts
/// last rather than being fetched (AGENTS N2).
/// </remarks>
public sealed class WantedBoardTests
{
    [Fact]
    public void OnlyWantedAndUnownedMountsAreOnTheBoard()
    {
        IReadOnlyList<BoardRow> rows = Build(
            [Mount(1, "Wanted unowned", owned: false), Mount(2, "Not wanted", owned: false), Mount(3, "Wanted owned", owned: true)],
            wanted: [1, 3]);

        Assert.Single(rows);
        Assert.Equal(1u, rows[0].Id);
    }

    [Fact]
    public void ARowCarriesTheFirstSourceLine()
    {
        MountShelfItem item = Mount(
            4,
            "Garlond GL-IS",
            owned: false,
            Source("Mog Station", "Mounts & Miscellany"),
            Source("Achievement", "A Legend for All Time"));

        IReadOnlyList<BoardRow> rows = Build([item], wanted: [4]);

        Assert.Equal("Mounts & Miscellany", rows[0].Source);
    }

    [Fact]
    public void AMountWithNoSourcesStillGetsARow()
    {
        IReadOnlyList<BoardRow> rows = Build([Mount(5, "Nothing recorded", owned: false)], wanted: [5]);

        Assert.Equal(string.Empty, rows[0].Source);
    }

    [Fact]
    public void TheTargetDefaultsToTheRecordedPrice()
    {
        MountShelfItem item = Mount(6, "Genie of the Lamp", owned: false, Source("V&C Dungeon", "100 Corvosi Manuscripts"));

        IReadOnlyList<BoardRow> rows = Build([item], wanted: [6]);

        Assert.Equal(100, rows[0].Want);
        Assert.Equal(0, rows[0].Have);
        Assert.Equal(0f, rows[0].Fraction);
    }

    [Fact]
    public void ASavedRecordSuppliesTheCountAndTheTarget()
    {
        MountShelfItem item = Mount(7, "Genie of the Lamp", owned: false, Source("V&C Dungeon", "100 Corvosi Manuscripts"));
        var grinds = new Dictionary<uint, MountGrindRecord>
        {
            [7] = new() { Have = 30, Want = 100, Note = "halfway", UpdatedAt = 1_700_000_000 },
        };

        IReadOnlyList<BoardRow> rows = Build([item], wanted: [7], grinds: grinds);

        Assert.Equal(30, rows[0].Have);
        Assert.Equal(100, rows[0].Want);
        Assert.Equal(0.3f, rows[0].Fraction, 3);
        Assert.Equal(1_700_000_000, rows[0].UpdatedAt);
    }

    [Fact]
    public void APatchOrderPutsTheOldestFirstAndTheUnrecordedLast()
    {
        IReadOnlyList<BoardRow> rows = Build(
            [
                Mount(10, "No patch", owned: false, patch: string.Empty),
                Mount(11, "Newest", owned: false, patch: "7.45"),
                Mount(12, "Oldest", owned: false, patch: "2.0"),
            ],
            wanted: [10, 11, 12],
            sort: MountBoardSort.Patch);

        Assert.Equal([12u, 11u, 10u], Ids(rows));
    }

    [Fact]
    public void APriceOrderPutsTheCheapestFirstAndTheUnpricedLast()
    {
        IReadOnlyList<BoardRow> rows = Build(
            [
                Mount(20, "Unpriced", owned: false),
                Mount(21, "Dear", owned: false),
                Mount(22, "Cheap", owned: false),
            ],
            wanted: [20, 21, 22],
            sort: MountBoardSort.Price,
            prices: id => id switch { 21 => 4_000_000, 22 => 250_000, _ => 0 });

        Assert.Equal([22u, 21u, 20u], Ids(rows));
    }

    [Fact]
    public void TiesFallBackToTheNameSoTheOrderIsStable()
    {
        IReadOnlyList<BoardRow> rows = Build(
            [
                Mount(30, "Xanthos", owned: false, patch: "4.0"),
                Mount(31, "Aithon", owned: false, patch: "2.5"),
                Mount(32, "Coeurl", owned: false, patch: "2.5"),
            ],
            wanted: [30, 31, 32],
            sort: MountBoardSort.Patch);

        Assert.Equal([31u, 32u, 30u], Ids(rows));
    }

    [Fact]
    public void APriceIsOnlyAskedForMountsOnTheBoard()
    {
        var asked = new List<uint>();

        _ = Build(
            [Mount(40, "Wanted", owned: false), Mount(41, "Owned", owned: true), Mount(42, "Ignored", owned: false)],
            wanted: [40, 41],
            prices: id =>
            {
                asked.Add(id);

                return 0;
            });

        Assert.Equal([40u], asked);
    }

    [Fact]
    public void AnEmptyWantedListIsAnEmptyBoard()
    {
        Assert.Empty(Build([Mount(50, "Anything", owned: false)], wanted: []));
    }

    private static IReadOnlyList<BoardRow> Build(
        IReadOnlyList<MountShelfItem> items,
        IReadOnlyList<uint> wanted,
        MountBoardSort sort = MountBoardSort.Patch,
        System.Func<uint, int>? prices = null,
        IReadOnlyDictionary<uint, MountGrindRecord>? grinds = null) =>
        WantedBoard.Build(items, new HashSet<uint>(wanted), grinds, prices ?? (_ => 0), sort);

    /// <summary>
    /// The row's progress line, which the board draws for every row on every frame and which had no test: the
    /// count with what is left, and the honest alternative for a mount the dataset records no token price for -
    /// "0 / 0 tokens" would read as a target nobody set.
    /// </summary>
    [Fact]
    public void ProgressTextNamesTheCountAndWhatIsLeft()
    {
        var row = new BoardRow(1, "Petalodus", "Island Sanctuary", "7.2", Have: 3, Want: 12, Price: 0, UpdatedAt: 0);

        Assert.Equal("3 / 12 tokens - 9 to go", WantedBoard.ProgressText(row));
    }

    [Fact]
    public void ProgressTextSaysSoWhenThereIsNoTokenPrice()
    {
        var row = new BoardRow(2, "Petalodus", "Island Sanctuary", "7.2", Have: 0, Want: 0, Price: 0, UpdatedAt: 0);

        Assert.Equal("No token price recorded", WantedBoard.ProgressText(row));
    }

    private static uint[] Ids(IReadOnlyList<BoardRow> rows)
    {
        var ids = new uint[rows.Count];

        for (var index = 0; index < rows.Count; index++)
        {
            ids[index] = rows[index].Id;
        }

        return ids;
    }

    private static MountShelfItem Mount(
        uint id,
        string name,
        bool owned,
        params MountSourceLine[] sources) =>
        Mount(id, name, owned, string.Empty, sources);

    private static MountShelfItem Mount(
        uint id,
        string name,
        bool owned,
        string patch,
        params MountSourceLine[] sources) =>
        new(id, name, 0, 0, 1, patch, owned, string.Empty, string.Empty, string.Empty, sources);

    private static MountSourceLine Source(string category, string detail) =>
        new(category, detail, string.Empty, 0);
}
