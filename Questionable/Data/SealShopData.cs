using Lumina.Excel.Sheets;
namespace Questionable.Data;

[RegisterSingleton]
internal sealed class SealShopData(IDataManager dataManager)
{
    private readonly Lazy<HashSet<uint>> _itemIds = new(() => dataManager.GetSubrowExcelSheet<GCScripShopItem>()
        .Flatten()
        .Where(x => x.Item.RowId != 0)
        .Select(x => x.Item.RowId)
        .ToHashSet());

    public bool IsSoldForSeals(uint itemId) => _itemIds.Value.Contains(itemId);
}
