using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace DutyRecap;

public enum RecapKind
{
    Other,
    Dungeon,
    EightMan,
    Alliance,
    Pvp,
}

/// <summary>One player's numbers for a run.</summary>
[Serializable]
public sealed class PlayerStat
{
    public string Name { get; set; } = string.Empty;

    public uint JobId { get; set; }

    public string JobName { get; set; } = string.Empty;

    public int Deaths { get; set; }

    public int PeakVuln { get; set; }

    public bool IsSelf { get; set; }
}

/// <summary>A finished duty, ready to be shown or compared against.</summary>
[Serializable]
public sealed class RunRecap
{
    public string DutyName { get; set; } = string.Empty;

    public RecapKind Kind { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime FinishedUtc { get; set; }

    public bool Completed { get; set; }

    public int Wipes { get; set; }

    public int TotalDeaths { get; set; }

    public int PartyChatMessages { get; set; }

    public string FirstBloodName { get; set; } = string.Empty;

    public string Grade { get; set; } = string.Empty;

    public string Snark { get; set; } = string.Empty;

    public List<PlayerStat> Players { get; set; } = new();
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public bool AutoOpenOnComplete { get; set; } = true;

    public bool DetectDeaths { get; set; } = true;

    public bool DetectVulnStacks { get; set; } = true;

    public bool DetectChat { get; set; } = true;

    public bool WindowOpen { get; set; }

    public List<RunRecap> History { get; set; } = new();

    public void Normalize()
    {
        this.History ??= new List<RunRecap>();
        if (this.History.Count > 50)
            this.History.RemoveRange(50, this.History.Count - 50);
    }
}
