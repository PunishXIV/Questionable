using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.Controller.Steps;
using Xunit;
using Questionable.External;

namespace Questionable.Tests.Steps;

public sealed class PurchaseStateTests
{
    private readonly TestClock _clock = new();
    private readonly PurchaseState _state;
    public PurchaseStateTests() => _state = new PurchaseState(_clock);

    [Fact]
    public void StartRequest_BlocksSecondPurchase()
    {
        Guid owner = _state.BeginRequest();
        Assert.Throws<TaskException>(() => _state.BeginRequest());
        Assert.False(_state.TryClearFinished(owner));
        Assert.False(_state.TryConfirmIdle());
    }

    [Fact]
    public void SuccessfulPurchase_WaitsForItemCheck()
    {
        Guid owner = Start();
        _state.ObservePurchase(owner, ExternalOperationStatus.Succeeded, "done");
        _state.ObservePurchase(owner, ExternalOperationStatus.Unknown, "expired");
        Assert.True(_state.HasOperation);
        Assert.Equal(ExternalOperationStatus.Succeeded, _state.Status);
        Assert.False(_state.TryClearFinished(Guid.NewGuid()));
        Assert.True(_state.TryClearFinished(owner));
    }

    [Theory]
    [InlineData((int)ExternalOperationStatus.Succeeded)]
    [InlineData((int)ExternalOperationStatus.Failed)]
    [InlineData((int)ExternalOperationStatus.Cancelled)]
    public void StoppedPurchase_EndsOnAnyResult(int result)
    {
        var terminal = (ExternalOperationStatus)result;
        Guid owner = Start();
        _state.Abandon();
        _state.ObservePurchase(Guid.NewGuid(), terminal, "wrong owner");
        Assert.True(_state.HasOperation);
        _state.ObservePurchase(owner, terminal, "done");
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void AcceptedCancel_WaitsForFinishedStatus()
    {
        Guid owner = Start();
        _state.Abandon();
        Assert.True(_state.TryBeginCancel(out Guid provider));
        _state.ObserveCancel(provider, true);
        _state.ObservePurchase(owner, ExternalOperationStatus.CancelRequested, "stopping");
        _clock.Advance(TimeSpan.FromSeconds(15));
        _state.ObservePurchase(owner, ExternalOperationStatus.CancelRequested, "still stopping");
        Assert.True(_state.HasOperation);
        Assert.True(_state.NeedsConfirmation);
        Assert.False(_state.TryBeginCancel(out _));
        Assert.Throws<TaskException>(() => _state.BeginRequest());
        StableIdle();
        Assert.False(_state.TryConfirmIdle()); // live operation status overrides an inconsistent idle flag
        _state.ObservePurchase(owner, ExternalOperationStatus.Cancelled, "stopped");
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void RejectedCancel_RetriesSamePurchase()
    {
        Start();
        _state.Abandon();
        Assert.True(_state.TryBeginCancel(out Guid first));
        _state.ObserveCancel(first, false);
        Assert.False(_state.TryBeginCancel(out _));
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(_state.TryBeginCancel(out Guid second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void UnknownStatus_NeedsConfirmation()
    {
        Guid owner = Start();
        _state.ObservePurchase(owner, ExternalOperationStatus.Unknown, "unavailable");
        _clock.Advance(TimeSpan.FromSeconds(9));
        _state.ObservePurchase(owner, ExternalOperationStatus.Unknown, "unavailable");
        Assert.False(_state.NeedsConfirmation);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _state.ObservePurchase(owner, (ExternalOperationStatus)99, "new unknown status");
        Assert.True(_state.NeedsConfirmation);
        Assert.True(_state.HasOperation);
        StableIdle();
        Assert.True(_state.CanConfirmIdle);
        Assert.True(_state.TryConfirmIdle());
        Assert.False(_state.HasOperation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void ManualRecovery_RejectsBusyOrMissing(bool? busy)
    {
        _state.MarkUncertain(_state.BeginRequest());
        StableIdle();
        _state.ObserveIdle(busy);
        Assert.False(_state.TryConfirmIdle());
        Assert.True(_state.HasOperation);
    }

    [Fact]
    public void ManualRecovery_NeedsFreshIdleAndConfirm()
    {
        _state.AcceptRequest(_state.BeginRequest(), Guid.Empty);
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
    public void ActiveStatus_ResetsIdleStreak()
    {
        Guid owner = Start();
        _state.MarkUncertain(owner);
        _state.ObservePurchase(owner, ExternalOperationStatus.Unknown, "lost status");
        StableIdle();
        Assert.True(_state.CanConfirmIdle);
        _state.ObservePurchase(owner, ExternalOperationStatus.Running, "still running");
        _state.ObserveIdle(false);
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        _state.ObservePurchase(owner, ExternalOperationStatus.Unknown, "lost again");
        _state.ObserveIdle(false);
        Assert.False(_state.TryConfirmIdle());
        StableIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    private Guid Start()
    {
        Guid owner = _state.BeginRequest();
        _state.AcceptRequest(owner, Guid.NewGuid());
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
