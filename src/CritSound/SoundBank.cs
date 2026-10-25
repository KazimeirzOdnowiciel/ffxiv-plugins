using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CritSound;

/// <summary>
/// Owns the sounds folder, the file catalog, and the playback path.
/// </summary>
public sealed class SoundBank : IDisposable
{
    private readonly IPluginLog log;
    private readonly string userDirectory;
    private readonly Dictionary<string, byte[]?> scaled = new(StringComparer.OrdinalIgnoreCase);

    public SoundBank(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;

        this.userDirectory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "sounds");
        System.IO.Directory.CreateDirectory(this.userDirectory);

        this.InstallBundledSounds(pluginInterface);
        this.Available = this.Scan();
    }

    /// <summary>File names of every .wav currently in the sounds folder.</summary>
    public IReadOnlyList<string> Available { get; private set; }

    public string SoundsDirectory => this.userDirectory;

    public IReadOnlyList<string> Rescan()
    {
        this.scaled.Clear();
        this.Available = this.Scan();
        return this.Available;
    }

    public bool Play(string fileName, int volume)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var path = Path.Combine(this.userDirectory, fileName);
        if (!File.Exists(path))
        {
            this.log.Warning("[CritSound] Sound file not found: {Path}", path);
            return false;
        }

        volume = Math.Clamp(volume, 0, 100);
        if (volume == 0)
            return false;

        // Cache the scaled variant, not just the file, so a burst of hits does no extra work.
        var key = $"{fileName}|{volume}";
        if (!this.scaled.TryGetValue(key, out var data))
        {
            data = Scale(File.ReadAllBytes(path), volume);
            this.scaled[key] = data;
        }

        // Anything we cannot parse as 16-bit PCM still plays, just without volume control.
        var played = data is not null ? Winmm.PlayMemory(data) : Winmm.PlayFile(path);
        if (!played)
            this.log.Debug("[CritSound] Playback failed for {File}.", fileName);

        return played;
    }

    public void Dispose()
    {
        this.scaled.Clear();
        Winmm.ReleaseAll();
    }

    private IReadOnlyList<string> Scan()
    {
        try
        {
            return System.IO.Directory
                .GetFiles(this.userDirectory, "*.wav")
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception)
        {
            this.log.Error(exception, "[CritSound] Could not list the sounds folder.");
            return Array.Empty<string>();
        }
    }

    private void InstallBundledSounds(IDalamudPluginInterface pluginInterface)
    {
        var bundled = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName ?? ".", "Sounds");
        if (!System.IO.Directory.Exists(bundled))
            return;

        foreach (var source in System.IO.Directory.GetFiles(bundled, "*.wav"))
        {
            var destination = Path.Combine(this.userDirectory, Path.GetFileName(source));
            if (File.Exists(destination))
                continue;

            try
            {
                File.Copy(source, destination);
            }
            catch (Exception exception)
            {
                this.log.Warning(exception, "[CritSound] Could not copy {File}", source);
            }
        }
    }

    /// <summary>
    /// Applies a linear gain to a 16-bit PCM RIFF/WAVE file. Returns null for anything else.
    /// </summary>
    private static byte[]? Scale(byte[] wav, int volume)
    {
        if (wav.Length < 44)
            return null;

        if (BitConverter.ToInt32(wav, 0) != 0x46464952 || BitConverter.ToInt32(wav, 8) != 0x45564157)
            return null;

        var position = 12;
        var format = -1;
        var dataOffset = -1;
        var dataLength = 0;

        while (position + 8 <= wav.Length)
        {
            var chunkId = BitConverter.ToInt32(wav, position);
            var chunkSize = BitConverter.ToInt32(wav, position + 4);
            if (chunkSize < 0)
                return null;

            if (chunkId == 0x20746D66) // "fmt "
            {
                format = BitConverter.ToUInt16(wav, position + 8);
                var bitsPerSample = BitConverter.ToInt16(wav, position + 22);
                if (bitsPerSample != 16)
                    return null;
            }
            else if (chunkId == 0x61746164) // "data"
            {
                dataOffset = position + 8;
                dataLength = Math.Min(chunkSize, wav.Length - dataOffset);
                break;
            }

            position += 8 + chunkSize + (chunkSize & 1);
        }

        if (format != 1 || dataOffset < 0 || dataLength <= 0)
            return null;

        if (volume >= 100)
            return wav;

        var result = (byte[])wav.Clone();
        var gain = volume / 100f;

        for (var i = 0; i + 1 < dataLength; i += 2)
        {
            var sample = BitConverter.ToInt16(result, dataOffset + i);
            var scaledSample = (int)(sample * gain);
            scaledSample = Math.Clamp(scaledSample, short.MinValue, short.MaxValue);
            result[dataOffset + i] = (byte)(scaledSample & 0xFF);
            result[dataOffset + i + 1] = (byte)((scaledSample >> 8) & 0xFF);
        }

        return result;
    }
}
