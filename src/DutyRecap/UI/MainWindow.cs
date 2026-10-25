using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DutyRecap.Core;
using DutyRecap.Detection;

namespace DutyRecap.UI;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Duty Recap###DutyRecapMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(560, 560);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 300),
            MaximumSize = new Vector2(1200, 1600),
        };
    }

    private Configuration Config => this.plugin.Configuration;

    private RecapSession Session => this.plugin.Session;

    public override void OnOpen()
    {
        this.Config.WindowOpen = true;
        this.plugin.SaveConfig();
    }

    public override void OnClose()
    {
        this.Config.WindowOpen = false;
        this.plugin.SaveConfig();
    }

    public override void Draw()
    {
        if (!this.Config.Enabled)
        {
            ImGui.TextWrapped("Duty Recap is disabled. Turn it back on in the settings.");
            if (ImGui.Button("Open settings"))
                this.plugin.OpenConfig();

            return;
        }

        if (this.Session.Current is { } live)
        {
            this.DrawLive(live);
            return;
        }

        var last = this.Config.History.FirstOrDefault();
        if (last is null)
        {
            this.DrawEmptyState();
            return;
        }

        this.DrawFinished(last);
    }

    // ------------------------------------------------------------------- live ----

    private void DrawLive(RunRecap run)
    {
        this.DrawTitle("D U T Y   R E C A P", run.DutyName);

        ColoredText(Theme.Gold, "in progress");
        ColoredText(Theme.TextDim, DescribeKind(run.Kind));
        ColoredText(Theme.Text, $"Elapsed: {Detectors.FormatClock(DateTime.UtcNow - run.StartedUtc)}");
        ColoredText(Theme.TextDim, $"Deaths so far: {this.Session.LiveDeaths}   -   Wipes: {this.Session.LiveWipes}");

        ImGui.Spacing();
        ImGui.Separator();

        this.DrawPlayerTable(this.Session.LivePlayers);

        ImGui.Separator();
        if (ImGui.SmallButton("settings"))
            this.plugin.OpenConfig();
    }

    // ---------------------------------------------------------------- finished ----

    private void DrawFinished(RunRecap run)
    {
        var elapsed = run.FinishedUtc - run.StartedUtc;
        var status = run.Completed ? "cleared" : "left";

        this.DrawTitle("D U T Y   R E C A P", run.DutyName);

        ColoredText(Theme.TextDim, DescribeKind(run.Kind));
        ColoredText(Theme.Text, $"{Detectors.FormatClock(elapsed)}  -  {status}");
        ColoredText(Theme.TextDim, $"{run.Wipes} wipe{(run.Wipes == 1 ? string.Empty : "s")}  -  {run.TotalDeaths} death{(run.TotalDeaths == 1 ? string.Empty : "s")}");

        if (run.PartyChatMessages > 0)
            ColoredText(Theme.TextFaint, $"{run.PartyChatMessages} party chat message{(run.PartyChatMessages == 1 ? string.Empty : "s")}");

        ImGui.Spacing();

        ImGui.TextUnformatted("Grade: ");
        ImGui.SameLine();
        ColoredText(Theme.GradeColor(run.Grade), run.Grade);

        if (run.Snark.Length > 0)
        {
            ImGui.SameLine();
            ColoredText(Theme.TextFaint, run.Snark);
        }

        if (!string.IsNullOrWhiteSpace(run.FirstBloodName))
            ColoredText(Theme.TextFaint, $"First blood: {run.FirstBloodName}");

        ImGui.Spacing();
        ImGui.Separator();

        this.DrawPlayerTable(run.Players);

        this.DrawBest(run);

        ImGui.Separator();

        if (ImGui.Button("Copy recap"))
        {
            ImGui.SetClipboardText(BuildRecapLine(run, elapsed));
            this.plugin.Print("Recap copied to the clipboard.");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("settings"))
            this.plugin.OpenConfig();

        this.DrawHistory();
    }

    private void DrawPlayerTable(IReadOnlyList<PlayerStat> players)
    {
        if (players.Count == 0)
        {
            ColoredText(Theme.TextFaint, "No party members recorded.");
            return;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.NoSavedSettings;

        if (!ImGui.BeginTable("##players", 4, flags))
            return;

        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 2f, 0);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 90f, 1);
        ImGui.TableSetupColumn("Deaths", ImGuiTableColumnFlags.WidthFixed, 64f, 2);
        ImGui.TableSetupColumn("Vuln", ImGuiTableColumnFlags.WidthFixed, 52f, 3);
        ImGui.TableHeadersRow();

        foreach (var player in players)
        {
            ImGui.TableNextColumn();
            var name = player.IsSelf ? player.Name + " (you)" : player.Name;
            ColoredText(player.IsSelf ? Theme.Gold : Theme.Text, name);

            ImGui.TableNextColumn();
            var job = string.IsNullOrEmpty(player.JobName) ? RoleLabel(player.JobId) : player.JobName;
            ColoredText(Theme.RoleColor(player.JobId), job);

            ImGui.TableNextColumn();
            ColoredText(player.Deaths > 0 ? Theme.Danger : Theme.TextDim, player.Deaths.ToString());

            ImGui.TableNextColumn();
            ColoredText(player.PeakVuln >= 3 ? Theme.Danger : Theme.TextDim, player.PeakVuln.ToString());
        }

        ImGui.EndTable();
    }

    private void DrawBest(RunRecap run)
    {
        var peers = this.Config.History
            .Where(r => r.DutyName == run.DutyName && r.Completed && r != run)
            .ToList();

        if (peers.Count == 0)
            return;

        var bestTime = peers.Min(r => (r.FinishedUtc - r.StartedUtc).TotalSeconds);
        var bestDeaths = peers.Min(r => r.TotalDeaths);

        ImGui.Spacing();
        ColoredText(
            Theme.TextDim,
            $"Personal best: {Detectors.FormatClock(TimeSpan.FromSeconds(bestTime))}  -  fewest deaths: {bestDeaths}");
    }

    private void DrawHistory()
    {
        var history = this.Config.History;
        if (history.Count <= 1)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Recent runs");

        foreach (var run in history.Skip(1).Take(5))
        {
            var when = run.FinishedUtc.ToLocalTime().ToString("MMM d HH:mm");
            var result = run.Completed ? "cleared" : "left";
            ColoredText(
                Theme.TextDim,
                $"{when}  {Truncate(run.DutyName, 24),-24}  {run.Wipes} wipe  {run.TotalDeaths} death  {result}  {run.Grade}");
        }
    }

    private void DrawEmptyState()
    {
        this.DrawTitle("D U T Y   R E C A P", "No duty yet");

        ImGui.TextWrapped(
            "A scorecard is recorded automatically when a duty starts and lands here when it ends. " +
            "Queue for something you will regret and come back.");

        ImGui.Spacing();
        if (ImGui.SmallButton("settings"))
            this.plugin.OpenConfig();
    }

    private void DrawTitle(string eyebrow, string title)
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();

        var labelPos = ImGui.GetCursorScreenPos();
        drawList.AddText(font, 11f, labelPos, Theme.U32(Theme.Gold), eyebrow);
        ImGui.Dummy(new Vector2(0f, 16f));

        var titlePos = ImGui.GetCursorScreenPos();
        drawList.AddText(font, 20f, titlePos, Theme.U32(Theme.Text), title);
        ImGui.Dummy(new Vector2(0f, 28f));
    }

    private static string BuildRecapLine(RunRecap run, TimeSpan elapsed) =>
        $"{run.DutyName} — {Detectors.FormatClock(elapsed)}, {run.Wipes} wipe{(run.Wipes == 1 ? string.Empty : "s")}, " +
        $"{run.TotalDeaths} death{(run.TotalDeaths == 1 ? string.Empty : "s")}. Grade {run.Grade}.";

    private static string DescribeKind(RecapKind kind) => kind switch
    {
        RecapKind.Dungeon => "4-player dungeon",
        RecapKind.EightMan => "8-player duty",
        RecapKind.Alliance => "24-player alliance raid",
        RecapKind.Pvp => "PvP",
        _ => "duty",
    };

    private static string RoleLabel(uint jobId) =>
        Detectors.IsTankJob(jobId) ? "Tank" : Detectors.IsHealerJob(jobId) ? "Healer" : "DPS";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "...";

    private static void ColoredText(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
