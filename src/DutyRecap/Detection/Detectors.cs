using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace DutyRecap.Detection;

public static class Detectors
{
    private static readonly HashSet<uint> TankJobs = new() { 1, 3, 19, 21, 32, 37 };

    private static readonly HashSet<uint> HealerJobs = new() { 6, 24, 28, 33, 40 };

    public static bool IsTankJob(uint jobId) => TankJobs.Contains(jobId);

    public static bool IsHealerJob(uint jobId) => HealerJobs.Contains(jobId);

    /// <summary>Only channels where a human typed something at the party.</summary>
    public static bool IsPartyChat(XivChatType type) => type is
        XivChatType.Party or
        XivChatType.Alliance or
        XivChatType.CrossParty or
        XivChatType.PvPTeam or
        XivChatType.Say or
        XivChatType.Yell;

    /// <summary>Status ids for "Vulnerability Up", resolved from the game's own sheets when possible.</summary>
    public static HashSet<uint> ResolveVulnerabilityStatusIds(IDataManager data)
    {
        var fallback = new HashSet<uint> { 638 };
        try
        {
            var sheet = data.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (sheet is null)
                return fallback;

            var ids = new HashSet<uint>();
            foreach (var row in sheet)
            {
                var name = row.Name.ToString();
                if (name.Equals("Vulnerability Up", StringComparison.OrdinalIgnoreCase))
                    ids.Add(row.RowId);
            }

            return ids.Count > 0 ? ids : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static string FormatClock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
