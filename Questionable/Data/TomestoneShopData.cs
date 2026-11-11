using Lumina.Excel.Sheets;
namespace Questionable.Data;

[RegisterSingleton]
internal sealed class TomestoneShopData(IDataManager dataManager)
{
    // Shops of this type name a tomestone by its row in the Tomestones sheet, not by its item id.
    private const int TomestoneShopType = 4;

    private readonly Lazy<HashSet<uint>> _tomestoneRows = new(() =>
    [
        .. dataManager.GetExcelSheet<TomestonesItem>().Select(x => x.Tomestones.RowId).Where(x => x != 0)
    ]);

    public bool IsSoldForTomestones(uint shopId, uint itemId)
    {
        if (dataManager.GetExcelSheet<SpecialShop>().GetRowOrDefault(shopId) is not { } shop)
            return false;

        return shop.Item.Any(entry =>
            entry.ReceiveItems.Any(x => x.Item.RowId == itemId) &&
            entry.ItemCosts.Any(x => IsTomestoneCost(x.ItemCost.RowId, shop.UseCurrencyType, _tomestoneRows.Value)));
    }

    internal static bool IsTomestoneCost(uint costId, int shopType, IReadOnlySet<uint> tomestoneRows) =>
        shopType == TomestoneShopType && tomestoneRows.Contains(costId);
}
