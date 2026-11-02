using System.Globalization;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using NSubstitute;
using Questionable.Controller;
using Xunit;
using Failure = Questionable.Controller.MovementController.PathfindingFailedException;
using Reason = Questionable.Controller.MovementController.PathfindingFailureReason;

namespace Questionable.Tests.Steps;

public sealed class NavmeshDelegationTests
{
    [Theory]
    [InlineData(false, "path")]
    [InlineData(false, "waypoints")]
    [InlineData(false, "start")]
    [InlineData(false, "submit")]
    [InlineData(false, "update")]
    [InlineData(false, "late")]
    [InlineData(false, "retry")]
    [InlineData(true, "path")]
    [InlineData(true, "waypoints")]
    [InlineData(true, "start")]
    [InlineData(true, "submit")]
    [InlineData(true, "update")]
    [InlineData(true, "late")]
    [InlineData(true, "retry")]
    public void DisabledOrPendingSimpleMoveBlocksDispatchAndOwnedUpdates(bool disabled, string phase)
    {
        using var fixture = new Fixture();
        if (phase is "update" or "late" or "retry")
        {
            fixture.Start(); // Cache the original permissive status.
            if (phase == "update")
                fixture.Now = 250;
            else if (phase == "late")
                fixture.Pending.SetResult([Vector3.Zero, fixture.Request.Position]);
            else
                fixture.SetLandingRetry();
        }
        fixture.Pathfind.ClearReceivedCalls();
        if (disabled)
            fixture.Allowed.InvokeFunc().Returns(false);
        else
            fixture.Simple.InvokeFunc().Returns(true);

        var failure = Assert.Throws<Failure>(() =>
        {
            switch (phase)
            {
                case "path": fixture.Movement.NavigateTo(default, null, Vector3.One, new()); break;
                case "waypoints": fixture.Movement.NavigateTo(default, null, new List<Vector3> { Vector3.One }, new()); break;
                case "start": fixture.Start(); break;
                case "submit": fixture.Movement.SubmitMovement([Vector3.One], true); break;
                default: fixture.Movement.Update(); break;
            }
        });

        Assert.Equal(disabled ? Reason.MovementDisabled : Reason.MovementBlocked, failure.Reason);
        Assert.Null(fixture.Movement.Destination);
        Assert.False(fixture.Movement.IsPathfinding);
        fixture.AssertNoTravelCalls();
        fixture.Pathfind.DidNotReceive().InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        fixture.SetAllowed.DidNotReceive().InvokeAction(Arg.Any<bool>());
        Assert.Equal(!disabled, fixture.Movement.LastFailure!.MovementAllowed);
        Assert.Equal(!disabled, fixture.Movement.LastFailure.SimpleMoveInProgress);
        if (phase is "path" or "waypoints")
            fixture.AssertNoGroundProbes(); // No attribution to the previous request.
    }

    [Fact]
    public void DisabledSubmittedRouteCannotResumeWhenMovementIsEnabledAgain()
    {
        using var fixture = new Fixture();
        bool routeRunning = false;
        fixture.Move.When(x => x.InvokeAction(Arg.Any<List<Vector3>>(), true))
            .Do(_ => routeRunning = true);
        fixture.Stop.When(x => x.InvokeAction()).Do(_ => routeRunning = false);
        Assert.True(fixture.Movement.SubmitMovement([fixture.Request.Position], true));
        Assert.True(routeRunning);

        fixture.Allowed.InvokeFunc().Returns(false);
        fixture.Now = 250;
        var failure = Assert.Throws<Failure>(fixture.Movement.Update);

        Assert.Equal(Reason.MovementDisabled, failure.Reason);
        Assert.Equal(Format(fixture.Request.Position), fixture.Movement.LastFailure!.Destination);
        Assert.Null(fixture.Movement.Destination);
        Assert.False(routeRunning);
        fixture.Stop.Received(2).InvokeAction(); // Initial submission, then disabled-route cleanup.

        fixture.Allowed.InvokeFunc().Returns(true);
        fixture.Now = 500;
        fixture.AssertQuestBoundary();
        Assert.False(routeRunning);
        fixture.Move.Received(1).InvokeAction(Arg.Any<List<Vector3>>(), true);
        fixture.SetAllowed.DidNotReceive().InvokeAction(Arg.Any<bool>());
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("purchase")]
    [InlineData("gather")]
    public void DisabledSubmittedRouteDoesNotStopExternalTakeover(string provider)
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Movement.SubmitMovement([fixture.Request.Position], true));
        fixture.Stop.ClearReceivedCalls();
        fixture.Move.ClearReceivedCalls();
        if (provider == "simple")
            fixture.Simple.InvokeFunc().Returns(true);
        else if (provider == "purchase")
        {
            fixture.Knightshopper.IsReady().Returns(true);
            fixture.Knightshopper.GetBusy().Returns((bool?)null);
        }
        else
        {
            fixture.Gather.IsAvailable().Returns(true);
            fixture.Gather.GetBusy().Returns((bool?)null);
        }
        fixture.Allowed.InvokeFunc().Returns(false);
        fixture.Now = 250;

        Assert.Equal(Reason.MovementDisabled, Assert.Throws<Failure>(fixture.Movement.Update).Reason);

        Assert.Null(fixture.Movement.Destination);
        fixture.AssertNoTravelCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunningRouteWaitsOutAPendingSimpleMove(bool outlastsTheWait)
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Movement.SubmitMovement([fixture.Request.Position], true));
        fixture.Stop.ClearReceivedCalls();
        fixture.Move.ClearReceivedCalls();

        // vnav re-paths a stuck route through SimpleMove by itself; our update pauses meanwhile.
        fixture.Simple.InvokeFunc().Returns(true);
        fixture.Now = 250;
        fixture.Movement.Update();
        Assert.True(fixture.Movement.IsPathfinding);
        Assert.Same(fixture.Request, fixture.Movement.Destination);

        if (outlastsTheWait)
        {
            fixture.Now += MovementController.SimpleMoveWaitMilliseconds;
            Assert.Equal(Reason.MovementBlocked, Assert.Throws<Failure>(fixture.Movement.Update).Reason);
            Assert.Null(fixture.Movement.Destination);
            Assert.Equal(true, fixture.Movement.LastFailure!.SimpleMoveInProgress);
        }
        else
        {
            fixture.Simple.InvokeFunc().Returns(false);
            fixture.Now = 500;
            fixture.AssertQuestBoundary();
            Assert.Same(fixture.Request, fixture.Movement.Destination);
        }
        Assert.False(fixture.Movement.IsPathfinding);
        fixture.AssertNoTravelCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopFreshlyProtectsPendingSimpleMoveWithOrWithoutLocalWork(bool owned)
    {
        using var fixture = new Fixture();
        if (owned)
            fixture.Start();
        else
            fixture.Movement.Destination = null;
        fixture.Simple.InvokeFunc().Returns(true);
        fixture.Simple.ClearReceivedCalls();

        fixture.Movement.Stop();

        fixture.Simple.Received(1).InvokeFunc();
        Assert.Null(fixture.Movement.Destination);
        Assert.False(fixture.Movement.IsPathfinding);
        fixture.AssertNoTravelCalls();
        fixture.AssertNoGroundProbes();
        Assert.Null(fixture.Movement.LastFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownOptionalStatusKeepsExistingSubmissionFallback(bool faulted)
    {
        using var fixture = new Fixture();
        foreach (var gate in fixture.BooleanStatusGates)
        {
            gate.HasFunction.Returns(faulted);
            if (faulted)
                gate.InvokeFunc().Returns(_ => throw new InvalidOperationException("Optional IPC unavailable"));
        }
        fixture.Queued.HasFunction.Returns(faulted);
        if (faulted)
            fixture.Queued.InvokeFunc().Returns(_ => throw new InvalidOperationException("Optional IPC unavailable"));

        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero, fixture.Request.Position]);
        fixture.AssertQuestBoundary();

        fixture.Move.Received(1).InvokeAction(Arg.Is<List<Vector3>>(p => p.SequenceEqual(new[] { fixture.Request.Position })), true);
        fixture.Stop.Received(1).InvokeAction();
        fixture.AssertNoGroundProbes();
        fixture.Movement.RecordFailure(new Failure(Reason.NotArrived, "Capture optional status"));
        var snapshot = fixture.Movement.LastFailure!;
        Assert.Null(snapshot.MovementAllowed);
        Assert.Null(snapshot.SimpleMoveInProgress);
        Assert.Null(snapshot.PathfindInProgress);
        Assert.Null(snapshot.QueuedPathfinds);
        fixture.SetAllowed.DidNotReceive().InvokeAction(Arg.Any<bool>());
    }

    [Fact]
    public void MountedFlightDispatchesWithoutPreflightGroundScan()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero, new(10, 20, 30), fixture.Request.Position]);

        fixture.AssertQuestBoundary();

        fixture.Move.Received(1).InvokeAction(Arg.Is<List<Vector3>>(p =>
            p.SequenceEqual(new[] { new Vector3(10, 20, 30), fixture.Request.Position })), true);
        Assert.Same(fixture.Request, fixture.Movement.Destination);
        fixture.AssertNoGroundProbes();
        Assert.Null(fixture.Movement.LastFailure);
    }

    [Fact]
    public void UpdateCachesOnlyOptionalStatusFor250MillisecondsAndDispatchRefreshes()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.AssertStatusReads(1);
        foreach (long now in new long[] { 0, 1, 100, 249 })
        {
            fixture.Now = now;
            fixture.AssertQuestBoundary();
        }
        fixture.AssertStatusReads(1);
        fixture.Now = 250;
        fixture.AssertQuestBoundary();
        fixture.AssertStatusReads(2);

        fixture.Now = 251;
        fixture.Pending.SetResult([Vector3.Zero, fixture.Request.Position]);
        fixture.AssertQuestBoundary();
        fixture.AssertStatusReads(3); // Fresh dispatch guard inside the cache window.
        fixture.AssertNoGroundProbes();

        fixture.Movement.Destination = null;
        fixture.Now = 1000;
        fixture.AssertQuestBoundary();
        fixture.AssertStatusReads(3);
    }

    [Fact]
    public void WhollyIdleUpdateDoesNotQueryOptionalStatusOrGround()
    {
        using var fixture = new Fixture();
        fixture.Movement.Destination = null;
        fixture.Allowed.InvokeFunc().Returns(false);
        fixture.Simple.InvokeFunc().Returns(true);

        fixture.AssertQuestBoundary();

        fixture.AssertStatusReads(0);
        fixture.AssertNoGroundProbes();
        Assert.Null(fixture.Movement.LastFailure);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FailureCapturesCachedStatusAndBoundedCandidatesWithoutMaskingCause(bool floorFails, bool nearestFails)
    {
        using var fixture = new Fixture();
        Vector3 floor = new(100, -1, 0);
        Vector3 nearest = new(100, -0.5f, 0);
        fixture.Floor.InvokeFunc(fixture.Request.Position, false, 0.2f)
            .Returns(_ => floorFails ? throw new InvalidOperationException("Floor unavailable") : floor);
        fixture.Nearest.InvokeFunc(fixture.Request.Position, 0.2f, 2f)
            .Returns(_ => nearestFails ? throw new InvalidOperationException("Nearest unavailable") : nearest);
        fixture.Start();
        fixture.AssertNoGroundProbes();
        fixture.Pathfinding.InvokeFunc().Returns(true);
        fixture.Queued.InvokeFunc().Returns(3);
        fixture.Now = 250;
        var cause = new InvalidOperationException("Original pathfinding failure");
        fixture.Pending.SetException(cause);

        var failure = Assert.Throws<Failure>(fixture.Movement.Update);

        Assert.Equal(Reason.ProviderFault, failure.Reason);
        Assert.Same(cause, failure.InnerException);
        Assert.Null(fixture.Movement.Destination);
        var snapshot = fixture.Movement.LastFailure!;
        Assert.Equal(true, snapshot.MovementAllowed);
        Assert.Equal(false, snapshot.SimpleMoveInProgress);
        Assert.Equal(true, snapshot.PathfindInProgress);
        Assert.Equal(3, snapshot.QueuedPathfinds);
        Assert.Equal(Format(fixture.Request.Position), snapshot.Destination);
        Assert.Equal(floorFails ? null : Format(floor), snapshot.FloorPoint);
        Assert.Equal(nearestFails ? null : Format(nearest), snapshot.NearestReachablePoint);

        fixture.Movement.RecordFailure(failure, "quest", "task");
        fixture.Movement.RecordFailure(failure);
        Assert.Equal(snapshot with { Quest = "quest", Task = "task" }, fixture.Movement.LastFailure);
        fixture.AssertStatusReads(2); // Capture/enrichment must not query status again.
        fixture.Floor.Received(1).InvokeFunc(fixture.Request.Position, false, 0.2f);
        fixture.Nearest.Received(1).InvokeFunc(fixture.Request.Position, 0.2f, 2f);
        fixture.AssertNoTravelCalls();
    }

    private static string Format(Vector3 point) => point.ToString("G9", CultureInfo.InvariantCulture);

    private sealed class Fixture : NavigationTestFixture
    {
        public ICallGateSubscriber<bool> Allowed { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<bool> Simple { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<bool> Pathfinding { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<int> Queued { get; } = Substitute.For<ICallGateSubscriber<int>>();
        public ICallGateSubscriber<bool, object> SetAllowed { get; } = Substitute.For<ICallGateSubscriber<bool, object>>();
        public ICallGateSubscriber<object> Stop { get; } = Substitute.For<ICallGateSubscriber<object>>();
        public ICallGateSubscriber<List<Vector3>, bool, object> Move { get; } = Substitute.For<ICallGateSubscriber<List<Vector3>, bool, object>>();
        public ICallGateSubscriber<Vector3, bool, float, Vector3?> Floor { get; } = Substitute.For<ICallGateSubscriber<Vector3, bool, float, Vector3?>>();
        public ICallGateSubscriber<Vector3, float, float, Vector3?> Nearest { get; } = Substitute.For<ICallGateSubscriber<Vector3, float, float, Vector3?>>();
        public IEnumerable<ICallGateSubscriber<bool>> BooleanStatusGates => [Allowed, Simple, Pathfinding];
        public TaskCompletionSource<List<Vector3>> Pending { get; } = new();
        public MovementController.DestinationData Request { get; } = new(default, 42, new(100, 0, 0), 1f,
            true, false, 1f, false, true);
        public long Now { get; set; }

        public Fixture()
        {
            var provider = Substitute.For<IExposedPlugin>();
            provider.InternalName.Returns("vnavmesh");
            provider.IsLoaded.Returns(true);
            provider.Version.Returns(new Version(1, 2, 3, 14));
            Plugin.InstalledPlugins.Returns([provider]);
            Plugin.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed").Returns(Allowed);
            Plugin.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").Returns(Simple);
            Plugin.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress").Returns(Pathfinding);
            Plugin.GetIpcSubscriber<int>("vnavmesh.Nav.PathfindNumQueued").Returns(Queued);
            Plugin.GetIpcSubscriber<bool, object>("vnavmesh.Path.SetMovementAllowed").Returns(SetAllowed);
            Plugin.GetIpcSubscriber<object>("vnavmesh.Path.Stop").Returns(Stop);
            Plugin.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").Returns(Move);
            Plugin.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor").Returns(Floor);
            Plugin.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable").Returns(Nearest);
            foreach (var gate in BooleanStatusGates)
                gate.HasFunction.Returns(true);
            Queued.HasFunction.Returns(true);
            Allowed.InvokeFunc().Returns(true);
            Pathfind.InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Pending.Task);
            Objects[0].Returns(Substitute.For<IGameObject>());
            Condition[ConditionFlag.Mounted].Returns(true);
            InitializeMovement(Request, () => Now);
        }

        public void Start() => Movement.StartPathfinding(Vector3.Zero, Request.Position, true);

        public void SetLandingRetry()
        {
            Movement.ResetPathfinding();
            typeof(MovementController).GetField("_previousDestinationData", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Movement, Request);
            typeof(MovementController).GetField("_landAndRetryTimeout", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Movement, DateTime.Now.AddSeconds(10));
        }

        public void AssertQuestBoundary() => Assert.Same(QuestBoundary, Record.Exception(Movement.Update));

        public void AssertNoTravelCalls()
        {
            Stop.DidNotReceive().InvokeAction();
            Move.DidNotReceive().InvokeAction(Arg.Any<List<Vector3>>(), Arg.Any<bool>());
        }

        public void AssertNoGroundProbes()
        {
            Floor.DidNotReceive().InvokeFunc(Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<float>());
            Nearest.DidNotReceive().InvokeFunc(Arg.Any<Vector3>(), Arg.Any<float>(), Arg.Any<float>());
        }

        public void AssertStatusReads(int count)
        {
            foreach (var gate in BooleanStatusGates)
                gate.Received(count).InvokeFunc();
            Queued.Received(count).InvokeFunc();
        }
    }
}
