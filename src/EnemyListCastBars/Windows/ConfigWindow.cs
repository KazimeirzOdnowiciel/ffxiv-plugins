using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace EnemyListCastBars.Windows;

/// <summary>
/// Settings window for <see cref="Plugin"/>.
/// </summary>
public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("Enemy List Cast Bars###EnemyListCastBarsConfigWindow")
    {
        this.plugin = plugin;
        this.configuration = plugin.Configuration;

        this.Size = new Vector2(460f, 480f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var changed = false;

        var enabled = this.configuration.Enabled;
        if (ImGui.Checkbox("Show cast bars on the enemy list", ref enabled))
        {
            this.configuration.Enabled = enabled;
            changed = true;
        }

        ImGui.TextDisabled("Positions are relative to the top-left corner of a single enemy list row.");

        if (ImGui.CollapsingHeader("Cast bar", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            changed |= this.DrawCastBarSection();
            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Cast text", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            changed |= this.DrawCastTextSection();
            ImGui.Unindent();
        }

        ImGui.Separator();

        if (ImGui.Button("Reset to defaults"))
        {
            this.configuration.ResetToDefaults();
            changed = true;
        }

        if (changed)
        {
            this.plugin.SaveConfiguration();
            this.plugin.Controller.RequestRebuild();
        }
    }

    private bool DrawCastBarSection()
    {
        var changed = false;

        var position = this.configuration.BarPosition;
        if (DragVector2("Position", ref position))
        {
            this.configuration.BarPosition = position;
            changed = true;
        }

        var size = this.configuration.BarSize;
        if (DragVector2("Size", ref size, 1f, 0f, 400f))
        {
            this.configuration.BarSize = size;
            changed = true;
        }

        var padding = this.configuration.BarFillPadding;
        if (ImGui.SliderFloat("Fill padding", ref padding, 0f, 10f, "%.0f"))
        {
            this.configuration.BarFillPadding = padding;
            changed = true;
        }

        changed |= ColorEdit("Interruptible", () => this.configuration.InterruptibleBarColor, value => this.configuration.InterruptibleBarColor = value);
        changed |= ColorEdit("Not interruptible", () => this.configuration.UninterruptibleBarColor, value => this.configuration.UninterruptibleBarColor = value);
        changed |= ColorEdit("Background", () => this.configuration.BarBackgroundColor, value => this.configuration.BarBackgroundColor = value);

        return changed;
    }

    private bool DrawCastTextSection()
    {
        var changed = false;

        var showName = this.configuration.ShowActionName;
        if (ImGui.Checkbox("Show cast name", ref showName))
        {
            this.configuration.ShowActionName = showName;
            changed = true;
        }

        var showTime = this.configuration.ShowRemainingTime;
        if (ImGui.Checkbox("Show remaining time", ref showTime))
        {
            this.configuration.ShowRemainingTime = showTime;
            changed = true;
        }

        var position = this.configuration.TextPosition;
        if (DragVector2("Position", ref position))
        {
            this.configuration.TextPosition = position;
            changed = true;
        }

        var size = this.configuration.TextSize;
        if (DragVector2("Size", ref size, 1f, 0f, 400f))
        {
            this.configuration.TextSize = size;
            changed = true;
        }

        var fontSize = (int)this.configuration.FontSize;
        if (ImGui.SliderInt("Font size", ref fontSize, 6, 24))
        {
            this.configuration.FontSize = (byte)fontSize;
            changed = true;
        }

        changed |= ColorEdit("Text", () => this.configuration.TextColor, value => this.configuration.TextColor = value);
        changed |= ColorEdit("Outline", () => this.configuration.TextOutlineColor, value => this.configuration.TextOutlineColor = value);

        return changed;
    }

    private static bool DragVector2(string label, ref Vector2 value, float speed = 1f, float minimum = -400f, float maximum = 400f)
        => ImGui.DragFloat2(label, ref value, speed, minimum, maximum, "%.0f");

    private static bool ColorEdit(string label, Func<Vector4> getter, Action<Vector4> setter)
    {
        var color = getter();
        if (!ImGui.ColorEdit4(label, ref color))
            return false;

        setter(color);
        return true;
    }
}
