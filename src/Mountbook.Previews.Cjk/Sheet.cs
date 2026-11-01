using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;

namespace Mountbook.Previews.Cjk;

/// <summary>One script's row of the sample: the same keys, drawn in that language.</summary>
/// <param name="Code">The table code, e.g. <c>ja</c>; the column's label.</param>
/// <param name="Title">The window title.</param>
/// <param name="Collected">"9 of 12 collected", with the numbers in the column's own order.</param>
/// <param name="Remaining">"3 still missing".</param>
/// <param name="Chips">The four ownership chips.</param>
/// <param name="Search">The search box's hint.</param>
/// <param name="Headers">The table's seven column headings.</param>
/// <param name="Section">"How to obtain".</param>
/// <param name="Badges">The detail pane's badges for one mount.</param>
/// <param name="Actions">The detail pane's action buttons.</param>
internal sealed record SampleColumn(
    string Code,
    string Title,
    string Collected,
    string Remaining,
    IReadOnlyList<string> Chips,
    string Search,
    IReadOnlyList<string> Headers,
    string Section,
    IReadOnlyList<string> Badges,
    IReadOnlyList<string> Actions);

/// <summary>
/// The CJK sample sheet: three columns of the plugin's own labels, in Japanese, Simplified Chinese and
/// Korean, drawn with the plugin's own type sizes and colours.
/// </summary>
/// <remarks>
/// The point of the image is the glyphs: the strings are the real keys, resolved through the real lookup, so
/// what is proven is that a table full of CJK renders as text rather than as tofu or question marks. It is
/// deliberately not the whole window - the goldens already pin the layout, and a second renderer drawing the
/// whole chrome would be a second thing to keep in step. What this adds is the one thing the pinned renderer
/// cannot do.
/// </remarks>
internal static class Sheet
{
    /// <summary>The image's width in pixels.</summary>
    public const int Width = 900;

    /// <summary>The image's height in pixels.</summary>
    public const int Height = 340;

    private const float Margin = 22f;

    private const float ColumnWidth = 278f;

    /// <summary>The fonts, by text role: the size the plugin draws that role at, at UI scale 1.</summary>
    private static readonly TextRole[] Roles =
    [
        TextRole.Title,
        TextRole.Subheadline,
        TextRole.Body,
        TextRole.Footnote,
        TextRole.Caption,
        TextRole.Callout,
    ];

    /// <summary>The roles the sheet needs, in the order the atlas should build them.</summary>
    public static IReadOnlyList<TextRole> NeededRoles => Roles;

    /// <summary>Draws the sheet into the frame's background draw list.</summary>
    /// <param name="columns">The three localised columns.</param>
    /// <param name="fonts">The font and size to draw each role with.</param>
    /// <param name="look">The Look the plugin hands its host, for colours and type sizes.</param>
    public static void Draw(IReadOnlyList<SampleColumn> columns, IReadOnlyDictionary<TextRole, (ImFontPtr Font, float Size)> fonts, Look look)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(look);

        ImDrawListPtr list = ImGui.GetBackgroundDrawList();

        list.AddRectFilled(Vector2.Zero, new Vector2(Width, Height), Colour(new Vector4(0.055f, 0.063f, 0.078f, 1f)), 0f);

        for (var index = 0; index < columns.Count; index++)
        {
            DrawColumn(list, columns[index], fonts, look, Margin + (index * (ColumnWidth + 20f)));
        }

        // A single line of Latin at the bottom, so the same image proves the face still draws the fifty
        // letters the rest of the interface is made of.
        Text(list, fonts, TextRole.Caption, new Vector2(Margin, Height - 24f),
            "Latin sample: Aithon, Auspicious Kamuy, Seafarer's Cowries - 12,000 gil - 48.7%", look.TertiaryLabel);
    }

    private static void DrawColumn(
        ImDrawListPtr list,
        SampleColumn column,
        IReadOnlyDictionary<TextRole, (ImFontPtr Font, float Size)> fonts,
        Look look,
        float left)
    {
        var top = Margin;

        // The column's code, in the face's Latin range: a label for the reader, not a translated string.
        Text(list, fonts, TextRole.Caption, new Vector2(left, top), column.Code, look.TertiaryLabel);

        Text(list, fonts, TextRole.Title, new Vector2(left, top + 18f), column.Title, look.Label);

        Text(list, fonts, TextRole.Subheadline, new Vector2(left, top + 56f), column.Collected, look.Label);
        Text(list, fonts, TextRole.Footnote, new Vector2(left, top + 80f), column.Remaining, look.SecondaryLabel);

        // The chips: drawn as boxes because the real ones are, and a box is what shows the text is not
        // clipped by its own width.
        var chipX = left;
        var chipTop = top + 106f;

        foreach (string chip in column.Chips)
        {
            float chipWidth = Measure(fonts, TextRole.Footnote, chip) + 18f;

            if (chipX + chipWidth > left + ColumnWidth)
            {
                chipX = left;
                chipTop += 26f;
            }

            Chip(list, fonts, look, new Vector2(chipX, chipTop), chip);
            chipX += chipWidth + 6f;
        }

        Text(list, fonts, TextRole.Body, new Vector2(left, chipTop + 34f), column.Search, look.Label);

        // The table's headings, separated by the same gap the table uses between columns.
        var headerX = left;

        foreach (string header in column.Headers)
        {
            Text(list, fonts, TextRole.Footnote, new Vector2(headerX, chipTop + 62f), header, look.SecondaryLabel);
            headerX += Measure(fonts, TextRole.Footnote, header) + 10f;
        }

        Text(list, fonts, TextRole.Subheadline, new Vector2(left, chipTop + 88f), column.Section, look.Accent);

        var badgeX = left;

        foreach (string badge in column.Badges)
        {
            Chip(list, fonts, look, new Vector2(badgeX, chipTop + 114f), badge);
            badgeX += Measure(fonts, TextRole.Footnote, badge) + 24f;
        }

        var actionX = left;

        foreach (string action in column.Actions)
        {
            float actionWidth = Measure(fonts, TextRole.Footnote, action) + 22f;

            list.AddRectFilled(
                new Vector2(actionX, chipTop + 144f),
                new Vector2(actionX + actionWidth, chipTop + 168f),
                Colour(look.Accent.WithAlpha(0.55f).ToVector4()),
                look.RadiusSmall);

            Text(list, fonts, TextRole.Footnote, new Vector2(actionX + 11f, chipTop + 148f), action, look.Background);
            actionX += actionWidth + 6f;
        }
    }

    private static void Chip(
        ImDrawListPtr list,
        IReadOnlyDictionary<TextRole, (ImFontPtr Font, float Size)> fonts,
        Look look,
        Vector2 at,
        string text)
    {
        float width = Measure(fonts, TextRole.Footnote, text) + 16f;

        list.AddRectFilled(at, at + new Vector2(width, 20f), Colour(look.Fill.WithAlpha(0.55f).ToVector4()), 10f);
        Text(list, fonts, TextRole.Footnote, at + new Vector2(8f, 3f), text, look.Label);
    }

    private static void Text(
        ImDrawListPtr list,
        IReadOnlyDictionary<TextRole, (ImFontPtr Font, float Size)> fonts,
        TextRole role,
        Vector2 at,
        string text,
        Color colour)
    {
        (ImFontPtr font, float size) = fonts[role];
        list.AddText(font, size, at, Colour(colour.ToVector4()), text);
    }

    private static float Measure(IReadOnlyDictionary<TextRole, (ImFontPtr Font, float Size)> fonts, TextRole role, string text)
    {
        (ImFontPtr font, float size) = fonts[role];
        return ImGui.CalcTextSizeA(font, size, float.MaxValue, 0f, text, out _).X;
    }

    private static uint Colour(Vector4 rgba) => ImGui.GetColorU32(rgba);
}
