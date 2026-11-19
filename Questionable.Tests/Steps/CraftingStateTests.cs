using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.Controller.Steps;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CraftingStateTests
{
    private readonly TestClock _clock = new();
    private readonly CraftingState _state;

    public CraftingStateTests() => _state = new CraftingState(_clock);

    [Fact]
    public void StartedCraft_BlocksSecondStart()
    {
        Guid id = _state.BeginRequest();

        Assert.Throws<TaskException>(() => _state.BeginRequest());
        Assert.Equal(id, _state.Id);
        Assert.False(_state.TryComplete(id));
        Assert.False(_state.TryConfirmIdle());
    }

    [Fact]
    public void CompletedCraft_WaitsForFreshIdle()
    {
        Guid id = _state.BeginRequest();
        _state.ObserveStatus(ArtisanStatus.Busy);
        Assert.True(_state.CanContinue(id));
        Assert.False(_state.TryComplete(id));

        ObserveStableIdle();
        Assert.True(_state.HasOperation); // status alone never releases ownership
        Assert.False(_state.TryComplete(Guid.NewGuid()));
        Assert.False(_state.TryConfirmIdle()); // a live executor cannot be acknowledged away
        Assert.True(_state.TryComplete(id));
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void AbandonedCraft_WaitsForConfirmation()
    {
        Guid id = _state.BeginRequest();
        _state.Abandon(id);
        _state.Abandon(id);
        ObserveStableIdle();

        Assert.True(_state.HasOperation);
        Assert.True(_state.CanConfirmIdle);
        Assert.False(_state.CanContinue(id));
        Assert.False(_state.TryComplete(id));
        Assert.Throws<TaskException>(() => _state.BeginRequest());
        Assert.True(_state.TryConfirmIdle());
        Assert.False(_state.HasOperation);
        Assert.False(_state.TryConfirmIdle());
    }

    [Theory]
    [InlineData((int)ArtisanStatus.Busy)]
    public void Confirmation_RejectsBusyOrMissing(int status)
    {
        _state.BeginRequest();
        _state.Abandon();
        ObserveStableIdle();
        _state.ObserveStatus((ArtisanStatus)status);
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(_state.TryConfirmIdle());
        Assert.True(_state.HasOperation);
    }

    [Fact]
    public void ProviderLoss_RequiresConfirmation()
    {
        Guid id = _state.BeginRequest();
        ObserveStableIdle();
        Assert.True(_state.CanContinue(id));

        _state.ObserveStatus(ArtisanStatus.Unavailable);

        Assert.True(_state.NeedsConfirmation);
        Assert.Equal(id, _state.Id);
        Assert.False(_state.CanContinue(id));
        Assert.False(_state.TryComplete(id));
        Assert.False(_state.TryConfirmIdle());
        Assert.Throws<TaskException>(() => _state.BeginRequest());
    }

    private void ObserveStableIdle()
    {
        _state.ObserveStatus(ArtisanStatus.Idle);
        for (int i = 0; i < 4; ++i)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _state.ObserveStatus(ArtisanStatus.Idle);
        }
    }
}
