using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Glasswork.Backend;
using Mountbook.Services;
using Mountbook.UI;
using Mountbook.Views;

namespace Mountbook.Windows;

public sealed class ConfigWindow : GlassWindow, IDisposable
{
    protected override float Radius => 20f;

    private readonly Plugin plugin;

    private readonly ConfigView view = new();

    /// <summary>Set once a draw has failed, so the failure is logged on the transition (N4).</summary>
    private bool loggedDrawFailure;

    /// <summary>Set on open: the next draw grows the window if the body it drew does not fit.</summary>
    private bool fitOnNextDraw;

    private int cachedOwned = -1;

    private int cachedTotal = -1;

    private string progressText = "Open the main window once to build the mount catalog.";

    public ConfigWindow(Plugin plugin)
        : base(plugin, "Mountbook Settings###MountbookConfig")
    {
        // 900 rather than the original 840: the wanted-list row pushed the About section's diagnostics
        // button below the fold, and a player who opens settings to copy diagnostics should not have to
        // scroll for it. FirstUseEver, so an existing window keeps the size the player gave it.
        Size = new Vector2(560f, 900f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Expands the window when it appears. ImGui remembers a collapsed window across sessions, and a
    /// collapsed settings window is a 22 px title bar: opening it from the header looked like nothing
    /// happened. Only <see cref="ImGuiCond.Appearing"/> is forced, so the player can still collapse it.
    /// </summary>
    public override void PreDraw()
    {
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Appearing);
        base.PreDraw();
    }

    /// <summary>
    /// Opening the window explicitly always expands it: ImGui's remembered collapse state belongs to a
    /// window the player left alone, not to one they just asked for from the header.
    /// </summary>
    public override void OnOpen()
    {
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        fitOnNextDraw = true;
        base.OnOpen();
    }

    public override void Draw()
    {
        try
        {
            DrawBody();

            if (loggedDrawFailure)
            {
                Plugin.Log.Debug("ConfigWindow.Draw recovered.");
                loggedDrawFailure = false;
            }
        }
        catch (Exception ex)
        {
            // Same reason as MainWindow: a per-frame catch, so log on the transition only (N4).
            if (!loggedDrawFailure)
            {
                Plugin.Log.Error(ex, "ConfigWindow.Draw failed.");
                loggedDrawFailure = true;
            }
        }
    }

    /// <summary>
    /// Gathers the state the settings body draws, draws it, and applies the one-frame requests.
    /// Configuration mutates in place and saves once at the end of the frame, so a click does not write
    /// the config file more than once.
    /// </summary>
    private void DrawBody()
    {
        DrawGlass();

        var config = plugin.Configuration;
        var state = Gather(config);

        // ImGui's remembered size outlives every row a later build adds, and a window shorter than its
        // body hides the About rows below a scroll nobody scrolls; the settings window was 750 px tall
        // with 823 px of content when the wanted-list row landed. The fit can only be measured after the
        // body has drawn, so it runs one frame behind the open.
        var fit = fitOnNextDraw;

        view.Draw(state);

        if (fit)
        {
            fitOnNextDraw = false;
            FitToContent();
        }

        var changed = false;

        if (state.ThemePickedIndex is { } picked && picked >= 0 && picked < state.ThemeNames.Length)
        {
            plugin.SetLook(state.ThemeNames[picked]);
        }

        if (state.FollowGameThemeSet is { } follow)
        {
            plugin.SetFollowGameTheme(follow);
        }

        if (state.CopyDiagnosticsRequested)
        {
            CopyDiagnostics(state);
        }

        if (state.CopyThemeRequested)
        {
            try
            {
                ImGui.SetClipboardText(plugin.Host.Look.ToJson());
                Gw.Toast("Theme JSON copied"u8, ToastKind.Success);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not copy the theme JSON to the clipboard.");
                Gw.Toast("Could not copy the theme."u8, ToastKind.Error);
            }
        }

        if (state.OpenThemesFolderRequested)
        {
            OpenThemesFolder();
        }

        if (state.ResetBrowseRequested)
        {
            config.SearchText = string.Empty;
            config.Ownership = MountOwnership.All;
            config.Expansion = MountExpansion.All;
            config.Order = MountOrder.Name;
            config.SelectedMountId = 0;
            changed = true;
        }

        if (state.CopyWantedRequested)
        {
            CopyWantedList(config);
        }

        if (state.ImportWantedRequested)
        {
            changed |= ImportWantedList(config);
        }

        if (changed)
        {
            Plugin.SaveConfigSafe(config);
        }
    }

    /// <summary>
    /// Puts the wanted list on the clipboard as the versioned JSON <see cref="MountWantedList"/> writes,
    /// with each mount's name beside its id. Failure is logged and toasted, never left to the draw guard.
    /// </summary>
    private void CopyWantedList(Configuration config)
    {
        try
        {
            Dictionary<uint, string> names = Names();
            var ids = new List<uint>(config.WantedMounts.Count);

            foreach (uint id in config.WantedMounts)
            {
                ids.Add(id);
            }

            ImGui.SetClipboardText(MountWantedList.Write(
                ids,
                id => names.TryGetValue(id, out string? name) ? name : null));

            Gw.Toast(
                ids.Count == 1 ? "Copied 1 wanted mount" : $"{ids.Count} wanted mounts copied",
                ToastKind.Success);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not copy the wanted list to the clipboard.");
            Gw.Toast("Could not copy the wanted list."u8, ToastKind.Error);
        }
    }

    /// <summary>
    /// Reads a wanted list off the clipboard and merges it into the player's own: an import never
    /// removes a mount from the list, so a friend's share cannot silently unfollow something.
    /// </summary>
    /// <returns>True when the wanted list changed, so the caller saves once at the end of the frame.</returns>
    private bool ImportWantedList(Configuration config)
    {
        string? json;

        try
        {
            json = ImGui.GetClipboardText();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read the clipboard for a wanted list import.");
            Gw.Toast("Could not read the clipboard."u8, ToastKind.Error);

            return false;
        }

        var known = new HashSet<uint>(plugin.Catalog.Items.Count);

        foreach (MountShelfItem item in plugin.Catalog.Items)
        {
            known.Add(item.Id);
        }

        if (!MountWantedList.TryParse(json, known, out MountWantedImport import, out string error))
        {
            // The message is the whole answer here: the file is the player's, so say what is wrong
            // with it rather than "import failed".
            Gw.Toast(error, ToastKind.Error);

            return false;
        }

        var added = 0;
        var already = 0;

        foreach (uint id in import.Wanted)
        {
            if (config.WantedMounts.Add(id))
            {
                added++;
            }
            else
            {
                already++;
            }
        }

        Plugin.Log.Information(
            "Wanted list import: {Added} added, {Already} already wanted, {Unknown} unknown, {Invalid} without an id.",
            added,
            already,
            import.Unknown,
            import.Invalid);

        Gw.Toast(
            MountWantedList.ImportSummary(added, already, import.Unknown, import.Invalid),
            added > 0 ? ToastKind.Success : ToastKind.Info);

        return added > 0;
    }

    /// <summary>The catalog's id → name, built once per click rather than cached: an import or an
    /// export is a click, and a cache would need invalidating when the catalog is rebuilt.</summary>
    private Dictionary<uint, string> Names()
    {
        IReadOnlyList<MountShelfItem> items = plugin.Catalog.Items;
        var names = new Dictionary<uint, string>(items.Count);

        foreach (MountShelfItem item in items)
        {
            names[item.Id] = item.Name;
        }

        return names;
    }

    /// <summary>
    /// Grows the window by exactly what the body overflowed, once per open. Never shrinks: a player who
    /// made the window smaller on purpose keeps that size until they open it again.
    /// </summary>
    /// <remarks>
    /// The deficit comes from ImGui's own scroll range rather than a cursor comparison: <c>GetScrollMaxY</c>
    /// is content height minus the region's height, which is the number of pixels the window is short by.
    /// A hand-measured version of this grew a 756 px window to 1010 for 823 px of content.
    /// </remarks>
    private static void FitToContent()
    {
        var overflow = ImGui.GetScrollMaxY();

        if (overflow <= 0f)
        {
            return;
        }

        ImGui.SetWindowSize(ImGui.GetWindowSize() + new Vector2(0f, overflow + 2f));
    }

    /// <summary>
    /// Copies a short diagnostics block: what a bug report needs. Failure is logged and toasted, never left
    /// to the draw guard (N4).
    /// </summary>
    private void CopyDiagnostics(ConfigViewState state)
    {
        try
        {
            ImGui.SetClipboardText(MountDiagnostics.Build(
                state.VersionText,
                state.ProgressText,
                state.CatalogText,
                plugin.Configuration.LookName,
                plugin.Configuration.FollowGameTheme));
            Gw.Toast("Diagnostics copied"u8, ToastKind.Success);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not copy the diagnostics to the clipboard.");
            Gw.Toast("Could not copy the diagnostics."u8, ToastKind.Error);
        }
    }

    /// <summary>Fills the view state from the plugin, including the theme picker's labels.</summary>
    private ConfigViewState Gather(Configuration config)
    {
        // The main window is not the only way in: this one is opened from the installer's config
        // button and by /mountbook config, and the catalog is built at load - before login - so its
        // owned count is zero. Without this line the first-run settings window says "0 of N mounts
        // collected" and its diagnostics block, which users paste into bug reports, says the same.
        plugin.Catalog.RefreshIfDue(Environment.TickCount64, false);

        plugin.RefreshLookCatalog();
        var lookNames = plugin.LookNames;

        var index = Array.IndexOf(lookNames, config.LookName);

        if (index < 0)
        {
            index = Array.FindIndex(lookNames, name => string.Equals(name, "eorzea", StringComparison.OrdinalIgnoreCase));
        }

        if (index < 0)
        {
            index = 0;
        }

        return new ConfigViewState
        {
            ProgressText = ProgressText(plugin.Catalog.Owned, plugin.Catalog.Total),
            ProgressFraction = plugin.Catalog.Total <= 0
                ? 0f
                : (float)plugin.Catalog.Owned / plugin.Catalog.Total,
            CatalogText = string.Concat(
                plugin.Catalog.Total.ToString(CultureInfo.InvariantCulture),
                " mounts, ",
                plugin.Catalog.SourceCount.ToString(CultureInfo.InvariantCulture),
                " with obtain data"),
            // The assembly version, not the manifest: IPluginManifest has no Version member in API 15, and
            // the assembly version is what the packager stamps anyway.
            VersionText = string.Concat("v", typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown"),
            ThemeLabels = plugin.LookLabels,
            ThemeNames = lookNames,
            ThemeIndex = index,
            FollowGameTheme = config.FollowGameTheme,
            ThemesDirectory = plugin.ThemesDirectory,
        };
    }

    /// <summary>
    /// Opens the folder the host reads theme JSONs from, creating it first so a fresh install has
    /// one to open. A failure is logged and toasted, never left to the draw guard (N4).
    /// </summary>
    private void OpenThemesFolder()
    {
        var folder = plugin.ThemesDirectory;

        try
        {
            Directory.CreateDirectory(folder);

            // UseShellExecute hands the folder to the desktop; without it Process.Start would try
            // to execute a directory. The returned handle is not needed after the spawn.
            using Process? process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not open the themes folder '{Folder}'.", folder);
            Gw.Toast("Could not open the folder."u8, ToastKind.Error);
        }
    }

    /// <summary>
    /// The progress line, rebuilt only when the numbers move: the format string runs in the draw
    /// path and should not allocate every frame.
    /// </summary>
    private string ProgressText(int owned, int total)
    {
        if (owned == cachedOwned && total == cachedTotal)
        {
            return progressText;
        }

        cachedOwned = owned;
        cachedTotal = total;
        progressText = total <= 0
            ? "Open the main window once to build the mount catalog."
            : $"{owned} of {total} mounts collected.";

        return progressText;
    }
}
