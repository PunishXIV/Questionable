using Lumina.Excel.Sheets;
namespace Questionable.Data;

[RegisterSingleton]
internal sealed class GilShopData(IDataManager dataManager)
{
    private readonly Lazy<HashSet<uint>> _itemIds = new(() => dataManager.GetSubrowExcelSheet<GilShopItem>()
        .Flatten()
        .Where(x => x.Item.RowId != 0)
        .Select(x => x.Item.RowId)
        .ToHashSet());

    public bool IsSoldForGil(uint itemId) => _itemIds.Value.Contains(itemId);
}
