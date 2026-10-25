using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using RouletteBingo.Core;

namespace RouletteBingo;

[Serializable]
public sealed class RunRecord
{
    public string DutyName { get; set; } = string.Empty;

    public int Marked { get; set; }

    public int Bingos { get; set; }

    public int Seconds { get; set; }

    public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;

    public bool Completed { get; set; }
}

/// <summary>Everything needed to rebuild the live card after a plugin reload.</summary>
[Serializable]
public sealed class CardState
{
    public string DutyName { get; set; } = string.Empty;

    public ulong Seed { get; set; }

    public DutyKind Kind { get; set; }

    public uint Marks { get; set; }

    public uint AutoMarks { get; set; }

    public long StartedUtcTicks { get; set; }

    public long FinishedUtcTicks { get; set; }

    public int PartyChatMessages { get; set; }

    public int Wipes { get; set; }
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>Roll a card automatically when a duty starts.</summary>
    public bool AutoStartCards { get; set; } = true;

    public bool AutoDetectDeaths { get; set; } = true;

    public bool AutoDetectChat { get; set; } = true;

    public bool AutoDetectVulnStacks { get; set; } = true;

    /// <summary>Occasional deadpan commentary in your own echo channel when you mark a square.</summary>
    public bool Snark { get; set; } = true;

    /// <summary>Off by default: never talk in party chat unless the player opts in.</summary>
    public bool AnnounceBingoInParty { get; set; }

    /// <summary>Cards made of nothing but the cursed squares.</summary>
    public bool CursedMode { get; set; }

    /// <summary>Draw the small 5x5 dot grid instead of the full card.</summary>
    public bool CompactGrid { get; set; }

    public bool ShowHistory { get; set; } = true;

    public float UiScale { get; set; } = 1.0f;

    public bool WindowOpen { get; set; }

    public CardState? ActiveCard { get; set; }

    public List<RunRecord> History { get; set; } = new();

    /// <summary>How often you have personally marked each square. Your personal curses.</summary>
    public Dictionary<string, int> SquareCounts { get; set; } = new();

    public int TotalCards { get; set; }

    public int TotalBingos { get; set; }

    public void Normalize()
    {
        this.UiScale = Math.Clamp(this.UiScale, 0.75f, 1.6f);

        if (this.History is null)
            this.History = new List<RunRecord>();
        if (this.History.Count > 25)
            this.History.RemoveRange(25, this.History.Count - 25);

        this.SquareCounts ??= new Dictionary<string, int>();
        this.TotalCards = Math.Max(0, this.TotalCards);
        this.TotalBingos = Math.Max(0, this.TotalBingos);
    }
}
