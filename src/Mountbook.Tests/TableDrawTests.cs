using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The two rules the table's drawing has to keep, checked against the source because the runtime cannot be
/// asked: one table id for every order, and the sort spec written from the order before the header row draws.
/// </summary>
/// <remarks>
/// <para>
/// Why a source guard: an ImGui table keeps its state per id - the widths it fitted to the cells, the spec it
/// saves - and the table used to choose its id from the order (##mountsTable0 through 7). Every order then
/// had a table of its own, so a click on a heading drew columns that had never been fitted to this list and
/// the rows visibly re-laid themselves out. Measured in the client against 369 rows: the Seats column 84 px
/// further right, the Source column 96 px further right and the Owned column 93 px further left than the
/// Name order drew them, with the row pitch unchanged.
/// </para>
/// <para>
/// The previews cannot hold that rule on their own: <c>goldens/sortclick.png</c> and
/// <c>goldens/sortclick-returned.png</c> are the same list before and after Name, Seats, Name and compare
/// equal to within the renderer's own tolerance - but a developer can re-baseline one of them, and the pair
/// then holds a moved column edge as the truth. These assertions are the half that cannot be re-baselined:
/// the id is not chosen from anything, no column declares itself the default sort, and the spec write sits
/// before the header row rather than after it.
/// </para>
/// <para>
/// The view's source is embedded into this assembly as text (see Mountbook.Tests.csproj), which is the same
/// mechanism the localisation guard uses: this project compiles without Dalamud, so it cannot load the view
/// and ask it anything.
/// </para>
/// </remarks>
public sealed class TableDrawTests
{
    /// <summary>The embedded resource name of the view's table partial.</summary>
    private const string TableSource = "Mountbook.Tests.Sources.Views.MountbookView.Table.cs";

    /// <summary>
    /// The embedded resource name of the view's grid partial, which is the half that decides what draws the
    /// list: the table itself, or the scrolling child the card grid lives in.
    /// </summary>
    private const string GridSource = "Mountbook.Tests.Sources.Views.MountbookView.Grid.cs";

    [Fact]
    public void TheTableOpensUnderTheOneId()
    {
        string source = Source();

        // The id is MountTable's, so the view naming one at all is a second id - which is the whole defect.
        Assert.DoesNotContain("##mountsTable", source, StringComparison.Ordinal);
        Assert.Contains("Gw.BeginTable(MountTable.TableId,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoColumnDeclaresItselfTheDefaultSort()
    {
        // A declared default sort carries no direction, so it can only ever draw an ascending caret, and ImGui
        // prefers its saved spec over it anyway. It was also the place the order leaked into the declarations
        // (MountTable.ColumnFor(state.Order) == ...): a table whose columns say which one is sorted is a table
        // whose layout depends on the order.
        foreach (string line in Source().Split('\n'))
        {
            if (!line.Contains("Gw.TableColumn(", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain("ColumnFor", line, StringComparison.Ordinal);
            Assert.DoesNotContain("defaultSort", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSpecIsWrittenFromTheOrderBeforeTheHeaderRow()
    {
        string source = Source();
        int write = source.IndexOf("TableSort.At(state.Order)", StringComparison.Ordinal);
        int headers = source.IndexOf("Gw.TableHeaders();", StringComparison.Ordinal);

        Assert.True(write >= 0, "the table no longer writes MountTable's spec from the order: the header would draw whatever spec ImGui restored for the id.");
        Assert.True(headers >= 0, "Gw.TableHeaders() is gone: the header row is not drawn, or its name changed.");
        Assert.True(
            write < headers,
            "the spec is written after the header row draws, so the caret a player sees is the previous frame's - a click's own direction is not repaired until the frame after the one that drew it.");
    }

    /// <summary>
    /// The table scrolls itself, which is the only way its heading can stay put: without
    /// <c>GwTableFlags.ScrollY</c> the header row is an ordinary first row of a table as tall as its rows, and
    /// whatever draws that table scrolls the heading off the top with them - a column can then only be clicked
    /// at scroll 0, so sorting always costs the reader their place (measured in the client at scroll.y 640
    /// against 369 rows).
    /// </summary>
    /// <remarks>
    /// The height is the second half of the same rule and is asserted with the flag: a zero height leaves the
    /// table as tall as its content, and the flag would then have nothing to scroll.
    /// </remarks>
    [Fact]
    public void TheTableScrollsItselfSoItsHeadingStaysOnScreen()
    {
        string source = Source();

        Assert.True(
            source.Contains("GwTableFlags.ScrollY", StringComparison.Ordinal),
            "the table no longer asks for its own scroll: the header row is a first row again, so scrolling the list takes it off the top and a heading can only be clicked at scroll 0.");

        Assert.DoesNotContain("new Vector2(width, 0f)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The table is drawn outside the scrolling child the card grid uses: a scrolling table inside a scrolling
    /// child has two scroll ranges - two bars, and the outer one still moves the frozen heading.
    /// </summary>
    /// <remarks>
    /// A source guard, and an ordering one: the table is drawn in the table branch of the filter column and the
    /// child opens in the other, so a table call that has moved inside a child shows up as a table call after
    /// the child's name in the file. The previews cannot see this - a frame drawn inside a child looks the
    /// same as one drawn outside it until something scrolls - and the live client is the only other place it
    /// shows.
    /// </remarks>
    [Fact]
    public void TheTableIsNotDrawnInsideAScrollingChild()
    {
        string source = Grid();

        int branch = source.IndexOf("if (state.ViewMode == MountViewMode.Table)", StringComparison.Ordinal);
        int table = source.IndexOf("DrawTable(state, gridHeight)", StringComparison.Ordinal);
        int child = source.IndexOf("\"##mounts\"", StringComparison.Ordinal);

        Assert.True(branch >= 0, "the filter column no longer branches on the view mode: the table and the card grid are drawn by one path.");
        Assert.True(table > branch, "the table is not drawn in the table branch of the filter column.");
        Assert.True(
            child > table,
            "the table is drawn after - or inside - the ##mounts scrolling child: that is two scroll ranges around one list, and the outer one scrolls the frozen heading away again.");
    }

    /// <summary>The view's table partial, as text, with its comments blanked so prose cannot satisfy a guard.</summary>
    /// <remarks>
    /// The id this defect removed is named in the comment that explains it ("##mountsTable0 through 7"), and
    /// an assertion that reads the comments would fail on the explanation rather than on the code.
    /// </remarks>
    private static string Source() => Embedded(TableSource);

    /// <summary>The view's grid partial, as text, with its comments blanked for the same reason.</summary>
    private static string Grid() => Embedded(GridSource);

    /// <summary>
    /// An embedded source file as text, with its comments blanked so prose cannot satisfy a guard.
    /// </summary>
    /// <param name="name">The manifest resource name the csproj embedded the file under.</param>
    private static string Embedded(string name)
    {
        using Stream stream = typeof(TableDrawTests).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"a source this guard reads is not embedded as {name}; see the Mountbook.Tests.csproj EmbeddedResource item.");

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        string text = System.Text.RegularExpressions.Regex.Replace(
            reader.ReadToEnd(),
            @"/\*.*?\*/",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return System.Text.RegularExpressions.Regex.Replace(text, @"//[^
]*", string.Empty);
    }
}
