using System;
using System.IO;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// Moving an unreadable settings file aside: the backup has to be beside the original, has to keep every byte,
/// and has to work (or say it could not) whether the file is locked or not.
/// </summary>
public sealed class ConfigRecoveryTests : IDisposable
{
    private readonly string folder = Path.Combine(
        AppContext.BaseDirectory,
        "config-recovery",
        Guid.NewGuid().ToString("N"));

    public ConfigRecoveryTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A failed cleanup is not a test result; the folder is under the temp path and will be pruned.
        }
    }

    [Fact]
    public void TheBackupSitsBesideTheOriginalAndNamesTheMoment()
    {
        string path = Path.Combine(folder, "Mountbook.json");

        string backup = ConfigRecovery.BackupPath(path, new DateTimeOffset(2026, 11, 1, 18, 30, 0, TimeSpan.Zero));

        Assert.Equal(folder, Path.GetDirectoryName(backup));
        Assert.Equal("Mountbook.corrupt-20261101-183000.json", Path.GetFileName(backup));
    }

    [Fact]
    public void ASecondFailureInTheSameSecondDoesNotOverwriteTheFirstBackup()
    {
        string path = Path.Combine(folder, "Mountbook.json");
        var at = new DateTimeOffset(2026, 11, 1, 18, 30, 0, TimeSpan.Zero);

        string first = ConfigRecovery.BackupPath(path, at);
        File.WriteAllText(first, "the first broken file");

        string second = ConfigRecovery.BackupPath(path, at);

        Assert.NotEqual(first, second);
        Assert.Equal("Mountbook.corrupt-20261101-183000-1.json", Path.GetFileName(second));
    }

    [Fact]
    public void MovingAnUnreadableFileAsideKeepsEveryByte()
    {
        string path = Path.Combine(folder, "Mountbook.json");
        const string Corrupt = "{ \"Version\": 1, \"WantedMounts\": [ 192, 82,";
        File.WriteAllText(path, Corrupt);

        string? backup = ConfigRecovery.TryMoveAside(path, new DateTimeOffset(2026, 11, 1, 18, 30, 0, TimeSpan.Zero));

        Assert.NotNull(backup);
        Assert.False(File.Exists(path));
        Assert.Equal(Corrupt, File.ReadAllText(backup));
    }

    [Fact]
    public void AMissingFileHasNothingToMoveAside()
    {
        // The normal first run: Dalamud's loader raises FileNotFound for the same reason, and there is nothing
        // to preserve.
        Assert.Null(ConfigRecovery.TryMoveAside(Path.Combine(folder, "Mountbook.json"), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ALockedFileIsCopiedRatherThanMoved()
    {
        string path = Path.Combine(folder, "Mountbook.json");
        const string Corrupt = "not json";
        File.WriteAllText(path, Corrupt);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            string? backup = ConfigRecovery.TryMoveAside(path, new DateTimeOffset(2026, 11, 1, 18, 30, 0, TimeSpan.Zero));

            Assert.NotNull(backup);
            Assert.Equal(Corrupt, File.ReadAllText(backup));

            // The move could not happen while the loader held the file; the copy keeps the evidence anyway.
            Assert.True(File.Exists(path));
        }
    }

    [Fact]
    public void AFileThatCanNeitherBeMovedNorReadReportsThatItCouldNot()
    {
        string path = Path.Combine(folder, "Mountbook.json");
        File.WriteAllText(path, "not json");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(ConfigRecovery.TryMoveAside(path, DateTimeOffset.UtcNow));
        }

        // Nothing was invented to fill the gap: the caller logs the null and the next save overwrites the file.
        Assert.Empty(Directory.GetFiles(folder, "*.corrupt-*"));
    }
}
