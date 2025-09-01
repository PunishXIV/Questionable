using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Questionable.Model.Gathering;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Questionable.Data;

internal sealed class GatheringData
{
    private readonly Dictionary<uint, GatheringPointId> _minerGatheringPoints = [];
    private readonly Dictionary<uint, GatheringPointId> _botanistGatheringPoints = [];
    private readonly Dictionary<uint, ushort> _itemIdToCollectability;
    private readonly Dictionary<uint, uint> _npcForCustomDeliveries;

    public GatheringData(IDataManager dataManager)
    {
        Dictionary<uint, uint> gatheringItemToItem = dataManager.GetExcelSheet<GatheringItem>()
            .Where(x => x.RowId != 0 && x.Item.RowId != 0)
            .ToDictionary(x => x.RowId, x => x.Item.RowId);

        foreach (var gatheringPointBase in dataManager.GetExcelSheet<GatheringPointBase>())
        {
            foreach (var gatheringItem in gatheringPointBase.Item.Where(x => x.RowId != 0))
            {
                if (gatheringItemToItem.TryGetValue(gatheringItem.RowId, out uint itemId))
                {
                    if (gatheringPointBase.GatheringType.RowId is 0 or 1)
                        _minerGatheringPoints[itemId] = new GatheringPointId((ushort)gatheringPointBase.RowId);
                    else if (gatheringPointBase.GatheringType.RowId is 2 or 3)
                        _botanistGatheringPoints[itemId] = new GatheringPointId((ushort)gatheringPointBase.RowId);
                }
            }
        }

        _itemIdToCollectability = dataManager.GetSubrowExcelSheet<SatisfactionSupply>()
            .Flatten()
            .Where(x => x.RowId > 0)
            .Where(x => x.Slot is 2)
            .Select(x => new
            {
                ItemId = x.Item.RowId,
                Collectability = x.CollectabilityHigh,
            })
            .Distinct()
            .ToDictionary(x => x.ItemId, x => x.Collectability);

        _npcForCustomDeliveries = dataManager.GetExcelSheet<SatisfactionNpc>()
            .Where(x => x.RowId > 0)
            .SelectMany(x => dataManager.GetSubrowExcelSheet<SatisfactionSupply>()
                .Flatten()
                .Where(y => y.RowId == x.SatisfactionNpcParams.Last().SupplyIndex)
                .Select(y => new
                {
                    ItemId = y.Item.RowId,
                    NpcId = x.Npc.RowId
                }))
            .Where(x => x.ItemId > 0)
            .Distinct()
            .ToDictionary(x => x.ItemId, x => x.NpcId);
    }

    public IEnumerable<GatheringPointId> MinerGatheringPoints => _minerGatheringPoints.Values;
    public IEnumerable<GatheringPointId> BotanistGatheringPoints => _botanistGatheringPoints.Values;

    public bool TryGetMinerGatheringPointByItemId(uint itemId, [NotNullWhen(true)] out GatheringPointId? gatheringPointId)
        => _minerGatheringPoints.TryGetValue(itemId, out gatheringPointId);

    public bool TryGetBotanistGatheringPointByItemId(uint itemId, [NotNullWhen(true)] out GatheringPointId? gatheringPointId)
        => _botanistGatheringPoints.TryGetValue(itemId, out gatheringPointId);

    public ushort GetRecommendedCollectability(uint itemId)
        => _itemIdToCollectability.GetValueOrDefault(itemId);

    public bool TryGetCustomDeliveryNpc(uint itemId, out uint npcId)
        => _npcForCustomDeliveries.TryGetValue(itemId, out npcId);
}
