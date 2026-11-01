using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Model.Questing;
using Xunit;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Tests.Steps;

public sealed class GatheringControllerTests
{
    private readonly IGatherChillIpc _ipc = Substitute.For<IGatherChillIpc>();
    private readonly GatheringOperationState _state = new();
    private readonly GatheringController _controller;
    private readonly Gather.StartGathering _executor;
    private readonly TaskQueue _queue = new(NullLogger.Instance);
    private readonly Gather.GatheringTask _task = new(new GatheredItem { ItemId = 42, ItemCount = 5, Collectability = 350 });
    private int _qualifyingCount;
    private int _movementStops;

    public GatheringControllerTests()
    {
        _ipc.IsReady().Returns(true);
        _ipc.GetBusy().Returns((bool?)false);
        _ipc.CanGather(42).Returns(true);
        _ipc.StartRequest(42, Arg.Any<int>(), out Arg.Any<string>()).Returns(call =>
        {
            call[2] = "started";
            Guid id = _state.BeginSubmission();
            _state.AcceptSubmission(id, id);
            return GatherStartResult.Started;
        });
        _ipc.When(x => x.CancelOwned()).Do(_ => _state.AbandonCurrent());
        _controller = new GatheringController(_ipc, _state, request =>
        {
            Assert.Equal(350, request.Collectability);
            return _qualifyingCount;
        }, () => ++_movementStops);
        _executor = new Gather.StartGathering(_controller);
    }

    [Fact]
    public void ExistingQualifyingInventory_SkipsEvenWithoutProvider()
    {
        _qualifyingCount = 5;
        _ipc.IsReady().Returns(false);
        Assert.False(_queue.StartTask(_executor, _task));
        Assert.False(_state.HasOperation);
        Assert.Equal(0, _movementStops);
        _ipc.DidNotReceiveWithAnyArgs().StartRequest(default, default, out _);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void BusyOrUnavailableProvider_DoesNotStopManualTravel(bool? busy)
    {
        _ipc.GetBusy().Returns(busy);
        Assert.Throws<TaskException>(() => _queue.StartTask(_executor, _task));
        Assert.Equal(0, _movementStops);
        _ipc.DidNotReceive().CancelOwned();
        _ipc.DidNotReceiveWithAnyArgs().StartRequest(default, default, out _);
    }

    [Fact]
    public void PartialInventory_RequestsOnlyMissingItemsAndVerifiesQuality()
    {
        _qualifyingCount = 2;
        Assert.True(_queue.StartTask(_executor, _task));
        _ipc.Received(1).StartRequest(42, 3, out Arg.Any<string>());
        Assert.Equal(1, _movementStops);
        Assert.Equal(ETaskResult.StillRunning, _executor.Update());
        _qualifyingCount = 5;
        Assert.Equal(ETaskResult.StillRunning, _executor.Update());
        _state.ObserveStatus(_state.OperationId!.Value, ExternalOperationStatus.Succeeded, "done");
        Assert.Equal(ETaskResult.TaskComplete, _executor.Update());
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void InsufficientQualifyingItems_DoNotAdvanceAfterProviderSuccess()
    {
        Assert.True(_queue.StartTask(_executor, _task));
        _qualifyingCount = 4;
        _state.ObserveStatus(_state.OperationId!.Value, ExternalOperationStatus.Succeeded, "done");
        Assert.Throws<TaskException>(() => _executor.Update());
        Assert.False(_state.HasOperation);
    }

    [Theory]
    [InlineData((int)ExternalOperationStatus.Failed)]
    [InlineData((int)ExternalOperationStatus.Cancelled)]
    public void FailedOrCancelledProvider_DoesNotAdvanceEvenIfInventoryWasFilled(int result)
    {
        Assert.True(_queue.StartTask(_executor, _task));
        _qualifyingCount = 5;
        _state.ObserveStatus(_state.OperationId!.Value, (ExternalOperationStatus)result, "not completed");
        Assert.Throws<TaskException>(() => _executor.Update());
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void Reset_RetainsOwnershipWhileCancellationIsPending()
    {
        Assert.True(_queue.StartTask(_executor, _task));
        Guid owner = _state.OperationId!.Value;
        Assert.True(_queue.Reset());
        Assert.Null(_queue.CurrentTaskExecutor);
        Assert.True(_state.HasOperation);
        Assert.True(_state.IsAbandoned);
        _ipc.Received(1).CancelOwned();
        Assert.Throws<TaskException>(() => _controller.Start(new(42, 5, 350), out _));
        _state.ObserveStatus(owner, ExternalOperationStatus.Cancelled, "stopped");
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void OldExecutor_CannotStopNewRequest()
    {
        Assert.True(_queue.StartTask(_executor, _task));
        _qualifyingCount = 5;
        _state.ObserveStatus(_state.OperationId!.Value, ExternalOperationStatus.Succeeded, "done");
        Assert.Equal(ETaskResult.TaskComplete, _executor.Update());
        Guid replacement = _state.BeginSubmission();
        _state.AcceptSubmission(replacement, replacement);
        _executor.StopNow();
        Assert.False(_state.IsAbandoned);
        _ipc.DidNotReceive().CancelOwned();
    }
}
