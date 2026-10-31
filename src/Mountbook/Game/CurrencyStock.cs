using System;
using System.Collections.Generic;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Mountbook.Game;

/// <summary>
/// Finds the item behind a token name, and counts how many the player has.
/// </summary>
/// <remarks>
/// <para>
/// The dataset spells prices the way the game's shop lines do - "12,000 Seafarer's Cowries" - which is the
/// item's <em>plural</em>, not its name. The Item sheet carries both, so the lookup is exact: one index of
/// plural to item id, built once on first use and kept.
/// </para>
/// <para>
/// Building it walks every item row, which is the price of not shipping a name table that would rot with
/// the next dataset refresh. It happens on the framework thread the first time a priced mount is opened, so
/// the cost lands on one frame rather than on every one.
/// </para>
/// </remarks>
internal sealed class CurrencyStock
{
    /// <summary>
    /// The containers a token can be sitting in. Retainers, mail and the armoire are deliberately absent:
    /// a token there cannot be spent without moving it first, so counting it would claim a purchase the
    /// player cannot make right now.
    /// </summary>
    private static readonly GameInventoryType[] Containers =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4,
        GameInventoryType.Currency,
        GameInventoryType.Crystals,
        GameInventoryType.SaddleBag1,
        GameInventoryType.SaddleBag2,
        GameInventoryType.PremiumSaddleBag1,
        GameInventoryType.PremiumSaddleBag2,
    ];

    private readonly IGameInventory inventory;

    private readonly IDataManager data;

    private readonly IPluginLog log;

    /// <summary>Item plural (ordinal-ignore-case) to item id, or null until the first lookup is made.</summary>
    private Dictionary<string, uint>? byPlural;

    private bool buildFailed;

    public CurrencyStock(IGameInventory inventory, IDataManager data, IPluginLog log)
    {
        this.inventory = inventory;
        this.data = data;
        this.log = log;
    }

    /// <summary>True when the index is ready, so the caller can avoid building it on a frame it does not need.</summary>
    public bool IsReady => byPlural is not null;

    /// <summary>
    /// The item id behind <paramref name="currency"/> ("Seafarer's Cowries"), or 0 when the sheet has no
    /// item with that plural. Builds the index on first call.
    /// </summary>
    /// <param name="currency">A token name as the dataset spells it.</param>
    public uint ItemIdOf(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || buildFailed)
        {
            return 0;
        }

        byPlural ??= BuildIndex();

        return byPlural is not null && byPlural.TryGetValue(currency.Trim(), out uint itemId) ? itemId : 0;
    }

    /// <summary>
    /// How many of <paramref name="itemId"/> the player is carrying, across the containers a token can be
    /// spent from. Returns 0 for an unknown item rather than a guess.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    public int Count(uint itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        var total = 0;

        foreach (GameInventoryType type in Containers)
        {
            try
            {
                foreach (GameInventoryItem item in inventory.GetInventoryItems(type))
                {
                    if (!item.IsEmpty && item.ItemId == itemId)
                    {
                        total += item.Quantity;
                    }
                }
            }
            catch (Exception ex)
            {
                // A container that is not loaded (a saddlebag with no chocobo, a retainer page) is normal;
                // the count is then short, which reads as "not enough" rather than as an error.
                log.Debug(ex, "Currency count could not read {Container}.", type);
            }
        }

        return total;
    }

    private Dictionary<string, uint>? BuildIndex()
    {
        try
        {
            DateTime started = DateTime.UtcNow;
            var index = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            var items = data.GetExcelSheet<Item>();

            foreach (Item item in items)
            {
                var plural = item.Plural.ExtractText();

                if (string.IsNullOrWhiteSpace(plural))
                {
                    continue;
                }

                // First writer wins: the sheet has duplicates (a retainer-sold copy of the same token), and
                // the lower id is the one the shops use.
                _ = index.TryAdd(plural.Trim(), item.RowId);
            }

            // Information, once: the scan is the one cost this feature pays, and knowing what it costs is
            // the difference between a measured claim and a guess.
            log.Information(
                "Currency index built: {Count} plurals in {Elapsed} ms.",
                index.Count,
                (long)(DateTime.UtcNow - started).TotalMilliseconds);

            return index;
        }
        catch (Exception ex)
        {
            buildFailed = true;
            log.Error(ex, "Could not index item plurals, so token prices will show without progress.");
            return null;
        }
    }
}
