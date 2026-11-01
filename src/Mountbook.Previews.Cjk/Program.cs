using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Mountbook.Services;

namespace Mountbook.Previews.Cjk;

/// <summary>
/// Renders the localisation sample sheet and compares it with its golden.
/// </summary>
/// <remarks>
/// <para>
/// Three tables are installed in turn - Japanese, Simplified Chinese, Korean - and the same keys are drawn
/// in each, so the image proves the lookup hands a translator's table to the drawing code and that the face
/// has the glyphs. The tables live in <c>fixtures/</c> and are deliberately partial: a key a fixture leaves
/// out reads in English, which is the fallback behaving as documented.
/// </para>
/// <para>
/// Usage: <c>--compare &lt;dir&gt;</c> renders and compares with <c>&lt;dir&gt;/cjk.png</c>, exiting 1 on any
/// difference; <c>--update</c> writes the image instead. There is no GPU involved: the frame ImGui lays out
/// is rasterised on the CPU, which is what makes the image the same on a build agent.
/// </para>
/// </remarks>
internal static unsafe class Program
{
    /// <summary>The committed face: a 243-glyph subset of the font Dalamud's own Japanese UI uses.</summary>
    private const string FontPath = "fonts/NotoSansCJKjp-Medium-subset.otf";

    /// <summary>The scenes: a table code and the fixture that fills it.</summary>
    private static readonly (string Code, string File)[] Scripts =
    [
        ("ja", "fixtures/strings.ja.json"),
        ("zh-Hans", "fixtures/strings.zh-Hans.json"),
        ("ko", "fixtures/strings.ko.json"),
    ];

    /// <summary>The keys the sheet draws, in the order a column reads.</summary>
    private static readonly string[] ChipKeys = ["ownership.all", "ownership.owned", "ownership.unowned", "ownership.wanted"];

    private static readonly string[] HeaderKeys =
    [
        "table.name", "table.patch", "table.seats", "table.source", "table.owned", "table.owners", "table.price",
    ];

    private static int Main(string[] args)
    {
        string? directory = null;
        string? output = null;
        var update = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--compare":
                    directory = index + 1 < args.Length ? args[++index] : throw new ArgumentException("--compare needs a directory");
                    break;
                case "--update":
                    update = true;
                    break;
                case "--out":
                    output = index + 1 < args.Length ? args[++index] : throw new ArgumentException("--out needs a directory");
                    break;
                default:
                    Console.Error.WriteLine($"unknown argument '{args[index]}'");
                    return 2;
            }
        }

        if (directory is null && !update)
        {
            Console.Error.WriteLine("usage: Mountbook.Previews.Cjk --compare <dir> [--out <dir>] | --update [--out <dir>]");
            return 2;
        }

        try
        {
            // Before anything touches ImGui: the bindings are Dalamud's, not ours to ship.
            string dalamud = Dalamud.Load();
            Console.WriteLine($"cjk golden: ImGui bindings from {dalamud}");

            return Run(directory ?? output ?? ".", output, update);
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
        {
            Console.Error.WriteLine($"cjk golden: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string directory, string? output, bool update)
    {
        // The plugin's own table first: the fixtures are partial, and what they leave out falls back to this.
        Loc.Scan();

        IReadOnlyList<SampleColumn> columns = BuildColumns(out IReadOnlyList<IReadOnlyDictionary<string, string>> fixtures);
        var glyphs = new SortedSet<char>();

        foreach (SampleColumn column in columns)
        {
            foreach (string text in Strings(column))
            {
                foreach (char character in text)
                {
                    if (!char.IsWhiteSpace(character))
                    {
                        _ = glyphs.Add(character);
                    }
                }
            }
        }

        byte[] image = Render(columns, out IReadOnlyList<string> missingGlyphs, out IReadOnlyList<string> missingKeys);

        Report(columns.Count, fixtures, glyphs.Count, missingKeys, missingGlyphs);

        if (missingKeys.Count > 0 || missingGlyphs.Count > 0)
        {
            return 1;
        }

        string name = "cjk.png";
        string golden = Path.Combine(directory, name);
        string actual = Path.Combine(output ?? directory, name);

        if (update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(golden))!);
            Png.Write(golden, Sheet.Width, Sheet.Height, image);
            Console.WriteLine($"  wrote {golden} ({Sheet.Width}x{Sheet.Height})");
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(actual))!);

        if (!File.Exists(golden))
        {
            Console.WriteLine($"cjk: missing golden {golden}; run --update to create it");
            return 1;
        }

        byte[] expected = File.ReadAllBytes(golden);
        byte[] encoded = Png.Encode(Sheet.Width, Sheet.Height, image);

        if (actual != golden)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(actual))!);
            File.WriteAllBytes(actual, encoded);
        }

        if (Pixels(encoded, expected))
        {
            Console.WriteLine("cjk: the render matches its golden");
            return 0;
        }

        Console.WriteLine($"cjk: the render differs from {golden}; the actual image is {actual}");
        return 1;
    }

    /// <summary>
    /// Renders the sheet. <paramref name="missingGlyphs"/> lists characters the face has no glyph for, which
    /// is the tofu the image exists to rule out; <paramref name="missingKeys"/> lists keys no table defines.
    /// </summary>
    private static byte[] Render(
        IReadOnlyList<SampleColumn> columns,
        out IReadOnlyList<string> missingGlyphs,
        out IReadOnlyList<string> missingKeys)
    {
        Look look = MountLook.Dense(Look.Eorzea());

        ImGui.CreateContext();
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(Sheet.Width, Sheet.Height);
        io.DeltaTime = 1f / 60f;
        io.IniFilename = null;

        ImFontAtlasPtr atlas = io.Fonts;
        var fonts = new Dictionary<TextRole, (ImFontPtr Font, float Size)>();

        // Everything the three scripts can need: Latin, general punctuation, kana, CJK ideographs, Hangul and
        // the full-width forms. The subset only carries the glyphs it kept, so this is a bound, not a promise.
        ushort[] ranges = [0x0020, 0x00FF, 0x2000, 0x206F, 0x3000, 0x30FF, 0x4E00, 0x9FFF, 0xAC00, 0xD7A3, 0xFF00, 0xFFEF, 0, 0];

        fixed (ushort* glyphRanges = ranges)
        {
            foreach (TextRole role in Sheet.NeededRoles)
            {
                float size = look.Type.ScaledSize(role);
                ImFontPtr font = atlas.AddFontFromFileTTF(Asset(FontPath), size, ImFontConfigPtr.Null, glyphRanges);

                if (font.IsNull)
                {
                    throw new InvalidOperationException($"the font {FontPath} could not be read (is it next to the binary?)");
                }

                fonts[role] = (font, size);
            }
        }

        if (!atlas.Build())
        {
            throw new InvalidOperationException("the font atlas failed to build");
        }

        var textures = new List<AtlasTexture>(atlas.Textures.Size);

        // 1.92 builds one texture per font. Each is given an id of its index plus one, so a draw command's
        // texture id maps straight back to the pixels here without a GPU handle.
        for (var index = 0; index < atlas.Textures.Size; index++)
        {
            byte* pixels = null;
            var width = 0;
            var height = 0;
            atlas.GetTexDataAsRGBA32(index, ref pixels, ref width, ref height);

            if (pixels == null || width <= 0 || height <= 0)
            {
                throw new InvalidOperationException($"font atlas texture {index} has no pixels");
            }

            var copy = new byte[width * height * 4];
            new ReadOnlySpan<byte>(pixels, copy.Length).CopyTo(copy);
            textures.Add(new AtlasTexture(copy, width, height));
            atlas.SetTexID(index, new ImTextureID((ulong)(index + 1)));
        }

        var missingKeysFound = new List<string>();

        foreach (string key in ColumnKeys())
        {
            if (!Loc.Has(key))
            {
                missingKeysFound.Add(key);
            }
        }

        var missingGlyphsFound = new List<string>();

        foreach (string text in columns.SelectMany(Strings))
        {
            foreach (char character in text)
            {
                if (char.IsWhiteSpace(character) || fonts[TextRole.Body].Font.FindGlyphNoFallback(character) is not null)
                {
                    continue;
                }

                string entry = $"U+{(int)character:X4} '{character}'";

                if (!missingGlyphsFound.Contains(entry))
                {
                    missingGlyphsFound.Add(entry);
                }
            }
        }

        missingGlyphs = missingGlyphsFound;
        missingKeys = missingKeysFound;

        ImGui.NewFrame();
        Sheet.Draw(columns, fonts, look);
        ImGui.Render();

        var target = new byte[Sheet.Width * Sheet.Height * 4];

        for (var index = 0; index < target.Length; index += 4)
        {
            target[index] = 14;
            target[index + 1] = 16;
            target[index + 2] = 20;
            target[index + 3] = 255;
        }

        Software.Render(ImGui.GetDrawData(), textures, target, Sheet.Width, Sheet.Height);

        return target;
    }

    /// <summary>Builds the three columns, installing each fixture so the keys resolve in that language.</summary>
    private static IReadOnlyList<SampleColumn> BuildColumns(out IReadOnlyList<IReadOnlyDictionary<string, string>> fixtures)
    {
        var columns = new List<SampleColumn>(Scripts.Length);
        var tables = new List<IReadOnlyDictionary<string, string>>(Scripts.Length);

        foreach ((string code, string file) in Scripts)
        {
            (InterfaceLanguage language, IReadOnlyDictionary<string, string> table) = StringTable.Load(Asset(file));

            Loc.Install(language, table);
            Loc.Use(language);
            tables.Add(table);

            columns.Add(new SampleColumn(
                code,
                Loc.T("window.main"),
                Loc.F("header.collected", "9", "12"),
                Loc.F("header.remaining.some", "3"),
                Resolve(ChipKeys),
                Loc.T("filter.search.hint"),
                Resolve(HeaderKeys),
                Loc.T("detail.obtain.section"),
                [Loc.T("badge.collected"), Loc.T("badge.flies"), Loc.F("badge.seats.many", "2")],
                Resolve(["detail.ride", "detail.linkWhistle", "detail.mountGuide", "detail.want"])));
        }

        fixtures = tables;
        return columns;
    }

    private static string[] Resolve(IReadOnlyList<string> keys)
    {
        var values = new string[keys.Count];

        for (var index = 0; index < keys.Count; index++)
        {
            values[index] = Loc.T(keys[index]);
        }

        return values;
    }

    /// <summary>
    /// An asset beside the binary. The fixtures and the face are copied to the output by the project file, so
    /// the tool works from any working directory - which it has to, because <c>dotnet run</c> starts in the
    /// repository root.
    /// </summary>
    /// <param name="relative">The path as the project carries it, e.g. <c>fixtures/strings.ja.json</c>.</param>
    private static string Asset(string relative) => Path.Combine(AppContext.BaseDirectory, relative);

    /// <summary>Every key the sheet asks for, for the report and for the "no table defines this" check.</summary>
    private static IEnumerable<string> ColumnKeys()
    {
        yield return "window.main";
        yield return "header.collected";
        yield return "header.remaining.some";
        yield return "filter.search.hint";
        yield return "detail.obtain.section";
        yield return "badge.seats.many";

        foreach (string key in ChipKeys)
        {
            yield return key;
        }

        foreach (string key in HeaderKeys)
        {
            yield return key;
        }

        yield return "badge.collected";
        yield return "badge.flies";
        yield return "detail.ride";
        yield return "detail.linkWhistle";
        yield return "detail.mountGuide";
        yield return "detail.want";
    }

    private static IEnumerable<string> Strings(SampleColumn column)
    {
        yield return column.Code;
        yield return column.Title;
        yield return column.Collected;
        yield return column.Remaining;
        yield return column.Search;
        yield return column.Section;

        foreach (string text in column.Chips)
        {
            yield return text;
        }

        foreach (string text in column.Headers)
        {
            yield return text;
        }

        foreach (string text in column.Badges)
        {
            yield return text;
        }

        foreach (string text in column.Actions)
        {
            yield return text;
        }
    }

    private static void Report(
        int columnCount,
        IReadOnlyList<IReadOnlyDictionary<string, string>> fixtures,
        int glyphCount,
        IReadOnlyList<string> missingKeys,
        IReadOnlyList<string> missingGlyphs)
    {
        Console.WriteLine(
            $"cjk golden: {columnCount} languages, {ColumnKeys().Count()} keys, {glyphCount} distinct non-space characters");

        for (var index = 0; index < fixtures.Count; index++)
        {
            Console.WriteLine($"  {Scripts[index].Code}: {fixtures[index].Count} strings from {Scripts[index].File}");
        }

        if (missingKeys.Count > 0)
        {
            Console.WriteLine("  no table defines: " + string.Join(", ", missingKeys));
        }

        if (missingGlyphs.Count > 0)
        {
            Console.WriteLine("  no glyph for: " + string.Join(", ", missingGlyphs));
        }
    }

    /// <summary>True when the encoded image and the golden hold the same bytes.</summary>
    private static bool Pixels(byte[] image, byte[] expected) => image.AsSpan().SequenceEqual(expected);
}
