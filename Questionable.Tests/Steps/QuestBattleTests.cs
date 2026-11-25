using Questionable.Controller.Steps.Interactions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestBattleTests
{
    [Fact]
    public void RetryOff_StopsOnFirstFailure() => Assert.Equal(1, QuestBattle.AttemptLimit(retry: false, maxAttempts: 5));

    [Fact]
    public void RetryOn_UsesTheSetting() => Assert.Equal(5, QuestBattle.AttemptLimit(retry: true, maxAttempts: 5));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(99, 10)]
    public void RetryOn_ClampsTheSetting(int saved, int expected) =>
        Assert.Equal(expected, QuestBattle.AttemptLimit(retry: true, maxAttempts: saved));
}
