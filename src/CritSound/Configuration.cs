using System;
using Dalamud.Configuration;

namespace CritSound;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Master switch.</summary>
    public bool Enabled { get; set; } = true;

    public bool CritEnabled { get; set; } = true;
    public string CritSound { get; set; } = "crit.wav";
    public int CritVolume { get; set; } = 70;

    public bool DirectHitEnabled { get; set; }
    public string DirectHitSound { get; set; } = "direct-hit.wav";
    public int DirectHitVolume { get; set; } = 55;

    public bool CritDirectHitEnabled { get; set; } = true;
    public string CritDirectHitSound { get; set; } = "crit-direct-hit.wav";
    public int CritDirectHitVolume { get; set; } = 90;

    /// <summary>Auto-attacks and damage-over-time ticks show up as their own flying-text kinds.</summary>
    public bool IncludeAutoAttacksAndDots { get; set; } = true;

    /// <summary>Minimum time between two sounds, in milliseconds. Keeps burst windows from turning into a machine gun.</summary>
    public int MinIntervalMs { get; set; } = 60;

    /// <summary>Stay quiet while the game's UI is hidden (cutscenes, character creation, etc).</summary>
    public bool OnlyWhenGameUiVisible { get; set; } = true;

    public (bool Enabled, string File, int Volume) Slot(CritTrigger trigger) => trigger switch
    {
        CritTrigger.Crit => (this.CritEnabled, this.CritSound, this.CritVolume),
        CritTrigger.DirectHit => (this.DirectHitEnabled, this.DirectHitSound, this.DirectHitVolume),
        _ => (this.CritDirectHitEnabled, this.CritDirectHitSound, this.CritDirectHitVolume),
    };

    public void Normalize()
    {
        this.MinIntervalMs = Math.Clamp(this.MinIntervalMs, 0, 2000);
        this.CritVolume = Math.Clamp(this.CritVolume, 0, 100);
        this.DirectHitVolume = Math.Clamp(this.DirectHitVolume, 0, 100);
        this.CritDirectHitVolume = Math.Clamp(this.CritDirectHitVolume, 0, 100);

        this.CritSound ??= "crit.wav";
        this.DirectHitSound ??= "direct-hit.wav";
        this.CritDirectHitSound ??= "crit-direct-hit.wav";
    }
}
