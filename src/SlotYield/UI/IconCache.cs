using System.Collections.Generic;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace SlotYield.UI;

/// <summary>
/// Shared game-icon lookups, cached per (icon, HQ) pair. The shared texture objects are
/// owned by Dalamud, so this only keeps the lookup handles, never the GPU textures.
/// </summary>
internal static class IconCache
{
    private static readonly Dictionary<(uint IconId, bool Hq), ISharedImmediateTexture> Cache = new();

    public static bool TryGet(uint iconId, bool hq, out IDalamudTextureWrap? wrap)
    {
        wrap = null;
        if (iconId == 0)
            return false;

        if (!Cache.TryGetValue((iconId, hq), out var shared))
        {
            shared = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup
            {
                IconId = iconId,
                ItemHq = hq,
            });
            Cache[(iconId, hq)] = shared;
        }

        return shared.TryGetWrap(out wrap, out _);
    }
}
