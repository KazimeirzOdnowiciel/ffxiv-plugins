using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Mountbook.Services;

namespace Mountbook.Game;

/// <summary>
/// The icon and artwork handles <see cref="Views.MountViewState.Textures"/> exposes to the view.
/// </summary>
/// <remarks>
/// <para>
/// What is cached is the <see cref="ISharedImmediateTexture"/> Dalamud returns, never the bare
/// texture id. The wrap behind a game icon is frame-scoped: its handle is only valid for the frame
/// that asked for it, so keeping just the <c>nint</c> produced dangling texture ids, which the
/// renderer drew as blank tiles or garbage. <see cref="RefreshHandles"/> therefore re-reads a live
/// wrap for the visible mounts every frame and hands those handles to the view.
/// </para>
/// <para>
/// Creating the shared texture is the part that can be slow, so it is still budgeted by
/// <see cref="Pump"/> (a few per frame instead of a few hundred on the first open). The large
/// artwork is resolved only for the selected mount, because the detail pane is the only place the
/// view draws it.
/// </para>
/// <para>
/// Failures are wrapped and logged on the transition, never once per icon per frame (N4).
/// </para>
/// </remarks>
internal sealed class MountTextureCache
{
    /// <summary>How many shared textures one <see cref="Pump"/> may create per frame.</summary>
    private const int BudgetPerFrame = 24;

    private readonly ITextureProvider textures;

    private readonly IPluginLog log;

    private readonly Dictionary<uint, ISharedImmediateTexture> shared = [];

    private readonly Dictionary<uint, nint> handles = [];

    private readonly Dictionary<uint, Vector2> sizes = [];

    private readonly Queue<uint> pending = new();

    private readonly HashSet<uint> queued = [];

    private bool loggedFailure;

    public MountTextureCache(ITextureProvider textures, IPluginLog log)
    {
        this.textures = textures;
        this.log = log;
    }

    /// <summary>The handles the view reads, refreshed by <see cref="RefreshHandles"/>.</summary>
    public IReadOnlyDictionary<uint, nint> Handles => handles;

    /// <summary>
    /// The pixel size behind each handle. The view needs it to cover-crop the hero artwork: drawing a
    /// non-square source into a fixed box stretches it, which is what made the detail image look
    /// squashed.
    /// </summary>
    public IReadOnlyDictionary<uint, Vector2> Sizes => sizes;

    /// <summary>True while there are shared textures still to create; drives <see cref="Pump"/>.</summary>
    public bool HasPending => pending.Count > 0;

    /// <summary>
    /// Queues whatever is missing for the drawn mounts: every small icon, plus the artwork of the
    /// selected mount. Called when the item list or the selection changes, never per frame.
    /// </summary>
    public void Request(IReadOnlyList<MountShelfItem> items, uint? selectedId)
    {
        if (selectedId is { } selected)
        {
            foreach (MountShelfItem item in items)
            {
                if (item.Id != selected)
                {
                    continue;
                }

                Enqueue(item.ArtId);
                break;
            }
        }

        foreach (MountShelfItem item in items)
        {
            Enqueue(item.IconId);
        }
    }

    /// <summary>
    /// Creates at most <see cref="BudgetPerFrame"/> of the queued shared textures, so the first open
    /// spreads the cost instead of hitching the client on one draw pass.
    /// </summary>
    public void Pump()
    {
        for (var created = 0; created < BudgetPerFrame && pending.Count > 0; created++)
        {
            uint iconId = pending.Dequeue();
            queued.Remove(iconId);
            EnsureShared(iconId);
        }
    }

    /// <summary>
    /// Re-reads a live wrap for every drawn mount and stores its handle for the current frame. This
    /// is what makes the icons survive: the wrap is frame-scoped, the shared texture is not.
    /// </summary>
    public void RefreshHandles(IReadOnlyList<MountShelfItem> items, uint? selectedId)
    {
        foreach (MountShelfItem item in items)
        {
            CopyHandle(item.IconId);

            if (selectedId is { } selected && item.Id == selected)
            {
                CopyHandle(item.ArtId);
            }
        }
    }

    private void CopyHandle(uint iconId)
    {
        if (iconId == 0 || !shared.TryGetValue(iconId, out ISharedImmediateTexture? texture))
        {
            return;
        }

        try
        {
            var wrap = texture.GetWrapOrEmpty();
            handles[iconId] = (nint)wrap.Handle.Handle;
            sizes[iconId] = wrap.Size;

            if (loggedFailure)
            {
                loggedFailure = false;
                log.Debug("Game icon textures are loading again.");
            }
        }
        catch (Exception ex)
        {
            if (!loggedFailure)
            {
                loggedFailure = true;
                log.Error(ex, "Could not read the game icon texture {IconId}; drawing a placeholder.", iconId);
            }
        }
    }

    private void EnsureShared(uint iconId)
    {
        if (iconId == 0 || shared.ContainsKey(iconId))
        {
            return;
        }

        try
        {
            shared[iconId] = textures.GetFromGameIcon(new GameIconLookup(iconId));
        }
        catch (Exception ex)
        {
            if (!loggedFailure)
            {
                loggedFailure = true;
                log.Error(ex, "Could not create the game icon texture {IconId}; drawing a placeholder.", iconId);
            }
        }
    }

    private void Enqueue(uint iconId)
    {
        if (iconId == 0 || shared.ContainsKey(iconId))
        {
            return;
        }

        if (queued.Add(iconId))
        {
            pending.Enqueue(iconId);
        }
    }
}
