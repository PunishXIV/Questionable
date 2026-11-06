using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class EquipRecommendedTests
{
    private readonly TestClock _clock = new();
    private readonly ICondition _condition = Substitute.For<ICondition>();
    private readonly Configuration _configuration = new();
    private readonly EquipRecommended.DoEquipRecommended _executor;
    private int _requests;

    public EquipRecommendedTests()
    {
        _configuration.General.GearsetUpdateSource = EGearsetUpdateSource.Stylist;
        _executor = new(null!, _condition, _configuration, null!,
            NullLogger<EquipRecommended.DoEquipRecommended>.Instance, _clock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BusyStylist_TimesOutWithoutNewRequest(bool submitted)
    {
        if (submitted)
            Assert.Equal(ETaskResult.StillRunning, Update(busy: false));

        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        _clock.Advance(TimeSpan.FromMilliseconds(29999));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var error = Assert.Throws<TaskException>(() => Update(busy: true));

        Assert.Contains("Stylist stayed busy for 30 seconds", error.Message);
        Assert.Contains("restart Questionable", error.Message);
        Assert.Equal(submitted ? 1 : 0, _requests);
        Assert.Equal(EGearsetUpdateSource.Stylist, _configuration.General.GearsetUpdateSource);
    }

    [Fact]
    public void InitialBusyWait_DoesNotShortenRequestTimeout()
    {
        Update(busy: true);
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: false));
        Assert.Equal(1, _requests);

        Update(busy: true);
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<TaskException>(() => Update(busy: true));
        Assert.Equal(1, _requests);
    }

    [Fact]
    public void Restart_ClearsBusyWaitAndRequest()
    {
        Update(busy: false);
        Update(busy: true);
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Throws<TaskException>(() => Update(busy: true));

        // Exercise Start's reset without calling native gear modules.
        _condition[ConditionFlag.InCombat].Returns(true);
        Assert.False(_executor.Start(new EquipRecommended.EquipTask()));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: false));
        Assert.Equal(2, _requests);
    }

    private ETaskResult Update(bool busy) => _executor.UpdateStylist(busy, () =>
    {
        ++_requests;
        return true;
    });
}
