using Questionable.Functions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AlliedSocietyPoolsTests
{
    [Fact]
    public void RankUpDay_KeepsOldRankSet() =>
        Assert.Equal([(4, false), (5, true)], AlliedSocietyQuestFunctions.Pools(5, rankedUp: true));

    [Fact]
    public void NormalDay_UsesCurrentRank() =>
        Assert.Equal([(5, false)], AlliedSocietyQuestFunctions.Pools(5, rankedUp: false));

    [Fact]
    public void FirstRank_HasNoOldSet() =>
        Assert.Equal([(1, false)], AlliedSocietyQuestFunctions.Pools(1, rankedUp: true));
}
