using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.Controller.Steps;
using Xunit;
using Questionable.External;

namespace Questionable.Tests.Steps;

public sealed class PurchaseOperationStateTests
{
    private readonly TestClock _clock = new();
    private readonly PurchaseOperationState _state;
    public PurchaseOperationStateTests() => _state = new PurchaseOperationState(_clock);

    [Fact]
    public void Submission_BlocksReplacementBeforeProviderReply()
    {
        Guid owner = _state.BeginSubmission();
        Assert.Throws<TaskException>(() => _state.BeginSubmission());
        Assert.False(_state.TryReleaseTerminal(owner));
        Assert.False(_state.TryConfirmIdle());
    }

    [Fact]
    public void Success_RemainsOwnedUntilExecutorVerifiesInventory()
    {
        Guid owner = Start();
        _state.ObserveStatus(owner, ExternalOperationStatus.Succeeded, "done");
        _state.ObserveStatus(owner, ExternalOperationStatus.Unknown, "expired");
        Assert.True(_state.HasOperation);
        Assert.Equal(ExternalOperationStatus.Succeeded, _state.Status);
        Assert.False(_state.TryReleaseTerminal(Guid.NewGuid()));
        Assert.True(_state.TryReleaseTerminal(owner));
    }

    [Theory]
    [InlineData((int)ExternalOperationStatus.Succeeded)]
    [InlineData((int)ExternalOperationStatus.Failed)]
    [InlineData((int)ExternalOperationStatus.Cancelled)]
    public void DetachedPurchase_ReleasesOnAnyAuthoritativeTerminalResult(int result)
    {
        var terminal = (ExternalOperationStatus)result;
        Guid owner = Start();
        _state.AbandonCurrent();
        _state.ObserveStatus(Guid.NewGuid(), terminal, "wrong owner");
        Assert.True(_state.HasOperation);
        _state.ObserveStatus(owner, terminal, "done");
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void AcceptedCancellation_IsNotCompletionAndEventuallyNeedsReconciliation()
    {
        Guid owner = Start();
        _state.AbandonCurrent();
        Assert.True(_state.TryBeginCancellation(out Guid provider));
        _state.ObserveCancellation(provider, true);
        _state.ObserveStatus(owner, ExternalOperationStatus.CancellationRequested, "stopping");
        _clock.Advance(TimeSpan.FromSeconds(15));
        _state.ObserveStatus(owner, ExternalOperationStatus.CancellationRequested, "still stopping");
        Assert.True(_state.HasOperation);
        Assert.True(_state.RequiresReconciliation);
        Assert.False(_state.TryBeginCancellation(out _));
        Assert.Throws<TaskException>(() => _state.BeginSubmission());
        StableIdle();
        Assert.False(_state.TryConfirmIdle()); // live operation status overrides an inconsistent idle flag
        _state.ObserveStatus(owner, ExternalOperationStatus.Cancelled, "stopped");
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void RejectedCancellation_RetriesOnlyTheOwnedIdAndIsThrottled()
    {
        Start();
        _state.AbandonCurrent();
        Assert.True(_state.TryBeginCancellation(out Guid first));
        _state.ObserveCancellation(first, false);
        Assert.False(_state.TryBeginCancellation(out _));
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(_state.TryBeginCancellation(out Guid second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void UnknownStatus_IsBoundedButNeverReleasesOwnership()
    {
        Guid owner = Start();
        _state.ObserveStatus(owner, ExternalOperationStatus.Unknown, "unavailable");
        _clock.Advance(TimeSpan.FromSeconds(9));
        _state.ObserveStatus(owner, ExternalOperationStatus.Unknown, "unavailable");
        Assert.False(_state.RequiresReconciliation);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _state.ObserveStatus(owner, (ExternalOperationStatus)99, "new unknown status");
        Assert.True(_state.RequiresReconciliation);
        Assert.True(_state.HasOperation);
        StableIdle();
        Assert.True(_state.CanConfirmIdle);
        Assert.True(_state.TryConfirmIdle());
        Assert.False(_state.HasOperation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void ManualRecovery_RejectsBusyOrUnavailableProvider(bool? busy)
    {
        _state.MarkUncertain(_state.BeginSubmission());
        StableIdle();
        _state.ObserveIdle(busy);
        Assert.False(_state.TryConfirmIdle());
        Assert.True(_state.HasOperation);
    }

    [Fact]
    public void ManualRecovery_RequiresFreshContinuousIdleAndExplicitConfirmation()
    {
        _state.AcceptSubmission(_state.BeginSubmission(), Guid.Empty);
        StableIdle();
        Assert.True(_state.CanConfirmIdle);
        Assert.True(_state.HasOperation);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(_state.TryConfirmIdle());
        _state.ObserveIdle(false);
        Assert.False(_state.TryConfirmIdle());
        StableIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void ActiveStatus_InvalidatesAnEarlierIdleStreak()
    {
        Guid owner = Start();
        _state.MarkUncertain(owner);
        _state.ObserveStatus(owner, ExternalOperationStatus.Unknown, "lost status");
        StableIdle();
        Assert.True(_state.CanConfirmIdle);
        _state.ObserveStatus(owner, ExternalOperationStatus.Running, "still running");
        _state.ObserveIdle(false);
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        _state.ObserveStatus(owner, ExternalOperationStatus.Unknown, "lost again");
        _state.ObserveIdle(false);
        Assert.False(_state.TryConfirmIdle());
        StableIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    private Guid Start()
    {
        Guid owner = _state.BeginSubmission();
        _state.AcceptSubmission(owner, Guid.NewGuid());
        return owner;
    }

    private void StableIdle()
    {
        _state.ObserveIdle(false);
        for (int i = 0; i < 4; ++i)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _state.ObserveIdle(false);
        }
    }
}
