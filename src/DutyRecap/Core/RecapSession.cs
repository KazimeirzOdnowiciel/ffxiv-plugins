using System;
using System.Collections.Generic;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.DutyState;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using DutyRecap.Detection;

namespace DutyRecap.Core;

/// <summary>
/// Owns the live run: who has died, how often, how many wipes, and how much the party chatted.
/// The detection is the same party-scan logic Roulette Bingo uses, aggregated per player.
/// </summary>
public sealed class RecapSession : IDisposable
{
    private readonly Plugin plugin;

    private readonly Dictionary<uint, PlayerStat> players = new();
    private readonly Dictionary<uint, bool> lastAlive = new();

    private DateTime nextPartyScan = DateTime.MinValue;
    private DateTime nextVulnScan = DateTime.MinValue;
    private bool seededAliveState;
    private HashSet<uint>? vulnerabilityIds;

    private int wipes;
    private int partyChatMessages;
    private bool firstBlood;
    private string firstBloodName = string.Empty;
    private int deaths;
    private readonly List<PlayerStat> sortedPlayers = new();
    private bool sortedDirty = true;
    private readonly List<uint> staleIds = new();

    public RecapSession(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public RunRecap? Current { get; private set; }

    public int LiveWipes => this.wipes;

    public int LiveDeaths => this.deaths;

    public IReadOnlyList<PlayerStat> LivePlayers
    {
        get
        {
            if (this.sortedDirty)
            {
                this.sortedPlayers.Clear();
                this.sortedPlayers.AddRange(this.players.Values);
                this.sortedPlayers.Sort((a, b) =>
                {
                    var byDeaths = b.Deaths.CompareTo(a.Deaths);
                    return byDeaths != 0 ? byDeaths : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                this.sortedDirty = false;
            }

            return this.sortedPlayers;
        }
    }

    public event Action<RunRecap>? Completed;

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

    // -------------------------------------------------------------- lifecycle ----

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        if (!Plugin.PluginConfig.Enabled)
            return;

        this.Current = new RunRecap
        {
            DutyName = this.DescribeDuty(args),
            Kind = this.DetectKind(),
            StartedUtc = DateTime.UtcNow,
        };

        this.players.Clear();
        this.lastAlive.Clear();
        this.seededAliveState = false;
        this.wipes = 0;
        this.partyChatMessages = 0;
        this.firstBlood = false;
        this.firstBloodName = string.Empty;
        this.deaths = 0;
        this.sortedPlayers.Clear();
        this.sortedDirty = true;
        this.staleIds.Clear();

        Plugin.Log.Information("[DutyRecap] Started {Duty}.", this.Current.DutyName);
    }

    private void OnDutyWiped(IDutyStateEventArgs args)
    {
        if (this.Current is null)
            return;

        this.wipes++;
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        if (this.Current is null)
            return;

        this.Finalize(completed: true);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        if (this.Current is null || this.InDuty())
            return;

        // Leaving the duty without a completion event: abandoned, disconnected, kicked.
        this.Finalize(completed: false);
    }

    // --------------------------------------------------------------- detection ----

    private void OnUpdate(IFramework framework)
    {
        if (this.Current is null || !Plugin.PluginConfig.Enabled)
            return;

        var now = DateTime.UtcNow;

        if (Plugin.PluginConfig.DetectDeaths && now >= this.nextPartyScan)
        {
            this.nextPartyScan = now.AddMilliseconds(250);
            this.ScanPartyDeaths();
        }

        if (Plugin.PluginConfig.DetectVulnStacks && now >= this.nextVulnScan)
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
            Plugin.Log.Debug(ex, "[DutyRecap] Party death scan failed.");
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
            var player = this.EnsurePlayer(member);

            var dead = member.CurrentHP == 0;
            if (this.lastAlive.TryGetValue(id, out var wasAlive))
            {
                if (wasAlive && dead && this.seededAliveState)
                {
                    player.Deaths++;
                    this.deaths++;
                    this.sortedDirty = true;

                    if (!this.firstBlood)
                    {
                        this.firstBlood = true;
                        this.firstBloodName = player.Name;
                    }
                }
            }

            this.lastAlive[id] = !dead;
        }

        this.staleIds.Clear();
        foreach (var key in this.lastAlive.Keys)
        {
            if (!seen.Contains(key))
                this.staleIds.Add(key);
        }

        foreach (var id in this.staleIds)
            this.lastAlive.Remove(id);

        this.seededAliveState = true;
    }

    private void ScanVulnerabilityStacks()
    {
        try
        {
            this.ScanVulnerabilityStacksCore();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[DutyRecap] Status scan failed.");
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

            if (stacks <= 0)
                continue;

            var player = this.EnsurePlayer(member);
            if (stacks > player.PeakVuln)
                player.PeakVuln = stacks;
        }
    }

    private PlayerStat EnsurePlayer(IPartyMember member)
    {
        var id = member.EntityId;
        if (this.players.TryGetValue(id, out var player))
            return player;

        var jobId = member.ClassJob.RowId;
        player = new PlayerStat
        {
            Name = member.Name.TextValue,
            JobId = jobId,
            JobName = Detectors.JobName(Plugin.DataManager, jobId),
            IsSelf = member.ContentId != 0 && member.ContentId == Plugin.PlayerState.ContentId,
        };

        this.players[id] = player;
        this.sortedDirty = true;
        return player;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (this.Current is null || !Plugin.PluginConfig.Enabled || !Plugin.PluginConfig.DetectChat)
            return;

        if (!Detectors.IsPartyChat(message.LogKind))
            return;

        this.partyChatMessages++;
    }

    // ------------------------------------------------------------------ finish ----

    private void Finalize(bool completed)
    {
        var run = this.Current;
        if (run is null)
            return;

        run.FinishedUtc = DateTime.UtcNow;
        run.Completed = completed;
        run.Wipes = this.wipes;
        run.PartyChatMessages = this.partyChatMessages;
        run.TotalDeaths = this.deaths;
        run.FirstBloodName = this.firstBlood ? this.firstBloodName : string.Empty;
        run.Players = new List<PlayerStat>(this.LivePlayers);
        run.Grade = Grade.Compute(run);
        run.Snark = Grade.SnarkFor(run);

        var config = Plugin.PluginConfig;
        config.History.Insert(0, run);
        if (config.History.Count > 50)
            config.History.RemoveRange(50, config.History.Count - 50);

        this.plugin.SaveConfig();

        Plugin.Log.Information(
            "[DutyRecap] {Duty}: {Deaths} deaths, {Wipes} wipes, completed={Completed}, grade {Grade}.",
            run.DutyName,
            run.TotalDeaths,
            run.Wipes,
            completed,
            run.Grade);

        this.Completed?.Invoke(run);

        if (completed && config.AutoOpenOnComplete)
            this.plugin.OpenRecap();

        this.Current = null;
        this.players.Clear();
        this.lastAlive.Clear();
        this.seededAliveState = false;
        this.sortedPlayers.Clear();
        this.sortedDirty = true;
        this.deaths = 0;
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

    private string DescribeDuty(IDutyStateEventArgs? args)
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
            Plugin.Log.Debug(ex, "[DutyRecap] Could not read the content finder condition.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            var territory = Plugin.ClientState.TerritoryType;
            name = territory == 0 ? "Unknown duty" : $"Duty {territory}";
        }

        return name.Trim();
    }

    private RecapKind DetectKind()
    {
        if (Plugin.ClientState.IsPvP)
            return RecapKind.Pvp;

        if (Plugin.PartyList.IsAlliance)
            return RecapKind.Alliance;

        var size = Plugin.PartyList.Length;
        if (size >= 8)
            return RecapKind.EightMan;

        return size > 0 ? RecapKind.Dungeon : RecapKind.Other;
    }
}
