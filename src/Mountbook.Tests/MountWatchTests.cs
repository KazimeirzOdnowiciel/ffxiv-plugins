using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The price watch's rules: what counts as a crossing, that a crossing is announced once and re-armed by a
/// price going back up, the jittered schedule, and the three lines the header, the pane and the toast show.
/// </summary>
/// <remarks>
/// These are the numbers and the wording a player acts on. The "once per crossing" rule is the one that
/// cannot be checked by looking at the screen - a duplicate toast looks exactly like a fresh one - so it is
/// pinned here, on the latch, rather than left to the code that drains answers.
/// </remarks>
public sealed class MountWatchTests
{
    /// <summary>A watch of five million gil, checked an hour ago against a price of four.</summary>
    private static MountWatchRecord Watched() => new() { Threshold = 5_000_000, CheckedAt = 1_000_000, LastSeen = 4_000_000 };

    [Fact]
    public void ARecordWithNoThresholdIsNotAWatch()
    {
        Assert.False(MountWatch.IsWatching(null));
        Assert.False(MountWatch.IsWatching(new MountWatchRecord()));
        Assert.False(MountWatch.IsWatching(new MountWatchRecord { Threshold = -1 }));
        Assert.True(MountWatch.IsWatching(new MountWatchRecord { Threshold = 1 }));
    }

    [Fact]
    public void TheCountIsOfLiveWatchesOnly()
    {
        var records = new Dictionary<uint, MountWatchRecord>
        {
            [1] = new() { Threshold = 1_000_000 },
            [2] = new(),
            [3] = new() { Threshold = 1, LastSeen = 0 },
        };

        Assert.Equal(2, MountWatch.Count(records));
        Assert.Equal(0, MountWatch.Count(new Dictionary<uint, MountWatchRecord>()));
    }

    [Theory]
    [InlineData(4_000_000, 5_000_000, true)]
    [InlineData(5_000_000, 5_000_000, true)]
    [InlineData(5_000_001, 5_000_000, false)]
    [InlineData(0, 5_000_000, false)]
    [InlineData(4_000_000, 0, false)]
    public void ACrossingIsAtOrUnderTheThresholdAndNeverZero(int price, int threshold, bool crossed) =>
        Assert.Equal(crossed, MountWatch.Crossed(price, threshold));

    [Fact]
    public void ACrossingIsAnnouncedOnceAndTheWatchStaysArmed()
    {
        var watch = new MountWatchRecord { Threshold = 5_000_000 };

        Assert.True(MountWatch.ShouldAlert(watch, 4_000_000));

        MountWatch.Observe(watch, 4_000_000, 1_000);

        // The next poll of the same price must stay quiet: that is the acceptance rule, not a detail.
        Assert.False(MountWatch.ShouldAlert(watch, 4_000_000));
        Assert.False(MountWatch.ShouldAlert(watch, 3_000_000));

        // A price back above the threshold re-arms it, so the next real drop is announced again.
        MountWatch.Observe(watch, 6_000_000, 2_000);

        Assert.False(watch.Alerted);
        Assert.True(MountWatch.ShouldAlert(watch, 4_000_000));
    }

    [Fact]
    public void NobodySellingItIsAnAnswerThatKeepsTheWatchArmed()
    {
        var watch = Watched();

        MountWatch.Observe(watch, 0, 2_000);

        Assert.Equal(0, watch.LastSeen);
        Assert.Equal(2_000, watch.CheckedAt);
        Assert.False(watch.Alerted);
        Assert.False(MountWatch.ShouldAlert(watch, 0));
    }

    [Fact]
    public void AHandEditedNegativePriceReadsAsNothingForSale()
    {
        var watch = Watched();

        MountWatch.Observe(watch, -5, 2_000);

        Assert.Equal(0, watch.LastSeen);
    }

    [Fact]
    public void AWatchThatWasAlreadySatisfiedDoesNotShoutWhenItIsSwitchedOn()
    {
        var watch = new MountWatchRecord { Threshold = 5_000_000 };

        // Seeded from a price the player is looking at, so there is nothing new to announce.
        MountWatch.Observe(watch, 4_000_000, 1_000);

        Assert.False(MountWatch.ShouldAlert(watch, 4_000_000));
    }

    [Fact]
    public void TheIntervalIsTheBaseAtHalfAndJittersBothWays()
    {
        var now = 50_000L;

        Assert.Equal(now + MountWatch.IntervalMs, MountWatch.NextDue(now, 0.5d));
        Assert.Equal(now + (long)(MountWatch.IntervalMs * 0.8d), MountWatch.NextDue(now, 0d));
        Assert.Equal(now + (long)(MountWatch.IntervalMs * 1.2d), MountWatch.NextDue(now, 1d));

        // A random source outside its range must not be able to schedule a poll in the past.
        Assert.Equal(now + (long)(MountWatch.IntervalMs * 0.8d), MountWatch.NextDue(now, -3d));
        Assert.Equal(now + (long)(MountWatch.IntervalMs * 1.2d), MountWatch.NextDue(now, 9d));
        Assert.Equal(now + MountWatch.IntervalMs, MountWatch.NextDue(now, double.NaN));
    }

    [Fact]
    public void TheJitterNeverMovesTheIntervalFarEnoughToBeAShorterSchedule()
    {
        for (var step = 0; step <= 100; step++)
        {
            long due = MountWatch.NextDue(0, step / 100d);
            long delay = due - 0;

            Assert.InRange(delay, 24L * 60L * 1000L, 36L * 60L * 1000L);
        }
    }

    [Fact]
    public void TheHeaderSaysNothingWhenNothingIsWatched()
    {
        Assert.Equal(string.Empty, MountWatch.HeaderLabel(0));
        Assert.Equal(string.Empty, MountWatch.HeaderLabel(-2));
        Assert.Equal("Watching 1", MountWatch.HeaderLabel(1));
        Assert.Equal("Watching 3", MountWatch.HeaderLabel(3));
    }

    [Fact]
    public void AnUnwatchedMountSaysNothingInThePane() =>
        Assert.Equal(string.Empty, MountWatch.Line(null, 2_000));

    [Fact]
    public void AFreshWatchSaysItHasNotBeenChecked()
    {
        var line = MountWatch.Line(new MountWatchRecord { Threshold = 5_000_000 }, 2_000);

        Assert.Equal("No price read yet", line);
    }

    [Fact]
    public void ACheckedWatchNamesThePriceAndWhenItWasSeen()
    {
        long now = 1_000_000 + (12 * 60);

        Assert.Equal("Last seen 4,000,000 gil 12 min ago", MountWatch.Line(Watched(), now));
    }

    [Fact]
    public void AWatchOnSomethingNobodySellsSaysSoRatherThanZeroGil()
    {
        var watch = new MountWatchRecord { Threshold = 5_000_000, CheckedAt = 1_000_000, LastSeen = 0 };

        Assert.Equal("No listings 2 hours ago", MountWatch.Line(watch, 1_000_000 + 7200));
    }

    [Fact]
    public void AWatchNothingHasReadYetSaysNever() =>
        Assert.Equal("never", MountWatch.When(0, 1_000_000));

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(119, "just now")]
    [InlineData(120, "2 min ago")]
    [InlineData(7199, "119 min ago")]
    [InlineData(7200, "2 hours ago")]
    [InlineData(172_799, "47 hours ago")]
    [InlineData(172_800, "2 days ago")]
    public void TheAgeOfAReadIsSaidInTheCoarsestHonestUnit(long age, string expected) =>
        Assert.Equal(expected, MountWatch.When(1_000_000, 1_000_000 + age));

    [Fact]
    public void TheToastNamesTheMountThePriceAndTheWatch()
    {
        Assert.Equal(
            "Azure Zu is listed at 4,100,000 gil - your watch was 5,000,000 gil",
            MountWatch.AlertText("Azure Zu", 4_100_000, 5_000_000));
    }

    [Fact]
    public void ANewWatchStartsAtThePriceOnScreen()
    {
        Assert.Equal(2_999_999, MountWatch.StartThreshold(2_999_999));
        Assert.Equal(MountWatch.DefaultThreshold, MountWatch.StartThreshold(0));
        Assert.Equal(MountWatch.MinThreshold, MountWatch.StartThreshold(1));
        Assert.Equal(MountWatch.MaxThreshold, MountWatch.StartThreshold(int.MaxValue));
    }
}
