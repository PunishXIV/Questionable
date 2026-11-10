using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Common;
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

    [Fact]
    public void TrialWithSupport_StaysSynced() =>
        Assert.False(Duty.CanUnsync(EContentType.Trials, hasSupport: true));

    [Fact]
    public void TrialWithoutSupport_CanUnsync() =>
        Assert.True(Duty.CanUnsync(EContentType.Trials, hasSupport: false));

    [Fact]
    public void DungeonWithSupport_CanUnsync() =>
        Assert.True(Duty.CanUnsync(EContentType.Dungeons, hasSupport: true));
}
