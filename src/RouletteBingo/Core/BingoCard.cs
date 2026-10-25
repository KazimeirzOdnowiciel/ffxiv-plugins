using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RouletteBingo.Core;

public readonly record struct BingoLine(string Label, int[] Cells);

/// <summary>
/// A live card: the 25 squares plus what the player has marked.
/// </summary>
public sealed class BingoCard
{
    private readonly bool[] marked = new bool[CardGenerator.CellCount];
    private readonly bool[] auto = new bool[CardGenerator.CellCount];

    public BingoCard(ulong seed, DutyKind kind, string dutyName, Suggestion[] cells, DateTime startedUtc)
    {
        if (cells.Length != CardGenerator.CellCount)
            throw new ArgumentException($"A card needs {CardGenerator.CellCount} cells.", nameof(cells));

        this.Seed = seed;
        this.Kind = kind;
        this.DutyName = dutyName;
        this.Cells = cells;
        this.StartedUtc = startedUtc;

        // The centre square is free and always counts.
        this.marked[CardGenerator.CenterIndex] = true;
    }

    public ulong Seed { get; }

    public DutyKind Kind { get; }

    public string DutyName { get; set; }

    public Suggestion[] Cells { get; }

    public DateTime StartedUtc { get; }

    public DateTime? FirstBingoUtc { get; private set; }

    public bool Finished { get; private set; }

    public DateTime? FinishedUtc { get; private set; }

    /// <summary>Stops the clock. Safe to call more than once.</summary>
    public void Finish(DateTime now)
    {
        if (this.Finished)
            return;

        this.Finished = true;
        this.FinishedUtc = now;
    }

    public bool IsMarked(int index) => this.marked[index];

    public bool IsAuto(int index) => this.auto[index];

    public int MarkedCount => this.marked.Count(m => m);

    public uint MarksMask
    {
        get
        {
            var mask = 0u;
            for (var i = 0; i < this.marked.Length; i++)
            {
                if (this.marked[i])
                    mask |= 1u << i;
            }

            return mask;
        }
    }

    public IReadOnlyList<BingoLine> CompletedLines
    {
        get
        {
            var lines = new List<BingoLine>(12);

            for (var row = 0; row < CardGenerator.GridSize; row++)
            {
                var cells = Enumerable.Range(0, CardGenerator.GridSize).Select(c => (row * CardGenerator.GridSize) + c).ToArray();
                if (cells.All(i => this.marked[i]))
                    lines.Add(new BingoLine($"Row {row + 1}", cells));
            }

            for (var col = 0; col < CardGenerator.GridSize; col++)
            {
                var cells = Enumerable.Range(0, CardGenerator.GridSize).Select(r => (r * CardGenerator.GridSize) + col).ToArray();
                if (cells.All(i => this.marked[i]))
                    lines.Add(new BingoLine($"Column {col + 1}", cells));
            }

            var diagonal = Enumerable.Range(0, CardGenerator.GridSize).Select(i => (i * CardGenerator.GridSize) + i).ToArray();
            if (diagonal.All(i => this.marked[i]))
                lines.Add(new BingoLine("Diagonal", diagonal));

            var anti = Enumerable.Range(0, CardGenerator.GridSize).Select(i => (i * CardGenerator.GridSize) + (CardGenerator.GridSize - 1 - i)).ToArray();
            if (anti.All(i => this.marked[i]))
                lines.Add(new BingoLine("Anti-diagonal", anti));

            return lines;
        }
    }

    public bool HasBingo => this.CompletedLines.Count > 0;

    /// <summary>Marks or unmarks a square. Returns true when something actually changed.</summary>
    public bool Toggle(int index, DateTime now)
    {
        if (index < 0 || index >= CardGenerator.CellCount)
            return false;

        if (this.marked[index])
        {
            this.marked[index] = false;
            this.auto[index] = false;
            return true;
        }

        this.marked[index] = true;
        this.auto[index] = false;

        if (this.FirstBingoUtc is null && this.HasBingo)
            this.FirstBingoUtc = now;

        return true;
    }

    /// <summary>Marks every square on the card that listens to <paramref name="detector"/>.</summary>
    public List<int> AutoMark(string detector, DateTime now)
    {
        var hits = new List<int>();
        if (string.IsNullOrEmpty(detector))
            return hits;

        for (var i = 0; i < this.Cells.Length; i++)
        {
            if (this.marked[i])
                continue;

            if (!string.Equals(this.Cells[i].Auto, detector, StringComparison.OrdinalIgnoreCase))
                continue;

            this.marked[i] = true;
            this.auto[i] = true;
            hits.Add(i);
        }

        if (hits.Count > 0 && this.FirstBingoUtc is null && this.HasBingo)
            this.FirstBingoUtc = now;

        return hits;
    }

    public void ApplyMarks(uint mask, uint autoMask = 0)
    {
        for (var i = 0; i < this.marked.Length; i++)
        {
            this.marked[i] = (mask & (1u << i)) != 0;
            this.auto[i] = (autoMask & (1u << i)) != 0 && this.marked[i];
        }
    }

    public uint AutoMask
    {
        get
        {
            var mask = 0u;
            for (var i = 0; i < this.auto.Length; i++)
            {
                if (this.auto[i] && this.marked[i])
                    mask |= 1u << i;
            }

            return mask;
        }
    }

    public string ToReport(DateTime? now = null)
    {
        var sb = new StringBuilder();
        var elapsed = (now ?? DateTime.UtcNow) - this.StartedUtc;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var lines = this.CompletedLines;
        var bingoText = lines.Count == 0
            ? "no bingo yet"
            : $"{lines.Count} bingo{(lines.Count == 1 ? string.Empty : "s")} ({string.Join(", ", lines.Select(l => l.Label))})";

        sb.AppendLine($"ROULETTE BINGO - {this.DutyName}");
        sb.AppendLine($"{DescribeKind(this.Kind)} | {elapsed:hh\\:mm\\:ss} | {this.MarkedCount}/{CardGenerator.CellCount} squares | {bingoText}");
        sb.AppendLine();

        for (var row = 0; row < CardGenerator.GridSize; row++)
        {
            var cells = new StringBuilder();
            for (var col = 0; col < CardGenerator.GridSize; col++)
            {
                var i = (row * CardGenerator.GridSize) + col;
                cells.Append(this.marked[i] ? "[X]" : "[ ]");
            }

            sb.AppendLine(cells.ToString());
        }

        sb.AppendLine();
        for (var i = 0; i < this.Cells.Length; i++)
        {
            if (i == CardGenerator.CenterIndex)
                continue;

            var mark = this.marked[i] ? "X" : " ";
            sb.AppendLine($"[{mark}] {this.Cells[i].Text}");
        }

        sb.AppendLine();
        sb.AppendLine($"Card code: {ShareCode.Encode(this.Seed, this.MarksMask, this.Kind)}");
        return sb.ToString().TrimEnd();
    }

    public static string DescribeKind(DutyKind kind) => kind switch
    {
        DutyKind.Dungeon => "4-player dungeon",
        DutyKind.EightMan => "8-player duty",
        DutyKind.Alliance => "24-player alliance raid",
        DutyKind.Pvp => "PvP",
        _ => "duty",
    };
}
