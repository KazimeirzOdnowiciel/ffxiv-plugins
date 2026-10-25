using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlotYield.Market;

/// <summary>
/// Small file cache in the plugin's config directory. Scans are minutes of HTTP, so they are worth
/// keeping across sessions; the envelope carries a schema version and a timestamp so stale data is
/// dropped instead of being quietly interpreted with a newer shape.
/// </summary>
public sealed class MarketCache
{
    /// <summary>Bump when the persisted shapes change; old files are discarded on read.</summary>
    public const int SchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string directory;
    private readonly Action<string>? log;

    public MarketCache(string directory, Action<string>? log = null)
    {
        this.directory = directory;
        this.log = log;
        Directory.CreateDirectory(directory);
    }

    public string DirectoryPath => this.directory;

    public bool TryLoad<T>(string key, TimeSpan maxAge, out T? value)
        where T : class
    {
        value = null;
        var path = this.PathFor(key);
        if (!File.Exists(path))
            return false;

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<T>>(File.ReadAllText(path), JsonOptions);

            if (envelope is null || envelope.Payload is null)
            {
                this.Discard(path, key, "empty envelope");
                return false;
            }

            if (envelope.Schema != SchemaVersion)
            {
                this.Discard(path, key, $"schema {envelope.Schema} != {SchemaVersion}");
                return false;
            }

            var age = DateTime.UtcNow - envelope.SavedUtc;
            if (age > maxAge)
            {
                this.log?.Invoke($"cache expired for {key} ({age.TotalMinutes:0} min old)");
                return false;
            }

            value = envelope.Payload;
            return true;
        }
        catch (Exception ex)
        {
            this.Discard(path, key, ex.Message);
            return false;
        }
    }

    public void Save<T>(string key, T payload)
        where T : class
    {
        try
        {
            var envelope = new Envelope<T>
            {
                Schema = SchemaVersion,
                SavedUtc = DateTime.UtcNow,
                Payload = payload,
            };

            var path = this.PathFor(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(envelope, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            this.log?.Invoke($"cache write failed for {key}: {ex.Message}");
        }
    }

    public void Clear()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(this.directory, "cache-*.json"))
                File.Delete(file);
        }
        catch (Exception ex)
        {
            this.log?.Invoke($"cache clear failed: {ex.Message}");
        }
    }

    /// <summary>Keeps the newest <paramref name="keep"/> cache files so old worlds do not pile up.</summary>
    public int Trim(int keep = 12)
    {
        var removed = 0;

        try
        {
            var files = Directory.EnumerateFiles(this.directory, "cache-*.json")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Skip(Math.Max(1, keep))
                .ToList();

            foreach (var file in files)
            {
                file.Delete();
                removed++;
            }
        }
        catch (Exception ex)
        {
            this.log?.Invoke($"cache trim failed: {ex.Message}");
        }

        return removed;
    }

    private void Discard(string path, string key, string reason)
    {
        this.log?.Invoke($"discarding cached {key}: {reason}");
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Not being able to delete a bad cache file is not worth failing over.
        }
    }

    private string PathFor(string key)
    {
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(this.directory, $"cache-{safe}.json");
    }

    private sealed class Envelope<T>
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; }

        [JsonPropertyName("savedUtc")]
        public DateTime SavedUtc { get; set; }

        [JsonPropertyName("payload")]
        public T? Payload { get; set; }
    }
}
