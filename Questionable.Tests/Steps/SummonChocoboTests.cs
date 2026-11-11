using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class SummonChocoboTests
{
    private static bool ShouldSummon(bool enabled = true, bool unlocked = true, bool openWorld = true,
        float timeLeft = 0, int greens = 5) =>
        SummonChocobo.ShouldSummon(enabled, unlocked, openWorld, timeLeft, greens);

    [Fact]
    public void ChocoboAway_IsCalled() => Assert.True(ShouldSummon());

    [Fact]
    public void LowTimer_IsToppedUp() => Assert.True(ShouldSummon(timeLeft: SummonChocobo.TopUpBelowSeconds - 1));

    [Fact]
    public void EnoughTimeLeft_IsLeftAlone() => Assert.False(ShouldSummon(timeLeft: SummonChocobo.TopUpBelowSeconds));

    [Fact]
    public void SettingOff_NeverCalls() => Assert.False(ShouldSummon(enabled: false));

    [Fact]
    public void BeforeUnlockQuest_NeverCalls() => Assert.False(ShouldSummon(unlocked: false));

    [Fact]
    public void TownOrDuty_NeverCalls() => Assert.False(ShouldSummon(openWorld: false));

    [Fact]
    public void NoGreens_NeverCalls() => Assert.False(ShouldSummon(greens: 0));
}
