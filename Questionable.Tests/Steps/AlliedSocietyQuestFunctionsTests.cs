using Questionable.Functions;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AlliedSocietyQuestFunctionsTests
{
    private static EAlliedSocietyRank? Highest(EAlliedSocietyRank current, params EAlliedSocietyRank[] ranks) =>
        AlliedSocietyQuestFunctions.GetHighestRank(ranks, current);

    [Fact]
    public void HighestRank_IsCurrentRank() =>
        Assert.Equal(EAlliedSocietyRank.Friendly, Highest(EAlliedSocietyRank.Friendly,
            EAlliedSocietyRank.Neutral, EAlliedSocietyRank.Friendly, EAlliedSocietyRank.Recognized));

    [Fact]
    public void HighestRank_SkipsRanksAboveYours() =>
        Assert.Equal(EAlliedSocietyRank.Recognized, Highest(EAlliedSocietyRank.Trusted,
            EAlliedSocietyRank.Neutral, EAlliedSocietyRank.Recognized, EAlliedSocietyRank.Sworn));

    [Fact]
    public void HighestRank_IgnoresStoryQuests()
    {
        Assert.Equal(EAlliedSocietyRank.Neutral, Highest(EAlliedSocietyRank.Allied,
            EAlliedSocietyRank.None, EAlliedSocietyRank.Neutral));
        Assert.Null(Highest(EAlliedSocietyRank.Allied, EAlliedSocietyRank.None));
    }

    [Fact]
    public void HighestRank_NoneBeforeJoining() =>
        Assert.Null(Highest(EAlliedSocietyRank.None, EAlliedSocietyRank.Neutral));
}
