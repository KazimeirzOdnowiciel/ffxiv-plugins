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

    /// <summary>
    /// True from the click that asked for a reset to defaults until the player answers the confirmation row.
    /// It lives here rather than in the view state because the state is rebuilt every frame and a
    /// confirmation has to survive that.
    /// </summary>
    private bool resetConfirmPending;

    private int cachedOwned = -1;

    private int cachedTotal = -1;

    private string progressText = Loc.T("config.loading");

    public ConfigWindow(Plugin plugin)
        : base(plugin, Loc.T("window.config") + "###MountbookConfig")
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

        // A confirmation belongs to the visit that opened it: a player who clicked Reset everything, closed the
        // window and came back should not find a data-destroying prompt still waiting for an answer.
        resetConfirmPending = false;
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
                Gw.Toast(Loc.T("toast.themeCopied"), ToastKind.Success);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not copy the theme JSON to the clipboard.");
                Gw.Toast(Loc.T("toast.themeCopyFailed"), ToastKind.Error);
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

            // The source filter is one of the four filters the main window's empty state clears, and this
            // Reset used to leave it behind: the settings button said "clears the search and filters" and
            // then persisted the very filter that emptied the list back into the config.
            config.SavedSource = string.Empty;

            config.Order = MountOrder.Name;
            config.SelectedMountId = 0;
            changed = true;
        }

        // The reset to defaults asks before it takes anything the player typed in. One rule, in Services, so
        // "it asked before clearing the wanted list" is a test and not a read of this method.
        if (state.ResetAllRequested)
        {
            if (ConfigReset.NeedsConfirmation(state.ResetImpact))
            {
                resetConfirmPending = true;
            }
            else
            {
                ResetToDefaults(config);
                changed = true;
            }
        }

        if (state.ResetCancelRequested)
        {
            resetConfirmPending = false;
        }

        if (state.ResetConfirmRequested)
        {
            resetConfirmPending = false;
            ResetToDefaults(config);
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
                ids.Count == 1
                    ? Loc.T("toast.wantedCopied.one")
                    : Loc.F("toast.wantedCopied.many", ids.Count),
                ToastKind.Success);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not copy the wanted list to the clipboard.");
            Gw.Toast(Loc.T("toast.wantedCopyFailed"), ToastKind.Error);
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
            Gw.Toast(Loc.T("toast.clipboardReadFailed"), ToastKind.Error);

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

    /// <summary>
    /// Puts every setting back to its default, including the wanted list, the grind records and the watches -
    /// the caller has already confirmed when any of those held something.
    /// </summary>
    /// <remarks>
    /// Mutates the live configuration rather than replacing it: the frame saves once at the end, and the main
    /// window may still be holding the set it copied this frame. The toast is the receipt, because a reset
    /// that worked looks exactly like a window that failed to draw anything.
    /// </remarks>
    /// <param name="config">The configuration the windows are drawing from.</param>
    private static void ResetToDefaults(Configuration config)
    {
        config.ResetToDefaults();
        Gw.Toast(Loc.T("toast.resetAll"), ToastKind.Success);
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
        Vector2 size = ImGui.GetWindowSize();
        float overflow = ImGui.GetScrollMaxY();

        // Held inside the screen. At 125-150% UI scale the body is ~1.4x taller, and growing the window to
        // its content put the About rows - and the buttons in them - below the desktop with no scrollbar to
        // find them by. A remembered size that no longer fits is shrunk to the same limit; otherwise the
        // rule is unchanged: grow by exactly the overflow, never shrink a window the player sized.
        var limit = MathF.Max(320f, ImGui.GetMainViewport().WorkSize.Y - 48f);
        var target = MathF.Min(overflow > 0f ? size.Y + overflow + 2f : size.Y, limit);

        if (MathF.Abs(target - size.Y) < 1f)
        {
            return;
        }

        ImGui.SetWindowSize(new Vector2(size.X, target));
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
                state.DatasetText,
                plugin.Configuration.LookName,
                plugin.Configuration.FollowGameTheme,
                state.DatasetCrossCheckText));
            Gw.Toast(Loc.T("toast.diagnosticsCopied"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not copy the diagnostics to the clipboard.");
            Gw.Toast(Loc.T("toast.diagnosticsCopyFailed"), ToastKind.Error);
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
            CatalogText = Loc.F(
                "config.catalog.summary",
                plugin.Catalog.Total.ToString(CultureInfo.InvariantCulture),
                plugin.Catalog.SourceCount.ToString(CultureInfo.InvariantCulture)),
            // The snapshot's own words: source, generated date, schema version, and whether it was
            // built offline. Empty only when the catalog could not be built at all.
            DatasetText = plugin.Catalog.DatasetText,
            DatasetSource = plugin.Catalog.DatasetSource,
            // Copied, never drawn: which seats/movement check the snapshot records, for a bug report.
            DatasetCrossCheckText = plugin.Catalog.DatasetCrossCheckText,
            // The assembly version, not the manifest: IPluginManifest has no Version member in API 15, and
            // the assembly version is what the packager stamps anyway.
            VersionText = Loc.F(
                "config.version.text",
                typeof(Plugin).Assembly.GetName().Version?.ToString() ?? Loc.T("config.version.unknown")),
            ThemeLabels = plugin.LookLabels,
            ThemeNames = lookNames,
            ThemeIndex = index,
            FollowGameTheme = config.FollowGameTheme,
            ThemesDirectory = plugin.ThemesDirectory,
            // What the reset confirmation would name, and whether it is open. Both are read here rather than
            // in the view: the view may not touch the config (T7).
            ResetImpact = new ConfigResetImpact(
                config.WantedMounts.Count,
                config.Grinds.Count,
                config.Watches.Count),
            ResetConfirmOpen = resetConfirmPending,
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
            Gw.Toast(Loc.T("toast.folderFailed"), ToastKind.Error);
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
            ? Loc.T("config.loading")
            : Loc.F("config.progress.text", owned, total);

        return progressText;
    }
}
