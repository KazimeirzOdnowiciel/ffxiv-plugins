using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SlotYield.UI;

/// <summary>A one-page reference of the window's controls and shortcuts.</summary>
public sealed class HelpWindow : Window
{
    private static readonly (string Keys, string Action)[] Mouse =
    {
        ("left-click a row", "open the details popup"),
        ("ctrl + click name", "add it to the multi-select"),
        ("right-click a row", "pin, copy, universalis, details"),
    };

    private static readonly (string Keys, string Action)[] Keyboard =
    {
        ("ctrl + f", "focus the item search"),
        ("esc", "clear the search"),
        ("up / down", "move the selected row"),
        ("enter", "pin the selected row"),
    };

    private static readonly (string Keys, string Action)[] Commands =
    {
        ("/slotyield", "open the main window"),
        ("/slotyield worth", "open the net worth window"),
    };

    public HelpWindow()
        : base("Slot Yield - help###SlotYieldHelp")
    {
        this.Size = new Vector2(480f, 440f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        UiKit.SectionHeader("What it does");
        Theme.Dimmed(
            "A scan prices every marketable item on your world, models how much of each item's daily " +
            "sales you can realistically capture, and solves which slots earn the most gil per day.");

        UiKit.SectionHeader("Mouse");
        DrawPairs("##helpmouse", Mouse);

        UiKit.SectionHeader("Keyboard");
        DrawPairs("##helpkeys", Keyboard);

        UiKit.SectionHeader("Commands");
        DrawPairs("##helpcommands", Commands);

        UiKit.SectionHeader("Windows");
        Theme.Faint("Net worth - your holdings and cash; Ticker - a top-three overlay; history - the full price chart.");
    }

    private static void DrawPairs(string id, (string Keys, string Action)[] rows)
    {
        if (!ImGui.BeginTable(id, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit))
            return;

        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthStretch);

        foreach (var (keys, action) in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Theme.Colored(Theme.Accent, keys);
            ImGui.TableNextColumn();
            Theme.Dimmed(action);
        }

        ImGui.EndTable();
    }
}
