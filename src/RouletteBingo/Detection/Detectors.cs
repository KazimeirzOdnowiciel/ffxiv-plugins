using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace RouletteBingo.Detection;

/// <summary>
/// The tiny set of things the plugin is willing to notice on its own. Everything else is
/// honour-system clicking, which is half the fun.
/// </summary>
public static class Detectors
{
    public const string ChatOwave = "chat.owave";
    public const string ChatGg = "chat.gg";
    public const string ChatFirst = "chat.first";
    public const string ChatWhere = "chat.where";
    public const string ChatSorry = "chat.sorry";
    public const string ChatThanks = "chat.ty";
    public const string ChatLag = "chat.lag";
    public const string ChatQuestion = "chat.question";
    public const string DeathTank = "death.tank";
    public const string DeathHealer = "death.healer";
    public const string DeathDragoon = "death.drg";
    public const string DeathSelf = "death.self";
    public const string VulnStacks = "vuln.3";
    public const string Wipe = "duty.wipe";
    public const string Silence = "silence";

    private static readonly (string Id, Regex Pattern)[] ChatRules =
    {
        (ChatOwave, new Regex(@"(^|\s)o/|(^|\s)o7(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (ChatGg, new Regex(@"\bgg\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (ChatFirst, new Regex(@"\b(first time|first run|first pull|1st time|new here|new to this)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (ChatWhere, new Regex(@"\bwhere (do |are )?(we|i) (go|going)\b|\bwhich way\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (ChatSorry, new Regex(@"\b(mb|my bad|sry|sorry|my fault)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (ChatThanks, new Regex(@"\b(ty|thx|thanks|thank you|tyvm)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (ChatLag, new Regex(@"\b(lag|lagging|laggy|dc'd|dcd|disconnect(ed)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    public static IReadOnlyList<(string Id, Regex Pattern)> Rules => ChatRules;

    /// <summary>Only channels where a human typed something at the party.</summary>
    public static bool IsPartyChat(XivChatType type) => type is
        XivChatType.Party or
        XivChatType.Alliance or
        XivChatType.CrossParty or
        XivChatType.PvPTeam or
        XivChatType.Say or
        XivChatType.Yell;

    public static bool IsQuestion(string message)
    {
        var trimmed = message.TrimEnd();
        return trimmed.EndsWith('?') && trimmed.Length >= 12;
    }

    private static readonly HashSet<uint> TankJobs = new() { 1, 3, 19, 21, 32, 37 };

    private static readonly HashSet<uint> HealerJobs = new() { 6, 24, 28, 33, 40 };

    private const uint DragoonJob = 22;

    public static bool IsTankJob(uint jobId) => TankJobs.Contains(jobId);

    public static bool IsHealerJob(uint jobId) => HealerJobs.Contains(jobId);

    public static bool IsDragoon(uint jobId) => jobId == DragoonJob;

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

    public static IEnumerable<string> TopSquares(IReadOnlyDictionary<string, int> counts, int take) =>
        counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(take).Select(kv => kv.Key);
}
