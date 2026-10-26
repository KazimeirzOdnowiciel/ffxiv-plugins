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

    private RunRecap? selectedRun;
    private string historySearch = string.Empty;
    private int historyCategoryIndex;
    private bool historyClearedOnly;

    public MainWindow(Plugin plugin)
        : base("Duty Recap###DutyRecapMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(680, 700);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 360),
            MaximumSize = new Vector2(1500, 1800),
        };
        this.BgAlpha = 1f;
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

        if (!ImGui.BeginTabBar("##dutyrecap-tabs"))
            return;

        if (ImGui.BeginTabItem("This run"))
        {
            this.DrawThisRunTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("History"))
        {
            this.DrawHistoryTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Records"))
        {
            this.DrawRecordsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // ---------------------------------------------------------------- this run ----

    private void DrawThisRunTab()
    {
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

    private void DrawLive(RunRecap run)
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var p = ImGui.GetCursorScreenPos();

        drawList.AddCircleFilled(p + new Vector2(5f, 7f), 4f, Theme.U32(Theme.Gold));
        drawList.AddText(font, 18f, p + new Vector2(16f, 0f), Theme.U32(Theme.Text), Truncate(run.DutyName, 42));
        ImGui.Dummy(new Vector2(0f, 24f));

        ColoredText(
            Theme.TextDim,
            $"{ContentLabel(run)}  -  {Detectors.FormatClock(DateTime.UtcNow - run.StartedUtc)}  -  in progress");
        ColoredText(Theme.TextDim, $"deaths so far {this.Session.LiveDeaths}  -  wipes {this.Session.LiveWipes}");

        DrawGoldRule();

        this.DrawPlayerTable(this.Session.LivePlayers);

        ImGui.Spacing();
        if (ImGui.SmallButton("settings"))
            this.plugin.OpenConfig();
    }

    private void DrawFinished(RunRecap run)
    {
        var elapsed = run.FinishedUtc - run.StartedUtc;
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var p = ImGui.GetCursorScreenPos();

        DrawStamp(drawList, p, 52f, run.Grade, Theme.GradeColor(run.Grade));

        var namePos = p + new Vector2(66f, 2f);
        drawList.AddText(font, 18f, namePos, Theme.U32(Theme.Text), Truncate(run.DutyName, 40));

        var metaPos = p + new Vector2(66f, 26f);
        drawList.AddText(
            font,
            13f,
            metaPos,
            Theme.U32(Theme.TextDim),
            $"{ContentLabel(run)}  -  {Detectors.FormatClock(elapsed)}");

        ImGui.Dummy(new Vector2(0f, 56f));

        DrawStatusPill(run.Completed ? "cleared" : "left", run.Completed ? Theme.Green : Theme.TextDim);

        if (run.Snark.Length > 0)
            ColoredText(Theme.TextFaint, run.Snark);

        if (!string.IsNullOrWhiteSpace(run.FirstBloodName))
            ColoredText(Theme.TextFaint, $"first blood  {run.FirstBloodName}");

        ImGui.Spacing();
        DrawStatRow(run.Wipes, run.TotalDeaths, run.PartyChatMessages);

        DrawGoldRule();
        this.DrawPullTimeline(run);

        this.DrawPlayerTable(run.Players);

        this.DrawBest(run);

        this.DrawChatSection(run);

        this.DrawFooterBar(run, elapsed);
    }

    private void DrawFooterBar(RunRecap run, TimeSpan elapsed)
    {
        DrawGoldRule();

        if (ImGui.Button("Copy recap"))
        {
            ImGui.SetClipboardText(BuildRecapLine(run, elapsed));
            this.plugin.Print("Recap copied to the clipboard.");
        }

        ImGui.SameLine(0f, 8f);
        if (ImGui.SmallButton("settings"))
            this.plugin.OpenConfig();

        ImGui.SameLine(0f, 16f);
        ColoredText(
            Theme.TextFaint,
            $"{run.Wipes} wipe{(run.Wipes == 1 ? string.Empty : "s")}  -  {run.TotalDeaths} death{(run.TotalDeaths == 1 ? string.Empty : "s")}");
    }

    // ---------------------------------------------------------------- history ----

    private void DrawHistoryTab()
    {
        var history = this.Config.History;
        if (history.Count == 0)
        {
            ColoredText(Theme.TextFaint, "No runs stored yet. Queue for something and come back.");
            return;
        }

        var categories = this.BuildCategoryList(history);

        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##histsearch", "search duty...", ref this.historySearch, 64);
        ImGui.SameLine(0f, 8f);

        if (this.historyCategoryIndex < 0 || this.historyCategoryIndex >= categories.Count)
            this.historyCategoryIndex = 0;

        ImGui.SetNextItemWidth(140f);
        ImGui.Combo("##histcat", ref this.historyCategoryIndex, categories.ToArray(), categories.Count);
        ImGui.SameLine(0f, 8f);
        ImGui.Checkbox("cleared only", ref this.historyClearedOnly);

        var filtered = this.FilterHistory(history, categories);
        if (filtered.Count == 0)
        {
            ImGui.Spacing();
            ColoredText(Theme.TextFaint, "No runs match the filter.");
            return;
        }

        ColoredText(Theme.TextFaint, $"{filtered.Count} of {history.Count} runs");

        var listHeight = Math.Max(140f, ImGui.GetContentRegionAvail().Y * 0.5f);
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.ScrollY;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8f, 6f));
        if (ImGui.BeginTable("##history", 6, flags, new Vector2(0f, listHeight)))
        {
            ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, 82f, 0);
            ImGui.TableSetupColumn("Duty", ImGuiTableColumnFlags.WidthStretch, 2.4f, 1);
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 100f, 2);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 58f, 3);
            ImGui.TableSetupColumn("W/D", ImGuiTableColumnFlags.WidthFixed, 54f, 4);
            ImGui.TableSetupColumn("Grade", ImGuiTableColumnFlags.WidthFixed, 44f, 5);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            for (var index = 0; index < filtered.Count; index++)
            {
                var run = filtered[index];
                ImGui.TableNextColumn();
                if (ImGui.Selectable(run.FinishedUtc.ToLocalTime().ToString("MM-dd HH:mm"), ReferenceEquals(this.selectedRun, run)))
                    this.selectedRun = run;

                ImGui.TableNextColumn();
                ColoredText(Theme.Text, Truncate(run.DutyName, 34));

                ImGui.TableNextColumn();
                ColoredText(Theme.TextDim, ContentShort(run));

                ImGui.TableNextColumn();
                ColoredText(Theme.TextDim, Detectors.FormatClock(run.FinishedUtc - run.StartedUtc));

                ImGui.TableNextColumn();
                ColoredText(Theme.TextDim, $"{run.Wipes}w {run.TotalDeaths}d");

                ImGui.TableNextColumn();
                ColoredText(Theme.GradeColor(run.Grade), run.Grade);
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();

        var selected = this.selectedRun;
        if (selected is null || !filtered.Contains(selected))
            selected = filtered[0];

        DrawGoldRule();
        ImGui.TextUnformatted(Truncate(selected.DutyName, 48));
        ColoredText(
            Theme.TextDim,
            $"{ContentLabel(selected)}  -  {(selected.Completed ? "cleared" : "left")}  -  {Detectors.FormatClock(selected.FinishedUtc - selected.StartedUtc)}  -  {selected.Wipes} wipes  -  {selected.TotalDeaths} deaths");

        this.DrawPullTimeline(selected);

        this.DrawChatSection(selected);
    }

    private List<string> BuildCategoryList(List<RunRecap> history)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < history.Count; index++)
            set.Add(ContentShort(history[index]));

        var list = new List<string> { "All" };
        list.AddRange(set);
        return list;
    }

    private List<RunRecap> FilterHistory(List<RunRecap> history, List<string> categories)
    {
        string? category = this.historyCategoryIndex > 0 && this.historyCategoryIndex < categories.Count
            ? categories[this.historyCategoryIndex]
            : null;
        var search = this.historySearch.Trim();

        var result = new List<RunRecap>();
        for (var index = 0; index < history.Count; index++)
        {
            var run = history[index];
            if (this.historyClearedOnly && !run.Completed)
                continue;
            if (category is not null && !string.Equals(ContentShort(run), category, StringComparison.OrdinalIgnoreCase))
                continue;
            if (search.Length > 0 && run.DutyName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            result.Add(run);
        }

        return result;
    }

    // ---------------------------------------------------------------- records ----

    private void DrawRecordsTab()
    {
        var history = this.Config.History;
        if (history.Count == 0)
        {
            ColoredText(Theme.TextFaint, "No runs stored yet.");
            return;
        }

        this.DrawRecordsSummary(history);

        var groups = new Dictionary<string, List<RunRecap>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < history.Count; index++)
        {
            var run = history[index];
            if (!groups.TryGetValue(run.DutyName, out var list))
            {
                list = new List<RunRecap>();
                groups[run.DutyName] = list;
            }

            list.Add(run);
        }

        var names = new List<string>(groups.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8f, 6f));
        if (ImGui.BeginTable("##records", 6, flags))
        {
            ImGui.TableSetupColumn("Duty", ImGuiTableColumnFlags.WidthStretch, 2.2f, 0);
            ImGui.TableSetupColumn("Runs", ImGuiTableColumnFlags.WidthFixed, 44f, 1);
            ImGui.TableSetupColumn("Fastest", ImGuiTableColumnFlags.WidthFixed, 64f, 2);
            ImGui.TableSetupColumn("Avg", ImGuiTableColumnFlags.WidthFixed, 64f, 3);
            ImGui.TableSetupColumn("Best deaths", ImGuiTableColumnFlags.WidthFixed, 80f, 4);
            ImGui.TableSetupColumn("Last", ImGuiTableColumnFlags.WidthFixed, 96f, 5);
            ImGui.TableHeadersRow();

            for (var index = 0; index < names.Count; index++)
            {
                var runs = groups[names[index]];
                var stats = ComputeStats(runs);

                ImGui.TableNextColumn();
                ColoredText(Theme.Text, Truncate(names[index], 34));

                ImGui.TableNextColumn();
                ColoredText(Theme.TextDim, runs.Count.ToString());

                ImGui.TableNextColumn();
                ColoredText(Theme.TextDim, stats.FastestSeconds > 0 ? Detectors.FormatClock(TimeSpan.FromSeconds(stats.FastestSeconds)) : "-");

                ImGui.TableNextColumn();
                ColoredText(Theme.TextDim, stats.AverageSeconds > 0 ? Detectors.FormatClock(TimeSpan.FromSeconds(stats.AverageSeconds)) : "-");

                ImGui.TableNextColumn();
                ColoredText(stats.BestDeaths > 0 ? Theme.Danger : Theme.TextDim, stats.BestDeaths.ToString());

                ImGui.TableNextColumn();
                ColoredText(Theme.TextFaint, stats.Last.ToLocalTime().ToString("MMM d HH:mm"));
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();
    }

    private readonly struct DutyStats
    {
        public DutyStats(double fastestSeconds, double averageSeconds, int bestDeaths, DateTime last)
        {
            this.FastestSeconds = fastestSeconds;
            this.AverageSeconds = averageSeconds;
            this.BestDeaths = bestDeaths;
            this.Last = last;
        }

        public double FastestSeconds { get; }

        public double AverageSeconds { get; }

        public int BestDeaths { get; }

        public DateTime Last { get; }
    }

    private static DutyStats ComputeStats(List<RunRecap> runs)
    {
        var fastest = double.MaxValue;
        var sum = 0.0;
        var timed = 0;
        var bestDeaths = int.MaxValue;
        var last = DateTime.MinValue;

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var seconds = (run.FinishedUtc - run.StartedUtc).TotalSeconds;

            if (run.Completed)
            {
                if (seconds < fastest)
                    fastest = seconds;
                sum += seconds;
                timed++;
            }

            if (run.TotalDeaths < bestDeaths)
                bestDeaths = run.TotalDeaths;
            if (run.FinishedUtc > last)
                last = run.FinishedUtc;
        }

        return new DutyStats(
            fastest == double.MaxValue ? 0 : fastest,
            timed == 0 ? 0 : sum / timed,
            bestDeaths == int.MaxValue ? 0 : bestDeaths,
            last == DateTime.MinValue ? DateTime.UtcNow : last);
    }

    // ------------------------------------------------------------------- chat ----

    private void DrawRecordsSummary(List<RunRecap> history)
    {
        var totalSeconds = 0.0;
        var totalWipes = 0;
        var totalDeaths = 0;
        var gradeCounts = new int[GradeLetters.Length];

        for (var index = 0; index < history.Count; index++)
        {
            var run = history[index];
            totalSeconds += Math.Max(0.0, (run.FinishedUtc - run.StartedUtc).TotalSeconds);
            totalWipes += run.Wipes;
            totalDeaths += run.TotalDeaths;
            gradeCounts[GradeIndex(run.Grade)]++;
        }

        ColoredText(Theme.Text, $"{history.Count} runs");
        ImGui.SameLine(0f, 14f);
        ColoredText(Theme.TextDim, $"time {Detectors.FormatClock(TimeSpan.FromSeconds(totalSeconds))}");
        ImGui.SameLine(0f, 14f);
        ColoredText(Theme.TextDim, $"{totalWipes} wipes");
        ImGui.SameLine(0f, 14f);
        ColoredText(Theme.TextDim, $"{totalDeaths} deaths");

        ImGui.Spacing();
        ColoredText(Theme.TextFaint, "grades");
        for (var index = 0; index < GradeLetters.Length; index++)
        {
            ImGui.SameLine(0f, 10f);
            ColoredText(Theme.GradeColor(GradeLetters[index]), $"{GradeLetters[index]} {gradeCounts[index]}");
        }

        DrawGoldRule();
    }

    private static readonly string[] GradeLetters = { "S", "A", "B", "C", "F" };

    private static int GradeIndex(string grade) => grade switch
    {
        "S" => 0,
        "A" => 1,
        "B" => 2,
        "C" => 3,
        _ => 4,
    };

    private void DrawPullTimeline(RunRecap run)
    {
        if (run.WipeTimes is null)
            return;

        var boundaries = new List<DateTime>(run.WipeTimes.Count + 2) { run.StartedUtc };
        boundaries.AddRange(run.WipeTimes);
        boundaries.Add(run.FinishedUtc);

        if (boundaries.Count < 2)
            return;

        var durations = new List<double>(boundaries.Count - 1);
        var total = 0.0;
        var longest = 0.0;
        for (var index = 0; index < boundaries.Count - 1; index++)
        {
            var seconds = Math.Max(0.0, (boundaries[index + 1] - boundaries[index]).TotalSeconds);
            durations.Add(seconds);
            total += seconds;
            if (seconds > longest)
                longest = seconds;
        }

        if (total <= 0)
            return;

        var label = durations.Count == 1 ? "1 pull" : $"{durations.Count} pulls";
        if (run.WipeTimes.Count > 0)
        {
            var firstWipe = Math.Max(0.0, (run.WipeTimes[0] - run.StartedUtc).TotalSeconds);
            label += $"  -  first wipe {Detectors.FormatClock(TimeSpan.FromSeconds(firstWipe))}";
        }

        label += $"  -  longest {Detectors.FormatClock(TimeSpan.FromSeconds(longest))}";
        ColoredText(Theme.TextDim, label);

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float height = 14f;
        var cursor = pos.X;

        for (var index = 0; index < durations.Count; index++)
        {
            var segment = Math.Max(2f, width * (float)(durations[index] / total));
            var isLast = index == durations.Count - 1;
            var color = isLast && run.Completed ? Theme.Green : Theme.Danger;
            drawList.AddRectFilled(
                new Vector2(cursor, pos.Y),
                new Vector2(cursor + segment - 1f, pos.Y + height),
                Theme.U32(color),
                2f);
            cursor += segment;
        }

        ImGui.Dummy(new Vector2(width, height + 4f));
    }

    private void DrawChatSection(RunRecap run)
    {
        var chat = run.Chat;
        if (chat is null || chat.Count == 0)
            return;

        DrawGoldRule();
        ImGui.TextUnformatted($"Chat ({chat.Count})");

        ImGui.BeginChild("##chatlines", new Vector2(0f, 140f));
        for (var index = 0; index < chat.Count; index++)
        {
            var line = chat[index];
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
            ImGui.TextWrapped($"[{line.Utc.ToLocalTime():HH:mm:ss}] {line.Channel}  {line.Sender}: {line.Text}");
            ImGui.PopStyleColor();
        }

        ImGui.EndChild();
    }

    // ------------------------------------------------------------------ table ----

    private void DrawPlayerTable(IReadOnlyList<PlayerStat> players)
    {
        if (players.Count == 0)
        {
            ColoredText(Theme.TextFaint, "No party members recorded.");
            return;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8f, 7f));
        if (ImGui.BeginTable("##players", 4, flags))
        {
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

        ImGui.PopStyleVar();
    }

    private void DrawBest(RunRecap run)
    {
        var bestTime = double.MaxValue;
        var bestDeaths = int.MaxValue;

        foreach (var peer in this.Config.History)
        {
            if (peer.DutyName != run.DutyName || !peer.Completed || ReferenceEquals(peer, run))
                continue;

            var seconds = (peer.FinishedUtc - peer.StartedUtc).TotalSeconds;
            if (seconds < bestTime)
                bestTime = seconds;

            if (peer.TotalDeaths < bestDeaths)
                bestDeaths = peer.TotalDeaths;
        }

        if (bestTime == double.MaxValue)
            return;

        ImGui.Spacing();
        ColoredText(
            Theme.TextDim,
            $"Personal best: {Detectors.FormatClock(TimeSpan.FromSeconds(bestTime))}  -  fewest deaths: {bestDeaths}");
    }

    private void DrawEmptyState()
    {
        ColoredText(Theme.Text, "No duty yet");
        ImGui.TextWrapped(
            "A scorecard is recorded automatically when a duty starts and lands here when it ends. " +
            "Queue for something you will regret and come back.");

        ImGui.Spacing();
        if (ImGui.SmallButton("settings"))
            this.plugin.OpenConfig();
    }

    // ---------------------------------------------------------------- drawing ----

    private static void DrawStamp(ImDrawListPtr drawList, Vector2 pos, float size, string grade, Vector4 color)
    {
        var min = pos;
        var max = pos + new Vector2(size, size);

        drawList.AddRectFilled(min, max, Theme.U32(color), size * 0.18f);

        var inset = size * 0.10f;
        drawList.AddRectFilled(min + new Vector2(inset, inset), max - new Vector2(inset, inset), Theme.U32(Theme.Panel), size * 0.12f);

        var font = ImGui.GetFont();
        var textSize = ImGui.CalcTextSize(grade);
        var scale = 24f / ImGui.GetFontSize();
        var w = textSize.X * scale;
        var h = textSize.Y * scale;
        var center = pos + new Vector2(size / 2f, size / 2f);
        drawList.AddText(font, 24f, center - new Vector2(w / 2f, h / 2f), Theme.U32(color), grade);
    }

    private static void DrawStatRow(int wipes, int deaths, int chat)
    {
        ColoredText(Theme.TextFaint, "wipes");
        ImGui.SameLine(0f, 6f);
        ColoredText(wipes > 0 ? Theme.Danger : Theme.TextDim, wipes.ToString());

        ImGui.SameLine(0f, 16f);
        ColoredText(Theme.TextFaint, "deaths");
        ImGui.SameLine(0f, 6f);
        ColoredText(deaths > 0 ? Theme.Danger : Theme.TextDim, deaths.ToString());

        if (chat > 0)
        {
            ImGui.SameLine(0f, 16f);
            ColoredText(Theme.TextFaint, "chat");
            ImGui.SameLine(0f, 6f);
            ColoredText(Theme.TextDim, chat.ToString());
        }
    }

    private static void DrawGoldRule()
    {
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0f, 4f);
        drawList.AddLine(pos, pos + new Vector2(width, 0f), Theme.WithAlpha(Theme.Gold, 0.22f), 1f);
        ImGui.Dummy(new Vector2(width, 9f));
    }

    private static void DrawStatusPill(string label, Vector4 color)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(textSize.X + 24f, 18f);

        drawList.AddRectFilled(pos, pos + size, Theme.WithAlpha(color, 0.16f), size.Y / 2f);
        drawList.AddCircleFilled(pos + new Vector2(9f, size.Y / 2f), 3f, Theme.U32(color));
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + new Vector2(16f, 2.5f), Theme.U32(color), label);
        ImGui.Dummy(size);
    }

    // ------------------------------------------------------------------- misc ----

    private static string BuildRecapLine(RunRecap run, TimeSpan elapsed) =>
        $"{run.DutyName}, {Detectors.FormatClock(elapsed)}, {run.Wipes} wipe{(run.Wipes == 1 ? string.Empty : "s")}, " +
        $"{run.TotalDeaths} death{(run.TotalDeaths == 1 ? string.Empty : "s")}. Grade {run.Grade}.";

    private static string ContentLabel(RunRecap run)
    {
        var label = string.IsNullOrEmpty(run.ContentType) ? DescribeKind(run.Kind) : run.ContentType;

        if (!string.IsNullOrEmpty(run.Difficulty) && run.Difficulty != "Normal")
            label += "  -  " + run.Difficulty;

        if (!string.IsNullOrEmpty(run.Expansion))
            label += "  -  " + run.Expansion;

        return label;
    }

    private static string ContentShort(RunRecap run) =>
        string.IsNullOrEmpty(run.ContentType) ? DescribeKind(run.Kind) : run.ContentType;

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
