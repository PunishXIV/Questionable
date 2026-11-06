using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.NavigationOverrides;
using Xunit;
using Reason = Questionable.Controller.MovementController.PathfindingFailureReason;

namespace Questionable.Tests.Steps;

public sealed class NavigationSubmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatusFailure_OnlyBlocksQuestionableMovement(bool missing)
    {
        using var fixture = new Fixture();
        fixture.Start();
        Exception cause = missing ? new IpcNotReadyError("vnavmesh.Path.IsRunning") : new InvalidOperationException("Status failed");
        fixture.Running.InvokeFunc().Returns(_ => throw cause);

        Assert.Null(fixture.Navmesh.GetPathRunning());
        Assert.True(fixture.Navmesh.IsPathRunning);
        Assert.Null(fixture.Movement.GetPathRunning());
        Assert.True(fixture.Movement.IsPathRunning);
        Assert.Empty(fixture.Navmesh.GetWaypoints());
        fixture.Waypoints.DidNotReceive().InvokeFunc();

        fixture.AssertFailure(Reason.StatusUnavailable, fixture.Movement.Update);
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        Assert.False(fixture.Movement.IsPathRunning);
        fixture.AssertQuestBoundary();
        fixture.AssertNoTravelCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownStatusIsForwarded(bool running)
    {
        using var fixture = new Fixture();
        fixture.Running.InvokeFunc().Returns(running);
        Assert.Equal(running, fixture.Navmesh.GetPathRunning());
        Assert.Equal(running, fixture.Movement.GetPathRunning());
        Assert.Equal(running, fixture.Movement.IsPathRunning);
        fixture.Movement.Destination = null;
        Assert.Equal(running, fixture.Movement.IsPathRunning);
    }

    [Fact]
    public void NullWaypointsAreAnEmptyRoute()
    {
        using var fixture = new Fixture();
        fixture.Running.InvokeFunc().Returns(true);
        fixture.Waypoints.InvokeFunc().Returns((List<Vector3>)null!);
        Assert.Empty(fixture.Navmesh.GetWaypoints());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavmeshRequest_ReportsStopAndMoveErrors(bool stopFails)
    {
        using var fixture = new Fixture();
        var cause = new InvalidOperationException("Submission failed");
        fixture.FailSubmission(cause, stopFails);
        Assert.Same(cause, Record.Exception(() => fixture.Navmesh.MoveTo([Vector3.One], false)));
        fixture.Stop.Received(1).InvokeAction();
        if (stopFails)
            fixture.Move.DidNotReceive().InvokeAction(Arg.Any<List<Vector3>>(), Arg.Any<bool>());
        else
            fixture.Move.Received(1).InvokeAction(Arg.Any<List<Vector3>>(), false);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RouteRequestFailure_ClearsRequestAndKeepsCause(bool stopFails, bool delayed)
    {
        using var fixture = new Fixture();
        var cause = new InvalidOperationException("Original provider diagnostic");
        fixture.FailSubmission(cause, stopFails);
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);

        var error = fixture.AssertFailure(Reason.StartFailed, delayed
            ? fixture.Movement.Update
            : () => { fixture.Movement.SubmitMovement([Vector3.One], false); });

        Assert.Same(cause, error.InnerException);
        Assert.Equal(DateTime.MaxValue, fixture.Movement.MovementStartedAt);
        fixture.AssertQuestBoundary();
        fixture.Stop.Received(1).InvokeAction();
    }

    [Fact]
    public void RouteStart_StopsBeforeMoving()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        fixture.Stop.When(x => x.InvokeAction()).Do(_ => calls.Add("stop"));
        fixture.Move.When(x => x.InvokeAction(Arg.Any<List<Vector3>>(), false)).Do(_ =>
        {
            Assert.Equal(DateTime.MaxValue, fixture.Movement.MovementStartedAt);
            calls.Add("move");
        });
        fixture.Start();
        DateTime before = DateTime.Now;

        Assert.True(fixture.Movement.SubmitMovement([Vector3.One], false));

        Assert.Collection(calls, call => Assert.Equal("stop", call), call => Assert.Equal("move", call));
        Assert.InRange(fixture.Movement.MovementStartedAt, before, DateTime.Now);
        Assert.False(fixture.Movement.IsPathfinding);
        Assert.NotNull(fixture.Movement.Destination);
    }

    [Theory]
    [InlineData("purchase", false)]
    [InlineData("purchase", true)]
    [InlineData("gather", false)]
    [InlineData("gather", true)]
    [InlineData("duty", false)]
    [InlineData("duty", true)]
    [InlineData("unknown-duty", false)]
    [InlineData("unknown-duty", true)]
    [InlineData("feesh", false)]
    [InlineData("feesh", true)]
    public void ManualProvider_BlocksRoutesBeforeSetup(string provider, bool direct)
    {
        using var fixture = new Fixture();
        fixture.Block(provider);

        fixture.AssertFailure(Reason.MovementBlocked, () =>
        {
            if (direct)
                fixture.Movement.NavigateTo(default, null, new List<Vector3> { Vector3.One }, new());
            else
                fixture.Movement.NavigateTo(default, null, Vector3.One, new());
        });

        fixture.AssertNoTravelCalls();
        fixture.Pathfind.DidNotReceive().InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        fixture.AssertQuestBoundary(); // Idle global updates must not keep throwing for manual work.
    }

    [Theory]
    [InlineData("purchase", false)]
    [InlineData("purchase", true)]
    [InlineData("gather", false)]
    [InlineData("gather", true)]
    [InlineData("duty", false)]
    [InlineData("duty", true)]
    [InlineData("unknown-duty", false)]
    [InlineData("unknown-duty", true)]
    [InlineData("feesh", false)]
    [InlineData("feesh", true)]
    public void ManualTakeoverBlocksDelayedRouteAndLandingRetry(string provider, bool landingRetry)
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        if (landingRetry)
        {
            fixture.Movement.ResetPathfinding();
            fixture.SetLandingRetry();
        }
        fixture.Block(provider);
        fixture.Pathfind.ClearReceivedCalls();

        fixture.AssertFailure(Reason.MovementBlocked, fixture.Movement.Update);

        fixture.AssertNoTravelCalls();
        fixture.Pathfind.DidNotReceive().InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        fixture.AssertQuestBoundary();
    }

    [Fact]
    public void ManualTakeover_IsCheckedBeforeSending()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        int queries = 0;
        fixture.Knightshopper.GetBusy().Returns(_ => ++queries >= 2);

        fixture.AssertFailure(Reason.MovementBlocked, fixture.Movement.Update);

        Assert.Equal(2, queries);
        fixture.AssertNoTravelCalls();
    }

    [Fact]
    public void PendingOperation_ClearsBeforeSending()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        int queries = 0;
        fixture.Knightshopper.GetBusy().Returns(_ =>
        {
            if (++queries == 2)
                fixture.Purchase.BeginSubmission();
            return false;
        });

        fixture.Movement.Update();

        Assert.Null(fixture.Movement.Destination);
        Assert.False(fixture.Movement.IsPathfinding);
        Assert.True(fixture.Purchase.HasOperation);
        fixture.AssertNoTravelCalls();
    }

    [Fact]
    public void MissingOptionalProviders_DoNotBlockRoute()
    {
        using var fixture = new Fixture();
        fixture.DutyStatus = () => throw new IpcNotReadyError("AutoDuty.IsStopped");
        fixture.Knightshopper.GetBusy().Returns((bool?)null);
        fixture.Gather.GetBusy().Returns((bool?)null);
        fixture.Feesh.GetBusy().Returns((bool?)null);

        Assert.True(fixture.Movement.SubmitMovement([Vector3.One], false));

        fixture.Stop.Received(1).InvokeAction();
        fixture.Move.Received(1).InvokeAction(Arg.Any<List<Vector3>>(), false);
    }

    [Theory]
    [InlineData("purchase", false)]
    [InlineData("purchase", true)]
    [InlineData("gather", false)]
    [InlineData("gather", true)]
    [InlineData("feesh", false)]
    [InlineData("feesh", true)]
    public void AvailableProviderWithUnknownBusyStatusBlocksInitialNavigation(string provider, bool direct)
    {
        using var fixture = new Fixture();
        fixture.BlockUnknownProvider(provider);

        fixture.AssertFailure(Reason.MovementBlocked, () =>
        {
            if (direct)
                fixture.Movement.NavigateTo(default, null, new List<Vector3> { Vector3.One }, new());
            else
                fixture.Movement.NavigateTo(default, null, Vector3.One, new());
        });

        fixture.AssertNoTravelCalls();
        fixture.Pathfind.DidNotReceive().InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("purchase")]
    [InlineData("gather")]
    public void LostBusyStatus_DoesNotReplaceProviderRoute(string provider)
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        fixture.BlockUnknownProvider(provider);

        fixture.AssertFailure(Reason.MovementBlocked, fixture.Movement.Update);

        fixture.AssertNoTravelCalls();
    }

    [Theory]
    [InlineData("purchase")]
    [InlineData("gather")]
    public void AvailableProviderWithUnknownBusyStatusProtectsTravelDuringStop(string provider)
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.BlockUnknownProvider(provider);

        fixture.Movement.Stop();
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        fixture.AssertQuestBoundary();

        Assert.Null(fixture.Movement.Destination);
        Assert.False(fixture.Movement.IsPathfinding);
        fixture.AssertNoTravelCalls();
    }

    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(20f, 0f, false)]
    [InlineData(0f, 0.75f, false)]
    public void OnePointRouteRequiresActualArrival(float x, float y, bool arrived)
    {
        using var fixture = new Fixture();
        fixture.Player.Position.Returns(new Vector3(x, y, 0));
        fixture.Movement.Destination = fixture.Movement.Destination! with
        {
            Position = Vector3.Zero,
            StopDistance = 2f,
            VerticalStopDistance = 0.5f,
        };
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero]); // A route endpoint is not evidence of player arrival.

        if (arrived)
        {
            fixture.Movement.Update();
            Assert.Null(fixture.Movement.Destination);
            Assert.NotEqual(DateTime.MaxValue, fixture.Movement.MovementStartedAt);
        }
        else
            fixture.AssertFailure(Reason.NoRoute, fixture.Movement.Update);

        fixture.AssertNoTravelCalls();
        Assert.False(fixture.Movement.IsPathfinding);
    }

    [Fact]
    public void OnePointRouteWithMissingPlayerCannotComplete()
    {
        using var fixture = new Fixture();
        fixture.Objects[0].Returns((IGameObject?)null);
        fixture.Start();
        fixture.Pending.SetResult([Vector3.One]);

        fixture.AssertFailure(Reason.NoRoute, fixture.Movement.Update);
        fixture.AssertNoTravelCalls();
    }

    [Fact]
    public void OnePointRecalculation_SendsPartialRoute()
    {
        using var fixture = new Fixture();
        fixture.Movement.Destination!.PartialRoute.Add(Vector3.One);
        fixture.Start();
        fixture.Pending.SetResult([Vector3.One]);

        fixture.AssertQuestBoundary();

        fixture.Move.Received(1).InvokeAction(Arg.Is<List<Vector3>>(x => x.Count == 1 && x[0] == Vector3.One), false);
        Assert.NotNull(fixture.Movement.Destination);
        Assert.False(fixture.Movement.IsPathfinding);
    }

    [Fact]
    public void EmptyDirectRoute_FailsBeforeSetup()
    {
        using var fixture = new Fixture();
        fixture.AssertFailure(Reason.NoRoute, () => fixture.Movement.NavigateTo(default, null, new List<Vector3>(), new()));
        fixture.AssertNoTravelCalls();
    }

    private sealed class Fixture : NavigationTestFixture
    {
        public IGameObject Player { get; } = Substitute.For<IGameObject>();
        public TaskCompletionSource<List<Vector3>> Pending { get; } = new();
        public ICallGateSubscriber<bool> Running { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<List<Vector3>> Waypoints { get; } = Substitute.For<ICallGateSubscriber<List<Vector3>>>();
        public ICallGateSubscriber<object> Stop { get; } = Substitute.For<ICallGateSubscriber<object>>();
        public ICallGateSubscriber<List<Vector3>, bool, object> Move { get; } = Substitute.For<ICallGateSubscriber<List<Vector3>, bool, object>>();

        public Fixture()
        {
            Plugin.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").Returns(Running);
            Plugin.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints").Returns(Waypoints);
            Plugin.GetIpcSubscriber<object>("vnavmesh.Path.Stop").Returns(Stop);
            Plugin.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").Returns(Move);
            Pathfind.InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Pending.Task);
            Objects[0].Returns(Player);
            InitializeMovement(new(default, null, Vector3.One, 1f, false, false, 1f, false, true), () => 0,
                new MovementOverrideController(Client, NullLogger<MovementOverrideController>.Instance));
            typeof(MovementController).GetProperty(nameof(MovementController.MovementStartedAt))!.SetValue(Movement, DateTime.MaxValue);
        }

        public void Start() => Movement.StartPathfinding(Vector3.Zero, Vector3.One, false);

        public void Block(string provider)
        {
            switch (provider)
            {
                case "purchase": Knightshopper.GetBusy().Returns(true); break;
                case "gather": Gather.GetBusy().Returns(true); break;
                case "feesh": Feesh.GetBusy().Returns(true); break;
                case "duty": DutyStatus = () => false; break;
                case "unknown-duty": DutyStatus = () => throw new InvalidOperationException("Status lost"); break;
                default: throw new ArgumentOutOfRangeException(nameof(provider));
            }
        }

        public void SetLandingRetry()
        {
            typeof(MovementController).GetField("_previousDestinationData", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Movement, Movement.Destination);
            typeof(MovementController).GetField("_landAndRetryTimeout", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Movement, DateTime.Now.AddSeconds(10));
        }

        public void BlockUnknownProvider(string provider)
        {
            if (provider == "purchase")
            {
                Knightshopper.IsReady().Returns(true);
                Knightshopper.GetBusy().Returns((bool?)null);
            }
            else if (provider == "feesh")
            {
                Feesh.IsAvailable().Returns(true);
                Feesh.GetBusy().Returns((bool?)null);
            }
            else
            {
                Gather.IsAvailable().Returns(true);
                Gather.GetBusy().Returns((bool?)null);
            }
        }

        public void FailSubmission(Exception cause, bool stopFails)
        {
            if (stopFails)
                Stop.When(x => x.InvokeAction()).Do(_ => throw cause);
            else
                Move.When(x => x.InvokeAction(Arg.Any<List<Vector3>>(), Arg.Any<bool>())).Do(_ => throw cause);
        }

        public MovementController.PathfindingFailedException AssertFailure(Reason reason, Action action)
        {
            var error = Assert.Throws<MovementController.PathfindingFailedException>(action);
            Assert.Equal(reason, error.Reason);
            Assert.Null(Movement.Destination);
            Assert.False(Movement.IsPathfinding);
            return error;
        }

        public void AssertQuestBoundary() => Assert.Same(QuestBoundary, Record.Exception(Movement.Update));

        public void AssertNoTravelCalls()
        {
            Stop.DidNotReceive().InvokeAction();
            Move.DidNotReceive().InvokeAction(Arg.Any<List<Vector3>>(), Arg.Any<bool>());
        }
    }
}
