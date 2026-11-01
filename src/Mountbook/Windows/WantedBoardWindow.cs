using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Mountbook.Services;
using Mountbook.UI;
using Mountbook.Views;

namespace Mountbook.Windows;

/// <summary>
/// The wanted board: a second window listing only the mounts the player starred and does not own, each
/// with its first source line and how far the grind has got.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the shelf answers "which mounts do I want" and a token grind asks a different
/// question - "how much is left, and what do I do next". The board is that list, sorted by the patch the
/// mounts arrived in or by what gil can shortcut.
/// </para>
/// <para>
/// Opened from the header's wanted badge and from nothing else: no new header button (the badge already
/// counts exactly these mounts), and the state is persisted like the main window's so the board comes back
/// with the session. Prices are read from the batch the main window already holds and are never fetched
/// here (AGENTS N2): a price shows when it is known, and an unpriced mount sorts last.
/// </para>
/// </remarks>
public sealed class WantedBoardWindow : GlassWindow
{
    protected override float Radius => 20f;

    private readonly Plugin plugin;

    private readonly MainWindow main;

    private readonly WantedBoardView view = new();

    private readonly BoardViewState state = new();

    /// <summary>Set once a draw has failed, so the failure is logged on the transition (N4).</summary>
    private bool loggedDrawFailure;

    /// <summary>
    /// The inputs the current rows were built from. A wanted board of two dozen rows rebuilt every frame
    /// allocates for no visible gain, and the row order only moves when one of these does.
    /// </summary>
    private int builtKey = int.MinValue;

    public WantedBoardWindow(Plugin plugin, MainWindow main)
        : base(plugin, Loc.T("window.board") + "###MountbookWantedBoard")
    {
        Size = new Vector2(560f, 620f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
        this.main = main;
        IsOpen = plugin.Configuration.BoardWindowOpen;
    }

    public override void OnOpen()
    {
        plugin.Configuration.BoardWindowOpen = true;
        Plugin.SaveConfigSafe(plugin.Configuration);
        base.OnOpen();
    }

    public override void OnClose()
    {
        plugin.Configuration.BoardWindowOpen = false;
        Plugin.SaveConfigSafe(plugin.Configuration);
        base.OnClose();
    }

    public override void Draw()
    {
        try
        {
            DrawBody();

            if (loggedDrawFailure)
            {
                Plugin.Log.Debug("WantedBoardWindow.Draw recovered.");
                loggedDrawFailure = false;
            }
        }
        catch (Exception ex)
        {
            // Same contract as the other windows: a per-frame catch, logged on the transition only.
            if (!loggedDrawFailure)
            {
                Plugin.Log.Error(ex, "WantedBoardWindow.Draw failed.");
                loggedDrawFailure = true;
            }
        }
    }

    private void DrawBody()
    {
        DrawGlass();

        var config = plugin.Configuration;

        // The clock is read here and handed to the view: a view that asked the wall clock itself could not
        // be compared against a golden image.
        state.NowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        state.Sort = config.BoardSort;

        var key = HashCode.Combine(plugin.Catalog.Revision, plugin.BoardRevision, config.BoardSort, main.PriceVersion);

        if (key != builtKey)
        {
            state.Rows = WantedBoard.Build(
                plugin.Catalog.Items,
                config.WantedMounts,
                config.Grinds,
                main.CachedPrice,
                config.BoardSort);

            builtKey = key;
        }

        view.Draw(state);

        // The sort control writes the state; persisting it is the window's job, like every other request.
        if (state.Sort != config.BoardSort)
        {
            config.BoardSort = state.Sort;
            Plugin.SaveConfigSafe(config);
        }
    }
}
