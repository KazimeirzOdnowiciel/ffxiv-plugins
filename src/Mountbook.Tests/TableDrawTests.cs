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

    /// <summary>The view's table partial, as text, with its comments blanked so prose cannot satisfy a guard.</summary>
    /// <remarks>
    /// The id this defect removed is named in the comment that explains it ("##mountsTable0 through 7"), and
    /// an assertion that reads the comments would fail on the explanation rather than on the code.
    /// </remarks>
    private static string Source()
    {
        using Stream stream = typeof(TableDrawTests).Assembly.GetManifestResourceStream(TableSource)
            ?? throw new InvalidOperationException(
                $"the view's table source is not embedded as {TableSource}; see the Mountbook.Tests.csproj EmbeddedResource item.");

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
