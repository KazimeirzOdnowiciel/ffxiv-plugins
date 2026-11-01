using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The rules <c>CHANGELOG.md</c> states about itself, checked instead of remembered: that every version
/// has a section, that the section written ahead of a bump is still marked unreleased and only becomes
/// a dated one when the project file catches up, that the versions descend, and that a change is one
/// short line of prose rather than a file name, a commit hash or a card id.
/// </summary>
/// <remarks>
/// Only the shape is pinned, never the wording: a changelog a test dictates the sentences of is one
/// nobody writes. The changelog and the plugin's project file are copied next to this assembly (see
/// Mountbook.Tests.csproj), so what is parsed is the file a reader opens and the version the build
/// actually ships, not a copy that could drift from either.
/// </remarks>
public sealed class ChangelogTests
{
    /// <summary>The changelog, copied beside the test binary.</summary>
    private const string ChangelogFileName = "CHANGELOG.md";

    /// <summary>The plugin's project file, copied beside the test binary for its one version property.</summary>
    private const string ProjectFileName = "Mountbook.csproj";

    /// <summary>A change that will not fit on one line of this length is two changes, or a release of its own.</summary>
    private const int MaxLineLength = 200;

    /// <summary>A section heading: a three-part version and either a release date or the word unreleased.</summary>
    private static readonly Regex Heading = new(@"^## (?<version>\d+\.\d+\.\d+) \((?<status>unreleased|\d{4}-\d{2}-\d{2})\)$", RegexOptions.Compiled);

    /// <summary>A bullet, and the marker it must use: the changelog writes every change with a hyphen.</summary>
    private static readonly Regex Bullet = new(@"^- (?<text>.+)$", RegexOptions.Compiled);

    /// <summary>A file extension anywhere in a line, which means the line is about code and not about the player.</summary>
    private static readonly Regex FileExtension = new(@"\.(cs|csproj|json|ps1|png|md|yml|yaml|dll|zip|exe)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A commit hash: seven or more hex characters in a row, delimited the way a hash is written.</summary>
    private static readonly Regex CommitHash = new(@"\b[0-9a-f]{7,40}\b", RegexOptions.Compiled);

    /// <summary>A kanban card id, which belongs in the card and the commit message, not in the changelog.</summary>
    private static readonly Regex CardId = new(@"\bt_[0-9a-f]{8}\b", RegexOptions.Compiled);

    /// <summary>The version property in the plugin's project file, which is where a version lives.</summary>
    private static readonly Regex ProjectVersion = new(@"<Version>(?<version>\d+(?:\.\d+){1,3})</Version>", RegexOptions.Compiled);

    [Fact]
    public void EveryVersionHeadingParsesAndNoVersionAppearsTwice()
    {
        Changelog changelog = Read();

        Assert.Empty(changelog.MalformedHeadings);
        Assert.NotEmpty(changelog.Sections);

        string[] duplicates = changelog.Sections
            .GroupBy(section => section.Version, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void TheVersionThisBuildShipsHasASection()
    {
        Changelog changelog = Read();
        string shipped = ShippedVersion();

        Assert.True(
            changelog.Sections.Any(section => section.Version == shipped),
            $"CHANGELOG.md has no section for {shipped}, the version in {ProjectFileName}. A bump lands with its entry, in one commit.");
    }

    [Fact]
    public void TheSectionWrittenAheadOfTheBumpIsTheOnlyUnreleasedOne()
    {
        Changelog changelog = Read();
        string shipped = ShippedVersion();
        Section[] pending = changelog.Sections.Where(section => section.Status == "unreleased").ToArray();

        Assert.True(pending.Length <= 1, $"{pending.Length} sections are marked unreleased; only the next release is written ahead of its bump.");

        foreach (Section section in pending)
        {
            Assert.Same(changelog.Sections[0], section);
            Assert.True(
                Compare(section.Version, shipped) > 0,
                $"The section for {section.Version} is still marked unreleased while {ProjectFileName} already builds that version. Dating the heading is what a bump edits.");
        }
    }

    [Fact]
    public void VersionsDescendNewestFirst()
    {
        Changelog changelog = Read();

        for (int i = 1; i < changelog.Sections.Count; i++)
        {
            string older = changelog.Sections[i].Version;
            string newer = changelog.Sections[i - 1].Version;

            Assert.True(
                Compare(older, newer) < 0,
                $"{older} comes after {newer}: sections run newest first, so a release is inserted at the top.");
        }
    }

    [Fact]
    public void EverySectionSaysSomethingAndEveryChangeUsesAHyphen()
    {
        Changelog changelog = Read();

        foreach (Section section in changelog.Sections)
        {
            Assert.True(
                section.Bullets.Count > 0,
                $"The section for {section.Version} has no line in it. An empty section is what an unfinished entry looks like.");
        }

        string[] otherMarkers = changelog.Lines
            .Where(line => line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("+ ", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(otherMarkers);
    }

    [Fact]
    public void AChangeIsOneShortLine()
    {
        Changelog changelog = Read();
        var tooLong = new List<string>();

        foreach (Section section in changelog.Sections)
        {
            foreach (string line in section.Bullets)
            {
                Assert.Equal(line.TrimEnd(), line);

                if (line.Length > MaxLineLength)
                {
                    tooLong.Add($"{section.Version} ({line.Length} chars): {line}");
                }
            }
        }

        Assert.True(tooLong.Count == 0, $"A change is one line, at most {MaxLineLength} characters:{Environment.NewLine}{string.Join(Environment.NewLine, tooLong)}");
    }

    [Fact]
    public void AChangeNamesNoFileNoCommitAndNoCard()
    {
        Changelog changelog = Read();
        var leaks = new List<string>();

        foreach (Section section in changelog.Sections)
        {
            foreach (string line in section.Bullets)
            {
                if (line.Contains('\\') || line.Contains('/'))
                {
                    leaks.Add($"{section.Version}: a path in \"{line}\"");
                }

                if (FileExtension.IsMatch(line))
                {
                    leaks.Add($"{section.Version}: a file name in \"{line}\"");
                }

                if (CommitHash.IsMatch(line))
                {
                    leaks.Add($"{section.Version}: a commit hash in \"{line}\"");
                }

                if (CardId.IsMatch(line))
                {
                    leaks.Add($"{section.Version}: a card id in \"{line}\"");
                }

                if (line.Contains('`'))
                {
                    leaks.Add($"{section.Version}: code formatting in \"{line}\"");
                }
            }
        }

        Assert.True(leaks.Count == 0, $"The changelog is about what a player notices:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
    }

    /// <summary>Reads the changelog beside this assembly and splits it into version sections.</summary>
    private static Changelog Read()
    {
        string path = Path.Combine(AppContext.BaseDirectory, ChangelogFileName);
        Assert.True(File.Exists(path), $"The changelog was not copied next to the test binary: {path}");
        return Changelog.Parse(File.ReadAllLines(path));
    }

    /// <summary>The version this build ships, as the changelog writes versions: three parts, no revision.</summary>
    private static string ShippedVersion()
    {
        string path = Path.Combine(AppContext.BaseDirectory, ProjectFileName);
        Assert.True(File.Exists(path), $"The plugin's project file was not copied next to the test binary: {path}");

        Match match = ProjectVersion.Match(File.ReadAllText(path));
        Assert.True(match.Success, $"No <Version> element in {ProjectFileName}, so there is nothing to match a changelog section against.");

        string[] parts = match.Groups["version"].Value.Split('.');
        return string.Join('.', parts.Take(3));
    }

    /// <summary>Orders two three-part versions the way the file does, numerically rather than as text.</summary>
    private static int Compare(string left, string right)
    {
        int[] a = left.Split('.').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        int[] b = right.Split('.').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();

        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
            {
                return a[i].CompareTo(b[i]);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <summary>One version's section: its heading, and the lines under it.</summary>
    private sealed class Section
    {
        internal Section(string version, string status)
        {
            Version = version;
            Status = status;
        }

        /// <summary>The version the heading names, as written.</summary>
        internal string Version { get; }

        /// <summary>Either a release date or <c>unreleased</c>.</summary>
        internal string Status { get; }

        /// <summary>The bullet lines of this section, subheadings and prose excluded.</summary>
        internal List<string> Bullets { get; } = new();
    }

    /// <summary>The whole file: its sections, and any heading that did not parse.</summary>
    private sealed class Changelog
    {
        private Changelog(string[] lines)
        {
            Lines = lines;
        }

        /// <summary>Every line of the file, for the rules that apply to all of it.</summary>
        internal string[] Lines { get; }

        /// <summary>The version sections, in file order.</summary>
        internal List<Section> Sections { get; } = new();

        /// <summary>Level-two headings that are not a version heading: the file has only version sections.</summary>
        internal List<string> MalformedHeadings { get; } = new();

        /// <summary>Splits the file at its version headings. Lines before the first one are the preamble.</summary>
        internal static Changelog Parse(string[] lines)
        {
            var changelog = new Changelog(lines);
            Section? current = null;

            foreach (string line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    Match match = Heading.Match(line);

                    if (!match.Success)
                    {
                        changelog.MalformedHeadings.Add(line);
                        current = null;
                        continue;
                    }

                    current = new Section(match.Groups["version"].Value, match.Groups["status"].Value);
                    changelog.Sections.Add(current);
                    continue;
                }

                if (current is not null && Bullet.IsMatch(line))
                {
                    current.Bullets.Add(line);
                }
            }

            return changelog;
        }
    }
}
