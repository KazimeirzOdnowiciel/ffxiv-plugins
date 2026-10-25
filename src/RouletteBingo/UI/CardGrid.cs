using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using RouletteBingo.Core;

namespace RouletteBingo.UI;

/// <summary>
/// Draws the 5x5 board by hand so the cells can carry a category colour, a rarity marker,
/// a check stamp and a bingo glow instead of looking like 25 buttons.
/// </summary>
public static class CardGrid
{
    private static readonly string[][] Wrapped = new string[CardGenerator.CellCount][];

    private static ulong cachedSeed;
    private static float cachedTextWidth = -1f;
    private static int cachedMaxLines = -1;

    public static void Draw(BingoCard card, float scale, bool compact, Action<int> onToggle)
    {
        var avail = ImGui.GetContentRegionAvail();
        var gap = MathF.Max(4f, 6f * scale);
        var cellWidth = (avail.X - (gap * 4f)) / 5f;
        var cellHeight = compact
            ? MathF.Max(18f, cellWidth * 0.19f)
            : Math.Clamp(cellWidth * 0.86f, 92f, 158f);

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var baseSize = ImGui.GetFontSize();
        var bodySize = baseSize * 0.93f * scale;
        var lineHeight = bodySize * 1.3f;
        var textWidth = cellWidth - (20f * scale);
        var maxLines = compact ? 0 : Math.Max(2, (int)((cellHeight - (26f * scale)) / lineHeight));

        if (!compact)
            EnsureWrapped(card, textWidth, maxLines);

        var lineCells = new HashSet<int>();
        foreach (var line in card.CompletedLines)
        {
            foreach (var cell in line.Cells)
                lineCells.Add(cell);
        }

        for (var i = 0; i < CardGenerator.CellCount; i++)
        {
            var row = i / CardGenerator.GridSize;
            var col = i % CardGenerator.GridSize;
            var pos = origin + new Vector2(col * (cellWidth + gap), row * (cellHeight + gap));
            var size = new Vector2(cellWidth, cellHeight);

            ImGui.SetCursorScreenPos(pos);
            var clicked = ImGui.InvisibleButton($"##bingocell{i}", size);
            var hovered = ImGui.IsItemHovered();

            DrawCell(drawList, card, i, pos, size, scale, compact, hovered, lineCells.Contains(i), bodySize, lineHeight, textWidth, font);

            if (hovered)
            {
                var square = card.Cells[i];
                var suffix = card.IsAuto(i) ? "  (auto-detected)" : card.IsMarked(i) ? "  (marked)" : string.Empty;
                ImGui.SetTooltip(
                    $"{Suggestion.CategoryName(square.Category)} - {Theme.RarityName(square.Rarity)}{suffix}\n{square.Text}\n\nClick to mark or unmark.");
            }

            if (clicked)
                onToggle(i);
        }

        var totalHeight = (cellHeight * CardGenerator.GridSize) + (gap * (CardGenerator.GridSize - 1));
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + totalHeight + gap));
    }

    private static void DrawCell(
        ImDrawListPtr drawList,
        BingoCard card,
        int index,
        Vector2 pos,
        Vector2 size,
        float scale,
        bool compact,
        bool hovered,
        bool inLine,
        float bodySize,
        float lineHeight,
        float textWidth,
        ImFontPtr font)
    {
        var square = card.Cells[index];
        var accent = Theme.CategoryColor(square.Category);
        var marked = card.IsMarked(index);
        var auto = card.IsAuto(index);
        var rounding = MathF.Max(3f, 5f * scale);

        var background = marked
            ? Theme.Lerp(Theme.Panel, accent, 0.17f)
            : hovered ? Theme.PanelHover : Theme.Panel;

        if (inLine)
            background = Theme.Lerp(background, Theme.Gold, 0.10f);

        drawList.AddRectFilled(pos, pos + size, Theme.U32(background), rounding);
        drawList.AddRect(pos, pos + size, Theme.U32(Theme.Border), rounding);

        if (inLine)
            drawList.AddRect(pos, pos + size, Theme.WithAlpha(Theme.Gold, 0.85f), rounding, 0, 2f * scale);
        else if (hovered)
            drawList.AddRect(pos, pos + size, Theme.WithAlpha(accent, 0.55f), rounding);

        if (compact)
        {
            if (marked)
            {
                var inset = new Vector2(2f * scale, 2f * scale);
                drawList.AddRectFilled(pos + inset, pos + size - inset, Theme.WithAlpha(accent, 0.85f), rounding * 0.6f);
            }

            return;
        }

        // Category dot + label.
        var dotCenter = pos + new Vector2(10f * scale, 11f * scale);
        drawList.AddCircleFilled(dotCenter, 2.6f * scale, Theme.WithAlpha(accent, marked ? 0.5f : 0.95f));
        drawList.AddText(
            font,
            9f * scale,
            new Vector2(pos.X + (17f * scale), pos.Y + (5f * scale)),
            Theme.WithAlpha(accent, marked ? 0.55f : 0.9f),
            Suggestion.CategoryName(square.Category).ToUpperInvariant());

        // Body copy.
        var textColor = marked ? Theme.Lerp(Theme.Text, Theme.Panel, 0.42f) : Theme.Text;
        var lines = Wrapped[index];
        if (lines is not null)
        {
            var y = pos.Y + (22f * scale);
            foreach (var line in lines)
            {
                drawList.AddText(font, bodySize, new Vector2(pos.X + (10f * scale), y), Theme.U32(textColor), line);
                y += lineHeight;
            }
        }

        if (marked)
        {
            var origin = new Vector2(pos.X + size.X - (16f * scale), pos.Y + (13f * scale));
            var checkColor = Theme.WithAlpha(auto ? Theme.Gold : accent, 0.95f);
            drawList.AddLine(origin, origin + new Vector2(3.5f * scale, 4f * scale), checkColor, 1.8f * scale);
            drawList.AddLine(origin + new Vector2(3.5f * scale, 4f * scale), origin + new Vector2(9f * scale, -5f * scale), checkColor, 1.8f * scale);
        }

        // Rarity pips, bottom right.
        var pips = Theme.RarityPips(square.Rarity);
        var pipColor = Theme.WithAlpha(Theme.RarityColor(square.Rarity), marked ? 0.4f : 0.85f);
        for (var p = 0; p < pips; p++)
        {
            var center = new Vector2(
                pos.X + size.X - (8f * scale) - (p * 5f * scale),
                pos.Y + size.Y - (8f * scale));
            drawList.AddCircleFilled(center, 1.6f * scale, pipColor);
        }

        // Face-down hint for unmarked "cursed" squares: a tiny gnawing detail in the corner.
        if (!marked && square.Rarity == BingoRarity.Cursed)
        {
            var center = new Vector2(pos.X + size.X - (8f * scale), pos.Y + (8f * scale));
            drawList.AddCircleFilled(center, 1.8f * scale, Theme.WithAlpha(Theme.CategoryColor(BingoCategory.Cursed), 0.55f));
        }
    }

    private static void EnsureWrapped(BingoCard card, float width, int maxLines)
    {
        if (card.Seed == cachedSeed
            && MathF.Abs(width - cachedTextWidth) < 0.75f
            && maxLines == cachedMaxLines)
        {
            return;
        }

        cachedSeed = card.Seed;
        cachedTextWidth = width;
        cachedMaxLines = maxLines;

        for (var i = 0; i < CardGenerator.CellCount; i++)
            Wrapped[i] = Wrap(card.Cells[i].Text, width, maxLines);
    }

    private static string[] Wrap(string text, float maxWidth, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (ImGui.CalcTextSize(candidate, false, 0f).X <= maxWidth || current.Length == 0)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
        }

        if (current.Length > 0)
            lines.Add(current);

        if (lines.Count <= maxLines)
            return lines.ToArray();

        var trimmed = lines.Take(maxLines).ToArray();
        trimmed[^1] = trimmed[^1] + "...";
        return trimmed;
    }
}
