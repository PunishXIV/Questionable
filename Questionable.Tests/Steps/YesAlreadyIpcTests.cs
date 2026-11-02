using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class YesAlreadyIpcTests
{
    [Fact]
    public void EnabledAtStartupIsTurnedOffForTheRunAndBackOnAfterwards()
    {
        var (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest: true, isEnabled: true, disabledByQuestionable: false);
        Assert.Equal(false, set);
        Assert.True(owned);

        (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest: true, isEnabled: false, disabledByQuestionable: owned);
        Assert.Null(set);
        Assert.True(owned);

        (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest: false, isEnabled: false, disabledByQuestionable: owned);
        Assert.Equal(true, set);
        Assert.False(owned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisabledByUserIsNeverTurnedOn(bool hasActiveQuest)
    {
        var (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest, isEnabled: false, disabledByQuestionable: false);
        Assert.Null(set);
        Assert.False(owned);
    }

    [Fact]
    public void ReEnabledByUserMidRunIsLeftAloneAndOwnershipEndsWithTheRun()
    {
        var (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest: true, isEnabled: true, disabledByQuestionable: true);
        Assert.Null(set);
        Assert.True(owned);

        (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest: false, isEnabled: true, disabledByQuestionable: owned);
        Assert.Null(set);
        Assert.False(owned);

        // The next run suppresses it again.
        (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest: true, isEnabled: true, disabledByQuestionable: owned);
        Assert.Equal(false, set);
        Assert.True(owned);
    }

    [Fact]
    public void IdleWithoutOwnershipDoesNothing()
    {
        var (set, owned) = YesAlreadyIpc.DecideToggle(hasActiveQuest: false, isEnabled: true, disabledByQuestionable: false);
        Assert.Null(set);
        Assert.False(owned);
    }
}
