using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;
using Mountbook.Services;

namespace Mountbook.Game;

/// <summary>
/// The two things the window can ask the game to do, plus the reasons the view should draw those
/// buttons disabled before they are ever clicked.
/// </summary>
/// <remarks>
/// <para>
/// Every call is guarded and fails soft (N4): a broken action-manager signature or a missing
/// condition service logs once and refuses the action, and <see cref="Ride"/> re-reads the unlocked
/// state immediately before acting so a stale cached flag can never mount an uncollected mount.
/// </para>
/// <para>
/// The item link is built with <see cref="RentedSeStringBuilder"/> and printed as
/// <see cref="ReadOnlySpan{T}"/> bytes through the API-15 <c>IChatGui.Print(ReadOnlySpan&lt;byte&gt;)</c>
/// overload: the mutable <c>SeString</c> path and its <c>Print(SeString)</c> overload are what API 16
/// removes (A2), while the byte overload survives.
/// </para>
/// </remarks>
internal sealed class MountActions
{
    private readonly MountCatalog catalog;

    private readonly IClientState clientState;

    private readonly ICondition condition;

    private readonly IChatGui chatGui;

    private readonly IPluginLog log;

    private readonly string displayName;

    private bool loggedFailure;

    private readonly IGameGui gameGui;

    /// <summary>When the guide was last opened, so a double dispatch cannot race the open.</summary>
    private long guideOpenedAt;

    public MountActions(
        MountCatalog catalog,
        IClientState clientState,
        ICondition condition,
        IChatGui chatGui,
        IGameGui gameGui,
        IPluginLog log,
        string displayName)
    {
        this.catalog = catalog;
        this.clientState = clientState;
        this.condition = condition;
        this.chatGui = chatGui;
        this.gameGui = gameGui;
        this.log = log;
        this.displayName = displayName;
    }

    /// <summary>
    /// Why "Ride" cannot act for the selected mount, or null when it can. The view disables the
    /// button itself for a mount the player does not own and for no selection, so those cases are
    /// not reasons here; this only covers window-level blockers.
    /// </summary>
    public unsafe string? DescribeRide(MountShelfItem? item)
    {
        if (item is null || !item.Owned)
        {
            return null;
        }

        if (!clientState.IsLoggedIn)
        {
            return "Log in to ride a mount.";
        }

        try
        {
            if (condition[ConditionFlag.InCombat])
            {
                return "You cannot mount in combat.";
            }

            if (condition[ConditionFlag.Casting])
            {
                return "Finish casting first.";
            }

            if (condition[ConditionFlag.OccupiedInEvent]
                || condition[ConditionFlag.OccupiedInQuestEvent]
                || condition[ConditionFlag.OccupiedInCutSceneEvent]
                || condition[ConditionFlag.WatchingCutscene]
                || condition[ConditionFlag.WatchingCutscene78])
            {
                return "Not during a cutscene or event.";
            }

            if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            {
                return "Wait for the area to load.";
            }

            if (condition[ConditionFlag.Unconscious])
            {
                return "You cannot mount while unconscious.";
            }

            if (ActionManager.Instance() == null)
            {
                return "The action system is not ready.";
            }

            // Ask the game the same question about the action we are about to use. The roulette
            // (GeneralAction 9) reports 654 "Mount unavailable in this location" even in places
            // where mounting a specific mount works, so the per-mount status is the honest test.
            if (ActionManager.Instance()->GetActionStatus(ActionType.Mount, item.Id) != 0)
            {
                return "The game will not let you mount here right now.";
            }

            // No NoteSuccess() here: this is a question asked every frame by the window, so clearing
            // the failure flag from it made the guard useless - one broken read logged an error and
            // then a false "works again" sixty times a second. Only the real actions clear it.
            return null;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return "The game state is unavailable right now.";
        }
    }

    /// <summary>
    /// Why "Link whistle" cannot act, or null when it can. The item id is not part of
    /// <see cref="MountShelfItem"/>, so without the dataset lookup there is nothing to link.
    /// </summary>
    public string? DescribeLink(uint itemId)
    {
        if (itemId == 0)
        {
            return "This mount has no whistle item.";
        }

        if (!clientState.IsLoggedIn)
        {
            return "Log in to link an item.";
        }

        return null;
    }

    /// <summary>
    /// Uses the game's own mount action for a collected mount. The unlocked check is repeated here,
    /// at click time, because the catalog's cached flag can be up to the refresh interval old.
    /// </summary>
    public unsafe bool Ride(uint mountId)
    {
        if (mountId == 0 || !clientState.IsLoggedIn)
        {
            return false;
        }

        try
        {
            if (!catalog.IsOwnedNow(mountId))
            {
                log.Debug("Ignoring a ride request for mount {MountId}: it is not collected.", mountId);
                return false;
            }

            var actionManager = ActionManager.Instance();
            if (actionManager == null)
            {
                return false;
            }

            if (!actionManager->UseAction(ActionType.Mount, mountId))
            {
                log.Debug("The game refused the mount action for mount {MountId}.", mountId);
                return false;
            }

            NoteSuccess();
            return true;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return false;
        }
    }

    /// <summary>
    /// Opens the game's own Mount Guide, which is where a mount is shown as a 3D model. The guide is
    /// its own addon ("MountNoteBook") driven by <c>AgentMountNoteBook</c>; the agent's inherited
    /// <c>Show()</c> virtual is what the game itself calls to open that window. The chat command
    /// <c>/mountguide</c> is handled by the game's command dispatcher, which
    /// <c>ICommandManager.ProcessCommand</c> does not reach from a plugin.
    /// </summary>
    public unsafe bool OpenMountGuide()
    {
        if (!clientState.IsLoggedIn)
        {
            return false;
        }

        try
        {
            if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            {
                log.Debug("Not opening the Mount Guide while an area is loading.");
                return false;
            }

            // Skip when it is already up, and swallow a repeat inside a second: one click can
            // dispatch twice, and a second open is wasted work (the old command path also closed
            // the guide on a second dispatch, which is what made the button look dead).
            if (IsMountGuideVisible())
            {
                return true;
            }

            var now = Environment.TickCount64;
            if (now - guideOpenedAt < 1000)
            {
                return true;
            }

            // AgentMountNoteBook.Instance() is AgentModule.Instance()->GetAgentByInternalId(
            // AgentId.MountNotebook), and Show() is AgentInterface's virtual function 3. Both were
            // confirmed in the FFXIVClientStructs 15.0.3.9 assembly the SDK references.
            var agent = AgentMountNoteBook.Instance();
            if (agent == null)
            {
                log.Debug("The Mount Guide agent is not available.");
                return false;
            }

            agent->Show();

            // Stamped after Show() returns: stamped before it, a throwing Show() still armed the
            // one-second debounce, so the next click reported success without opening anything.
            guideOpenedAt = now;
            NoteSuccess();
            return true;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return false;
        }
    }

    /// <summary>True while the game's Mount Guide addon is loaded and visible.</summary>
    private unsafe bool IsMountGuideVisible()
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName("MountNoteBook").Address;
        return addon is not null && addon->IsVisible && addon->IsReady;
    }

    /// <summary>The reason the Mount Guide button should be disabled, or null when it may act.</summary>
    public string? DescribeMountGuide()
    {
        if (!clientState.IsLoggedIn)
        {
            return "Log in to open the Mount Guide.";
        }

        try
        {
            if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            {
                return "Wait for the area to load.";
            }

            // Per-frame question, like DescribeRide: the failure flag belongs to the real actions.
            return null;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return "The game state is unavailable right now.";
        }
    }

    /// <summary>
    /// Prints the mount's whistle as a real item link in chat. Does nothing when the dataset has no
    /// item for the mount or the item sheet cannot name it.
    /// </summary>
    public bool LinkWhistle(uint itemId)
    {
        if (itemId == 0 || !clientState.IsLoggedIn)
        {
            return false;
        }

        try
        {
            ReadOnlySeString name = ItemUtil.GetItemName(itemId, includeIcon: false);
            if (name.IsEmpty)
            {
                log.Debug("Not linking item {ItemId}: the item sheet has no name for it.", itemId);
                return false;
            }

            var rarityColor = ItemUtil.GetItemRarityColorType(itemId);

            // The same payload order SeString.CreateItemLink uses: colour, edge colour, the link
            // with its plain text, the display text, then the terminators.
            using RentedSeStringBuilder builder = new();
            _ = builder.Append('[')
                .Append(displayName)
                .Append("] ")
                .PushColorType(rarityColor)
                .PushEdgeColorType(rarityColor + 1)
                .PushLinkItem(itemId, name.ExtractText())
                .Append(name)
                .PopLink()
                .PopEdgeColorType()
                .PopColorType();

            chatGui.Print(builder.GetViewAsSpan());
            NoteSuccess();
            return true;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return false;
        }
    }

    private void LogFailure(Exception ex)
    {
        if (loggedFailure)
        {
            return;
        }

        loggedFailure = true;
        log.Error(ex, "A mount action failed.");
    }

    private void NoteSuccess()
    {
        if (!loggedFailure)
        {
            return;
        }

        loggedFailure = false;
        log.Debug("The mount actions work again.");
    }
}
