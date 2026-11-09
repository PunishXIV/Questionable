using Questionable.Data;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestDataTests
{
    public static TheoryData<IReadOnlyList<ElementId>, ushort, ushort> SocietyLists => new()
    {
        { QuestData.PelupeluQuests, 5193, 5226 },
        { QuestData.MamoolJaQuests, 5255, 5288 },
        { QuestData.YokHuyQuests, 5330, 5363 },
    };

    [Theory]
    [MemberData(nameof(SocietyLists))]
    public void SocietyList_RunsStoryToLastDaily(IReadOnlyList<ElementId> quests, ushort first, ushort last)
    {
        Assert.Equal(34, quests.Count);
        Assert.Equal(first, quests[0].Value);
        Assert.Equal(last, quests[^1].Value);
    }
}
