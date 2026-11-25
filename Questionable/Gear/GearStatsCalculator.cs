using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
namespace Questionable.Gear;

[RegisterSingleton]
public sealed class GearStatsCalculator
{
    private static readonly uint[] CanHaveOffhand = [2, 6, 8, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32];
    private readonly ExcelSheet<Item> _itemSheet;

    public GearStatsCalculator(IDataManager dataManager)
    {
        ArgumentNullException.ThrowIfNull(dataManager);
        _itemSheet = dataManager.GetExcelSheet<Item>();
    }

    public unsafe short CalculateAverageItemLevel(InventoryContainer* container)
    {
        uint sum = 0U;
        int calculatedSlots = 12;
        for (int i = 0; i < 13; i++)
        {
            if (i == 5)
                continue;

            InventoryItem* inventoryItem = container->GetInventorySlot(i);
            if (inventoryItem == null || inventoryItem->ItemId == 0)
                continue;

            Item? item = _itemSheet.GetRowOrDefault(inventoryItem->ItemId);
            if (item == null)
                continue;

            if (item.Value.ItemUICategory.RowId == 105)
            {
                if (i == 0)
                    calculatedSlots -= 1;
                calculatedSlots -= 1;
                continue;
            }

            if (i == 0 && !CanHaveOffhand.Contains(item.Value.ItemUICategory.RowId))
            {
                sum += item.Value.LevelItem.RowId;
                i++;
            }

            sum += item.Value.LevelItem.RowId;
        }

        return (short)(sum / calculatedSlots);
    }
}
