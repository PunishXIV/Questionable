using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Questionable.Controller.Steps.Interactions;

internal static class EquipmentSlots
{
    public static List<ushort>? For(Item item) =>
        item.EquipSlotCategory.RowId switch
        {
            >= 1 and <= 11 => [(ushort)(item.EquipSlotCategory.RowId - 1)],
            12 => [11, 12], // rings
            13 => [0],
            17 => [13], // soul crystal
            _ => null
        };

    public static unsafe bool IsEquipped(InventoryManager* inventoryManager, List<ushort> slots, uint itemId)
    {
        foreach (ushort slot in slots)
        {
            InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, slot);
            if (itemSlot != null && itemSlot->ItemId == itemId)
                return true;
        }

        return false;
    }

    public static bool IsArmoryFullToast(IDataManager dataManager, SeString message) =>
        GameFunctions.GameStringEquals(message.TextValue,
            DataManagerAdapter.GetString<LogMessage>(dataManager, 709, x => x.Text));

    public static unsafe bool TryFindFirstEmptySlot(InventoryContainer* container, out ushort slot)
    {
        for (ushort i = 0; i < container->Size; i++)
        {
            InventoryItem* itemSlot = container->GetInventorySlot(i);
            if (itemSlot == null || itemSlot->ItemId == 0)
            {
                slot = i;
                return true;
            }
        }

        slot = 0;
        return false;
    }
}
