using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlotYield.UI;

/// <summary>
/// Small drawing primitives that give Slot Yield its own look. Everything here is a few
/// hundred vertices at most, so the window stays inside the plugin's draw budget.
/// </summary>
internal static class UiKit
{
    /// <summary>A hairline across the window, for separating the header from the content.</summary>
    public static void Rule(float alpha = 0.06f)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.Dummy(new Vector2(width, 1f));
        ImGui.GetWindowDrawList().AddLine(origin, origin + new Vector2(width, 0f), Theme.WithAlpha(Theme.Text, alpha));
    }

    /// <summary>A small rounded label, for scannable one-line lists (retainers, tags, statuses).</summary>
    public static void Chip(string label, Vector4 color)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var textSize = ImGui.CalcTextSize(label);
        const float padX = 7f;
        const float padY = 2.5f;
        var size = new Vector2(textSize.X + (padX * 2f), textSize.Y + (padY * 2f));

        drawList.AddRectFilled(pos, pos + size, Theme.WithAlpha(color, 0.14f), size.Y / 2f);
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + new Vector2(padX, padY), Theme.U32(color), label);
        ImGui.Dummy(size);
    }

    /// <summary>
    /// A section title that reads as chrome rather than a stock label: a small brass diamond,
    /// the title in dimmed text, then a hairline that trails off to the right edge.
    /// </summary>
    public static void SectionHeader(string label)
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var origin = ImGui.GetCursorScreenPos();
        var height = ImGui.GetTextLineHeight();
        var mid = origin.Y + (height * 0.5f);

        var center = new Vector2(origin.X + 4f, mid);
        drawList.AddQuadFilled(
            center + new Vector2(0f, -3.2f),
            center + new Vector2(3.2f, 0f),
            center + new Vector2(0f, 3.2f),
            center + new Vector2(-3.2f, 0f),
            Theme.U32(Theme.Accent));

        drawList.AddText(font, ImGui.GetFontSize(), origin + new Vector2(14f, 0f), Theme.U32(Theme.TextDim), label);

        var textEnd = origin.X + 14f + ImGui.CalcTextSize(label).X + 10f;
        var right = origin.X + ImGui.GetContentRegionAvail().X;
        if (right > textEnd)
            drawList.AddLine(new Vector2(textEnd, mid), new Vector2(right, mid), Theme.WithAlpha(Theme.Text, 0.07f));

        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, height + 3f));
    }

    /// <summary>
    /// The retainer slot strip: one cell per portfolio slot, filled ones brass-shaded.
    /// This is the window's signature element — the product's model (N slots competing for
    /// gil/day) made visible without opening a tab.
    /// </summary>
    public static void SlotStrip(ImDrawListPtr list, Vector2 origin, float width, float height, int total, int filled, out int hovered)
    {
        hovered = -1;
        if (total <= 0 || width <= 0f)
            return;

        var gap = 2f;
        var cell = (width - (gap * (total - 1))) / total;
        var rounding = MathF.Min(2.5f, cell * 0.25f);
        var mouse = ImGui.GetMousePos();
        var filledCount = Math.Clamp(filled, 0, total);

        for (var i = 0; i < total; i++)
        {
            var min = new Vector2(origin.X + (i * (cell + gap)), origin.Y);
            var max = min + new Vector2(cell, height);
            var over = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;

            if (over)
                hovered = i;

            if (i < filledCount)
            {
                var tint = Theme.Lerp(Theme.Accent, Theme.AccentBright, total <= 1 ? 0f : i / (float)(total - 1));
                list.AddRectFilled(min, max, Theme.U32(tint), rounding);
            }
            else
            {
                list.AddRectFilled(min, max, Theme.WithAlpha(Theme.PanelRaised, 0.85f), rounding);
                list.AddRect(min, max, Theme.U32(Theme.Border), rounding);
            }

            if (over)
            {
                list.AddRect(min, max, Theme.U32(Theme.AccentBright), rounding, 1.2f);
            }
        }
    }

    /// <summary>A thin progress track, used for the scan line in the header.</summary>
    public static void ProgressLine(ImDrawListPtr list, Vector2 origin, float width, float height, float fraction)
    {
        if (width <= 0f)
            return;

        var rounding = height * 0.5f;
        var end = origin + new Vector2(width, height);
        list.AddRectFilled(origin, end, Theme.WithAlpha(Theme.PanelRaised, 0.9f), rounding);

        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped <= 0f)
            return;

        var filledEnd = new Vector2(origin.X + MathF.Max(height, width * clamped), origin.Y + height);
        list.AddRectFilled(origin, filledEnd, Theme.U32(Theme.Accent), rounding);
    }

    /// <summary>A 0..1 bar for expected share, drawn inline in a table cell.</summary>
    public static void Bar(ImDrawListPtr list, Vector2 origin, Vector2 size, float fraction, Vector4 color)
    {
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var rounding = size.Y * 0.5f;
        list.AddRectFilled(origin, origin + size, Theme.WithAlpha(Theme.PanelRaised, 0.9f), rounding);

        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped > 0f)
        {
            list.AddRectFilled(origin, origin + new Vector2(MathF.Max(size.Y, size.X * clamped), size.Y), Theme.U32(color), rounding);
        }
    }

    /// <summary>
    /// A filter toggle that reads as a chip instead of a bare ImGui checkbox: tinted when on,
    /// outlined when off. Same interaction, far less "debug window".
    /// </summary>
    public static bool ToggleChip(string label, ref bool value)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, value ? Theme.WithAlpha(Theme.Accent, 0.30f) : Theme.U32(Theme.PanelRaised));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, value ? Theme.WithAlpha(Theme.Accent, 0.42f) : Theme.U32(Theme.PanelHover));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.WithAlpha(Theme.Accent, 0.50f));
        ImGui.PushStyleColor(ImGuiCol.Text, value ? Theme.Text : Theme.TextDim);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        if (clicked)
        {
            value = !value;
        }

        return clicked;
    }

    /// <summary>Icon id for an item row, or 0 when the sheet has no such row.</summary>
    public static uint IconIdForItem(uint itemId)
    {
        try
        {
            return Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRow(itemId).Icon;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Item icon at text height, with a blank of the same width when the texture is not ready.</summary>
    public static void ItemIcon(uint iconId, bool hq, float size)
    {
        if (IconCache.TryGet(iconId, hq, out var wrap) && wrap is not null)
        {
            ImGui.Image(wrap.Handle, new Vector2(size, size));
            return;
        }

        ImGui.Dummy(new Vector2(size, size));
    }
}
