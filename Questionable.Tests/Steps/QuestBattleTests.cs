using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using NSubstitute;
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

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void StartWait_InterruptsOnlyOutside(bool inDuty, bool expected)
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.BoundByDuty].Returns(inDuty);
        Assert.Equal(expected, new QuestBattle.StartExecutor(condition).ShouldInterruptOnDamage());
    }
}
