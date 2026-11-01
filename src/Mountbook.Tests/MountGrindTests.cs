using System.Collections.Generic;
using System.Text.Json;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The grind tracker's rules: what the target falls back to, how the bar clamps, when a record is worth
/// keeping, and the wording the pane and the board share.
/// </summary>
/// <remarks>
/// These are the numbers a player checks against the game, so they are pinned here rather than left to the
/// drawing code: a bar that reads 3 / 10 for four tokens, or a record that survives a restart as zero, is a
/// worse bug than a misplaced pixel.
/// </remarks>
public sealed class MountGrindTests
{
    private static readonly MountCost Toy = new(10, "Corvosi Manuscripts", string.Empty);

    [Fact]
    public void ThePlayersOwnTargetWinsOverThePrice()
    {
        var record = new MountGrindRecord { Have = 3, Want = 25 };

        Assert.Equal(25, MountGrind.Target(record, Toy));
    }

    [Fact]
    public void AMountWithNoTargetUsesTheRecordedPrice()
    {
        var record = new MountGrindRecord { Have = 3 };

        Assert.Equal(10, MountGrind.Target(record, Toy));
    }

    [Fact]
    public void AMountWithNeitherHasNoTarget()
    {
        Assert.Equal(0, MountGrind.Target(null, null));
        Assert.Equal(0, MountGrind.Target(new MountGrindRecord(), null));
    }

    [Fact]
    public void ANegativeCountReadsAsZero()
    {
        Assert.Equal(0, MountGrind.Have(new MountGrindRecord { Have = -4 }));
        Assert.Equal(0, MountGrind.Have(null));
    }

    [Fact]
    public void TheBarIsTheShareOfTheTargetAndNeverOverfills()
    {
        Assert.Equal(0.3f, MountGrind.Fraction(3, 10), 3);
        Assert.Equal(1f, MountGrind.Fraction(14, 10));
        Assert.Equal(0f, MountGrind.Fraction(3, 0));
        Assert.Equal(0f, MountGrind.Fraction(0, 10));
        Assert.Equal(0f, MountGrind.Fraction(-2, 10));
    }

    [Fact]
    public void ARecordIsWorthKeepingWhenItSaysAnything()
    {
        Assert.False(MountGrind.IsTracked(null));
        Assert.False(MountGrind.IsTracked(new MountGrindRecord()));
        Assert.False(MountGrind.IsTracked(new MountGrindRecord { Note = "   " }));
        Assert.True(MountGrind.IsTracked(new MountGrindRecord { Have = 1 }));
        Assert.True(MountGrind.IsTracked(new MountGrindRecord { Want = 10 }));
        Assert.True(MountGrind.IsTracked(new MountGrindRecord { Note = "needs two more weeks" }));
    }

    [Fact]
    public void AnEmptyRecordIsNotKept()
    {
        Assert.Null(MountGrind.Kept(new MountGrindRecord { Have = 0, Want = 0, Note = " " }));
        Assert.NotNull(MountGrind.Kept(new MountGrindRecord { Have = 0, Want = 10 }));
    }

    [Fact]
    public void TheCountLineReadsAsTheTaskAsked()
    {
        Assert.Equal("3 / 10 tokens", MountGrind.Label(3, 10));
        Assert.Equal("1,200 / 2,000 tokens", MountGrind.Label(1200, 2000));
    }

    [Fact]
    public void WhatIsLeftReadsAsAGapOrAsReady()
    {
        Assert.Equal("7 to go", MountGrind.Remaining(3, 10));
        Assert.Equal("enough to buy it", MountGrind.Remaining(10, 10));
        Assert.Equal("enough to buy it", MountGrind.Remaining(12, 10));
        Assert.Equal("no token price recorded", MountGrind.Remaining(3, 0));
    }

    [Fact]
    public void TheAgeOfARecordIsSpokenInTheUnitsThatFit()
    {
        const long now = 1_800_000_000;

        Assert.Equal("not tracked yet", MountGrind.Updated(0, now));
        Assert.Equal("updated just now", MountGrind.Updated(now - 30, now));
        Assert.Equal("updated 3 hours ago", MountGrind.Updated(now - (3 * 3600), now));
        Assert.Equal("updated 5 days ago", MountGrind.Updated(now - (5 * 86400), now));
    }

    [Fact]
    public void TouchingARecordStampsTheMomentItChanged()
    {
        var record = new MountGrindRecord { Have = 2 };

        MountGrindRecord stamped = MountGrind.Touch(record, 42);

        Assert.Equal(42, stamped.UpdatedAt);
        Assert.Equal(2, stamped.Have);
    }

    /// <summary>
    /// The record is persisted as part of <c>Configuration</c>'s JSON, and this is the shape it lands in: a
    /// map keyed by the mount id with four named members. Pinned because a rename here is a config migration
    /// (AGENTS N5), and because "progress survives a restart" is exactly this round trip.
    /// </summary>
    [Fact]
    public void ARecordSurvivesTheRoundTripAConfigFileMakes()
    {
        var saved = new Dictionary<uint, MountGrindRecord>
        {
            [412] = new() { Have = 3, Want = 10, Note = "two more weeks", UpdatedAt = 1_800_000_000 },
        };

        string json = JsonSerializer.Serialize(saved);
        Dictionary<uint, MountGrindRecord>? read = JsonSerializer.Deserialize<Dictionary<uint, MountGrindRecord>>(json);

        Assert.NotNull(read);
        Assert.True(read!.TryGetValue(412, out MountGrindRecord? record));
        Assert.Equal(3, record!.Have);
        Assert.Equal(10, record.Want);
        Assert.Equal("two more weeks", record.Note);
        Assert.Equal(1_800_000_000, record.UpdatedAt);
        Assert.Contains("\"Have\":3", json, System.StringComparison.Ordinal);
        Assert.Contains("\"UpdatedAt\":1800000000", json, System.StringComparison.Ordinal);
    }
}
