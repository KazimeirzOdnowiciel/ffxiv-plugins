using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace SlotYield.Game;

/// <summary>What a stock read found, split by where it came from.</summary>
public sealed record StockSnapshot(
    Dictionary<uint, int> Items,
    int BagStacks,
    int SaddlebagStacks,
    int RetainerStacks,
    string RetainerName);

/// <summary>
/// Reads the player's bags, crystal pouch, saddlebag, and the inventory of whichever retainer is
/// currently open. Read-only and wrapped: anything odd is skipped rather than guessed at.
///
/// Only the active retainer's pages are in memory - the client loads a retainer's inventory when you
/// open that retainer - so that is the one this can see.
/// </summary>
public static unsafe class PlayerInventory
{
    private static readonly InventoryType[] Bags =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    private static readonly InventoryType[] Saddlebags =
    {
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
    };

    private static readonly InventoryType[] RetainerPages =
    {
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
    };

    /// <summary>Every filled slot in the bags and crystal pouch, for listing automation.</summary>
    public static List<(InventoryType Type, int Slot, uint ItemId, int Quantity, bool Hq)> Slots()
    {
        var result = new List<(InventoryType, int, uint, int, bool)>(256);

        try
        {
            var manager = InventoryManager.Instance();
            if (manager is null)
                return result;

            foreach (var type in Bags)
            {
                var container = manager->GetInventoryContainer(type);
                if (container is null || !container->IsLoaded)
                    continue;

                for (var i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item is null)
                        continue;

                    var itemId = item->GetBaseItemId();
                    var quantity = (int)item->GetQuantity();

                    if (itemId == 0 || quantity <= 0)
                        continue;

                    result.Add((type, i, itemId, quantity, ((uint)item->Flags & 1u) != 0));
                }
            }
        }
        catch
        {
            // not logged in
        }

        return result;
    }

    /// <summary>Gil currently on hand, or 0 when the currency container is not loaded.</summary>
    public static long Gil()
    {
        try
        {
            var manager = InventoryManager.Instance();
            var container = manager is null ? null : manager->GetInventoryContainer(InventoryType.Currency);

            if (container is null || !container->IsLoaded)
                return 0;

            var slot = container->GetInventorySlot(0);
            return slot is null ? 0 : slot->GetQuantity();
        }
        catch
        {
            return 0;
        }
    }

    public static StockSnapshot Read()
    {
        var items = new Dictionary<uint, int>(384);

        var bags = Collect(Bags, items) + Collect(new[] { InventoryType.Crystals }, items);
        var saddlebags = Collect(Saddlebags, items);

        var retainerStacks = 0;
        var retainerName = string.Empty;

        try
        {
            var manager = RetainerManager.Instance();
            var active = manager is null || !manager->IsReady ? null : manager->GetActiveRetainer();

            if (active is not null && active->RetainerId != 0)
            {
                retainerName = active->NameString;
                retainerStacks = Collect(RetainerPages, items) +
                                 Collect(new[] { InventoryType.RetainerCrystals }, items);
            }
        }
        catch
        {
            // No retainer open, or the manager is mid-load.
        }

        return new StockSnapshot(items, bags, saddlebags, retainerStacks, retainerName);
    }

    private static int Collect(IReadOnlyList<InventoryType> types, Dictionary<uint, int> items)
    {
        var stacks = 0;

        try
        {
            var manager = InventoryManager.Instance();
            if (manager is null)
                return 0;

            foreach (var type in types)
            {
                var container = manager->GetInventoryContainer(type);
                if (container is null || !container->IsLoaded)
                    continue;

                for (var i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item is null)
                        continue;

                    var itemId = item->GetBaseItemId();
                    var quantity = (int)item->GetQuantity();

                    if (itemId == 0 || quantity <= 0)
                        continue;

                    items[itemId] = items.GetValueOrDefault(itemId) + quantity;
                    stacks++;
                }
            }
        }
        catch
        {
            // A container being rebuilt mid-read.
        }

        return stacks;
    }
}
