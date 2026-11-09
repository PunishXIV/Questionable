using Questionable.Domain;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestInfoTests
{
    [Theory]
    [InlineData(400, 65, 4, 1_040u)]     // To the Bannock, level 4
    [InlineData(400, 300, 39, 46_800u)]  // Coming to Ishgard, level 50
    [InlineData(0, 1680, 168, 0u)]       // level 100 quests give no XP
    public void Experience_ScalesWithQuestLevel(int factor, int scaled, int modifier, uint expected) =>
        Assert.Equal(expected, QuestInfo.ScaleExperience((ushort)factor, (ushort)scaled, (byte)modifier));
}
