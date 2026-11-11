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

    private static bool ShouldRestock(bool enabled = true, bool unlocked = true, bool townOrOpenWorld = true,
        int greens = 0, bool canBuy = true) =>
        SummonChocobo.ShouldRestock(enabled, unlocked, townOrOpenWorld, greens, canBuy);

    [Fact]
    public void OutOfGreens_Restocks() => Assert.True(ShouldRestock());

    [Fact]
    public void GreensLeft_NoRestock() => Assert.False(ShouldRestock(greens: 1));

    [Fact]
    public void CannotBuy_NoRestock() => Assert.False(ShouldRestock(canBuy: false));

    [Fact]
    public void InDuty_NoRestock() => Assert.False(ShouldRestock(townOrOpenWorld: false));

    [Fact]
    public void RestockNeedsSettingAndQuest()
    {
        Assert.False(ShouldRestock(enabled: false));
        Assert.False(ShouldRestock(unlocked: false));
    }
}
