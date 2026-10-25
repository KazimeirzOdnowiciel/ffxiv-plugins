using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SlotYield.Game;

/// <summary>
/// Drives the retainer sell dialog the way Marketbuddy does: the price lives in the numeric input at
/// NodeList[15], and confirming is a synthesized CHANGE event aimed at the dialog's Confirm button.
///
/// Two separate calls on purpose - set the value, look at it, then confirm - so a wrong number is
/// visible before anything is committed.
/// </summary>
public static unsafe class AddonInteraction
{
    private const int PriceNodeIndex = 15;
    private const int ExpectedNodeCount = 23;

    public static bool IsOpen() =>
        Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSell", 1) is not null;

    public static string SetPrice(int price)
    {
        try
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSell", 1);

            if (addon is null)
                return "RetainerSell is not open";

            var nodeCount = addon->UldManager.NodeListCount;

            if (nodeCount != ExpectedNodeCount)
                return $"unexpected RetainerSell layout ({nodeCount} nodes, expected {ExpectedNodeCount}); not touching it";

            var node = addon->UldManager.NodeList[PriceNodeIndex];
            if (node is null)
                return "price node missing";

            var component = (AtkComponentNumericInput*)node->GetComponent();
            if (component is null)
                return "price node has no component";

            var before = component->Value;
            component->SetValue(price);
            var after = component->Value;

            return $"price component {before} -> {after} (wanted {price}); not confirmed";
        }
        catch (Exception ex)
        {
            return $"set price failed: {ex.Message}";
        }
    }

    /// <summary>Sets the price and reports back what the component actually accepted.</summary>
    public static int ReadPrice()
    {
        try
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSell", 1);

            if (addon is null || addon->UldManager.NodeListCount != ExpectedNodeCount)
                return 0;

            var node = addon->UldManager.NodeList[PriceNodeIndex];
            if (node is null)
                return 0;

            var component = (AtkComponentNumericInput*)node->GetComponent();
            return component is null ? 0 : component->Value;
        }
        catch
        {
            return 0;
        }
    }

    public static bool TrySetPrice(int price, out int actual, out string message)
    {
        actual = 0;

        try
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSell", 1);

            if (addon is null)
            {
                message = "dialog is not open";
                return false;
            }

            if (addon->UldManager.NodeListCount != ExpectedNodeCount)
            {
                message = $"unexpected layout ({addon->UldManager.NodeListCount} nodes)";
                return false;
            }

            var node = addon->UldManager.NodeList[PriceNodeIndex];
            if (node is null)
            {
                message = "price node missing";
                return false;
            }

            var component = (AtkComponentNumericInput*)node->GetComponent();
            if (component is null)
            {
                message = "price component missing";
                return false;
            }

            component->SetValue(price);
            actual = component->Value;
            message = $"price {actual}";
            return actual == price;
        }
        catch (Exception ex)
        {
            message = $"set price failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Presses Confirm on the open sell dialog. This is the step that commits the listing.</summary>
    public static string Confirm()
    {
        try
        {
            var addon = Plugin.GameGui.GetAddonByName<AddonRetainerSell>("RetainerSell", 1);

            if (addon is null)
                return "RetainerSell is not open";

            if (addon->AtkUnitBase.UldManager.NodeListCount != ExpectedNodeCount)
                return "unexpected RetainerSell layout; not confirming";

            // Marketbuddy sends event 25 ("CHANGE"); ClientStructs names that slot ButtonClick in its
            // own enum mapping, so the raw value is cast rather than using a name.
            SendClick(&addon->AtkUnitBase, (AtkEventType)25, 21, addon->Confirm);
            return "confirm sent";
        }
        catch (Exception ex)
        {
            return $"confirm failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Mirrors Marketbuddy's SendClick: an AtkEvent struct with the target at +0x8 and the addon at
    /// +0x10, handed to the addon's own ReceiveEvent.
    /// </summary>
    private static void SendClick(AtkUnitBase* addon, AtkEventType type, uint param, void* target)
    {
        var eventData = stackalloc byte[0x40];
        var inputData = stackalloc byte[0x40];

        new Span<byte>(eventData, 0x40).Clear();
        new Span<byte>(inputData, 0x40).Clear();

        *(nint*)(eventData + 0x8) = (nint)target;
        *(nint*)(eventData + 0x10) = (nint)addon;

        ((AtkEventListener*)addon)->ReceiveEvent(type, (int)param, (AtkEvent*)eventData, (AtkEventData*)inputData);
    }
}
