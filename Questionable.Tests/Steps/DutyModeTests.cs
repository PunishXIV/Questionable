using Questionable.Controller.Steps.Interactions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class DutyModeTests
{
    [Fact]
    public void SupportWithoutSupport_NeedsDutyFinder() =>
        Assert.True(Duty.NeedsDutyFinder(Duty.Mode.Support, hasSupport: false));

    [Fact]
    public void SupportWithSupport_RunsInAutoDuty() =>
        Assert.False(Duty.NeedsDutyFinder(Duty.Mode.Support, hasSupport: true));

    [Fact]
    public void UnsyncedWithoutSupport_RunsInAutoDuty() =>
        Assert.False(Duty.NeedsDutyFinder(Duty.Mode.Regular, hasSupport: false));

    [Fact]
    public void VariantWithoutSupport_RunsInAutoDuty() =>
        Assert.False(Duty.NeedsDutyFinder(Duty.Mode.Variant, hasSupport: false));

    [Theory]
    [InlineData(65, 50, 15, true)]
    [InlineData(64, 50, 15, false)]
    [InlineData(60, 50, 10, true)]
    [InlineData(59, 50, 10, false)]
    [InlineData(100, 90, 15, false)]
    public void LevelGap_DecidesUnsync(int level, int syncLevel, int gap, bool expected) =>
        Assert.Equal(expected, Duty.MeetsLevelGap(level, syncLevel, gap));

    [Theory]
    [InlineData(60, 0, true)]
    [InlineData(59, 0, false)]
    [InlineData(80, 99, true)]
    [InlineData(79, 99, false)]
    public void SavedGap_IsClamped(int level, int gap, bool expected) =>
        Assert.Equal(expected, Duty.MeetsLevelGap(level, syncLevel: 50, gap));
}
