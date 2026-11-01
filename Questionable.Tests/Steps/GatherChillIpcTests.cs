using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class GatherChillIpcTests
{
    private readonly ICallGateSubscriber<int> _version = Substitute.For<ICallGateSubscriber<int>>();
    private readonly ICallGateSubscriber<bool> _busy = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<uint, bool> _canGather = Substitute.For<ICallGateSubscriber<uint, bool>>();
    private readonly ICallGateSubscriber<uint, int, bool> _gather = Substitute.For<ICallGateSubscriber<uint, int, bool>>();
    private readonly ICallGateSubscriber<object> _stop = Substitute.For<ICallGateSubscriber<object>>();
    private readonly ICallGateSubscriber<bool> _path = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<bool> _pathfind = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<bool> _simplePathfind = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly TestClock _clock = new();
    private readonly GatheringOperationState _state;
    private readonly GatherChillIpc _ipc;
    private bool? _gameIdle = true;
    private bool _providerBusy;

    public GatherChillIpcTests()
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        plugin.GetIpcSubscriber<int>("GatherChill.ApiVersion").Returns(_version);
        plugin.GetIpcSubscriber<bool>("GatherChill.IsBusy").Returns(_busy);
        plugin.GetIpcSubscriber<uint, bool>("GatherChill.CanGather").Returns(_canGather);
        plugin.GetIpcSubscriber<uint, int, bool>("GatherChill.GatherItem").Returns(_gather);
        plugin.GetIpcSubscriber<object>("GatherChill.Stop").Returns(_stop);
        plugin.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").Returns(_path);
        plugin.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress").Returns(_pathfind);
        plugin.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").Returns(_simplePathfind);
        _version.InvokeFunc().Returns(2);
        _busy.InvokeFunc().Returns(_ => _providerBusy);
        _gather.InvokeFunc(42, 3).Returns(_ =>
        {
            _providerBusy = true;
            return true;
        });
        _state = new GatheringOperationState(_clock);
        _ipc = new GatherChillIpc(plugin, NullLogger<GatherChillIpc>.Instance, _state, () => _gameIdle);
    }

    [Fact]
    public void ExistingApi2_SubmitsOneItemAndAdditionalQuantity()
    {
        Start();
        _gather.Received(1).InvokeFunc(42, 3);
        Assert.True(_state.HasOperation);
        Assert.Null(_state.ProviderOperationId); // the legacy API supplies no remote identity
        Assert.Equal(ExternalOperationStatus.Running, _state.Status);
        Assert.Equal(GatherStartResult.Busy, _ipc.StartRequest(42, 3, out _));
    }

    [Fact]
    public void BusyManualWork_IsNeverStartedOrStopped()
    {
        _busy.InvokeFunc().Returns(true);
        Assert.Equal(GatherStartResult.Busy, _ipc.StartRequest(42, 3, out _));
        _ipc.CancelOwned();
        Assert.False(_state.HasOperation);
        _gather.DidNotReceiveWithAnyArgs().InvokeFunc(default, default);
        _stop.DidNotReceive().InvokeAction();
    }

    [Fact]
    public void RejectedStart_DoesNotAcquireOrCancelWork()
    {
        _gather.Configure().InvokeFunc(42, 3).Returns(false);
        Assert.Equal(GatherStartResult.Busy, _ipc.StartRequest(42, 3, out _));
        _ipc.CancelOwned();
        Assert.False(_state.HasOperation);
        _stop.DidNotReceive().InvokeAction();
    }

    [Fact]
    public void AmbiguousStart_DoesNotBlindlySendGlobalStopOrRetry()
    {
        _gather.Configure().InvokeFunc(42, 3).Returns(_ => throw new InvalidOperationException("reply lost"));
        Assert.Equal(GatherStartResult.Uncertain, _ipc.StartRequest(42, 3, out _));
        _ipc.CancelOwned();
        Assert.True(_state.RequiresReconciliation);
        Assert.True(_state.HasOperation);
        Assert.Equal(GatherStartResult.Busy, _ipc.StartRequest(42, 3, out _));
        _gather.Received(1).InvokeFunc(42, 3);
        _stop.DidNotReceive().InvokeAction();
        ObserveIdle();
        Assert.True(_state.HasOperation);
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void NormalCompletion_RequiresFreshStableIdleAndExecutorVerification()
    {
        Start();
        _busy.InvokeFunc().Returns(false);
        _ipc.PollOperation();
        Assert.False(_state.IsTerminal);
        ObserveIdle();
        Assert.Equal(ExternalOperationStatus.Succeeded, _state.Status);
        Assert.True(_state.HasOperation);
        Assert.True(_state.TryReleaseTerminal(_state.OperationId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ProviderIdle_DoesNotReleaseWhileChildrenOrGatheringStillActive(int activeChild)
    {
        Start();
        _busy.InvokeFunc().Returns(false);
        if (activeChild == 0) _path.InvokeFunc().Returns(true);
        if (activeChild == 1) _pathfind.InvokeFunc().Returns(true);
        if (activeChild == 2) _simplePathfind.InvokeFunc().Returns(true);
        if (activeChild == 3) _gameIdle = false;
        ObserveIdle();
        Assert.True(_state.HasOperation);
        Assert.False(_state.IsTerminal);
        Assert.False(_state.CanConfirmIdle);
    }

    [Fact]
    public void KnownCancellation_IsSentOnceAndWaitsForActualIdle()
    {
        Start();
        _ipc.CancelOwned();
        _ipc.CancelOwned();
        _stop.Received(1).InvokeAction();
        Assert.True(_state.HasOperation);
        Assert.True(_state.IsAbandoned);
        _busy.InvokeFunc().Returns(false);
        ObserveIdle();
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void CancellationTimeout_RetainsGuardAndDoesNotRepeatGlobalStop()
    {
        Start();
        _ipc.CancelOwned();
        _ipc.PollOperation();
        _clock.Advance(TimeSpan.FromSeconds(15));
        _ipc.PollOperation();
        Assert.True(_state.RequiresReconciliation);
        Assert.True(_state.HasOperation);
        _ipc.CancelOwned();
        _stop.Received(1).InvokeAction();
        _busy.InvokeFunc().Returns(false);
        ObserveIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void ProviderDisappearance_PreservesGuardEvenAfterItReturnsIdle()
    {
        Start();
        _busy.InvokeFunc().Returns(_ => throw new InvalidOperationException("unloaded"));
        _ipc.PollOperation();
        _ipc.CancelOwned();
        _stop.DidNotReceive().InvokeAction();
        _busy.Configure().InvokeFunc().Returns(false);
        ObserveIdle();
        Assert.True(_state.HasOperation);
        Assert.True(_state.RequiresReconciliation);
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void MissingChildStatus_IsUnknownNotIdle()
    {
        Start();
        _busy.InvokeFunc().Returns(false);
        _path.InvokeFunc().Returns(_ => throw new InvalidOperationException("navmesh unloaded"));
        ObserveIdle();
        Assert.True(_state.RequiresReconciliation);
        Assert.False(_state.TryConfirmIdle());
    }

    [Fact]
    public void LoggedOutState_CannotBeAcknowledgedAsIdle()
    {
        Start();
        _busy.InvokeFunc().Returns(false);
        _gameIdle = null;
        ObserveIdle();
        Assert.True(_state.HasOperation);
        Assert.False(_state.TryConfirmIdle());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingChildStatus_IsNotMaskedByAnotherBusyChild(int missingChild)
    {
        Start();
        _busy.InvokeFunc().Returns(false);
        ICallGateSubscriber<bool>[] children = [_path, _pathfind, _simplePathfind];
        children[missingChild].InvokeFunc().Returns(_ => throw new InvalidOperationException("child unavailable"));
        children[(missingChild + 1) % children.Length].InvokeFunc().Returns(true);

        _ipc.PollOperation();
        _ipc.CancelOwned();

        Assert.True(_state.HasOperation);
        Assert.True(_state.RequiresReconciliation);
        Assert.False(_state.TryConfirmIdle());
        _stop.DidNotReceive().InvokeAction();

        foreach (var child in children)
            child.Configure().InvokeFunc().Returns(false);
        ObserveIdle();
        Assert.True(_state.HasOperation);
        Assert.True(_state.TryConfirmIdle());
    }

    private void Start() => Assert.Equal(GatherStartResult.Started, _ipc.StartRequest(42, 3, out _));

    private void ObserveIdle()
    {
        _ipc.PollOperation();
        for (int i = 0; i < 4; ++i)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _ipc.PollOperation();
        }
    }
}
