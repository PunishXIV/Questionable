// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Lumina.Excel.Sheets;
namespace Questionable.Data;

/// <summary>
///     Which items are sold by regular gil vendors, i.e. which purchases Knightshopper can make with gil.
/// </summary>
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
