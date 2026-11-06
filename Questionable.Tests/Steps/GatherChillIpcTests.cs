using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class GatherChillIpcTests
{
    private readonly Guid _jobId = Guid.NewGuid();
    private readonly ICallGateSubscriber<int> _version = Substitute.For<ICallGateSubscriber<int>>();
    private readonly ICallGateSubscriber<bool> _busy = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<uint, bool> _canGather = Substitute.For<ICallGateSubscriber<uint, bool>>();
    private readonly ICallGateSubscriber<string, uint, int, (int, Guid, string)> _startJob =
        Substitute.For<ICallGateSubscriber<string, uint, int, (int, Guid, string)>>();
    private readonly ICallGateSubscriber<Guid, (int, int, int, string)> _getJob =
        Substitute.For<ICallGateSubscriber<Guid, (int, int, int, string)>>();
    private readonly ICallGateSubscriber<string, Guid, bool> _cancelJob = Substitute.For<ICallGateSubscriber<string, Guid, bool>>();
    private readonly TestClock _clock = new();
    private readonly GatheringOperationState _state;
    private readonly GatherChillIpc _ipc;
    private bool _providerBusy;
    private ExternalOperationStatus _jobState = ExternalOperationStatus.Running;

    public GatherChillIpcTests()
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        plugin.GetIpcSubscriber<int>("GatherChill.ApiVersion").Returns(_version);
        plugin.GetIpcSubscriber<bool>("GatherChill.IsBusy").Returns(_busy);
        plugin.GetIpcSubscriber<uint, bool>("GatherChill.CanGather").Returns(_canGather);
        plugin.GetIpcSubscriber<string, uint, int, (int, Guid, string)>("GatherChill.StartJob").Returns(_startJob);
        plugin.GetIpcSubscriber<Guid, (int, int, int, string)>("GatherChill.GetJob").Returns(_getJob);
        plugin.GetIpcSubscriber<string, Guid, bool>("GatherChill.CancelJob").Returns(_cancelJob);
        _version.InvokeFunc().Returns(3);
        _busy.InvokeFunc().Returns(_ => _providerBusy);
        _startJob.InvokeFunc(GatherChillIpc.Owner, 42, 3).Returns(_ =>
        {
            _providerBusy = true;
            return (0, _jobId, "Gathering 3x 42");
        });
        _getJob.InvokeFunc(_jobId).Returns(_ => ((int)_jobState, 1, 3, _jobState.ToString()));
        _cancelJob.InvokeFunc(GatherChillIpc.Owner, _jobId).Returns(true);
        _state = new GatheringOperationState(_clock);
        _ipc = new GatherChillIpc(plugin, NullLogger<GatherChillIpc>.Instance, _state);
    }

    [Fact]
    public void StartJob_TracksReturnedJob()
    {
        Assert.True(_ipc.IsAvailable());
        Assert.True(_ipc.IsReady());
        Start();
        _startJob.Received(1).InvokeFunc(GatherChillIpc.Owner, 42, 3);
        Assert.Equal(_jobId, _state.ProviderOperationId);
        Assert.Equal(ExternalOperationStatus.Running, _state.Status);
        Assert.Equal(GatherStartResult.Busy, _ipc.StartRequest(42, 3, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OlderApi_IsAvailableButNotReadyAndNeverStarts(int version)
    {
        _version.InvokeFunc().Returns(version);
        Assert.True(_ipc.IsAvailable());
        Assert.False(_ipc.IsReady());
        Assert.Equal(GatherStartResult.NotReady, _ipc.StartRequest(42, 3, out _));
        _startJob.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default, default);
        Assert.False(_state.HasOperation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrInvalidApi_IsUnavailableAndNeverStarts(bool missing)
    {
        _version.InvokeFunc().Returns(_ => missing ? throw new InvalidOperationException("provider absent") : 0);
        Assert.False(_ipc.IsAvailable());
        Assert.False(_ipc.IsReady());
        Assert.Equal(GatherStartResult.NotReady, _ipc.StartRequest(42, 3, out _));
        _startJob.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default, default);
        Assert.False(_state.HasOperation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OlderApiIdle_DoesNotConfirmUncertainStart(int version)
    {
        _startJob.Configure().InvokeFunc(GatherChillIpc.Owner, 42, 3).Returns(_ => throw new InvalidOperationException("reply lost"));
        Assert.Equal(GatherStartResult.Uncertain, _ipc.StartRequest(42, 3, out _));
        _version.InvokeFunc().Returns(version);
        _providerBusy = false;

        ObserveIdle();

        Assert.False(_state.TryConfirmIdle());
        Assert.True(_state.HasOperation);
        Assert.True(_state.RequiresReconciliation);
        _cancelJob.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default);
    }

    [Fact]
    public void BusyManualWork_IsNeverStartedOrCancelled()
    {
        _providerBusy = true;
        Assert.Equal(GatherStartResult.Busy, _ipc.StartRequest(42, 3, out _));
        _ipc.CancelOwned();
        Assert.False(_state.HasOperation);
        _startJob.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default, default);
        _cancelJob.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default);
    }

    [Theory]
    [InlineData(1, (int)GatherStartResult.Busy)]
    [InlineData(2, (int)GatherStartResult.Unsupported)]
    [InlineData(3, (int)GatherStartResult.Invalid)]
    [InlineData(4, (int)GatherStartResult.NotReady)]
    public void RefusedStart_DoesNotAcquireWork(int result, int expected)
    {
        _startJob.Configure().InvokeFunc(GatherChillIpc.Owner, 42, 3).Returns((result, Guid.Empty, "refused"));
        Assert.Equal((GatherStartResult)expected, _ipc.StartRequest(42, 3, out string message));
        Assert.Equal("refused", message);
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void LostStartReply_NeedsConfirmation()
    {
        _startJob.Configure().InvokeFunc(GatherChillIpc.Owner, 42, 3).Returns(_ => throw new InvalidOperationException("reply lost"));
        Assert.Equal(GatherStartResult.Uncertain, _ipc.StartRequest(42, 3, out _));
        _ipc.CancelOwned();
        Assert.True(_state.RequiresReconciliation);
        _cancelJob.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default);
        _providerBusy = false;
        ObserveIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void StartedWithoutJobId_IsUncertain()
    {
        _startJob.Configure().InvokeFunc(GatherChillIpc.Owner, 42, 3).Returns((0, Guid.Empty, "started"));
        Assert.Equal(GatherStartResult.Uncertain, _ipc.StartRequest(42, 3, out _));
        Assert.True(_state.RequiresReconciliation);
    }

    [Fact]
    public void Stopping_WaitsForTheJobsOwnOutcome()
    {
        Start();
        _jobState = ExternalOperationStatus.CancellationRequested;
        _ipc.PollOperation();
        Assert.False(_state.IsTerminal);

        _jobState = ExternalOperationStatus.Succeeded;
        _ipc.PollOperation();
        Assert.True(_state.IsTerminal);
        Assert.Equal(ExternalOperationStatus.Succeeded, _state.Status);
        Assert.Equal("Succeeded", _state.Message);
    }

    [Fact]
    public void CancelJob_CancelsOnlyOurJob()
    {
        Start();
        _ipc.CancelOwned();
        _ipc.CancelOwned();
        _cancelJob.Received(1).InvokeFunc(GatherChillIpc.Owner, _jobId);
        Assert.True(_state.HasOperation);

        _jobState = ExternalOperationStatus.Cancelled;
        _ipc.PollOperation();
        Assert.False(_state.HasOperation);
    }

    [Fact]
    public void RefusedCancel_IsRetriedAfterTheThrottle()
    {
        Start();
        _cancelJob.Configure().InvokeFunc(GatherChillIpc.Owner, _jobId).Returns(false);
        _ipc.CancelOwned();
        _clock.Advance(TimeSpan.FromSeconds(3));
        _ipc.PollOperation();
        _cancelJob.Received(2).InvokeFunc(GatherChillIpc.Owner, _jobId);
    }

    [Fact]
    public void MissingJobAfterIdle_NeedsConfirmation()
    {
        Start();
        _jobState = ExternalOperationStatus.Unknown;
        _ipc.PollOperation();
        _clock.Advance(TimeSpan.FromSeconds(11));
        _ipc.PollOperation();
        Assert.True(_state.RequiresReconciliation);
        Assert.False(_state.TryConfirmIdle());

        _providerBusy = false;
        ObserveIdle();
        Assert.True(_state.TryConfirmIdle());
    }

    [Fact]
    public void UnreadableJob_IsUnknownNotFinished()
    {
        Start();
        _getJob.Configure().InvokeFunc(_jobId).Returns(_ => throw new InvalidOperationException("provider gone"));
        _ipc.PollOperation();
        Assert.Equal(ExternalOperationStatus.Unknown, _state.Status);
        Assert.False(_state.IsTerminal);
    }

    private void Start()
    {
        Assert.Equal(GatherStartResult.Started, _ipc.StartRequest(42, 3, out _));
        _ipc.PollOperation();
    }

    private void ObserveIdle()
    {
        _ipc.ObserveIdle();
        _clock.Advance(TimeSpan.FromSeconds(1));
        _ipc.ObserveIdle();
        _clock.Advance(TimeSpan.FromSeconds(1));
        _ipc.ObserveIdle();
    }
}
