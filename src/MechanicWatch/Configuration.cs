using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace MechanicWatch;

/// <summary>One status the window tracks, with the name captured at the time it was added.</summary>
public sealed record WatchEntry(uint StatusId, string Name);

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool WindowOpen { get; set; } = true;

    public List<WatchEntry> Watch { get; set; } = new();

    /// <summary>Add statuses by itself while in combat, instead of relying on Capture.</summary>
    public bool AutoLearn { get; set; } = true;

    /// <summary>Statuses removed by hand, so auto-learn does not put them back.</summary>
    public List<uint> Ignored { get; set; } = new();

    /// <summary>Longest remaining time that still counts as a mechanic status when auto-learning.</summary>
    public int AutoMaxSeconds { get; set; } = 30;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void Normalize()
    {
        this.Watch ??= new List<WatchEntry>();
        this.Ignored ??= new List<uint>();
        this.AutoMaxSeconds = Math.Clamp(this.AutoMaxSeconds, 3, 120);

        if (this.Watch.Count > 80)
            this.Watch.RemoveRange(0, this.Watch.Count - 80);
    }
}
