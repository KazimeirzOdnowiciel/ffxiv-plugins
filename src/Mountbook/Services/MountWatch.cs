using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>
/// One mount's price watch, as the config persists it: the price the player asked to be told about, when
/// the market was last read for it, and what that read found.
/// </summary>
/// <remarks>
/// <see cref="Alerted"/> is bookkeeping the feature needs rather than a fact about the market: a poll that
/// finds the same price again must not raise a second toast, so the crossing is latched until a read puts the
/// price back above the threshold. Zero is a real answer for <see cref="LastSeen"/> ("nobody is selling it"),
/// which is why the latch is a flag of its own and not a sentinel price.
/// </remarks>
public sealed class MountWatchRecord
{
    /// <summary>
    /// The price worth interrupting the player for, in gil. Zero means "not watched": a record with no
    /// threshold is how the pane switches a watch off, and it is never persisted.
    /// </summary>
    public int Threshold { get; set; }

    /// <summary>Unix seconds of the last completed read, or zero for a watch the market has not answered yet.</summary>
    public long CheckedAt { get; set; }

    /// <summary>The cheapest price that read found, in gil, or zero when nobody listed the mount.</summary>
    public int LastSeen { get; set; }

    /// <summary>True while a crossing has already been announced, so the next poll of the same price is silent.</summary>
    public bool Alerted { get; set; }
}

/// <summary>
/// The price watch's rules: what counts as a crossing, when the next poll is due, and the three lines the
/// header, the pane and the toast show. Pure and Dalamud-free (T1), so the schedule can be tested without
/// waiting thirty minutes and the "once per crossing" rule is a unit test rather than a hope.
/// </summary>
/// <remarks>
/// <para>
/// A watch is opt-in per mount and is the only reason this plugin polls anything: with no record whose
/// threshold is above zero the caller asks the market for nothing (the N2 rule, "a price cannot ship, so it
/// is asked for" - asked for on a schedule the player set up, not on a timer that runs by itself).
/// </para>
/// <para>
/// The polling interval is deliberately long and jittered. Every watched mount costs a data-center
/// aggregation, and that service is shared with every other player's plugin; a fixed cadence would also
/// put every Mountbook in the same data center in lockstep after a patch-day login.
/// </para>
/// </remarks>
public static class MountWatch
{
    /// <summary>
    /// The half hour a watch waits between reads, before <see cref="JitterShare"/> moves it. Prices on a
    /// data center change over hours, so a shorter interval would spend requests on the same listings.
    /// </summary>
    public const long IntervalMs = 30L * 60L * 1000L;

    /// <summary>
    /// How far the interval may move, as a share of <see cref="IntervalMs"/>: +/- 20%, i.e. 24 to 36
    /// minutes. Wide enough that a hundred clients do not line up, narrow enough that "half-hourly" stays true.
    /// </summary>
    public const double JitterShare = 0.2;

    /// <summary>The step the pane's threshold stepper moves.</summary>
    public const int ThresholdStep = 100_000;

    /// <summary>
    /// The lowest threshold a watch may hold. Not the stepper's step: a whistle can be worth less than one
    /// step, and a watch whose floor is 100,000 gil could never watch one.
    /// </summary>
    public const int MinThreshold = 1_000;

    /// <summary>The top of the stepper's range: past any whistle a player would set an alert for.</summary>
    public const int MaxThreshold = 100_000_000;

    /// <summary>
    /// The threshold a fresh watch starts at when no price is known yet: a round million gil. Only a
    /// starting point - the stepper exists so the player sets their own number.
    /// </summary>
    public const int DefaultThreshold = 1_000_000;

    /// <summary>True when the record stands for a watch the player switched on.</summary>
    /// <param name="record">The saved record, or null for a mount that was never watched.</param>
    public static bool IsWatching(MountWatchRecord? record) => record is { Threshold: > 0 };

    /// <summary>How many records are live watches, for the header's count.</summary>
    /// <param name="records">The saved records, keyed by mount id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="records"/> is null.</exception>
    public static int Count(IReadOnlyDictionary<uint, MountWatchRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var watching = 0;

        foreach (MountWatchRecord record in records.Values)
        {
            if (IsWatching(record))
            {
                watching++;
            }
        }

        return watching;
    }

    /// <summary>
    /// True when <paramref name="price"/> is at or under the threshold. Zero is not a crossing: it is the
    /// market saying nobody lists the mount, and "it is 0 gil on no world" is not a price to buy at.
    /// </summary>
    /// <param name="price">The cheapest price that read found, in gil.</param>
    /// <param name="threshold">The price the player set, in gil.</param>
    public static bool Crossed(int price, int threshold) => price > 0 && threshold > 0 && price <= threshold;

    /// <summary>
    /// True when this read is the crossing worth a toast: under the threshold, and not already announced.
    /// The latch is what makes it "once per crossing" - the next poll of the same price reads
    /// <see cref="MountWatchRecord.Alerted"/> and stays quiet.
    /// </summary>
    /// <param name="record">The watch the read belongs to.</param>
    /// <param name="price">The cheapest price the read found, in gil.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public static bool ShouldAlert(MountWatchRecord record, int price)
    {
        ArgumentNullException.ThrowIfNull(record);

        return !record.Alerted && Crossed(price, record.Threshold);
    }

    /// <summary>
    /// Writes one read onto the record: what was seen, when, and whether it is now in the announced state.
    /// A price back above the threshold, or no listings at all, clears the latch so the next crossing is
    /// announced again - the watch stays armed for the life of the record.
    /// </summary>
    /// <param name="record">The watch the read belongs to.</param>
    /// <param name="price">The cheapest price the read found, in gil, or zero for no listings.</param>
    /// <param name="now">Unix seconds now.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public static void Observe(MountWatchRecord record, int price, long now)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.LastSeen = Math.Max(price, 0);
        record.CheckedAt = now;
        record.Alerted = Crossed(record.LastSeen, record.Threshold);
    }

    /// <summary>
    /// When the next poll is due, as <paramref name="nowMs"/> plus the interval with its jitter applied.
    /// </summary>
    /// <param name="nowMs">The monotonic milliseconds the caller keeps its clock in.</param>
    /// <param name="roll">A uniform random number in 0..1; 0.5 is exactly the base interval.</param>
    public static long NextDue(long nowMs, double roll)
    {
        // Clamped rather than trusted: the value comes from a random source, and an out-of-range roll must
        // not be able to schedule a poll in the past (a poll every frame) or a day away.
        double unit = Math.Clamp(double.IsNaN(roll) ? 0.5d : roll, 0d, 1d);
        double factor = 1d + (((unit * 2d) - 1d) * JitterShare);

        return nowMs + (long)(IntervalMs * factor);
    }

    /// <summary>
    /// The header's badge, e.g. <c>Watching 2</c>, or empty when no mount is watched. Empty and not
    /// "Watching 0": a badge counting nothing is chrome for a feature the player has not switched on.
    /// </summary>
    /// <param name="watching">How many mounts are watched.</param>
    public static string HeaderLabel(int watching) =>
        watching <= 0 ? string.Empty : Loc.F("header.watching", Number(watching));

    /// <summary>
    /// The line under the watch control: what the last read found, and when. Empty when the mount is not
    /// watched, so a mount nobody watches costs the pane no space.
    /// </summary>
    /// <remarks>
    /// The threshold is not repeated here - the stepper beside it is the threshold, and a line that said it
    /// twice was the one part of this block that did not fit the detail pane at the default window size.
    /// </remarks>
    /// <param name="record">The saved record, or null.</param>
    /// <param name="now">Unix seconds now.</param>
    public static string Line(MountWatchRecord? record, long now)
    {
        if (!IsWatching(record))
        {
            return string.Empty;
        }

        if (record!.CheckedAt <= 0)
        {
            return Loc.T("watch.line.never");
        }

        var when = When(record.CheckedAt, now);

        return record.LastSeen > 0
            ? Loc.F("watch.line.seen", Gil(record.LastSeen), when)
            : Loc.F("watch.line.none", when);
    }

    /// <summary>
    /// The toast: the mount, the price that crossed, and the number the player set. Built here rather than in
    /// the plugin so the wording is pinned by a test, like every other sentence the plugin says.
    /// </summary>
    /// <param name="mount">The mount's display name.</param>
    /// <param name="price">The price that crossed, in gil.</param>
    /// <param name="threshold">The price the player set, in gil.</param>
    public static string AlertText(string mount, int price, int threshold) =>
        Loc.F("watch.toast", mount, Gil(price), Gil(threshold));

    /// <summary>
    /// The threshold a watch starts at: the price already on screen when the player switches it on, so
    /// "tell me when it is no worse than this" needs no arithmetic, and the default otherwise.
    /// </summary>
    /// <param name="knownPrice">The cheapest price already known for the mount, or zero.</param>
    public static int StartThreshold(int knownPrice) =>
        Math.Clamp(knownPrice > 0 ? knownPrice : DefaultThreshold, MinThreshold, MaxThreshold);

    /// <summary>How long ago a read ran, in the pane's words: <c>just now</c>, <c>12 min ago</c>.</summary>
    /// <param name="checkedAt">Unix seconds of the read, or zero.</param>
    /// <param name="now">Unix seconds now.</param>
    public static string When(long checkedAt, long now)
    {
        if (checkedAt <= 0)
        {
            return Loc.T("watch.when.never");
        }

        long seconds = Math.Max(now - checkedAt, 0);

        if (seconds < 120)
        {
            return Loc.T("watch.when.now");
        }

        if (seconds < 7200)
        {
            return Loc.F("watch.when.minutes", Number(seconds / 60));
        }

        return seconds < 172_800
            ? Loc.F("watch.when.hours", Number(seconds / 3600))
            : Loc.F("watch.when.days", Number(seconds / 86_400));
    }

    /// <summary>A price as the interface and the log both write it: grouped, and invariant so a language cannot change the number.</summary>
    /// <param name="gil">The price in gil.</param>
    public static string Gil(int gil) => gil.ToString("N0", CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
