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

/// <summary>One stored party-chat line.</summary>
[Serializable]
public sealed class ChatLine
{
    public DateTime Utc { get; set; }

    public string Sender { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;
}

/// <summary>A finished duty, ready to be shown or compared against.</summary>
[Serializable]
public sealed class RunRecap
{
    public string DutyName { get; set; } = string.Empty;

    public RecapKind Kind { get; set; }

    /// <summary>Game content type, e.g. "Dungeons", "Trials", "Raids".</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Required expansion, e.g. "A Realm Reborn", "Dawntrail".</summary>
    public string Expansion { get; set; } = string.Empty;

    /// <summary>Normal, Savage, Extreme, Ultimate, Unreal, Criterion, High-end.</summary>
    public string Difficulty { get; set; } = string.Empty;

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

    public List<ChatLine> Chat { get; set; } = new();

    public List<DateTime> WipeTimes { get; set; } = new();
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

    /// <summary>Keep the actual party chat lines so a run can be re-read later.</summary>
    public bool StoreChatLog { get; set; } = true;

    /// <summary>Upper bound on stored chat lines per run.</summary>
    public int ChatLineLimit { get; set; } = 40;

    public bool WindowOpen { get; set; }

    public List<RunRecap> History { get; set; } = new();

    public void Normalize()
    {
        this.History ??= new List<RunRecap>();
        if (this.History.Count > 50)
            this.History.RemoveRange(50, this.History.Count - 50);

        this.ChatLineLimit = Math.Clamp(this.ChatLineLimit, 5, 200);

        foreach (var run in this.History)
        {
            run.Chat ??= new List<ChatLine>();
            if (run.Chat.Count > this.ChatLineLimit)
                run.Chat.RemoveRange(this.ChatLineLimit, run.Chat.Count - this.ChatLineLimit);

            run.WipeTimes ??= new List<DateTime>();
            run.Players ??= new List<PlayerStat>();
        }
    }
}
