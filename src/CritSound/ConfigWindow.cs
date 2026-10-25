using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CritSound;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Crit Sound###CritSoundConfig")
    {
        this.plugin = plugin;
        this.Size = new Vector2(560, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    private Configuration Config => this.plugin.Configuration;

    public override void Draw()
    {
        var config = this.Config;
        var changed = false;

        ImGui.TextUnformatted("Audio feedback for critical hits, direct hits, and critical direct hits.");
        ImGui.Spacing();

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Enabled = enabled;
            changed = true;
        }

        ImGui.Separator();

        changed |= this.DrawSlot("Critical hit", CritTrigger.Crit);
        changed |= this.DrawSlot("Direct hit", CritTrigger.DirectHit);
        changed |= this.DrawSlot("Critical direct hit", CritTrigger.CritDirectHit);

        ImGui.Separator();

        var autoAndDots = config.IncludeAutoAttacksAndDots;
        if (ImGui.Checkbox("Also react to auto-attacks and damage-over-time ticks", ref autoAndDots))
        {
            config.IncludeAutoAttacksAndDots = autoAndDots;
            changed = true;
        }

        var onlyWithUi = config.OnlyWhenGameUiVisible;
        if (ImGui.Checkbox("Stay silent while the game UI is hidden", ref onlyWithUi))
        {
            config.OnlyWhenGameUiVisible = onlyWithUi;
            changed = true;
        }

        var minInterval = config.MinIntervalMs;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderInt("Minimum gap between sounds (ms)", ref minInterval, 0, 1000))
        {
            config.MinIntervalMs = minInterval;
            config.Normalize();
            changed = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Sounds folder");
        ImGui.TextWrapped(this.plugin.Sounds.SoundsDirectory);

        if (ImGui.Button("Open folder"))
            OpenFolder(this.plugin.Sounds.SoundsDirectory);

        ImGui.SameLine();
        if (ImGui.Button("Copy path"))
            ImGui.SetClipboardText(this.plugin.Sounds.SoundsDirectory);

        ImGui.SameLine();
        if (ImGui.Button("Rescan"))
        {
            this.plugin.Sounds.Rescan();
            Plugin.Log.Information("[CritSound] Rescanned sounds folder: {Count} file(s).", this.plugin.Sounds.Available.Count);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Drop your own 16-bit PCM .wav files into that folder, then hit Rescan and pick them above. The bundled sounds are copied there on first run.");

        ImGui.Separator();

        if (ImGui.Button("Reset to defaults"))
        {
            this.ResetToDefaults();
            changed = true;
        }

        if (changed)
            this.plugin.SaveConfig();
    }

    private bool DrawSlot(string label, CritTrigger trigger)
    {
        var slot = this.Config.Slot(trigger);
        var enabled = slot.Enabled;
        var file = slot.File;
        var volume = slot.Volume;
        var changed = false;

        ImGui.Spacing();
        ImGui.TextUnformatted(label);

        if (ImGui.Checkbox($"Enabled###enabled{trigger}", ref enabled))
            changed = true;

        ImGui.SameLine();
        if (ImGui.Button($"Test###test{trigger}"))
            this.plugin.Preview(trigger);

        ImGui.SetNextItemWidth(250);
        if (DrawSoundCombo($"Sound###sound{trigger}", ref file, this.plugin.Sounds.Available))
            changed = true;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(170);
        var roundedVolume = Math.Clamp(volume, 0, 100);
        if (ImGui.SliderInt($"Volume###volume{trigger}", ref roundedVolume, 0, 100, "%d%%"))
        {
            volume = roundedVolume;
            changed = true;
        }

        if (!changed)
            return false;

        SetSlot(this.Config, trigger, enabled, file, volume);
        return true;
    }

    private static void SetSlot(Configuration config, CritTrigger trigger, bool enabled, string file, int volume)
    {
        switch (trigger)
        {
            case CritTrigger.Crit:
                config.CritEnabled = enabled;
                config.CritSound = file;
                config.CritVolume = volume;
                break;
            case CritTrigger.DirectHit:
                config.DirectHitEnabled = enabled;
                config.DirectHitSound = file;
                config.DirectHitVolume = volume;
                break;
            default:
                config.CritDirectHitEnabled = enabled;
                config.CritDirectHitSound = file;
                config.CritDirectHitVolume = volume;
                break;
        }
    }

    private static bool DrawSoundCombo(string id, ref string fileName, System.Collections.Generic.IReadOnlyList<string> available)
    {
        if (available.Count == 0)
        {
            ImGui.TextUnformatted("(no .wav files found)");
            return false;
        }

        var items = new string[available.Count];
        for (var i = 0; i < available.Count; i++)
            items[i] = available[i];

        var currentName = fileName;
        var index = Array.FindIndex(items, item => string.Equals(item, currentName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            index = 0;

        if (!ImGui.Combo(id, ref index, items, items.Length))
            return false;

        fileName = items[index];
        return true;
    }

    private void ResetToDefaults()
    {
        var defaults = new Configuration();

        this.Config.CritEnabled = defaults.CritEnabled;
        this.Config.CritSound = defaults.CritSound;
        this.Config.CritVolume = defaults.CritVolume;

        this.Config.DirectHitEnabled = defaults.DirectHitEnabled;
        this.Config.DirectHitSound = defaults.DirectHitSound;
        this.Config.DirectHitVolume = defaults.DirectHitVolume;

        this.Config.CritDirectHitEnabled = defaults.CritDirectHitEnabled;
        this.Config.CritDirectHitSound = defaults.CritDirectHitSound;
        this.Config.CritDirectHitVolume = defaults.CritDirectHitVolume;

        this.Config.IncludeAutoAttacksAndDots = defaults.IncludeAutoAttacksAndDots;
        this.Config.MinIntervalMs = defaults.MinIntervalMs;
        this.Config.OnlyWhenGameUiVisible = defaults.OnlyWhenGameUiVisible;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Explorer refusing to open is not worth taking the plugin down for.
        }
    }
}
