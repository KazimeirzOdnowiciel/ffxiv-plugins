namespace Mountbook.Services;

/// <summary>
/// What a reset to defaults would take with it, counted from the collections the player fills in themselves.
/// The counts exist to answer one question - does this reset need a confirmation - and to let the settings window
/// name what is about to go.
/// </summary>
/// <param name="Wanted">Mount ids on the hunting list.</param>
/// <param name="Grinds">Mounts with a grind record (a count, a target or a note).</param>
/// <param name="Watches">Mounts with a price watch.</param>
/// <param name="Views">Saved filter presets, each one a name and the five axes of the filter block.</param>
public readonly record struct ConfigResetImpact(int Wanted, int Grinds, int Watches, int Views)
{
    /// <summary>How many pieces of the player's own work a reset would discard.</summary>
    public int Total => Wanted + Grinds + Watches + Views;
}

/// <summary>
/// The reset-to-defaults rule: a reset that would destroy something the player entered has to be confirmed,
/// and one that would not is a single click.
/// </summary>
/// <remarks>
/// The wanted list is the case this exists for - it is the one thing in the file a player cannot retype from
/// memory (a static's hunting list, a friend's suggestions) - but grind records, price watches and saved views
/// are the same kind of input, so one rule covers all of them rather than a special case for the list that
/// happens to be oldest. Everything else in the config is a setting: a filter, a theme, a sort order, all one
/// click away from being set again. A saved view is on this side of the line because its *name* is typed: the
/// five axes are one click away, the word the player chose for them is not.
/// </remarks>
public static class ConfigReset
{
    /// <summary>True when <paramref name="impact"/> holds something the player would have to re-enter.</summary>
    /// <param name="impact">What the config currently holds.</param>
    public static bool NeedsConfirmation(in ConfigResetImpact impact) => impact.Total > 0;
}
