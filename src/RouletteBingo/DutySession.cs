using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.DutyState;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using RouletteBingo.Core;
using RouletteBingo.Detection;

namespace RouletteBingo;

/// <summary>
/// Owns the live card and all of the (few) things the plugin detects by itself.
/// </summary>
public sealed class DutySession : IDisposable
{
    private readonly Plugin plugin;
    private readonly Dictionary<uint, bool> lastAlive = new();
    private readonly HashSet<string> firedDetectors = new(StringComparer.OrdinalIgnoreCase);

    private DateTime nextPartyScan = DateTime.MinValue;
    private DateTime nextVulnScan = DateTime.MinValue;
    private bool seededAliveState;
    private HashSet<uint>? vulnerabilityIds;

    public DutySession(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public BingoCard? Card { get; private set; }

    public int PartyChatMessages { get; private set; }

    public int Wipes { get; private set; }

    public TimeSpan Elapsed
    {
        get
        {
            if (this.Card is null)
                return TimeSpan.Zero;

            var end = this.Card.FinishedUtc ?? DateTime.UtcNow;
            return end - this.Card.StartedUtc;
        }
    }

    /// <summary>Raised when the plugin marked squares on its own. Indexes are the newly marked cells.</summary>
    public event Action<BingoCard, IReadOnlyList<int>>? AutoMarked;

    /// <summary>Raised when a new line is completed. The argument is the line label.</summary>
    public event Action<BingoCard, string>? LineCompleted;

    private static Configuration Config => Plugin.PluginConfig;

    private static SuggestionPool Pool => Plugin.SquarePool;

    // -------------------------------------------------------------- lifecycle ----

    public void Attach()
    {
        Plugin.DutyState.DutyStarted += this.OnDutyStarted;
        Plugin.DutyState.DutyWiped += this.OnDutyWiped;
        Plugin.DutyState.DutyCompleted += this.OnDutyCompleted;
        Plugin.ChatGui.ChatMessage += this.OnChatMessage;
        Plugin.Framework.Update += this.OnUpdate;
        Plugin.ClientState.TerritoryChanged += this.OnTerritoryChanged;
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyStarted -= this.OnDutyStarted;
        Plugin.DutyState.DutyWiped -= this.OnDutyWiped;
        Plugin.DutyState.DutyCompleted -= this.OnDutyCompleted;
        Plugin.ChatGui.ChatMessage -= this.OnChatMessage;
        Plugin.Framework.Update -= this.OnUpdate;
        Plugin.ClientState.TerritoryChanged -= this.OnTerritoryChanged;
    }

    public void Resume()
    {
        var state = Config.ActiveCard;
        if (state is null || state.Seed == 0 || string.IsNullOrWhiteSpace(state.DutyName))
            return;

        try
        {
            var generated = CardGenerator.Generate(Pool, state.Seed, state.Kind, Config.CursedMode);
            var card = new BingoCard(state.Seed, state.Kind, state.DutyName, generated.Cells, new DateTime(state.StartedUtcTicks, DateTimeKind.Utc));
            card.ApplyMarks(state.Marks, state.AutoMarks);
            this.Card = card;
            this.PartyChatMessages = state.PartyChatMessages;
            this.Wipes = state.Wipes;
            if (state.FinishedUtcTicks > 0)
                card.Finish(new DateTime(state.FinishedUtcTicks, DateTimeKind.Utc));

            Plugin.Log.Information("[RouletteBingo] Resumed card '{Duty}' ({Seed}).", card.DutyName, card.Seed);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[RouletteBingo] Could not resume the saved card.");
            Config.ActiveCard = null;
        }
    }

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        if (!Config.Enabled || !Config.AutoStartCards)
            return;

        // A card left over from an abandoned run is closed out, not reused.
        if (this.Card is { Finished: false })
            this.FinalizeRun(completed: false);

        this.StartCard(args);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        if (this.Card is null)
            return;

        // Leaving the duty without a completion event (abandoned, disconnected, kicked).
        if (!this.InDuty())
            this.FinalizeRun(completed: false);
    }

    private void OnDutyWiped(IDutyStateEventArgs args)
    {
        if (this.Card is null)
            return;

        this.Wipes++;
        this.AutoMark(Detectors.Wipe);
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        if (this.Card is null)
            return;

        if (Config.AutoDetectChat && this.PartyChatMessages < 3)
            this.AutoMark(Detectors.Silence);

        this.FinalizeRun(completed: true);
    }

    // ------------------------------------------------------------------- card ----

    public void StartCard(IDutyStateEventArgs? args = null)
    {
        var (dutyName, kind) = this.DescribeDuty(args);
        var seed = CardGenerator.SeedFor(dutyName, Plugin.ClientState.TerritoryType, DateTime.UtcNow, Config.TotalCards);

        try
        {
            var generated = CardGenerator.Generate(Pool, seed, kind, Config.CursedMode);
            this.Card = new BingoCard(seed, kind, dutyName, generated.Cells, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[RouletteBingo] Could not generate a card.");
            return;
        }

        this.PartyChatMessages = 0;
        this.Wipes = 0;
        this.firedDetectors.Clear();
        this.lastAlive.Clear();
        this.seededAliveState = false;

        Plugin.Log.Information("[RouletteBingo] New card for '{Duty}' ({Kind}) seed {Seed}.", dutyName, kind, seed);
        this.SaveCardState();
        this.plugin.NotifyCardChanged();
    }

    public bool Toggle(int index)
    {
        if (this.Card is null)
            return false;

        var wasMarked = this.Card.IsMarked(index);
        var linesBefore = this.Card.CompletedLines.Count;

        if (!this.Card.Toggle(index, DateTime.UtcNow))
            return false;

        if (!wasMarked && index != CardGenerator.CenterIndex)
            this.NoteManualMark(this.Card.Cells[index]);

        var linesAfter = this.Card.CompletedLines;
        if (linesAfter.Count > linesBefore)
        {
            var label = linesAfter[^1].Label;
            this.OnBingo(this.Card, label);
        }

        this.SaveCardState();
        return true;
    }

    public bool LoadCode(string code)
    {
        if (!ShareCode.TryDecode(code, out var seed, out var marks, out var kind))
            return false;

        var generated = CardGenerator.Generate(Pool, seed, kind, Config.CursedMode);
        var card = new BingoCard(seed, kind, $"Shared card ({kind})", generated.Cells, DateTime.UtcNow);
        card.ApplyMarks(marks);

        this.Card = card;
        this.PartyChatMessages = 0;
        this.Wipes = 0;
        this.firedDetectors.Clear();
        this.SaveCardState();
        this.plugin.NotifyCardChanged();
        return true;
    }

    public string CurrentReport() => this.Card?.ToReport(this.Card.FinishedUtc ?? DateTime.UtcNow) ?? "No active card.";

    public string CurrentCode() => this.Card is null ? string.Empty : ShareCode.Encode(this.Card.Seed, this.Card.MarksMask, this.Card.Kind);

    // --------------------------------------------------------------- detection ----

    private void OnUpdate(IFramework framework)
    {
        if (this.Card is null || this.Card.Finished || !Config.Enabled)
            return;

        var now = DateTime.UtcNow;

        if (Config.AutoDetectDeaths && now >= this.nextPartyScan)
        {
            this.nextPartyScan = now.AddMilliseconds(250);
            this.ScanPartyDeaths();
        }

        if (Config.AutoDetectVulnStacks && now >= this.nextVulnScan)
        {
            this.nextVulnScan = now.AddSeconds(1);
            this.ScanVulnerabilityStacks();
        }
    }

    private void ScanPartyDeaths()
    {
        try
        {
            this.ScanPartyDeathsCore();
        }
        catch (Exception ex)
        {
            // Never let a detection slip take the plugin (and the player's UI) down mid-fight.
            Plugin.Log.Debug(ex, "[RouletteBingo] Party death scan failed.");
        }
    }

    private void ScanPartyDeathsCore()
    {
        var party = Plugin.PartyList;
        var seen = new HashSet<uint>();

        for (var i = 0; i < party.Length; i++)
        {
            var member = party[i];
            if (member is null)
                continue;

            var id = member.EntityId;
            if (id == 0)
                continue;

            seen.Add(id);
            var dead = member.CurrentHP == 0;

            if (this.lastAlive.TryGetValue(id, out var wasAlive))
            {
                if (wasAlive && dead && this.seededAliveState)
                    this.OnPartyDeath(member);
            }

            this.lastAlive[id] = !dead;
        }

        foreach (var stale in this.lastAlive.Keys.Where(k => !seen.Contains(k)).ToList())
            this.lastAlive.Remove(stale);

        this.seededAliveState = true;
    }

    private void OnPartyDeath(IPartyMember member)
    {
        if (member.ContentId != 0 && member.ContentId == Plugin.PlayerState.ContentId)
            this.AutoMark(Detectors.DeathSelf);

        var job = member.ClassJob.RowId;

        if (Detectors.IsDragoon(job))
            this.AutoMark(Detectors.DeathDragoon);
        else if (Detectors.IsTankJob(job))
            this.AutoMark(Detectors.DeathTank);
        else if (Detectors.IsHealerJob(job))
            this.AutoMark(Detectors.DeathHealer);
    }

    private void ScanVulnerabilityStacks()
    {
        try
        {
            this.ScanVulnerabilityStacksCore();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[RouletteBingo] Status scan failed.");
        }
    }

    private void ScanVulnerabilityStacksCore()
    {
        this.vulnerabilityIds ??= Detectors.ResolveVulnerabilityStatusIds(Plugin.DataManager);
        if (this.vulnerabilityIds.Count == 0)
            return;

        var party = Plugin.PartyList;
        for (var i = 0; i < party.Length; i++)
        {
            var member = party[i];
            if (member is null || member.CurrentHP == 0)
                continue;

            var stacks = 0;
            foreach (var status in member.Statuses)
            {
                if (this.vulnerabilityIds.Contains(status.StatusId))
                    stacks++;
            }

            if (stacks >= 3)
            {
                this.AutoMark(Detectors.VulnStacks);
                return;
            }
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (this.Card is null || this.Card.Finished || !Config.AutoDetectChat || !Config.Enabled)
            return;

        if (!Detectors.IsPartyChat(message.LogKind))
            return;

        this.PartyChatMessages++;

        var text = message.Message.TextValue;
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var (id, pattern) in Detectors.Rules)
        {
            if (this.firedDetectors.Contains(id))
                continue;

            if (pattern.IsMatch(text))
                this.AutoMark(id);
        }

        if (!this.firedDetectors.Contains(Detectors.ChatQuestion) && Detectors.IsQuestion(text))
            this.AutoMark(Detectors.ChatQuestion);
    }

    private void AutoMark(string detector)
    {
        if (this.Card is null || this.Card.Finished)
            return;

        if (!this.firedDetectors.Add(detector))
            return;

        var linesBefore = this.Card.CompletedLines.Count;
        var hits = this.Card.AutoMark(detector, DateTime.UtcNow);
        if (hits.Count == 0)
            return;

        Plugin.Log.Debug("[RouletteBingo] Auto-marked {Count} square(s) for '{Detector}'.", hits.Count, detector);

        foreach (var hit in hits)
            this.NoteManualMark(this.Card.Cells[hit], automatic: true);

        this.AutoMarked?.Invoke(this.Card, hits);

        var lines = this.Card.CompletedLines;
        if (lines.Count > linesBefore)
            this.OnBingo(this.Card, lines[^1].Label);

        this.SaveCardState();
    }

    // ------------------------------------------------------------------ events ----

    private void OnBingo(BingoCard card, string label)
    {
        Plugin.Log.Information("[RouletteBingo] BINGO: {Line} ({Marked}/25).", label, card.MarkedCount);

        if (Config.AnnounceBingoInParty && card.Kind != DutyKind.Other)
        {
            Plugin.ChatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Party,
                Message = $"BINGO: {label} - {card.MarkedCount}/25 squares",
            });
        }

        this.LineCompleted?.Invoke(card, label);
    }

    private void NoteManualMark(Suggestion square, bool automatic = false)
    {
        if (!automatic)
        {
            Config.SquareCounts.TryGetValue(square.Id, out var count);
            Config.SquareCounts[square.Id] = count + 1;
        }

        if (automatic || !Config.Snark || square.Category == BingoCategory.Free)
            return;

        // Not every mark needs a comment; a deadpan aside now and then is the joke.
        if (Random.Shared.NextDouble() > 0.22)
            return;

        var lines = square.Rarity switch
        {
            BingoRarity.Cursed => Plugin.CursedSnark,
            BingoRarity.Rare => Plugin.RareSnark,
            _ => Plugin.Snark,
        };

        Plugin.ChatGui.Print(lines[Random.Shared.Next(lines.Length)]);
    }

    // ------------------------------------------------------------------ finish ----

    private void FinalizeRun(bool completed)
    {
        var card = this.Card;
        if (card is null || card.Finished)
            return;

        card.Finish(DateTime.UtcNow);

        var elapsed = DateTime.UtcNow - card.StartedUtc;
        var worthRecording = completed || card.MarkedCount > 1 || elapsed.TotalSeconds > 60;

        if (worthRecording)
        {
            var record = new RunRecord
            {
                DutyName = card.DutyName,
                Marked = card.MarkedCount,
                Bingos = card.CompletedLines.Count,
                Seconds = (int)elapsed.TotalSeconds,
                FinishedUtc = DateTime.UtcNow,
                Completed = completed,
            };

            Config.History.Insert(0, record);
            if (Config.History.Count > 25)
                Config.History.RemoveRange(25, Config.History.Count - 25);

            Config.TotalCards++;
        }

        Config.TotalBingos += card.CompletedLines.Count;
        this.SaveCardState();
        this.plugin.SaveConfig();

        Plugin.Log.Information(
            "[RouletteBingo] Run finished: {Duty}, {Marked}/25, {Bingos} bingo(s), completed={Completed}.",
            card.DutyName,
            card.MarkedCount,
            card.CompletedLines.Count,
            completed);

        this.plugin.NotifyCardChanged();
    }

    public void SaveCardState()
    {
        var card = this.Card;
        if (card is null)
        {
            Config.ActiveCard = null;
            return;
        }

        Config.ActiveCard = new CardState
        {
            DutyName = card.DutyName,
            Seed = card.Seed,
            Kind = card.Kind,
            Marks = card.MarksMask,
            AutoMarks = card.AutoMask,
            StartedUtcTicks = card.StartedUtc.Ticks,
            FinishedUtcTicks = card.FinishedUtc?.Ticks ?? 0,
            PartyChatMessages = this.PartyChatMessages,
            Wipes = this.Wipes,
        };
    }

    // ------------------------------------------------------------------ helpers ----

    private bool InDuty()
    {
        try
        {
            return Plugin.DutyState.ContentFinderCondition.ValueNullable is not null;
        }
        catch
        {
            return false;
        }
    }

    private (string Name, DutyKind Kind) DescribeDuty(IDutyStateEventArgs? args)
    {
        var name = string.Empty;

        try
        {
            var rowRef = args?.ContentFinderCondition ?? Plugin.DutyState.ContentFinderCondition;
            if (rowRef.ValueNullable is { } cfc)
                name = cfc.Name.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[RouletteBingo] Could not read the content finder condition.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            var territory = Plugin.ClientState.TerritoryType;
            name = territory == 0 ? "Manual card" : $"Duty {territory}";
        }

        return (name.Trim(), this.DetectKind());
    }

    private DutyKind DetectKind()
    {
        if (Plugin.ClientState.IsPvP)
            return DutyKind.Pvp;

        if (Plugin.PartyList.IsAlliance)
            return DutyKind.Alliance;

        var size = Plugin.PartyList.Length;
        if (size >= 8)
            return DutyKind.EightMan;

        return size > 0 ? DutyKind.Dungeon : DutyKind.Other;
    }
}
