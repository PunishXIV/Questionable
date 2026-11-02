using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Model.Questing;
using Xunit;
using static Questionable.External.KnightshopperIpc;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Tests.Steps;

public sealed class PurchaseTests
{
    private readonly ICallGateSubscriber<int> _version = Substitute.For<ICallGateSubscriber<int>>();
    private readonly ICallGateSubscriber<bool> _busy = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<int, (uint, uint)[], (int, Guid, string)> _start =
        Substitute.For<ICallGateSubscriber<int, (uint, uint)[], (int, Guid, string)>>();
    private readonly ICallGateSubscriber<Guid, (int, int, int, uint, string)> _status =
        Substitute.For<ICallGateSubscriber<Guid, (int, int, int, uint, string)>>();
    private readonly ICallGateSubscriber<Guid, bool> _cancel = Substitute.For<ICallGateSubscriber<Guid, bool>>();
    private readonly Guid _providerId = Guid.NewGuid();
    private readonly TestClock _clock = new();
    private readonly PurchaseOperationState _operation;
    private readonly KnightshopperIpc _ipc;
    private readonly Purchase.DoBuy _executor;
    private readonly TaskQueue _queue = new(NullLogger.Instance);
    private readonly Purchase.BuyTask _task = new(42, 5);
    private int _owned;
    private int _movementStops;

    public PurchaseTests()
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        plugin.GetIpcSubscriber<int>("Knightshopper.ApiVersion").Returns(_version);
        plugin.GetIpcSubscriber<bool>("Knightshopper.IsBusy").Returns(_busy);
        plugin.GetIpcSubscriber<int, (uint, uint)[], (int, Guid, string)>("Knightshopper.Purchase.StartItems").Returns(_start);
        plugin.GetIpcSubscriber<Guid, (int, int, int, uint, string)>("Knightshopper.Purchase.GetStatus").Returns(_status);
        plugin.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.Cancel").Returns(_cancel);
        _version.InvokeFunc().Returns(4);
        _start.InvokeFunc(2, Arg.Any<(uint, uint)[]>()).Returns(((int)EStartResult.Started, _providerId, "started"));
        _cancel.Configure().InvokeFunc(_providerId).Returns(true);
        _operation = new PurchaseOperationState(_clock);
        _ipc = new KnightshopperIpc(plugin, NullLogger<KnightshopperIpc>.Instance, _operation);
        _executor = new Purchase.DoBuy(_ipc, _operation, _ => _owned, () => ++_movementStops, _clock);
        Status(ExternalOperationStatus.Running);
    }

    [Fact]
    public void PartialInventory_SubmitsTargetTotalAndWaitsForTerminalSuccess()
    {
        _owned = 2;
        Start();
        _start.Received(1).InvokeFunc(2, Arg.Is<(uint, uint)[]>(items => items.Length == 1 && items[0].Item1 == 42u && items[0].Item2 == 5u));
        Assert.Equal(1, _movementStops);
        _owned = 5;
        _ipc.PollOperation();
        Assert.Equal(ETaskResult.StillRunning, _executor.Update());
        Status(ExternalOperationStatus.Succeeded);
        _ipc.PollOperation();
        Assert.True(_operation.HasOperation);
        Assert.Equal(ETaskResult.TaskComplete, _executor.Update());
        Assert.False(_operation.HasOperation);
        _executor.StopNow();
        _cancel.DidNotReceive().InvokeFunc(Arg.Any<Guid>());
    }

    [Fact]
    public void PreexistingItems_DoNotStartOrInterruptAnyProviderWork()
    {
        _owned = 5;
        _busy.InvokeFunc().Returns(true);
        Assert.False(_queue.StartTask(_executor, _task));
        Assert.Equal(0, _movementStops);
        AssertNoSubmissionOrCancellation();
    }

    [Fact]
    public void ManualShopping_BlocksStartWithoutStoppingItsTravel()
    {
        _busy.InvokeFunc().Returns(true);
        Assert.Throws<TaskException>(Start);
        Assert.Equal(0, _movementStops);
        Assert.False(_operation.HasOperation);
        Assert.Equal(EStartResult.Busy, _ipc.BuyWithGil(42, 5, out _, out _));
        AssertNoSubmissionOrCancellation();
    }

    [Fact]
    public void UnavailableBusyQuery_IsNotTreatedAsIdle()
    {
        _busy.InvokeFunc().Returns(_ => throw new InvalidOperationException("unloaded"));
        Assert.Throws<TaskException>(Start);
        Assert.Null(_ipc.GetBusy());
        Assert.Equal(0, _movementStops);
        AssertNoSubmissionOrCancellation();
    }

    [Theory]
    [InlineData((int)EStartResult.Busy)]
    [InlineData((int)EStartResult.EmptyList)]
    [InlineData((int)EStartResult.NotAllowed)]
    [InlineData((int)EStartResult.ItemUnavailable)]
    [InlineData((int)EStartResult.NotReady)]
    public void ExplicitRejection_ReleasesSubmissionAndDoesNotCancelOtherWork(int result)
    {
        _start.InvokeFunc(2, Arg.Any<(uint, uint)[]>()).Returns((result, Guid.Empty, "rejected"));
        Assert.Throws<TaskException>(Start);
        Assert.False(_operation.HasOperation);
        _ipc.CancelOwned();
        _cancel.DidNotReceive().InvokeFunc(Arg.Any<Guid>());
    }

    [Fact]
    public void ItemsArrivingDuringSubmission_AllowEmptyListOnlyAfterInventoryVerification()
    {
        _start.InvokeFunc(2, Arg.Any<(uint, uint)[]>()).Returns(_ =>
        {
            _owned = 5;
            return ((int)EStartResult.EmptyList, Guid.Empty, "already owned");
        });
        Assert.False(_queue.StartTask(_executor, _task));
        Assert.False(_operation.HasOperation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AmbiguousStart_RetainsOwnershipAndCannotBeRetried(bool throws)
    {
        _start.InvokeFunc(2, Arg.Any<(uint, uint)[]>()).Returns(_ => throws
            ? throw new InvalidOperationException("reply lost after acceptance")
            : ((int)EStartResult.Started, Guid.Empty, "missing id"));
        Assert.Throws<TaskException>(Start);
        Assert.Null(_queue.CurrentTaskExecutor);
        Assert.True(_operation.HasOperation);
        Assert.True(_operation.IsAbandoned);
        Assert.True(_operation.RequiresReconciliation);
        Assert.Equal(EStartResult.Busy, _ipc.BuyWithGil(42, 5, out _, out _));
        _start.Received(1).InvokeFunc(2, Arg.Any<(uint, uint)[]>());
        _cancel.DidNotReceive().InvokeFunc(Arg.Any<Guid>());
    }

    [Fact]
    public void QueueReset_RetainsIdUntilCancellationAcknowledged()
    {
        Start();
        Assert.True(_queue.Reset());
        Assert.Null(_queue.CurrentTaskExecutor);
        Assert.Equal(_providerId, _operation.ProviderOperationId);
        _ipc.CancelOwned();
        Status(ExternalOperationStatus.CancellationRequested);
        _ipc.PollOperation();
        Assert.True(_operation.HasOperation);
        _cancel.Received(1).InvokeFunc(_providerId);
        Assert.Equal(EStartResult.Busy, _ipc.BuyWithGil(43, 2, out _, out _));
        Status(ExternalOperationStatus.Cancelled);
        _ipc.PollOperation();
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void StaleExecutor_CannotCancelALaterPurchase()
    {
        Start();
        _owned = 5;
        Status(ExternalOperationStatus.Succeeded);
        _ipc.PollOperation();
        Assert.Equal(ETaskResult.TaskComplete, _executor.Update());
        Assert.Equal(EStartResult.Started, _ipc.BuyWithGil(43, 2, out _, out _));
        _executor.StopNow();
        Assert.False(_operation.IsAbandoned);
        _cancel.DidNotReceive().InvokeFunc(Arg.Any<Guid>());
    }

    [Fact]
    public void StopDuringSubmission_CancelsAcceptedOperationWhenItsIdArrives()
    {
        _start.InvokeFunc(2, Arg.Any<(uint, uint)[]>()).Returns(_ =>
        {
            _ipc.CancelOwned();
            return ((int)EStartResult.Started, _providerId, "started after stop");
        });
        Start();
        Assert.True(_operation.IsAbandoned);
        _ipc.PollOperation();
        _cancel.Received(1).InvokeFunc(_providerId);
        Assert.True(_operation.HasOperation);
    }

    [Fact]
    public void UnsupportedApi_RejectsBeforeAcquiringOwnershipOrStoppingMovement()
    {
        _version.InvokeFunc().Returns(1);
        Assert.Throws<TaskException>(Start);
        Assert.Equal(EStartResult.NotReady, _ipc.BuyWithGil(42, 5, out _, out _));
        Assert.Equal(0, _movementStops);
        Assert.False(_operation.HasOperation);
        AssertNoSubmissionOrCancellation();
    }

    [Theory]
    [InlineData((int)EStartResult.Uncertain)]
    [InlineData(-2)]
    [InlineData(1234)]
    public void UnrecognisedStartResult_RetainsUncertainty(int result)
    {
        _start.InvokeFunc(2, Arg.Any<(uint, uint)[]>()).Returns((result, Guid.Empty, "new result"));
        Assert.Throws<TaskException>(Start);
        Assert.True(_operation.HasOperation);
        Assert.True(_operation.RequiresReconciliation);
        _cancel.DidNotReceive().InvokeFunc(Arg.Any<Guid>());
    }

    [Fact]
    public void CancellationException_RetainsOwnerAndRetriesWithoutStartingAgain()
    {
        Start();
        _cancel.InvokeFunc(_providerId).Returns(_ => throw new InvalidOperationException("unloaded"));
        Assert.True(_queue.Reset());
        _clock.Advance(TimeSpan.FromSeconds(2));
        _cancel.Configure().InvokeFunc(_providerId).Returns(true);
        _ipc.PollOperation();
        _cancel.Received(2).InvokeFunc(_providerId);
        Assert.Equal(_providerId, _operation.ProviderOperationId);
        _start.Received(1).InvokeFunc(2, Arg.Any<(uint, uint)[]>());
    }

    [Fact]
    public void StatusFailure_TimesOutWithoutReleasingProviderOwnership()
    {
        Start();
        _status.InvokeFunc(_providerId).Returns(_ => throw new InvalidOperationException("unloaded"));
        _ipc.PollOperation();
        Assert.Equal(ETaskResult.StillRunning, _executor.Update());
        _clock.Advance(TimeSpan.FromSeconds(10));
        _ipc.PollOperation();
        Assert.Throws<TaskException>(() => _executor.Update());
        Assert.True(_operation.HasOperation);
        _queue.Reset();
        Status(ExternalOperationStatus.Succeeded);
        _ipc.PollOperation();
        Assert.False(_operation.HasOperation); // late result resolves abandoned ownership, never advances the old task
    }

    [Fact]
    public void SuccessWithoutItems_FailsAfterShortInventoryPropagationGrace()
    {
        Start();
        Status(ExternalOperationStatus.Succeeded);
        _ipc.PollOperation();
        Assert.Equal(ETaskResult.StillRunning, _executor.Update());
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Throws<TaskException>(() => _executor.Update());
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void DelayedInventoryUpdate_CompletesWithoutSubmittingAgain()
    {
        Start();
        Status(ExternalOperationStatus.Succeeded);
        _ipc.PollOperation();
        Assert.Equal(ETaskResult.StillRunning, _executor.Update());
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        _owned = 5;
        Assert.Equal(ETaskResult.TaskComplete, _executor.Update());
        Assert.False(_operation.HasOperation);
        _start.Received(1).InvokeFunc(2, Arg.Any<(uint, uint)[]>());
    }

    [Theory]
    [InlineData((int)ExternalOperationStatus.Failed)]
    [InlineData((int)ExternalOperationStatus.Cancelled)]
    public void TerminalFailure_StopsEvenWhenItemsArePresent(int result)
    {
        Start();
        _owned = 5;
        Status((ExternalOperationStatus)result);
        _ipc.PollOperation();
        Assert.Throws<TaskException>(() => _executor.Update());
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void HqRequirement_IsNotSilentlyPurchasedAsNormalQuality()
    {
        Assert.Throws<TaskException>(() => _queue.StartTask(_executor, _task with { Quality = EItemQuality.HQ }));
        AssertNoSubmissionOrCancellation();
    }

    [Theory]
    [InlineData(EItemQuality.Any, 5)]
    [InlineData(EItemQuality.NQ, 2)]
    [InlineData(EItemQuality.HQ, 3)]
    public void Verification_RespectsRequestedQuality(EItemQuality quality, int expected) =>
        Assert.Equal(expected, Purchase.CountItems(quality, 2, 3));

    private void Start() => Assert.True(_queue.StartTask(_executor, _task));
    private void Status(ExternalOperationStatus state) =>
        _status.Configure().InvokeFunc(_providerId).Returns(((int)state, 0, 1, 42u, state.ToString()));

    private void AssertNoSubmissionOrCancellation()
    {
        _start.DidNotReceive().InvokeFunc(Arg.Any<int>(), Arg.Any<(uint, uint)[]>());
        _cancel.DidNotReceive().InvokeFunc(Arg.Any<Guid>());
    }
}
