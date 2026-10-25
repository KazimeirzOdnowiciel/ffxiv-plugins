using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text;
using GilLedger.Game;

namespace GilLedger.Core;

/// <summary>
/// Owns the ledger. The balance poller is the source of truth for amounts; chat hints label
/// each change when the timing and magnitude line up. A sale collected from a retainer often
/// lands a few seconds after the "you sold" message, so sale hints live longer.
/// </summary>
public sealed class Ledger
{
    private readonly Plugin plugin;

    private readonly List<PendingHint> hints = new();

    private long lastGil;
    private bool seeded;

    public Ledger(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public long LastGil => this.lastGil;

    public void OnChat(XivChatType type, string text)
    {
        var config = this.plugin.Configuration;
        if (!config.Enabled)
            return;

        var hint = ChatParser.Parse(type, text);
        if (hint is null)
            return;

        if (hint.Kind == GilKind.Sale && !config.WatchRetainerSales)
            return;

        var ttl = hint.Kind == GilKind.Sale ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(4);
        this.hints.Add(new PendingHint(hint, DateTime.UtcNow, ttl));
    }

    public void Tick(long gil, DateTime now)
    {
        if (!this.seeded)
        {
            this.lastGil = gil;
            this.seeded = true;
            return;
        }

        var delta = gil - this.lastGil;
        if (delta == 0)
        {
            this.Prune(now);
            return;
        }

        this.lastGil = gil;

        var config = this.plugin.Configuration;
        if (!config.Enabled || !config.ReconcileBalance)
        {
            this.Prune(now);
            return;
        }

        var hint = this.Match(delta, now);
        var kind = hint is null ? GilKind.Unattributed : hint.Hint.Kind;
        var note = hint is null ? "unattributed" : hint.Hint.Text;

        if (hint is not null)
            this.hints.Remove(hint);

        this.AddEntry(kind, delta, gil, note, now);
        this.Prune(now);
    }

    private PendingHint? Match(long delta, DateTime now)
    {
        var magnitude = Math.Abs(delta);

        return this.hints
            .Where(h => now - h.Utc <= h.Ttl)
            .Where(h => h.Hint.Amount == 0 || Math.Abs(h.Hint.Amount) == magnitude)
            .OrderBy(h => h.Hint.Amount == 0 ? 1 : 0)
            .ThenByDescending(h => h.Utc)
            .FirstOrDefault();
    }

    private void AddEntry(GilKind kind, long delta, long balanceAfter, string note, DateTime now)
    {
        var config = this.plugin.Configuration;
        var entry = new GilEntry
        {
            Id = config.NextId++,
            Utc = now,
            Kind = kind,
            Amount = delta,
            BalanceAfter = balanceAfter,
            Note = note,
        };

        config.Entries.Insert(0, entry);

        if (config.Entries.Count > 20_000)
            config.Entries.RemoveRange(config.Entries.Count - 1, config.Entries.Count - 20_000);

        this.plugin.SaveConfig();
    }

    private void Prune(DateTime now)
    {
        if (this.hints.Count == 0)
            return;

        this.hints.RemoveAll(h => now - h.Utc > h.Ttl);
    }

    private sealed class PendingHint
    {
        public PendingHint(ParsedHint hint, DateTime utc, TimeSpan ttl)
        {
            this.Hint = hint;
            this.Utc = utc;
            this.Ttl = ttl;
        }

        public ParsedHint Hint { get; }

        public DateTime Utc { get; }

        public TimeSpan Ttl { get; }
    }
}
