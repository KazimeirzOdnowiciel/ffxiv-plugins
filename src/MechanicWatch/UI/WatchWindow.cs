using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MechanicWatch.UI;

public sealed class WatchWindow : Window
{
    private static readonly Vector4 Urgent = new(0.94f, 0.36f, 0.33f, 1f);
    private static readonly Vector4 Soon = new(0.96f, 0.72f, 0.30f, 1f);
    private static readonly Vector4 Calm = new(0.85f, 0.88f, 0.92f, 1f);
    private static readonly Vector4 Faint = new(0.50f, 0.52f, 0.58f, 1f);

    private readonly Plugin plugin;

    public WatchWindow(Plugin plugin)
        : base("Mechanic Watch###MechanicWatchMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(520f, 300f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(340f, 160f),
            MaximumSize = new Vector2(1600f, 900f),
        };
    }

    public override void OnOpen()
    {
        this.plugin.Configuration.WindowOpen = true;
        this.plugin.Configuration.Save();
    }

    public override void OnClose()
    {
        this.plugin.Configuration.WindowOpen = false;
        this.plugin.Configuration.Save();
    }

    public override void Draw()
    {
        if (ImGui.SmallButton("Capture"))
        {
            var added = this.plugin.Capture();
            ImGui.SetTooltip(added > 0
                ? $"added {added} status(es)"
                : "nothing new - every status on the party is already watched");
        }

        ImGui.SameLine();

        if (ImGui.SmallButton("Clear"))
        {
            this.plugin.Configuration.Watch.Clear();
            this.plugin.Configuration.Save();
        }

        ImGui.SameLine();

        var auto = this.plugin.Configuration.AutoLearn;
        if (ImGui.Checkbox("auto", ref auto))
        {
            this.plugin.Configuration.AutoLearn = auto;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Learns statuses by itself while in combat. Removed statuses stay removed.");

        ImGui.SameLine();
        ImGui.TextColored(Faint, $"{this.plugin.Configuration.Watch.Count} watched - right-click to remove");

        var members = this.plugin.GetMembers();

        if (members.Count == 0)
        {
            ImGui.TextColored(Faint, "Not in a party.");
            return;
        }

        var watch = this.plugin.Configuration.Watch;

        if (watch.Count == 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Faint, "No statuses watched yet.");
            ImGui.TextColored(Faint, "Press Capture during a mechanic: every status on the party is added.");
            return;
        }

        ImGui.Separator();

        var columns = watch.Count + 2;

        if (!ImGui.BeginTable("##watch", columns,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.NoSavedSettings))
            return;

        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 2.4f, 0);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 26f, 1);

        foreach (var member in members)
        {
            ImGui.TableSetupColumn(member.Job, ImGuiTableColumnFlags.WidthFixed, 46f);
        }

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        for (var i = 0; i < columns; i++)
        {
            ImGui.TableNextColumn();

            if (i < 2)
            {
                ImGui.TableHeader(string.Empty);
                continue;
            }

            var member = members[i - 2];
            ImGui.TableHeader(member.Job);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(member.IsSelf ? $"{member.Name} (you)" : member.Name);
            }
        }

        foreach (var entry in watch.ToList())
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Selectable(entry.Name, false, ImGuiSelectableFlags.None, new Vector2(0f, 0f));

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                this.plugin.Remove(entry.StatusId);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    this.plugin.StatusName(entry.StatusId) +
                    $"\nstatus id {entry.StatusId}\nright-click to stop watching");
            }

            var holders = 0;
            var cells = new List<(bool Has, float Remaining, ushort Param)>(members.Count);

            foreach (var member in members)
            {
                var found = member.Statuses.FirstOrDefault(status => status.StatusId == entry.StatusId);

                if (found is null)
                {
                    cells.Add((false, 0f, 0));
                    continue;
                }

                holders++;
                cells.Add((true, found.Remaining, found.Param));
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(Faint, holders.ToString());

            foreach (var cell in cells)
            {
                ImGui.TableNextColumn();

                if (!cell.Has)
                {
                    ImGui.TextColored(Faint, "-");
                    continue;
                }

                var color = cell.Remaining <= 3f ? Urgent : cell.Remaining <= 6f ? Soon : Calm;
                var text = cell.Param > 1
                    ? $"{MathF.Ceiling(cell.Remaining):0}s x{cell.Param}"
                    : $"{MathF.Ceiling(cell.Remaining):0}s";

                ImGui.TextColored(color, text);
            }
        }

        ImGui.EndTable();
    }
}
