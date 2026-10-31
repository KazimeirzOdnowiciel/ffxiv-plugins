using System;

namespace Mountbook.Services;

/// <summary>
/// Builds the Gamer Escape wiki URL for a mount, so the detail pane can hand the player to the page that
/// explains the fight or the event in full.
/// </summary>
/// <remarks>
/// The escaping is deliberately minimal and matches what other Dalamud collection plugins do in the field:
/// spaces become underscores and the apostrophe becomes <c>%27</c>. Escaping everything (as
/// <see cref="Uri.EscapeDataString(string)"/> would) also works on the wiki, but it turns readable names into
/// percent soup for no benefit, and the two characters above are the only ones that break the route.
/// </remarks>
public static class MountWiki
{
    /// <summary>The wiki's mount page URL for <paramref name="displayName"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="displayName"/> is null.</exception>
    public static string Url(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        var page = displayName.Trim()
            .Replace(" ", "_", StringComparison.Ordinal)
            .Replace("'", "%27", StringComparison.Ordinal);

        return string.Concat("https://ffxiv.gamerescape.com/wiki/", page);
    }
}
