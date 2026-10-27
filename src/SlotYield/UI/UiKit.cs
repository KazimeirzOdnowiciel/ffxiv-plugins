using System;
using System.Collections.Generic;
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

    /// <summary>A drawn empty state: brass diamond, centred title, centred body.</summary>
    public static void EmptyState(string title, string body)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        var center = new Vector2(origin.X + (width * 0.5f), origin.Y + 24f);
        drawList.AddQuadFilled(
            center + new Vector2(0f, -11f),
            center + new Vector2(11f, 0f),
            center + new Vector2(0f, 11f),
            center + new Vector2(-11f, 0f),
            Theme.WithAlpha(Theme.Accent, 0.55f));

        ImGui.Dummy(new Vector2(width, 44f));

        var titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (width - titleSize.X) * 0.5f));
        Theme.Colored(Theme.Text, title);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (width * 0.72f));
        ImGui.TextWrapped(body);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>A circular gauge: track plus an arc for the fraction used.</summary>
    public static void Ring(ImDrawListPtr list, Vector2 center, float radius, float thickness, float fraction, Vector4 color)
    {
        var arcRadius = radius - (thickness * 0.5f);
        const int steps = 48;

        DrawArc(list, center, arcRadius, 0f, MathF.PI * 2f, steps, Theme.WithAlpha(Theme.Text, 0.18f), thickness);

        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped > 0f)
            DrawArc(list, center, arcRadius, -MathF.PI * 0.5f, (-MathF.PI * 0.5f) + (MathF.PI * 2f * clamped), Math.Max(2, (int)(steps * clamped)), Theme.U32(color), thickness);
    }

    private static void DrawArc(ImDrawListPtr list, Vector2 center, float arcRadius, float start, float end, int steps, uint color, float thickness)
    {
        var previous = new Vector2(center.X + (MathF.Cos(start) * arcRadius), center.Y + (MathF.Sin(start) * arcRadius));

        for (var index = 1; index <= steps; index++)
        {
            var angle = start + ((end - start) * index / steps);
            var next = new Vector2(center.X + (MathF.Cos(angle) * arcRadius), center.Y + (MathF.Sin(angle) * arcRadius));
            list.AddLine(previous, next, color, thickness);
            previous = next;
        }
    }

    /// <summary>Three dots filled by confidence, so a row's sample quality is visible at a glance.</summary>
    public static void ConfidenceDots(double confidence, float dotRadius = 2.2f)
    {
        var filled = (int)MathF.Ceiling(Math.Clamp((float)confidence, 0f, 1f) * 3f);
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var y = start.Y + (ImGui.GetTextLineHeight() * 0.5f);

        for (var index = 0; index < 3; index++)
        {
            var center = new Vector2(start.X + 3f + (index * 5.5f), y);
            var dotColor = index < filled ? Theme.Accent : new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.22f);
            drawList.AddCircleFilled(center, dotRadius, Theme.U32(dotColor));
        }

        ImGui.Dummy(new Vector2(3f + (3f * 5.5f), ImGui.GetTextLineHeight()));
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

        drawList.AddRectFilled(pos, pos + size, Theme.WithAlpha(color, 0.22f), size.Y / 2f);
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + new Vector2(padX, padY), Theme.U32(color), label);
        ImGui.Dummy(size);
    }

    /// <summary>
    /// A section title that reads as chrome rather than a stock label: a small brass diamond,
    /// the title in dimmed text, then a hairline that trails off to the right edge.
    /// </summary>
    public static void SectionHeader(string label, Vector4? accent = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var origin = ImGui.GetCursorScreenPos();
        var height = ImGui.GetTextLineHeight();
        var mid = origin.Y + (height * 0.5f);
        var diamondColor = accent ?? Theme.Accent;
        var labelColor = accent ?? Theme.TextDim;

        var center = new Vector2(origin.X + 4f, mid);
        drawList.AddQuadFilled(
            center + new Vector2(0f, -3.2f),
            center + new Vector2(3.2f, 0f),
            center + new Vector2(0f, 3.2f),
            center + new Vector2(-3.2f, 0f),
            Theme.U32(diamondColor));

        drawList.AddText(font, ImGui.GetFontSize(), origin + new Vector2(14f, 0f), Theme.U32(labelColor), label);

        var textEnd = origin.X + 14f + ImGui.CalcTextSize(label).X + 10f;
        var right = origin.X + ImGui.GetContentRegionAvail().X;
        if (right > textEnd)
            drawList.AddLine(new Vector2(textEnd, mid), new Vector2(right, mid), Theme.WithAlpha(Theme.Text, 0.07f));

        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, height + 7f));
    }

    /// <summary>
    /// The retainer slot strip: one cell per portfolio slot, filled ones brass-shaded.
    /// This is the window's signature element — the product's model (N slots competing for
    /// gil/day) made visible without opening a tab.
    /// </summary>
    public static void SlotStrip(ImDrawListPtr list, Vector2 origin, float width, float height, int total, int filled, out int hovered, IReadOnlyList<(uint IconId, bool Hq)>? icons = null, float pulse = 0f)
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
                if (i == filledCount - 1 && pulse > 0f)
                    tint = Theme.Lerp(tint, Theme.AccentBright, pulse);

                list.AddRectFilled(min, max, Theme.U32(tint), rounding);

                if (icons is not null && i < icons.Count)
                {
                    var (iconId, hq) = icons[i];
                    if (IconCache.TryGet(iconId, hq, out var iconWrap) && iconWrap is not null)
                    {
                        var side = MathF.Max(4f, height - 4f);
                        var iconMin = new Vector2(min.X + ((cell - side) * 0.5f), min.Y + ((height - side) * 0.5f));
                        list.AddImage(iconWrap.Handle, iconMin, iconMin + new Vector2(side, side));
                    }
                }
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

        // A highlight sweeps the filled part while the scan runs, so the bar reads as active.
        var filledWidth = filledEnd.X - origin.X;
        if (filledWidth > height * 2f)
        {
            var phase = (float)(ImGui.GetTime() % 1.6) / 1.6f;
            var bandWidth = MathF.Max(height * 2f, filledWidth * 0.20f);
            var bandX = origin.X - bandWidth + ((filledWidth + bandWidth) * phase);
            var bandMin = MathF.Max(origin.X, bandX);
            var bandMax = MathF.Min(filledEnd.X, bandX + bandWidth);
            if (bandMax > bandMin)
                list.AddRectFilled(new Vector2(bandMin, origin.Y), new Vector2(bandMax, origin.Y + height), Theme.WithAlpha(Theme.AccentBright, 0.55f), rounding);
        }
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
