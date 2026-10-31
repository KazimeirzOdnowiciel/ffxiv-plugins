using System;
using System.IO;
using System.Text.Json;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The committed dataset is a shipped artifact, so it is parsed here exactly as the plugin parses
/// it at runtime. A bad regeneration that drops ids, patches or source text fails this test.
/// </summary>
public sealed class MountSourcesDataTests
{
    private const string DatasetFileName = "mount-sources.json";

    [Fact]
    public void ShippedDatasetParsesAndCoversEveryRow()
    {
        string json = ReadDataset();
        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.Count > 300, $"Expected more than 300 mounts in the dataset, found {shelf.Count}.");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement mounts = document.RootElement.GetProperty("mounts");
        Assert.Equal(mounts.GetArrayLength(), shelf.Count);

        foreach (JsonElement mount in mounts.EnumerateArray())
        {
            uint id = mount.GetProperty("id").GetUInt32();
            Assert.True(id > 0, "A dataset row has no mount id.");

            string patch = mount.GetProperty("patch").GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(patch), $"Mount {id} has an empty patch.");

            Assert.True(shelf.TryGet(id, out MountSources sources), $"The parser dropped mount {id}.");
            Assert.Equal(patch, sources.Patch);

            foreach (MountSourceLine line in sources.Lines)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(line.Detail),
                    $"Mount {id} has a source line without text.");
            }

            foreach (JsonElement source in mount.GetProperty("sources").EnumerateArray())
            {
                string text = source.GetProperty("text").GetString() ?? string.Empty;
                Assert.False(
                    string.IsNullOrWhiteSpace(text),
                    $"Mount {id} has a source entry without text.");
            }
        }
    }

    [Fact]
    public void ShippedDatasetNamesItsSourceAndGenerationDate()
    {
        using JsonDocument document = JsonDocument.Parse(ReadDataset());

        Assert.Equal("ffxivcollect.com", document.RootElement.GetProperty("source").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("generated").GetString()));
    }

    private static string ReadDataset()
    {
        string path = Path.Combine(AppContext.BaseDirectory, DatasetFileName);
        Assert.True(File.Exists(path), $"The dataset was not copied next to the test binary: {path}");
        return File.ReadAllText(path);
    }
}
