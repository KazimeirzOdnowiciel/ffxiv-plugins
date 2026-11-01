using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly WantedBoardWindow boardWindow;

    /// <summary>
    /// The Universalis reads behind the price watches, off the draw thread.
    /// </summary>
    /// <remarks>
    /// A client of its own rather than the main window's, for two reasons: a watch outlives the window (it
    /// polls and toasts with the book closed), and it must not take the answer to a price the player asked
    /// for away from the pane that is waiting for it - both readers pull from the same finished queue.
    /// </remarks>
    private readonly MarketPrices watchMarket;

    /// <summary>
    /// When the next watch poll is due, on <see cref="Environment.TickCount64"/>'s clock. Zero means "now",
    /// which is how a watch switched on this session is read without waiting out an interval.
    /// </summary>
    private long watchPollAt;

    /// <summary>
    /// How soon a poll is tried again when it could start nothing. The usual cause is no character yet, so
    /// there is no home world and therefore no data center to ask; a full interval of silence after a login
    /// is the wrong answer to that.
    /// </summary>
    private const long WatchRetryMs = 30_000;

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

    /// <summary>Set once the first draw's cost has been logged: the split of the one frame Dalamud
    /// reports as a hitch, kept out of the log on every later frame.</summary>
    private bool firstFrameCostLogged;

    public Plugin()
    {
        // Before anything else builds a label: the views read their strings once, at the first draw.
        Localisation.Apply(PluginInterface, Log);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();

        Host = new GlassworkHost(PluginInterface, Log);

        // One assignment restyles every widget, surface and pane. Other families: Neutral,
        // Liquid, Minimal, HighContrast, Retro, Classic, Clear Blue/Grey/Green/Pink, Aurora,
        // Catppuccin, Nord - each also exists in a Light variant.
        SeedImportedThemes();
        Host.Look = MountLook.Dense(Look.Eorzea());
        Host.FollowGameTheme = Configuration.FollowGameTheme;
        Theme.Bind(Host.Look);

        Catalog = new MountCatalog(Data, ClientState, Log);
        Textures = new MountTextureCache(TextureProvider, Log);
        Actions = new MountActions(Catalog, ClientState, Condition, ChatGui, GameGui, Log, DisplayName);
        watchMarket = new MarketPrices(Log, DataCenterName);

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
        boardWindow = new WantedBoardWindow(this, mainWindow);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(boardWindow);

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                // Enumerate every subcommand. A bare "opens the window" leaves the user
                // guessing at what else exists.
                HelpMessage = Loc.T("command.help"),
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

        // The watch client is the plugin's, not a window's, so it is disposed here: leaving it alive would
        // keep a socket pool (and a poll) running past the unload.
        watchMarket.Dispose();

        CommandManager.RemoveHandler(CommandName);

        // Last, and only after Draw is unsubscribed: a glass callback recorded after the
        // layer is gone faults the client.
        Host.Dispose();
    }

    /// <summary>
    /// Prefixes a chat line with the plugin's name. The tag itself is the plugin's identity and is not
    /// translated; the sentence after it is (<see cref="Loc"/>).
    /// </summary>
    /// <param name="text">The localised sentence.</param>
    private static string Tag(string text) => string.Concat("[", DisplayName, "] ", text);

    public void ToggleConfigUi() => configWindow.Toggle();

    public void ToggleMainUi() => mainWindow.Toggle();

    /// <summary>
    /// Opens the wanted board. Called from the header's wanted badge, which is its only door: the badge
    /// already counts the mounts the board lists, and the task that added the board asked for no new
    /// header button.
    /// </summary>
    public void OpenBoard() => boardWindow.IsOpen = true;

    /// <summary>
    /// The player's data center, e.g. <c>Light</c>, or empty before a character is loaded.
    /// </summary>
    /// <remarks>
    /// A market answer is asked for by data center and never by world: a world-only answer hides every
    /// cheaper listing one hop away, and cross-world travel makes those buyable. <c>ValueNullable</c> on both
    /// hops is deliberate - a <c>RowRef</c> throws before the character loads, and "no price today" must
    /// never arrive as an exception on a draw path.
    /// </remarks>
    internal static string DataCenterName()
    {
        try
        {
            return PlayerState.HomeWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText()
                ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Bumped whenever something the wanted board draws changes outside the catalog: a mount starred or
    /// unstarred, a tracker record written. The board rebuilds its rows when this moves, instead of
    /// re-allocating them every frame.
    /// </summary>
    internal int BoardRevision { get; set; }

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
        Host.Look = MountLook.Dense(chosen);
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
        // The first draw after a load is the expensive one: Dalamud logs it as a HITCH past 100 ms
        // and this project has never known which phase pays it. One Information line, once per load,
        // splits it - the glass layer opening its frame, the theme/setup, the windows' own draw, the
        // close - so a later session works on the phase that is actually slow.
        Stopwatch? firstFrame = firstFrameCostLogged ? null : Stopwatch.StartNew();
        var afterBegin = 0d;
        var afterSetup = 0d;
        var afterWindows = 0d;

        try
        {
            // BeginFrame inside the guard: a fault while the glass host opened its frame used to escape
            // the framework's draw pass entirely, and EndFrame never ran.
            Host.BeginFrame();
            afterBegin = firstFrame?.Elapsed.TotalMilliseconds ?? 0d;

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

            afterSetup = firstFrame?.Elapsed.TotalMilliseconds ?? 0d;

            // The price watch's clock, and the only thing that makes this plugin touch the network on its
            // own. It never waits on a socket: requests are started as tasks and read back on a later frame.
            TickWatchPoll();

            WindowSystem.Draw();
            afterWindows = firstFrame?.Elapsed.TotalMilliseconds ?? 0d;

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

            if (firstFrame is not null)
            {
                firstFrameCostLogged = true;
                Log.Information(
                    "First frame cost: open {Open:0.0} ms, setup {Setup:0.0} ms, windows {Windows:0.0} ms, close {Close:0.0} ms, total {Total:0.0} ms.",
                    afterBegin,
                    afterSetup - afterBegin,
                    afterWindows - afterSetup,
                    firstFrame.Elapsed.TotalMilliseconds - afterWindows,
                    firstFrame.Elapsed.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// The price watch's clock: asks the market for every watched mount when the jittered interval is up, and
    /// puts the answers that have landed back onto the records.
    /// </summary>
    /// <remarks>
    /// This runs from the draw pass because that is this plugin's only per-frame hook - but no HTTP happens
    /// on it (AGENTS N2). <see cref="MarketPrices.Request"/> only starts a task, so the socket work sits on a
    /// worker thread and the answer is read back on a later frame; the cost here is a dictionary walk and one
    /// clock read. Nothing at all is asked for while no watch is live, which is the acceptance rule expressed
    /// as code: the feature cannot reach the network until the player switches a watch on.
    /// </remarks>
    private void TickWatchPoll()
    {
        DrainWatchAnswers();

        if (MountWatch.Count(Configuration.Watches) == 0)
        {
            // Back to zero, so a watch switched on later is read at once instead of waiting out an interval
            // that was armed for a set which no longer existed.
            watchPollAt = 0;
            return;
        }

        long now = Environment.TickCount64;

        if (watchPollAt != 0 && now < watchPollAt)
        {
            return;
        }

        watchPollAt = AskForWatchedPrices() > 0
            ? MountWatch.NextDue(now, Random.Shared.NextDouble())
            : now + WatchRetryMs;
    }

    /// <summary>
    /// Starts a request for every watched mount's price and reports how many were actually started.
    /// </summary>
    /// <remarks>
    /// Forced past the quote cache: the poll interval is the schedule the player chose, so a quote the pane
    /// already holds is exactly the answer a poll exists to replace. The count tells the caller whether to
    /// arm an interval or retry shortly - no character loaded means no data center, so no request.
    /// </remarks>
    private int AskForWatchedPrices()
    {
        if (!Catalog.EnsureBuilt())
        {
            return 0;
        }

        var started = 0;

        foreach (KeyValuePair<uint, MountWatchRecord> pair in Configuration.Watches)
        {
            if (MountWatch.IsWatching(pair.Value)
                && Catalog.TryGetItemId(pair.Key, out uint itemId)
                && watchMarket.Request(itemId, force: true))
            {
                started++;
            }
        }

        return started;
    }

    /// <summary>
    /// Applies every watch answer that has landed: the record takes what was seen and when, and a crossing
    /// that has not been announced yet raises its toast and stays latched until the price goes back up.
    /// </summary>
    private void DrainWatchAnswers()
    {
        var changed = false;

        while (watchMarket.TryTakeFinished(out uint itemId, out MarketQuote? quote, out bool failed))
        {
            // A failure is not an answer: MarketPrices logs the first one (N4) and this leaves CheckedAt
            // alone, so the pane keeps saying when the watch was last really read and the next poll retries.
            if (failed)
            {
                continue;
            }

            foreach (KeyValuePair<uint, MountWatchRecord> pair in Configuration.Watches)
            {
                if (!MountWatch.IsWatching(pair.Value)
                    || !Catalog.TryGetItemId(pair.Key, out uint watchedItemId)
                    || watchedItemId != itemId)
                {
                    continue;
                }

                int price = quote?.MinPrice ?? 0;
                string name = WatchName(pair.Key);

                if (MountWatch.ShouldAlert(pair.Value, price))
                {
                    Gw.Toast(
                        MountWatch.AlertText(name, price, pair.Value.Threshold),
                        ToastKind.Success);
                }

                MountWatch.Observe(pair.Value, price, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                changed = true;

                // The same formatter the pane draws, so the log and the screen cannot report different numbers.
                Log.Information(
                    "Watch: {Mount} {Quote}",
                    name,
                    MountMarket.Describe(quote, DataCenterName()));
            }
        }

        if (changed)
        {
            SaveConfigSafe(Configuration);
        }
    }

    /// <summary>
    /// A watched mount's display name for the toast, falling back to its id when the catalog does not carry
    /// it (a hand-edited config naming something that is not a mount).
    /// </summary>
    /// <param name="mountId">The mount id the record is keyed by.</param>
    private string WatchName(uint mountId)
    {
        foreach (MountShelfItem item in Catalog.Items)
        {
            if (item.Id == mountId)
            {
                return item.Name;
            }
        }

        return string.Concat("#", mountId.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
                ChatGui.PrintError(Tag(Loc.F("command.unknown", args.Trim(), CommandName)));
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
        ChatGui.Print(Tag(Loc.F("command.missing", missing, Catalog.Total)));
    }

    /// <summary>Prints the size of the wanted list, and says so plainly when it is empty.</summary>
    private void PrintWanted()
    {
        if (!EnsureCatalogForCommand(out _))
        {
            return;
        }

        var wanted = Configuration.WantedMounts.Count;

        ChatGui.Print(Tag(wanted == 0
            ? Loc.T("command.wanted.empty")
            : Loc.F("command.wanted", wanted)));
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
            ChatGui.Print(Tag(Loc.T("command.recent.empty")));

            return;
        }

        ChatGui.Print(Tag(Loc.F("command.recent.summary", MountRecentDigest.Summary(lines.Count))));

        foreach (RecentCollectedLine line in lines)
        {
            ChatGui.Print(Loc.F("command.recent.line", line.Name, line.When));
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
            ChatGui.Print(Tag(Loc.T("command.login")));
            return false;
        }

        if (Catalog.EnsureBuilt())
        {
            return true;
        }

        ChatGui.PrintError(Tag(Loc.F("command.catalogNotReady", CommandName)));

        return false;
    }

    private void PrintStatus()
    {
        if (!ClientState.IsLoggedIn)
        {
            ChatGui.Print(Tag(Loc.T("command.status.login")));
            return;
        }

        if (!Catalog.EnsureBuilt())
        {
            ChatGui.PrintError(Tag(Loc.F("command.catalogNotReady", CommandName)));
            return;
        }

        var percent = MountProgress.Percent(Catalog.Owned, Catalog.Total);
        ChatGui.Print(Tag(Loc.F("command.status", Catalog.Owned, Catalog.Total, percent)));
    }
}
