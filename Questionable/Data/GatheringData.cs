using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
namespace Questionable.Data;

[RegisterSingleton]
internal sealed class GatheringData
{
    private readonly HashSet<uint> _botanistItems = [];
    private readonly Dictionary<uint, ushort> _itemIdToCollectability;
    private readonly HashSet<uint> _minerItems = [];
    private readonly Dictionary<uint, uint> _npcForCustomDeliveries;

    public GatheringData(IDataManager dataManager, ILogger<GatheringData> logger)
    {
        Dictionary<uint, uint> gatheringItemToItem = dataManager.GetExcelSheet<GatheringItem>()
            .Where(x => x.RowId != 0 && x.Item.RowId != 0)
            .ToDictionary(x => x.RowId, x => x.Item.RowId);

        foreach (GatheringPointBase gatheringPointBase in dataManager.GetExcelSheet<GatheringPointBase>())
        {
            foreach (RowRef gatheringItem in gatheringPointBase.Item.Where(x => x.RowId != 0))
            {
                if (gatheringItemToItem.TryGetValue(gatheringItem.RowId, out uint itemId))
                {
                    if (gatheringPointBase.GatheringType.RowId is 0 or 1)
                        _minerItems.Add(itemId);
                    else if (gatheringPointBase.GatheringType.RowId is 2 or 3)
                        _botanistItems.Add(itemId);
                }
            }
        }
        logger.LogDebug($"Loaded {_minerItems.Count + _botanistItems.Count} gathering items ({_minerItems.Count} + {_botanistItems.Count})");

        _itemIdToCollectability = dataManager.GetSubrowExcelSheet<SatisfactionSupply>()
            .Flatten()
            .Where(x => x.RowId > 0 && x.Slot is 2)
            .Select(x => new
            {
                ItemId = x.Item.RowId,
                Collectability = x.CollectabilityHigh
            })
            .Distinct()
            .ToDictionary(x => x.ItemId, x => x.Collectability);

        _npcForCustomDeliveries = dataManager.GetExcelSheet<SatisfactionNpc>()
            .Where(x => x.RowId > 0)
            .SelectMany(x => dataManager.GetSubrowExcelSheet<SatisfactionSupply>()
                .Flatten()
                .Where(y => y.RowId == x.SatisfactionNpcParams[^1].SupplyIndex)
                .Select(y => new
                {
                    ItemId = y.Item.RowId,
                    NpcId = x.Npc.RowId
                }))
            .Where(x => x.ItemId > 0)
            .Distinct()
            .ToDictionary(x => x.ItemId, x => x.NpcId);
    }

    public bool IsGatherableBy(uint itemId, Job classJob) => classJob switch
    {
        Job.MIN => _minerItems.Contains(itemId),
        Job.BTN => _botanistItems.Contains(itemId),
        var _ => false
    };

    public ushort GetRecommendedCollectability(uint itemId) => _itemIdToCollectability.GetValueOrDefault(itemId);

    public bool TryGetCustomDeliveryNpc(uint itemId, out uint npcId) => _npcForCustomDeliveries.TryGetValue(itemId, out npcId);
}
