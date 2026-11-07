// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu

using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Data;
using Xunit;

namespace Questionable.Tests.AutoGen;

public sealed class SheetLookupTests(GameDataFixture fixture) : IClassFixture<GameDataFixture>
{
    [GameDataFact]
    public void CardRewards_KeepItemsAndOrder()
    {
        Assert.True(fixture.Available, "Game data is required for sheet lookup parity checks.");
        var excel = fixture.GameData!.Quests.Module;
        var cards = excel.GetSheet<TripleTriadCardResident>().ToArray();
        var items = excel.GetSheet<Item>().ToArray();
        var indexed = QuestData.CreateCardRewardLookup(cards, items);

        foreach (uint questId in cards.Select(card => card.Quest.RowId).Distinct())
        {
            var expected = cards.Where(card => card.Quest.RowId == questId)
                .Select(card => items.Where(item => item.ItemAction.Value.Action.RowId == 3357 &&
                                                   item.AdditionalData.RowId == card.RowId)
                    .Select(item => (Item?)item).FirstOrDefault())
                .Where(item => item.HasValue)
                .Select(item => item!.Value.RowId);
            Assert.Equal(expected, indexed[questId].Select(item => item.RowId));
        }
        Assert.Empty(indexed[uint.MaxValue]);
    }

    [GameDataFact]
    public void CustomDelivery_KeepsOldMappings()
    {
        Assert.True(fixture.Available, "Game data is required for sheet lookup parity checks.");
        var excel = fixture.GameData!.Quests.Module;
        var supplies = excel.GetSubrowSheet<SatisfactionSupply>();
        var npcs = excel.GetSheet<SatisfactionNpc>();
        var dataManager = Substitute.For<IDataManager>();
        dataManager.GetExcelSheet<GatheringItem>().Returns(excel.GetSheet<GatheringItem>());
        dataManager.GetExcelSheet<GatheringPointBase>().Returns(excel.GetSheet<GatheringPointBase>());
        dataManager.GetExcelSheet<SatisfactionNpc>().Returns(npcs);
        dataManager.GetSubrowExcelSheet<SatisfactionSupply>().Returns(supplies);
        var data = new GatheringData(dataManager, NullLogger<GatheringData>.Instance);

        var expected = npcs.Where(npc => npc.RowId > 0)
            .SelectMany(npc => supplies.Flatten()
                .Where(supply => supply.RowId == npc.SatisfactionNpcParams[^1].SupplyIndex)
                .Select(supply => (ItemId: supply.Item.RowId, NpcId: npc.Npc.RowId)))
            .Where(pair => pair.ItemId > 0).Distinct()
            .ToDictionary(pair => pair.ItemId, pair => pair.NpcId);

        foreach (var item in excel.GetSheet<Item>())
        {
            bool found = data.TryGetCustomDeliveryNpc(item.RowId, out uint npcId);
            Assert.Equal(expected.TryGetValue(item.RowId, out uint expectedNpcId), found);
            Assert.Equal(expectedNpcId, npcId);
        }
    }
}
