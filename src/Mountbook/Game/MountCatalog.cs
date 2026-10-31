using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Mountbook.Services;

namespace Mountbook.Game;

/// <summary>
/// Builds and owns the mount catalog: one <see cref="MountShelfItem"/> per mount in the game,
/// filled from the Lumina sheets, the embedded obtain data and the player's unlocked-mount state.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is built lazily on the first draw (or <c>/mountbook status</c>) rather than in the
/// plugin constructor, so a broken sheet or a logged-out client cannot stop the plugin from loading.
/// The static half - names, icons, flavour text, patch and sources - is read once. Only the
/// <see cref="MountShelfItem.Owned"/> flag is refreshed, and only while the window is open, at most
/// once every <see cref="RefreshIntervalMs"/> milliseconds: sixty game reads a second would be a
/// pointless allocation and marshalling cost, and a collection cannot change that fast.
/// </para>
/// <para>
/// Every call into the game is wrapped: a failed sheet read or a broken
/// <c>PlayerState.Instance()-&gt;IsMountUnlocked</c> signature leaves the catalog empty or unowned
/// and logs the transition once (N4), never a stack trace per frame.
/// </para>
/// </remarks>
internal sealed class MountCatalog
{
    /// <summary>The logical name set by the csproj; a rename there must be mirrored here.</summary>
    private const string ResourceName = "Mountbook.Data.mount-sources.json";

    /// <summary>How often the owned flags may be re-read while the window is open.</summary>
    private const int RefreshIntervalMs = 2_000;

    private readonly IDataManager data;

    private readonly IClientState clientState;

    private readonly IPluginLog log;

    private readonly List<MountShelfItem> items = [];

    private readonly Dictionary<uint, string> itemNames = [];

    /// <summary>Scratch space for one owned-flag read, so the flags can be applied as a set.</summary>
    private bool[] ownedFlags = [];

    private bool loggedItemFailure;

    private MountShelf? shelf;

    private bool built;

    private int owned;

    private int revision;

    private long nextRefreshAt;

    private bool loggedShelfFailure;

    private bool loggedBuildFailure;

    private bool loggedOwnedFailure;

    public MountCatalog(IDataManager data, IClientState clientState, IPluginLog log)
    {
        this.data = data;
        this.clientState = clientState;
        this.log = log;
    }

    /// <summary>The catalog in sheet order; the caller filters and orders it.</summary>
    public IReadOnlyList<MountShelfItem> Items => items;

    /// <summary>How many of the mounts in the catalog are collected.</summary>
    public int Owned => owned;

    /// <summary>How many mounts the catalog holds; zero until <see cref="EnsureBuilt"/> succeeds.</summary>
    public int Total => items.Count;

    /// <summary>Whether the static half of the catalog has been read.</summary>
    public bool IsBuilt => built;

    /// <summary>How many entries the embedded obtain dataset carries (zero when it failed to load).</summary>
    public int SourceCount => shelf?.Count ?? 0;

    /// <summary>Changes when the items or their owned flags change, so callers can re-query.</summary>
    public int Revision => revision;

    /// <summary>
    /// Runs the periodic refresh if its interval has elapsed (or when <paramref name="force"/> asks
    /// for it, as opening the window does): the first call builds the catalog, later calls only
    /// re-read the owned flags. Returns whether work ran, which the window also uses to retry icon
    /// textures that were not loaded yet.
    /// </summary>
    public bool RefreshIfDue(long now, bool force)
    {
        if (!force && now < nextRefreshAt)
        {
            return false;
        }

        nextRefreshAt = now + RefreshIntervalMs;

        if (!built && !TryBuild())
        {
            return true;
        }

        RefreshOwned();
        return true;
    }

    /// <summary>
    /// Builds the catalog now if it is missing, and refreshes the owned flags. Used by the
    /// <c>status</c> subcommand, which must work before the window was ever drawn.
    /// </summary>
    public bool EnsureBuilt()
    {
        if (!built && !TryBuild())
        {
            return false;
        }

        RefreshOwned();
        return true;
    }

    /// <summary>
    /// Reads the player's unlocked state for one mount right now. The button handler uses this as
    /// its last-moment guard; the cached <see cref="MountShelfItem.Owned"/> flag is only for drawing.
    /// </summary>
    public unsafe bool IsOwnedNow(uint mountId)
    {
        if (mountId == 0)
        {
            return false;
        }

        try
        {
            return ReadUnlocked(mountId);
        }
        catch (Exception ex)
        {
            LogOwnedFailure(ex);
            return false;
        }
    }

    /// <summary>Looks up the whistle item id for a mount, or false when the dataset has none.</summary>
    public bool TryGetItemId(uint mountId, out uint itemId)
    {
        itemId = 0;

        if (shelf is null || !shelf.TryGet(mountId, out MountSources sources) || sources.ItemId == 0)
        {
            return false;
        }

        itemId = sources.ItemId;
        return true;
    }

    /// <summary>
    /// The name of the item that grants a mount (its whistle), or null when the dataset has no item for it.
    /// </summary>
    /// <remarks>
    /// The embedded dataset carries the item id only, and a player searches for the *item*, so the name is
    /// resolved from the Item sheet on demand and cached: it is one row read per mount ever looked at.
    /// </remarks>
    public string? ItemNameFor(uint mountId)
    {
        if (!TryGetItemId(mountId, out var itemId))
        {
            return null;
        }

        if (itemNames.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        try
        {
            var items = data.GetExcelSheet<Item>();

            if (!items.TryGetRow(itemId, out Item row))
            {
                return null;
            }

            var name = row.Name.ExtractText();

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            itemNames[itemId] = name;
            return name;
        }
        catch (Exception ex)
        {
            if (!loggedItemFailure)
            {
                loggedItemFailure = true;
                log.Error(ex, "Could not read the item name for item {ItemId}.", itemId);
            }

            return null;
        }
    }

    /// <summary>
    /// Reads the Mount and MountTransient sheets plus the embedded obtain data. Ownership is left
    /// false here on purpose: <see cref="RefreshOwned"/> fills it and can fail without taking the
    /// names and icons down with it.
    /// </summary>
    private bool TryBuild()
    {
        try
        {
            var mountSheet = data.GetExcelSheet<Mount>();
            var transientSheet = data.GetExcelSheet<MountTransient>();
            var source = LoadShelf();

            var next = new List<MountShelfItem>(mountSheet.Count);
            foreach (Mount row in mountSheet)
            {
                var mountId = row.RowId;
                if (mountId == 0)
                {
                    continue;
                }

                // The sheet's Singular column is written for mid-sentence use ("battle bear"); the
                // book shows the name the way the Mount Guide does ("Battle Bear").
                var name = MountName.ToDisplay(row.Singular.ExtractText());
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var description = string.Empty;
                var enhancedDescription = string.Empty;
                var tooltip = string.Empty;

                if (transientSheet.TryGetRow(mountId, out MountTransient transient))
                {
                    description = transient.Description.ExtractText();
                    enhancedDescription = transient.DescriptionEnhanced.ExtractText();
                    tooltip = transient.Tooltip.ExtractText();
                }

                var patch = string.Empty;
                IReadOnlyList<MountSourceLine> sources = Array.Empty<MountSourceLine>();
                MountCommunity? community = null;

                if (source.TryGet(mountId, out MountSources mountSources))
                {
                    patch = mountSources.Patch;
                    sources = mountSources.Lines;
                    community = mountSources.Community;
                }

                // 64000 + the small icon id is the large artwork the Mount Guide shows.
                var iconId = (uint)row.Icon;
                var artId = iconId == 0 ? 0u : 64000u + iconId;
                var seats = row.ExtraSeats + 1;

                next.Add(new MountShelfItem(
                    mountId,
                    name,
                    iconId,
                    artId,
                    seats,
                    patch,
                    false,
                    description,
                    enhancedDescription,
                    tooltip,
                    sources,
                    community,
                    MountBarding.Supports((uint)row.EquipHead, (uint)row.EquipBody, (uint)row.EquipLeg, (uint)row.EquipFoot),
                    // The sheet always answers this one (a flag, not a nullable), which is why the search
                    // operator needs no "unrecorded" case the way tradeable: does.
                    row.IsFlying != 0));
            }

            items.Clear();
            items.AddRange(next);
            built = true;
            owned = 0;
            revision++;

            if (loggedBuildFailure)
            {
                loggedBuildFailure = false;
                log.Debug("The mount catalog is available again ({Total} mounts).", items.Count);
            }

            return true;
        }
        catch (Exception ex)
        {
            if (!loggedBuildFailure)
            {
                loggedBuildFailure = true;
                log.Error(ex, "Could not build the mount catalog from the game's sheets.");
            }

            return false;
        }
    }

    /// <summary>
    /// Re-reads the owned flag of every catalog entry. A failure keeps the previous flags and logs
    /// the transition, because the player's collection is not worth taking the window down for.
    /// </summary>
    private unsafe void RefreshOwned()
    {
        if (!clientState.IsLoggedIn)
        {
            MarkAllUnowned();
            return;
        }

        try
        {
            var playerState = PlayerState.Instance();
            if (playerState == null)
            {
                return;
            }

            // Read every flag first, then apply. Applying inside the read loop meant a throw part-way
            // left `items` half-updated while `owned` and `revision` still held their pre-loop values,
            // so the header count and the grid disagreed until the next successful refresh.
            if (ownedFlags.Length != items.Count)
            {
                ownedFlags = new bool[items.Count];
            }

            var changed = false;
            var ownedNow = 0;

            for (var index = 0; index < items.Count; index++)
            {
                var unlocked = playerState->IsMountUnlocked(items[index].Id);
                ownedFlags[index] = unlocked;

                if (unlocked)
                {
                    ownedNow++;
                }
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (ownedFlags[index] != items[index].Owned)
                {
                    items[index] = items[index] with { Owned = ownedFlags[index] };
                    changed = true;
                }
            }

            owned = ownedNow;

            if (changed)
            {
                revision++;
            }

            if (loggedOwnedFailure)
            {
                loggedOwnedFailure = false;
                log.Debug("The owned-mount state is readable again.");
            }
        }
        catch (Exception ex)
        {
            LogOwnedFailure(ex);
        }
    }

    /// <summary>
    /// Logged out means "collection unknown", and showing the last character's owned flags under a
    /// different one would be a lie, so every entry is drawn unowned until the next refresh.
    /// </summary>
    private void MarkAllUnowned()
    {
        if (owned == 0)
        {
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Owned)
            {
                items[index] = item with { Owned = false };
            }
        }

        owned = 0;
        revision++;
    }

    private unsafe bool ReadUnlocked(uint mountId)
    {
        if (!clientState.IsLoggedIn)
        {
            return false;
        }

        var playerState = PlayerState.Instance();
        return playerState != null && playerState->IsMountUnlocked(mountId);
    }

    private void LogOwnedFailure(Exception ex)
    {
        if (loggedOwnedFailure)
        {
            return;
        }

        loggedOwnedFailure = true;
        log.Error(ex, "Could not read the unlocked-mount state; treating every mount as unowned.");
    }

    /// <summary>
    /// Loads the embedded obtain dataset. A missing or malformed resource is a packaging defect,
    /// logged once; the catalog still builds from the sheets, with an empty patch and no sources.
    /// </summary>
    private MountShelf LoadShelf()
    {
        if (shelf is not null)
        {
            return shelf;
        }

        try
        {
            using Stream? stream = typeof(MountCatalog).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                if (!loggedShelfFailure)
                {
                    loggedShelfFailure = true;
                    log.Error(
                        "The embedded mount source data '{ResourceName}' is missing; patches and obtain sources will be empty.",
                        ResourceName);
                }

                shelf = MountShelf.Parse("{}");
                return shelf;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            shelf = MountShelf.Parse(reader.ReadToEnd());
            return shelf;
        }
        catch (Exception ex)
        {
            if (!loggedShelfFailure)
            {
                loggedShelfFailure = true;
                log.Error(ex, "Could not read the embedded mount source data; patches and obtain sources will be empty.");
            }

            shelf = MountShelf.Parse("{}");
            return shelf;
        }
    }
}
