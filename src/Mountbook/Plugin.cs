using System;
using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glasswork;
using Glasswork.Backend;
using Glasswork.Dalamud;
using Mountbook.Game;
using Mountbook.Services;
using Mountbook.UI;
using Mountbook.Windows;

namespace Mountbook;

/// <summary>
/// Entry point. Services are injected by Dalamud into the <see cref="PluginServiceAttribute"/>
/// properties before the constructor runs, which is why they are never null here.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public static string DisplayName => "Mountbook";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    [PluginService] internal static IDataManager Data { get; private set; } = null!;

    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;

    /// <summary>The player's bags, read once per mount selection to count a token price.</summary>
    [PluginService] internal static IGameInventory Inventory { get; private set; } = null!;

    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    /// <summary>
    /// The InternalName is derived from AssemblyName and must never change after release:
    /// it is the config directory, the log tag and the plugin identity.
    /// </summary>
    private const string CommandName = "/mountbook";

    public Configuration Configuration { get; }

    /// <summary>One Glasswork host per plugin: it owns the glass layer and the theme.</summary>
    public GlassworkHost Host { get; }

    public readonly WindowSystem WindowSystem = new("Mountbook");

    /// <summary>The mount catalog, built from the Lumina sheets and the embedded obtain data.</summary>
    internal MountCatalog Catalog { get; }

    /// <summary>The icon and artwork handles the view draws, cached by icon id.</summary>
    internal MountTextureCache Textures { get; }

    /// <summary>The two guarded actions the view can ask for.</summary>
    internal MountActions Actions { get; }

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    /// <summary>Set once the draw pass has failed, so it is logged on the transition and not per frame.</summary>
    private bool loggedDrawFailure;

    /// <summary>Set once the saved Look has been applied on the first frame.</summary>
    private bool lookApplied;

    /// <summary>
    /// Bumped every time the Look the windows draw with changes. The view caches text measurements,
    /// which depend on the Look's type face, and keys them on this (see MountViewState.MeasurementGeneration).
    /// </summary>
    internal int LookRevision { get; private set; }

    /// <summary>Set once closing the host frame has failed, so it is logged on the transition only.</summary>
    private bool loggedEndFrameFailure;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();

        Host = new GlassworkHost(PluginInterface, Log);

        // One assignment restyles every widget, surface and pane. Other families: Neutral,
        // Liquid, Minimal, HighContrast, Retro, Classic, Clear Blue/Grey/Green/Pink, Aurora,
        // Catppuccin, Nord - each also exists in a Light variant.
        SeedImportedThemes();
        Host.Look = Look.Eorzea();
        Host.FollowGameTheme = Configuration.FollowGameTheme;
        Theme.Bind(Host.Look);

        Catalog = new MountCatalog(Data, ClientState, Log);
        Textures = new MountTextureCache(TextureProvider, Log);
        Actions = new MountActions(Catalog, ClientState, Condition, ChatGui, GameGui, Log, DisplayName);

        // Build the mount list here rather than in the first draw: reading ~700 sheet rows plus the
        // embedded dataset is a load-time cost, and paying it inside a draw pass stalls the client.
        // A failure here is not fatal - the catalog is rebuilt on first use.
        try
        {
            if (Catalog.EnsureBuilt())
            {
                // One line of context for the log: it makes a user report ("it says I own 12") checkable
                // against what the plugin actually read, without a debug build.
                Log.Information(
                    "Catalog ready: {Total} mounts, {Owned} collected, {Dataset} with obtain data.",
                    Catalog.Total,
                    Catalog.Owned,
                    Catalog.SourceCount);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Building the mount catalog at load failed; it will retry on first use.");
        }

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                // Enumerate every subcommand. A bare "opens the window" leaves the user
                // guessing at what else exists.
                HelpMessage = "Toggles the mount collection window.\n"
                              + $"{CommandName} config - Opens the settings window.\n"
                              + $"{CommandName} status - Prints how many mounts you have collected.\n"
                              + $"{CommandName} missing - Prints how many mounts you are still missing.\n"
                              + $"{CommandName} wanted - Prints how many mounts are on your wanted list.\n"
                              + $"{CommandName} recent - Lists the mounts you collected in the last two weeks.",
            });

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("{Plugin} loaded.", DisplayName);
    }

    public void Dispose()
    {
        // Unhook everything: a leaked handler survives an in-game plugin reload and
        // then draws into a disposed window system.
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        // RemoveAllWindows only drops the references. The windows own the view, its state and the live
        // texture-handle maps, so they are disposed here, before the host they draw through (N11).
        mainWindow.Dispose();
        configWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        // Last, and only after Draw is unsubscribed: a glass callback recorded after the
        // layer is gone faults the client.
        Host.Dispose();
    }

    public void ToggleConfigUi() => configWindow.Toggle();

    public void ToggleMainUi() => mainWindow.Toggle();

    /// <summary>The folder the host reads theme JSONs from, for the settings window.</summary>
    public string ThemesDirectory => Host.Themes.Directory;

    /// <summary>
    /// Imports Slot Yield's two palettes as real theme files on first run: "Graphite" is that
    /// plugin's dark brass look (Glasswork's Eorzea) and "Parchment" its light variant. They are
    /// written with <see cref="LookLibrary.Save"/> so the picker lists them beside the built-ins and
    /// the user can edit or delete them like any other theme.
    /// </summary>
    /// <remarks>
    /// Only runs when the themes folder holds no theme at all: seeding over someone's own files
    /// would be a data loss, and an existing install must keep exactly what it had.
    /// </remarks>
    private void SeedImportedThemes()
    {
        try
        {
            // The host loads its library on BeginFrame; at construction it is still empty, so a
            // count check without a refresh would re-seed (and overwrite) the files every load.
            Host.Themes.Refresh();

            if (Host.Themes.Count > 0)
            {
                return;
            }

            // The library identifies a theme by the Look's own Name field, not by the file name:
            // saving without renaming produced files that still called themselves "Eorzea".
            if (Look.TryFromName("eorzea", out Look? graphite) && graphite is not null)
            {
                Look themed = graphite.Clone();
                themed.Name = "Graphite";
                Host.Themes.Save(themed, "Graphite");
            }

            if (Look.TryFromName("eorzea-light", out Look? parchment) && parchment is not null)
            {
                Look themed = parchment.Clone();
                themed.Name = "Parchment";
                Host.Themes.Save(themed, "Parchment");
            }

            Log.Information(
                "Seeded the imported themes (Graphite, Parchment) into {Directory}.",
                Host.Themes.Directory);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not seed the imported themes; the built-in Looks are still available.");
        }
    }

    /// <summary>Switches the Look by name and rebinds every theme token from it (N9).</summary>
    public void SetLook(string name)
    {
        Look? chosen = null;

        if (Look.TryFromName(name, out Look? builtIn) && builtIn is not null)
        {
            chosen = builtIn;
        }
        else
        {
            // A malformed themes directory must not take the theme picker down with it: this runs on a
            // click, and an escaping throw left the picker looking dead with no toast.
            try
            {
                Host.Themes.Refresh();
                if (Host.Themes.TryGet(name, out Look? theme) && theme is not null)
                {
                    chosen = theme.Clone();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not read the theme '{Theme}'.", name);
                Gw.Toast("That theme could not be loaded. See the log."u8, ToastKind.Error);
                return;
            }
        }

        if (chosen is null)
        {
            return;
        }

        var changed = !string.Equals(Configuration.LookName, name, StringComparison.Ordinal);
        Configuration.LookName = name;
        Host.Look = chosen;
        Theme.Bind(Host.Look);
        LookRevision++;

        if (changed)
        {
            SaveConfigSafe(Configuration);
        }
    }

    /// <summary>Follow the player's game window theme; the Look is then the host's, not the picker's.</summary>
    public void SetFollowGameTheme(bool follow)
    {
        Configuration.FollowGameTheme = follow;
        Host.FollowGameTheme = follow;
        Theme.Bind(Host.Look);
        LookRevision++;
        SaveConfigSafe(Configuration);
    }

    /// <summary>Built-in Look names first, then the themes in the config folder's themes directory.</summary>
    public string[] LookNames { get; private set; } = [];

    /// <summary>Display labels for <see cref="LookNames"/>, in the same order.</summary>
    public string[] LookLabels { get; private set; } = [];

    private int lookCatalogSize = -1;

    /// <summary>
    /// Rebuilds the picker's list when a theme file was added or removed. The host refreshes its
    /// library on BeginFrame, so comparing the count is enough to notice a change.
    /// </summary>
    public void RefreshLookCatalog()
    {
        var themes = Host.Themes;
        var size = Look.BuiltInNames.Length + themes.Count;
        if (size == lookCatalogSize && LookNames.Length == size)
        {
            return;
        }

        lookCatalogSize = size;
        var names = new string[size];
        var labels = new string[size];
        var at = 0;

        foreach (var name in Look.BuiltInNames)
        {
            names[at] = name;
            labels[at] = FormatLookLabel(name);
            at++;
        }

        for (var index = 0; index < themes.Count; index++)
        {
            names[at] = themes[index].Name;
            labels[at] = themes[index].Name;
            at++;
        }

        LookNames = names;
        LookLabels = labels;
    }

    /// <summary>"clear-blue-light" reads as "Clear Blue - light".</summary>
    private static string FormatLookLabel(string name)
    {
        var light = name.EndsWith("-light", StringComparison.OrdinalIgnoreCase);
        var core = light ? name[..^6] : name;
        var words = core.Split('-', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            words[index] = word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
        }

        var label = string.Join(' ', words);
        return light ? label + " - light" : label;
    }

    /// <summary>
    /// BeginFrame first, EndFrame last, around every window. BeginFrame releases the previous
    /// frame's captures and picks up the device, the UI scale and the reduced-motion setting.
    /// </summary>
    /// <remarks>
    /// EndFrame is in a finally on purpose: if a window throws, the glass frame this opened must
    /// still be closed (N11). Logging happens on the transition only - this path runs sixty times a
    /// second, so a per-frame Error or Debug line becomes log spam.
    /// </remarks>
    private void OnDraw()
    {
        // Expected, harmless log noise on the first frame that draws the Mono text role (normally the
        // first frame after a load): Glasswork builds a Mono face and asks Dalamud's
        // FontAtlasBuildToolkitUtilities.FindConfigPtr for that face's config. In Dalamud 15.0.3.9
        // that helper returns a pointer to one of its own stack locals (IL: ldloca.s + Unsafe.AsPointer),
        // so Glasswork reads a dead ImFontConfig; its catch only covers malformed-font exceptions, so a
        // NullReferenceException escapes and Dalamud logs "An error has occurred while during
        // FontAtlasBuildStepDelegate PreBuild call" plus Glasswork's "A Glasswork font failed to load".
        // Glasswork then marks that slot failed and Mono falls back to the current ImGui font for the
        // session. Not a Mountbook lifetime bug, and not fixable here without patching Dalamud - do not
        // add a workaround around it.
        try
        {
            // BeginFrame inside the guard: a fault while the glass host opened its frame used to escape
            // the framework's draw pass entirely, and EndFrame never ran.
            Host.BeginFrame();

            // The theme library is only populated once the host has run a frame, so a theme file saved
            // by name cannot be resolved from the constructor. The saved Look is applied on the first
            // draw, when the library is real.
            if (!lookApplied)
            {
                // Set after the call: flagged first, one throw from a malformed theme file meant the
                // player's saved Look was never applied again for the rest of the session, while the
                // config still named it.
                SetLook(Configuration.LookName);
                lookApplied = true;
            }

            // While the host follows the game theme it restyles the Look itself; keep the facade in step
            // so the hand-painted parts use the same tokens as the widgets (never per frame otherwise).
            if (Host.FollowGameTheme || !ReferenceEquals(Theme.Look, Host.Look))
            {
                Theme.Bind(Host.Look);
            }

            WindowSystem.Draw();

            if (loggedDrawFailure)
            {
                Log.Debug("The UI draw pass recovered.");
                loggedDrawFailure = false;
            }
        }
        catch (Exception ex)
        {
            if (!loggedDrawFailure)
            {
                Log.Error(ex, "The UI draw pass failed.");
                loggedDrawFailure = true;
            }
        }
        finally
        {
            try
            {
                Host.EndFrame();
            }
            catch (Exception ex)
            {
                // Never let closing the frame take the plugin down; log the transition only (N4).
                if (!loggedEndFrameFailure)
                {
                    loggedEndFrameFailure = true;
                    Log.Error(ex, "Host.EndFrame failed; the next frame will try again.");
                }
            }
        }
    }

    /// <summary>
    /// Saves the configuration without letting a failed write escape.
    /// </summary>
    /// <remarks>
    /// <c>Window.OnOpen</c> and <c>OnClose</c> are invoked from <c>WindowSystem.Draw</c>, outside the
    /// guard around <c>Window.Draw</c>, so an exception during a save there aborts the whole draw
    /// pass and skips <c>Host.EndFrame</c>. Every window write therefore goes through this method.
    /// </remarks>
    internal static void SaveConfigSafe(Configuration configuration)
    {
        try
        {
            configuration.Save();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Saving the configuration failed.");
        }
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                mainWindow.Toggle();
                break;

            case "config":
                configWindow.Toggle();
                break;

            case "status":
                PrintStatus();
                break;

            case "missing":
                PrintMissing();
                break;

            case "wanted":
                PrintWanted();
                break;

            case "recent":
                PrintRecent();
                break;

            default:
                ChatGui.PrintError(
                    $"[{DisplayName}] Unknown subcommand '{args.Trim()}'. Try {CommandName}, {CommandName} config, {CommandName} status, {CommandName} missing, {CommandName} wanted or {CommandName} recent.");
                break;
        }
    }

    /// <summary>
    /// One chat line for the <c>status</c> subcommand. Building the catalog here is the same lazy
    /// build the window does, so the command works before the window was ever opened.
    /// </summary>
    /// <summary>Prints how many mounts are left to collect.</summary>
    private void PrintMissing()
    {
        if (!EnsureCatalogForCommand(out _))
        {
            return;
        }

        var missing = Catalog.Total - Catalog.Owned;
        ChatGui.Print($"[{DisplayName}] {missing} of {Catalog.Total} mounts still missing.");
    }

    /// <summary>Prints the size of the wanted list, and says so plainly when it is empty.</summary>
    private void PrintWanted()
    {
        if (!EnsureCatalogForCommand(out _))
        {
            return;
        }

        var wanted = Configuration.WantedMounts.Count;

        ChatGui.Print(wanted == 0
            ? $"[{DisplayName}] Your wanted list is empty. Click a card's marker (or the Want button) to add one."
            : $"[{DisplayName}] {wanted} mounts on your wanted list.");
    }

    /// <summary>
    /// Prints the mounts collected inside the fourteen-day window, newest first. The data is already
    /// persisted for the "New" markers; this is the same knowledge in the form of a question a player
    /// actually asks ("what did I get last week").
    /// </summary>
    private void PrintRecent()
    {
        if (!EnsureCatalogForCommand(out _))
        {
            return;
        }

        // The record is normally advanced by the window's draw loop; do it here as well so the command
        // answers with what is true now rather than with whatever the last draw saw.
        mainWindow.TrackRecentlyCollectedNow();

        IReadOnlyList<RecentCollectedLine> lines = MountRecentDigest.Build(
            Configuration.RecentCollected,
            Catalog.Items,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (lines.Count == 0)
        {
            ChatGui.Print(
                $"[{DisplayName}] Nothing collected in the last two weeks. A mount you earn is listed here for fourteen days.");

            return;
        }

        ChatGui.Print($"[{DisplayName}] {MountRecentDigest.Summary(lines.Count)}.");

        foreach (RecentCollectedLine line in lines)
        {
            ChatGui.Print($"  {line.Name} - {line.When}");
        }
    }

    /// <summary>
    /// The guards PrintStatus needs before reading the catalog: logged in, and built. Returns false when the
    /// command already printed why it cannot answer.
    /// </summary>
    private bool EnsureCatalogForCommand(out int unused)
    {
        unused = 0;

        if (!ClientState.IsLoggedIn)
        {
            ChatGui.Print($"[{DisplayName}] Log in first.");
            return false;
        }

        if (Catalog.EnsureBuilt())
        {
            return true;
        }

        ChatGui.PrintError(
            $"[{DisplayName}] The mount catalog is not ready yet. Open {CommandName} once the game has finished loading.");

        return false;
    }

    private void PrintStatus()
    {
        if (!ClientState.IsLoggedIn)
        {
            ChatGui.Print($"[{DisplayName}] Log in to see how many mounts you have collected.");
            return;
        }

        if (!Catalog.EnsureBuilt())
        {
            ChatGui.PrintError(
                $"[{DisplayName}] The mount catalog is not ready yet. Open {CommandName} once the game has finished loading.");
            return;
        }

        var percent = MountProgress.Percent(Catalog.Owned, Catalog.Total);
        ChatGui.Print($"[{DisplayName}] {Catalog.Owned} of {Catalog.Total} mounts collected ({percent}%).");
    }
}
