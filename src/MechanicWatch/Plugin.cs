using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Statuses;
using MechanicWatch.UI;

namespace MechanicWatch;

public sealed record MemberStatus(uint StatusId, float Remaining, ushort Param);

public sealed record Member(string Job, string Name, bool IsSelf, IReadOnlyList<MemberStatus> Statuses);

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/mechanicwatch";
    public const string AliasName = "/mwatch";

    private readonly WindowSystem windowSystem = new("MechanicWatch");
    private readonly WatchWindow window;

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IPartyList PartyList { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static ICondition Condition { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    public Configuration Configuration { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Normalize();

        this.window = new WatchWindow(this);
        this.windowSystem.AddWindow(this.window);
        this.window.IsOpen = Configuration.WindowOpen;

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.Open;
        PluginInterface.UiBuilder.OpenConfigUi += this.Open;

        Framework.Update += this.OnTick;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Mechanic Watch. Options: capture, clear.",
        });

        try
        {
            CommandManager.AddHandler(AliasName, new CommandInfo(this.OnCommand)
            {
                HelpMessage = "Open Mechanic Watch.",
            });
        }
        catch
        {
            // alias already taken
        }
    }

    public void Open()
    {
        this.window.IsOpen = true;
        Configuration.WindowOpen = true;
    }

    public void Dispose()
    {
        Framework.Update -= this.OnTick;
        CommandManager.RemoveHandler(CommandName);

        if (CommandManager.Commands.ContainsKey(AliasName))
            CommandManager.RemoveHandler(AliasName);

        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= this.Open;
        PluginInterface.UiBuilder.OpenConfigUi -= this.Open;
        this.windowSystem.RemoveAllWindows();
        Configuration.Save();
    }

    private void OnCommand(string command, string args)
    {
        switch ((args ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "capture":
                var added = this.Capture();
                ChatGui.Print($"[Mechanic Watch] Added {added} status(es). {Configuration.Watch.Count} watched.");
                break;

            case "clear":
                Configuration.Watch.Clear();
                Configuration.Ignored.Clear();
                Configuration.Save();
                ChatGui.Print("[Mechanic Watch] Watchlist cleared.");
                break;

            default:
                this.Open();
                break;
        }
    }

    private DateTime nextScanUtc;

    /// <summary>Learns mechanic statuses by itself while in combat, so Capture is only a fallback.</summary>
    private void OnTick(IFramework framework)
    {
        if (!Configuration.AutoLearn || !Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
            return;

        if (DateTime.UtcNow < this.nextScanUtc)
            return;

        this.nextScanUtc = DateTime.UtcNow.AddMilliseconds(750);
        this.Learn();
    }

    public int Learn()
    {
        var known = Configuration.Watch.Select(entry => entry.StatusId).ToHashSet();
        var ignored = Configuration.Ignored.ToHashSet();
        var added = 0;

        foreach (var member in this.GetMembers())
        {
            foreach (var status in member.Statuses)
            {
                if (status.StatusId == 0 || known.Contains(status.StatusId) || ignored.Contains(status.StatusId))
                    continue;

                if (!this.IsMechanicStatus(status))
                    continue;

                known.Add(status.StatusId);
                Configuration.Watch.Add(new WatchEntry(status.StatusId, this.StatusName(status.StatusId)));
                added++;
            }
        }

        if (added > 0)
        {
            Configuration.Save();
            Log.Debug("[Mechanic Watch] Learned {Added} status(es); {Total} watched.", added, Configuration.Watch.Count);
        }

        return added;
    }

    /// <summary>
    /// Picks statuses the game itself considers party-list worthy, or ones that lock the player,
    /// or short-lived effects. Food, FC buffs and permanent passives are excluded by construction.
    /// </summary>
    private bool IsMechanicStatus(MemberStatus status)
    {
        if (status.Remaining <= 0f || status.Remaining > Configuration.AutoMaxSeconds)
            return false;

        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (sheet is null || !sheet.TryGetRow(status.StatusId, out var row))
                return status.Remaining <= 15f;

            if (row.IsPermanent || row.IsFcBuff)
                return false;

            return row.PartyListPriority > 0
                   || row.LockActions
                   || row.LockControl
                   || row.LockMovement
                   || row.IsGaze;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Adds every status currently on the party to the watchlist.</summary>
    public int Capture()
    {
        var known = Configuration.Watch.Select(entry => entry.StatusId).ToHashSet();
        var added = 0;

        foreach (var member in this.GetMembers())
        {
            foreach (var status in member.Statuses)
            {
                if (status.StatusId == 0 || !known.Add(status.StatusId))
                    continue;

                Configuration.Watch.Add(new WatchEntry(status.StatusId, this.StatusName(status.StatusId)));
                added++;
            }
        }

        if (added > 0)
            Configuration.Save();

        Log.Information("[Mechanic Watch] Captured {Added} status(es); {Total} watched.", added, Configuration.Watch.Count);
        return added;
    }

    public void Remove(uint statusId)
    {
        Configuration.Watch.RemoveAll(entry => entry.StatusId == statusId);

        if (!Configuration.Ignored.Contains(statusId))
            Configuration.Ignored.Add(statusId);

        Configuration.Save();
    }

    public List<Member> GetMembers()
    {
        var members = new List<Member>(8);

        if (PartyList.Length > 0)
        {
            var selfId = ObjectTable.LocalPlayer?.EntityId ?? 0;

            foreach (var member in PartyList)
            {
                members.Add(new Member(
                    JobName(member.ClassJob),
                    member.Name.TextValue,
                    member.EntityId == selfId,
                    Read(member.Statuses)));
            }

            return Label(members);
        }

        if (ObjectTable.LocalPlayer is { } player)
            members.Add(new Member(JobName(player.ClassJob), player.Name.TextValue, true, Read(player.StatusList)));

        return Label(members);
    }

    /// <summary>Two White Mages would produce two identical columns, so number the duplicates.</summary>
    private static List<Member> Label(List<Member> members)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var labeled = new List<Member>(members.Count);

        foreach (var member in members)
        {
            var job = member.Job;

            if (!totals.ContainsKey(job))
            {
                totals[job] = members.Count(other => other.Job == job);
                seen[job] = 0;
            }

            if (totals[job] <= 1)
            {
                labeled.Add(member);
                continue;
            }

            seen[job]++;
            labeled.Add(member with { Job = job + Superscript(seen[job]) });
        }

        return labeled;
    }

    private static string Superscript(int value) => value switch
    {
        1 => "\u00b9",
        2 => "\u00b2",
        3 => "\u00b3",
        4 => "\u2074",
        5 => "\u2075",
        6 => "\u2076",
        7 => "\u2077",
        8 => "\u2078",
        _ => $"({value})",
    };

    public string StatusName(uint statusId)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            return sheet is not null && sheet.TryGetRow(statusId, out var row)
                ? row.Name.ToString() ?? $"Status {statusId}"
                : $"Status {statusId}";
        }
        catch
        {
            return $"Status {statusId}";
        }
    }

    private static string JobName(Lumina.Excel.RowRef<Lumina.Excel.Sheets.ClassJob> job)
    {
        try
        {
            var abbreviation = job.Value.Abbreviation.ToString();
            return string.IsNullOrWhiteSpace(abbreviation) ? "?" : abbreviation;
        }
        catch
        {
            return "?";
        }
    }

    private static List<MemberStatus> Read(StatusList statuses)
    {
        var list = new List<MemberStatus>(8);

        try
        {
            foreach (var status in statuses)
                list.Add(new MemberStatus(status.StatusId, status.RemainingTime, status.Param));
        }
        catch
        {
            // status list mid-update
        }

        return list;
    }
}
