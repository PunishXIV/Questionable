using Lumina.Excel.Sheets;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.AutoGen;

public sealed class SheetLookupTests(GameDataFixture fixture) : IClassFixture<GameDataFixture>
{
    [GameDataFact]
    public void ItemReward_ToStringGivesTypeAndName()
    {
        Assert.True(fixture.Available, "Game data is required for sheet lookup parity checks.");
        var mountItem = fixture.GameData!.Quests.Module.GetSheet<Item>()
            .First(item => item.ItemAction.Value.Action.RowId == 1322);

        var reward = ItemReward.Create(mountItem, new QuestId(1));

        Assert.NotNull(reward);
        Assert.Equal($"Mount: {reward.Name}", reward.ToString());
    }

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
}
