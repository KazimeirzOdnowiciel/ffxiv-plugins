using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using Lumina.Excel;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace EnemyListCastBars;

/// <summary>
/// Draws a cast bar on every row of the game's enemy list by attaching
/// <see cref="ProgressBarEnemyCastNode"/>s to the <c>_EnemyList</c> addon.
/// </summary>
public sealed unsafe class EnemyListCastBarController : IDisposable
{
    /// <summary>Addon that hosts the enemy list.</summary>
    private const string AddonName = "_EnemyList";

    /// <summary>Number of leading addon nodes that are never enemy rows.</summary>
    private const int FirstRowNodeIndex = 4;

    /// <summary>Entity id the game uses for an unused enemy list slot.</summary>
    private const uint EmptyEntityId = 0xE0000000;

    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;
    private readonly IDataManager dataManager;
    private readonly IPluginLog pluginLog;
    private readonly Configuration configuration;
    private readonly Func<bool> isKamiToolKitReady;

    private readonly List<RowNodes> rows = [];

    private AtkUnitBase* addon;
    private ushort builtNodeListCount = ushort.MaxValue;
    private bool rebuildRequested;
    private bool warnedAboutMissingRows;

    public EnemyListCastBarController(
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IDataManager dataManager,
        IPluginLog pluginLog,
        Configuration configuration,
        Func<bool> isKamiToolKitReady)
    {
        this.addonLifecycle = addonLifecycle;
        this.gameGui = gameGui;
        this.dataManager = dataManager;
        this.pluginLog = pluginLog;
        this.configuration = configuration;
        this.isKamiToolKitReady = isKamiToolKitReady;

        this.addonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonName, this.OnAddonEvent);
        this.addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, this.OnAddonEvent);
    }

    /// <summary>
    /// Discards the current nodes so they are recreated with the latest settings on the next frame.
    /// </summary>
    public void RequestRebuild() => this.rebuildRequested = true;

    public void Dispose()
    {
        this.addonLifecycle.UnregisterListener(this.OnAddonEvent);
        this.DestroyRows();
    }

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        if (type == AddonEvent.PreFinalize)
        {
            this.DestroyRows();
            return;
        }

        // KamiToolKit has to finish initializing before any native node may be created.
        if (!this.configuration.Enabled || !this.isKamiToolKitReady())
        {
            this.DestroyRows();
            return;
        }

        var addonPtr = this.gameGui.GetAddonByName(AddonName);
        if (addonPtr.IsNull)
        {
            this.DestroyRows();
            return;
        }

        var unitBase = (AtkUnitBase*)addonPtr.Address;
        if (unitBase == null)
        {
            this.DestroyRows();
            return;
        }

        // The addon rebuilds its rows whenever the list size changes, so mirror that here.
        if (this.rebuildRequested || this.addon != unitBase || this.builtNodeListCount != unitBase->UldManager.NodeListCount)
        {
            this.DestroyRows();
            this.BuildRows(unitBase);
        }

        this.UpdateRows();
    }

    private void BuildRows(AtkUnitBase* unitBase)
    {
        var uldManager = &unitBase->UldManager;
        var rowPointers = new List<nint>();

        for (var i = FirstRowNodeIndex; i < uldManager->NodeListCount; i++)
        {
            var node = uldManager->NodeList[i];
            if (node == null || node->GetNodeType() != NodeType.Component)
                continue;

            // Each enemy list row is a component button; every other component is ignored.
            if (node->GetAsAtkComponentButton() == null)
                continue;

            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;

            rowPointers.Add((nint)componentNode);
        }

        // The addon stores its rows bottom-up, while the enemy list data is ordered top-down.
        rowPointers.Reverse();

        foreach (var rowPointer in rowPointers)
            this.rows.Add(new RowNodes((AtkComponentNode*)rowPointer, this.configuration));

        this.addon = unitBase;
        this.rebuildRequested = false;

        if (rowPointers.Count > 0)
        {
            this.builtNodeListCount = uldManager->NodeListCount;
            return;
        }

        // The addon has not built its rows yet, so try again on the next frame.
        this.builtNodeListCount = ushort.MaxValue;

        if (!this.warnedAboutMissingRows)
        {
            this.warnedAboutMissingRows = true;
            this.pluginLog.Warning("No rows found on the {Addon} addon, cast bars will not be drawn.", AddonName);
        }
    }

    private void UpdateRows()
    {
        if (this.rows.Count == 0)
            return;

        var enemyList = EnemyListNumberArray.Instance();
        if (enemyList == null)
        {
            this.HideAllRows();
            return;
        }

        var characterManager = CharacterManager.Instance();
        var enemyCount = Math.Min(enemyList->EnemyCount, this.rows.Count);

        for (var i = 0; i < this.rows.Count; i++)
        {
            var row = this.rows[i];

            if (i >= enemyCount)
            {
                row.Hide();
                continue;
            }

            var entityId = (uint)enemyList->Enemies[i].EntityId;
            if (entityId is 0 or EmptyEntityId)
            {
                row.Hide();
                continue;
            }

            var battleChara = characterManager->LookupBattleCharaByEntityId(entityId);
            if (battleChara == null)
            {
                row.Hide();
                continue;
            }

            ref var cast = ref battleChara->CastInfo;
            var remaining = cast.TotalCastTime - cast.CurrentCastTime;

            if (cast.ActionId == 0 || (!cast.IsCasting && remaining <= 0f))
            {
                row.Hide();
                continue;
            }

            var progress = cast.TotalCastTime > 0f ? cast.CurrentCastTime / cast.TotalCastTime : 0f;

            row.Show(progress, cast.Interruptible, this.BuildText((ActionType)cast.ActionType, cast.ActionId, remaining));
        }
    }

    private string BuildText(ActionType actionType, uint actionId, float remainingTime)
    {
        var name = this.configuration.ShowActionName ? this.GetActionName(actionType, actionId) : string.Empty;
        var time = this.configuration.ShowRemainingTime ? $"{MathF.Max(remainingTime, 0f):0.0}s" : string.Empty;

        if (name.Length == 0)
            return time;

        return time.Length == 0 ? name : $"{name}  {time}";
    }

    private string GetActionName(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action || actionId == 0)
            return string.Empty;

        ExcelSheet<LuminaAction>? sheet = this.dataManager.GetExcelSheet<LuminaAction>();
        if (sheet is null || !sheet.TryGetRow(actionId, out var action))
            return string.Empty;

        return action.Name.ToString();
    }

    private void HideAllRows()
    {
        foreach (var row in this.rows)
            row.Hide();
    }

    private void DestroyRows()
    {
        foreach (var row in this.rows)
            row.Dispose();

        this.rows.Clear();
        this.addon = null;
        this.builtNodeListCount = ushort.MaxValue;
    }

    /// <summary>
    /// The nodes drawn on a single enemy list row: the bar frame, the progress fill and the cast text.
    /// </summary>
    private sealed class RowNodes : IDisposable
    {
        /// <summary>Texture holding both the cast bar frame and its fill, as used by the party list.</summary>
        private const string BarTexture = "ui/uld/PartyList_GaugeCast.tex";

        private readonly Configuration configuration;
        private readonly float fillMaxWidth;

        private readonly SimpleNineGridNode frame;
        private readonly ColorImageNode fill;
        private readonly TextNode text;

        private string lastText = string.Empty;
        private bool? lastInterruptible;

        public RowNodes(AtkComponentNode* row, Configuration configuration)
        {
            this.configuration = configuration;

            var padding = Math.Clamp(configuration.BarFillPadding, 0f, configuration.BarSize.Y / 2f);
            this.fillMaxWidth = Math.Max(configuration.BarSize.X - (padding * 2f), 1f);

            this.frame = new SimpleNineGridNode
            {
                TexturePath = BarTexture,
                TextureCoordinates = new Vector2(0f, 12f),
                TextureSize = new Vector2(204f, 20f),
                LeftOffset = 20f,
                RightOffset = 20f,
                Position = configuration.BarPosition,
                Size = configuration.BarSize,
                Color = configuration.BarBackgroundColor,
                IsVisible = false,
            };
            this.frame.AttachNode(row);

            // The fill is a plain stretching image rather than part of the frame's nine grid, so that
            // animating its width grows a smooth bar instead of squeezing the texture into itself.
            this.fill = new ColorImageNode
            {
                TexturePath = BarTexture,
                TextureCoordinates = new Vector2(8f, 3f),
                TextureSize = new Vector2(188f, 7f),
                Position = configuration.BarPosition + new Vector2(padding),
                Size = new Vector2(this.fillMaxWidth, Math.Max(configuration.BarSize.Y - (padding * 2f), 1f)),
                IsVisible = false,
            };
            this.fill.AttachNode(row);

            this.text = new TextNode
            {
                Position = configuration.TextPosition,
                Size = configuration.TextSize,
                AlignmentType = AlignmentType.Center,
                TextFlags = TextFlags.Edge,
                FontSize = configuration.FontSize,
                TextColor = configuration.TextColor,
                TextOutlineColor = configuration.TextOutlineColor,
                IsVisible = false,
            };
            this.text.AttachNode(row);
        }

        public void Show(float progress, bool interruptible, string text)
        {
            this.frame.IsVisible = true;
            this.fill.IsVisible = true;
            this.fill.Width = Math.Clamp(progress, 0f, 1f) * this.fillMaxWidth;

            if (this.lastInterruptible != interruptible)
            {
                this.lastInterruptible = interruptible;
                this.fill.Color = interruptible
                    ? this.configuration.InterruptibleBarColor
                    : this.configuration.UninterruptibleBarColor;
            }

            this.text.IsVisible = text.Length > 0;
            if (this.lastText != text)
            {
                this.lastText = text;
                this.text.String = text;
            }
        }

        public void Hide()
        {
            this.frame.IsVisible = false;
            this.fill.IsVisible = false;
            this.text.IsVisible = false;
        }

        public void Dispose()
        {
            this.frame.Dispose();
            this.fill.Dispose();
            this.text.Dispose();
        }
    }
}
