using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.Controller.Steps;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CraftingOperationStateTests
{
    private readonly TestClock _clock = new();
    private readonly CraftingOperationState _state;

    public CraftingOperationStateTests() => _state = new CraftingOperationState(_clock);

    [Fact]
    public void StartedCraft_BlocksSecondStart()
    {
        Guid id = _state.BeginSubmission();

        Assert.Throws<TaskException>(() => _state.BeginSubmission());
        Assert.Equal(id, _state.OperationId);
        Assert.False(_state.TryComplete(id));
        Assert.False(_state.TryConfirmIdle());
    }

    [Fact]
    public void CompletedCraft_WaitsForFreshIdle()
    {
        Guid id = _state.BeginSubmission();
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
        Guid id = _state.BeginSubmission();
        _state.Abandon(id);
        _state.Abandon(id);
        ObserveStableIdle();

        Assert.True(_state.HasOperation);
        Assert.True(_state.CanConfirmIdle);
        Assert.False(_state.CanContinue(id));
        Assert.False(_state.TryComplete(id));
        Assert.Throws<TaskException>(() => _state.BeginSubmission());
        Assert.True(_state.TryConfirmIdle());
        Assert.False(_state.HasOperation);
        Assert.False(_state.TryConfirmIdle());
    }

    [Fact]
    public void UncertainCraftStart_WaitsForConfirmation()
    {
        Guid id = _state.BeginSubmission();
        _state.MarkUncertain(id);
        ObserveStableIdle();

        Assert.True(_state.RequiresReconciliation);
        Assert.False(_state.TryComplete(id));
        Assert.True(_state.HasOperation);
    }

    [Theory]
    [InlineData((int)ArtisanStatus.Busy)]
    [InlineData((int)ArtisanStatus.Queued)]
    [InlineData((int)ArtisanStatus.Paused)]
    [InlineData((int)ArtisanStatus.Unavailable)]
    public void Confirmation_RejectsBusyOrUnavailableCraft(int status)
    {
        _state.BeginSubmission();
        _state.AbandonCurrent();
        ObserveStableIdle();
        _state.ObserveStatus((ArtisanStatus)status);
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(_state.TryConfirmIdle());
        Assert.True(_state.HasOperation);
    }

    [Fact]
    public void ProviderLoss_RequiresConfirmation()
    {
        Guid id = _state.BeginSubmission();
        ObserveStableIdle();
        Assert.True(_state.CanContinue(id));

        _state.ObserveStatus(ArtisanStatus.Unavailable);

        Assert.True(_state.RequiresReconciliation);
        Assert.Equal(id, _state.OperationId);
        Assert.False(_state.CanContinue(id));
        Assert.False(_state.TryComplete(id));
        Assert.False(_state.TryConfirmIdle());
        Assert.Throws<TaskException>(() => _state.BeginSubmission());
    }

    [Fact]
    public void ProviderLossAfterIdle_RequiresConfirmation()
    {
        Guid id = _state.BeginSubmission();
        _state.ObserveStatus(ArtisanStatus.Busy);
        _state.ObserveStatus(ArtisanStatus.Unavailable);
        _state.ObserveStatus(ArtisanStatus.Busy);
        Assert.True(_state.RequiresReconciliation);
        Assert.False(_state.CanContinue(id));
        Assert.False(_state.TryConfirmIdle());
        ObserveStableIdle();

        Assert.True(_state.RequiresReconciliation);
        Assert.False(_state.CanContinue(id));
        Assert.False(_state.TryComplete(id));
        Assert.Equal(id, _state.OperationId);
        Assert.True(_state.TryConfirmIdle());
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void StaleIdleSnapshot_CannotAuthorizeConfirmationOrCompletion()
    {
        Guid id = _state.BeginSubmission();
        ObserveStableIdle();
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(_state.TryComplete(id));
        _state.ObserveStatus(ArtisanStatus.Idle);
        Assert.False(_state.TryComplete(id));
        _state.AbandonCurrent();
        Assert.False(_state.TryConfirmIdle());

        ObserveStableIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void BriefIdleBeforeQueuedWork_DoesNotEnableConfirmation()
    {
        _state.BeginSubmission();
        _state.AbandonCurrent();
        _state.ObserveStatus(ArtisanStatus.Idle);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(_state.TryConfirmIdle());
        _state.ObserveStatus(ArtisanStatus.Queued);
        _clock.Advance(TimeSpan.FromSeconds(4));
        _state.ObserveStatus(ArtisanStatus.Idle);
        Assert.False(_state.TryConfirmIdle());
        ObserveStableIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void LateOldExecutor_CannotAbandonOrCompleteANewOperation()
    {
        Guid oldId = _state.BeginSubmission();
        _state.Abandon(oldId);
        ObserveStableIdle();
        Assert.True(_state.TryConfirmIdle());
        Guid newId = _state.BeginSubmission();

        _state.Abandon(oldId);
        _state.MarkUncertain(oldId);
        ObserveStableIdle();
        Assert.False(_state.TryComplete(oldId));
        Assert.False(_state.RequiresReconciliation);
        Assert.Equal(newId, _state.OperationId);
        Assert.True(_state.TryComplete(newId));
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
