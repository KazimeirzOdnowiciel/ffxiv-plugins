using System;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>
/// What the player has banked toward one mount, as the config persists it: the tokens counted, the
/// number the mount needs, and their own note.
/// </summary>
/// <remarks>
/// One record per mount id, because a token grind is measured per mount even when two mounts share a
/// currency: the shop price differs, the count does not. All three members are player-set, so an empty
/// record (zero, zero, no note) means "never touched" and is not persisted at all.
/// </remarks>
public sealed class MountGrindRecord
{
    /// <summary>Tokens banked so far, as the player counted them.</summary>
    public int Have { get; set; }

    /// <summary>
    /// The target: how many tokens this mount needs. Zero means "use the price the dataset records",
    /// so a mount whose shop price is known needs no target typed in by hand.
    /// </summary>
    public int Want { get; set; }

    /// <summary>The player's own note, or null when they wrote none.</summary>
    public string? Note { get; set; }

    /// <summary>Unix seconds when the record last changed, or zero for one that never has.</summary>
    public long UpdatedAt { get; set; }
}

/// <summary>
/// The grind tracker's arithmetic: what the target is, how full the bar is, and the one line that
/// says both. Pure and Dalamud-free (T1), so the pane, the wanted board and the tests compute a
/// fraction with one implementation rather than three.
/// </summary>
public static class MountGrind
{
    /// <summary>How many tokens the mount needs: the player's own target if they set one, else the price.</summary>
    /// <param name="record">The saved record, or null for a mount nobody has touched.</param>
    /// <param name="cost">The price parsed from the dataset, or null for a mount that costs no tokens.</param>
    public static int Target(MountGrindRecord? record, MountCost? cost) =>
        record is { Want: > 0 } ? record.Want : cost?.Amount ?? 0;

    /// <summary>Tokens banked, with a hand-edited negative count read as zero rather than as a negative bar.</summary>
    /// <param name="record">The saved record, or null.</param>
    public static int Have(MountGrindRecord? record) => Math.Max(record?.Have ?? 0, 0);

    /// <summary>
    /// How full the bar is, clamped to 0..1. A target of zero is an empty bar rather than a division by
    /// zero, and overshooting the target fills the bar instead of overfilling it: banking tokens past the
    /// price is normal play, and the bar is not a progress ring that has to add up.
    /// </summary>
    /// <param name="have">Tokens banked.</param>
    /// <param name="want">The target.</param>
    public static float Fraction(int have, int want)
    {
        if (want <= 0 || have <= 0)
        {
            return 0f;
        }

        return Math.Clamp((float)have / want, 0f, 1f);
    }

    /// <summary>
    /// True when the player has written something worth persisting: a count, a target, or a note. The
    /// record is then drawn, so opening a mount nobody tracked costs the pane no space.
    /// </summary>
    /// <param name="record">The candidate record, or null.</param>
    public static bool IsTracked(MountGrindRecord? record) =>
        record is not null
        && (record.Have > 0 || record.Want > 0 || !string.IsNullOrWhiteSpace(record.Note));

    /// <summary>
    /// The tracked record, or null when nothing is worth keeping. Used on the way into the config, so a
    /// dragged stepper back to zero does not leave an empty record behind.
    /// </summary>
    /// <param name="record">The candidate, or null.</param>
    public static MountGrindRecord? Kept(MountGrindRecord? record) => IsTracked(record) ? record : null;

    /// <summary>
    /// The line the pane and the board both draw, e.g. <c>3 / 10 tokens</c>. The target is included even
    /// when it is only the shop price, so the line answers "how far off am I" without the price line.
    /// </summary>
    /// <param name="have">Tokens banked.</param>
    /// <param name="want">The target.</param>
    public static string Label(int have, int want) =>
        Loc.F(
            "grind.progress",
            Count(have),
            Count(want));

    /// <summary>
    /// What is left, in the same words as the price line: <c>7 to go</c>, or <c>ready to buy</c> once the
    /// count reaches the target. A target of zero says nothing rather than counting down from nothing.
    /// </summary>
    /// <param name="have">Tokens banked.</param>
    /// <param name="want">The target.</param>
    public static string Remaining(int have, int want)
    {
        if (want <= 0)
        {
            return Loc.T("grind.remaining.unknown");
        }

        return have >= want
            ? Loc.T("grind.remaining.ready")
            : Loc.F("grind.remaining", Count(want - Math.Max(have, 0)));
    }

    /// <summary>
    /// How long ago the record changed, for the board's trailing line: <c>updated 2 days ago</c>.
    /// </summary>
    /// <param name="updatedAt">Unix seconds, or zero for a record that was never stamped.</param>
    /// <param name="now">Unix seconds now.</param>
    public static string Updated(long updatedAt, long now)
    {
        if (updatedAt <= 0)
        {
            return Loc.T("grind.updated.never");
        }

        long seconds = Math.Max(now - updatedAt, 0);
        long days = seconds / 86400;
        long hours = seconds / 3600;

        if (days >= 2)
        {
            return Loc.F("grind.updated.days", Count(days));
        }

        if (hours >= 2)
        {
            return Loc.F("grind.updated.hours", Count(hours));
        }

        return Loc.T("grind.updated.now");
    }

    /// <summary>
    /// Stamps a record with the moment it changed. One place, so every writer - the pane, the board -
    /// leaves the same value behind.
    /// </summary>
    /// <param name="record">The record that changed.</param>
    /// <param name="now">Unix seconds now.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public static MountGrindRecord Touch(MountGrindRecord record, long now)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.UpdatedAt = now;

        return record;
    }

    /// <summary>A count as the interface writes it: grouped, and invariant so a language cannot change the number.</summary>
    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
