using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Movement;
using Xunit;
using Failure = Questionable.Controller.MovementController.PathfindingFailedException;
using Reason = Questionable.Controller.MovementController.PathfindingFailureReason;

namespace Questionable.Tests.Steps;

public sealed class NavigationFailureCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceFailureCapturesBeforeCleanupAndPreservesCause(bool timeout)
    {
        using var fixture = new Fixture();
        var cause = new InvalidOperationException("Original provider failure");
        fixture.Start();
        fixture.Pending.SetException(cause);
        fixture.Now = timeout ? 30_000 : 123;
        DateTimeOffset before = DateTimeOffset.UtcNow;

        var error = Assert.Throws<Failure>(fixture.Movement.Update);

        Assert.Same(cause, error.InnerException);
        Assert.Null(fixture.Movement.Destination);
        Assert.False(fixture.Movement.IsPathfinding);
        var snapshot = Assert.IsType<NavigationFailureSnapshot>(fixture.Movement.LastFailure);
        Assert.Equal((timeout ? Reason.TimedOut : Reason.ProviderFault).ToString(), snapshot.Reason);
        Assert.Equal((double)fixture.Now, snapshot.ElapsedMilliseconds);
        Assert.Equal(Format(Vector3.One), snapshot.Destination);
        Assert.Equal(Format(Vector3.Zero), snapshot.PlayerPosition);
        Assert.Equal(958u, snapshot.TerritoryId);
        Assert.Equal(42u, snapshot.DataId);
        Assert.Equal(false, snapshot.Fly);
        Assert.Equal(1f, snapshot.StopDistance);
        Assert.Equal(typeof(Failure).FullName, snapshot.ExceptionType);
        Assert.Equal(cause.GetType().FullName, snapshot.InnerExceptionType);
        Assert.Equal(cause.Message, snapshot.InnerExceptionMessage);
        Assert.InRange(snapshot.TimestampUtc, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, snapshot.TimestampUtc.Offset);
    }

    [Fact]
    public void StatusFailureDoesNotQueryStatusAgainWhileCapturing()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Now = 456;
        fixture.Running.InvokeFunc().Returns(_ => throw new InvalidOperationException("IPC lost"));
        fixture.Plugin.InstalledPlugins.Returns(_ => throw new InvalidOperationException("Plugin unloaded"));
        fixture.Objects[0].Returns(_ => throw new InvalidOperationException("Player unavailable"));

        var error = Assert.Throws<Failure>(fixture.Movement.Update);

        Assert.Equal(Reason.StatusUnavailable, error.Reason);
        var snapshot = Assert.IsType<NavigationFailureSnapshot>(fixture.Movement.LastFailure);
        Assert.Equal(456d, snapshot.ElapsedMilliseconds);
        Assert.Equal(Format(Vector3.One), snapshot.Destination);
        Assert.Null(snapshot.NavmeshVersion);
        Assert.Null(snapshot.PlayerPosition);
        Assert.Null(fixture.Movement.Destination);
        fixture.Running.Received(1).InvokeFunc();
    }

    [Fact]
    public void EmptyMovementWaypointsCaptureDestinationBeforeClearing()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetResult([Vector3.Zero]);

        var error = Assert.Throws<Failure>(fixture.Movement.Update);

        Assert.Equal(Reason.NoRoute, error.Reason);
        Assert.Equal(Format(Vector3.One), fixture.Movement.LastFailure!.Destination);
        Assert.Null(fixture.Movement.Destination);
    }

    [Fact]
    public void Failure_RemainsAfterNewRoute()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetException(new InvalidOperationException("First failure"));
        Assert.Throws<Failure>(fixture.Movement.Update);
        var snapshot = fixture.Movement.LastFailure;

        fixture.Movement.ResetPathfinding();
        fixture.StopWithoutNativeInput();
        fixture.Movement.Destination = fixture.Request with { Position = new Vector3(200, 0, 0) };
        fixture.Pending = new();
        fixture.Start();
        Assert.True(fixture.Movement.SubmitMovement([new Vector3(200, 0, 0)], false));

        Assert.Same(snapshot, fixture.Movement.LastFailure);
        Assert.Equal(Format(Vector3.One), snapshot!.Destination);
    }

    [Fact]
    public void RepeatedRecordEnrichesOnlyItsExceptionWithoutRecapturing()
    {
        using var fixture = new Fixture();
        var first = new Failure(Reason.NotArrived, "First failure");
        fixture.Movement.RecordFailure(first, "first quest", "first task");
        var original = fixture.Movement.LastFailure!;
        fixture.Player.Position.Returns(new Vector3(9, 8, 7));
        fixture.Movement.Destination = fixture.Request with { Position = new Vector3(200, 0, 0) };
        fixture.Movement.RecordFailure(first, "updated quest", "updated task");

        Assert.Equal(original with { Quest = "updated quest", Task = "updated task" }, fixture.Movement.LastFailure);
        Assert.Equal("first quest", original.Quest); // Earlier immutable snapshots stay unchanged.
        fixture.Movement.RecordFailure(first);
        Assert.Equal("updated task", fixture.Movement.LastFailure!.Task);

        var second = new Failure("Different external failure");
        fixture.Movement.RecordFailure(second);
        var latest = fixture.Movement.LastFailure!;
        Assert.Equal("Unknown", latest.Reason);
        Assert.Equal(second.Message, latest.Message);
        Assert.Equal(Format(new Vector3(9, 8, 7)), latest.PlayerPosition);
        Assert.Equal(Format(new Vector3(200, 0, 0)), latest.Destination);
        Assert.Null(latest.Quest);
        Assert.Null(latest.Task);
    }

    [Fact]
    public void UnknownExternalFailureWithMissingContextIsSafe()
    {
        using var fixture = new Fixture();
        fixture.Movement.Destination = null;
        fixture.Objects[0].Returns((IGameObject?)null);
        fixture.Client.TerritoryType.Returns(_ => throw new InvalidOperationException("Logged out"));
        fixture.Plugin.InstalledPlugins.Returns(_ => throw new InvalidOperationException("Unloaded"));
        var cause = new InvalidOperationException("Original cause");
        var error = new Failure("External failure", cause);

        fixture.Movement.RecordFailure(error);

        Assert.Same(cause, error.InnerException);
        var snapshot = fixture.Movement.LastFailure!;
        Assert.Equal("Unknown", snapshot.Reason);
        Assert.Equal(0u, snapshot.TerritoryId);
        Assert.Null(snapshot.PlayerPosition);
        Assert.Null(snapshot.Destination);
        Assert.Null(snapshot.DataId);
        Assert.Null(snapshot.Fly);
        Assert.Null(snapshot.StopDistance);
        Assert.Null(snapshot.ElapsedMilliseconds);
        Assert.Null(snapshot.NavmeshVersion);
        fixture.Running.DidNotReceive().InvokeFunc();
        fixture.Knightshopper.DidNotReceive().GetBusy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockedRoute_DoesNotUsePreviousDestination(bool direct)
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Now = 999;
        fixture.Knightshopper.GetBusy().Returns(true);

        var error = Assert.Throws<Failure>(() =>
        {
            if (direct)
                fixture.Movement.NavigateTo(default, 99, new List<Vector3> { new(100, 0, 0) }, new());
            else
                fixture.Movement.NavigateTo(default, 99, new Vector3(100, 0, 0), new());
        });
        // An executor fallback must not overwrite the deliberate unknown initial request.
        fixture.Movement.RecordFailureWithDestination(error, fixture.Request);

        Assert.Equal(Reason.MovementBlocked, error.Reason);
        Assert.Null(fixture.Movement.LastFailure!.Destination);
        Assert.Null(fixture.Movement.LastFailure.DataId);
        Assert.Null(fixture.Movement.LastFailure.ElapsedMilliseconds);
        Assert.Null(fixture.Movement.Destination);
    }

    [Fact]
    public void LocalCancellationAndManualStopDoNotRecordFailure()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Movement.ResetPathfinding();
        fixture.Pending.SetException(new InvalidOperationException("Late failure after cancellation"));
        fixture.StopWithoutNativeInput();
        fixture.AssertNoNavigationFailure();

        Assert.Null(fixture.Movement.LastFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecutorUpdateRecordsFailureAfterControllerDestinationWasCleared(bool statusUnavailable)
    {
        using var fixture = new Fixture();
        fixture.StopWithoutNativeInput();
        fixture.LoadNavmesh();
        if (statusUnavailable)
            fixture.Running.InvokeFunc().Returns(_ => throw new InvalidOperationException("Status lost"));
        MoveTask task = new(958, new Vector3(100, 0, 0), StopDistance: 2f, DataId: 123, Fly: true,
            RestartNavigation: false);
        var executor = fixture.CreateExecutor(task);
        typeof(MovementController).GetProperty(nameof(MovementController.MovementStartedAt))!
            .SetValue(fixture.Movement, DateTime.Now.AddSeconds(-10));

        var error = Assert.Throws<Failure>(() => executor.Update());

        Assert.Equal(statusUnavailable ? Reason.StatusUnavailable : Reason.NotArrived, error.Reason);
        var snapshot = fixture.Movement.LastFailure!;
        Assert.Equal(Format(task.Destination), snapshot.Destination);
        Assert.Equal(task.DataId, snapshot.DataId);
        Assert.Equal(true, snapshot.Fly);
        Assert.Equal(2f, snapshot.StopDistance);
        Assert.Null(snapshot.ElapsedMilliseconds);
    }

    [Fact]
    public void ExecutorWaitsOutANavmeshReloadAndMovesAgainWithoutUsingUpTheRetry()
    {
        using var fixture = new Fixture();
        fixture.StopWithoutNativeInput();
        fixture.LoadNavmesh();
        int starts = 0;
        MoveTask task = new(958, new Vector3(100, 0, 0), RestartNavigation: false);
        var executor = fixture.CreateExecutor(task, () => starts++);
        typeof(MovementController).GetProperty(nameof(MovementController.MovementStartedAt))!
            .SetValue(fixture.Movement, DateTime.Now.AddSeconds(-10));

        // vnav dropped the route while it rebuilt the zone's navmesh (e.g. on a first visit).
        fixture.NavReady.InvokeFunc().Returns(false);
        Assert.Equal(ETaskResult.StillRunning, executor.Update());
        Assert.Equal(0, starts);

        fixture.NavReady.InvokeFunc().Returns(true);
        Assert.Equal(ETaskResult.StillRunning, executor.Update());
        Assert.Equal(1, starts);

        // A later ordinary stop still fails, as retries are disabled for this step.
        Assert.Equal(Reason.NotArrived, Assert.Throws<Failure>(() => executor.Update()).Reason);
    }

    [Fact]
    public void ExecutorStartupRecordsOriginalExceptionBeforeQueueRollback()
    {
        using var fixture = new Fixture();
        var cause = new InvalidOperationException("Original startup cause");
        var error = new Failure(Reason.StartFailed, "Startup failed", cause);
        fixture.Objects[0].Returns(_ => throw error);
        fixture.Purchase.BeginSubmission(); // Keep rollback away from native game input.
        var executor = fixture.CreateExecutor(new(958, Vector3.One));
        var queue = new TaskQueue(NullLogger.Instance);

        Assert.Same(error, Assert.Throws<Failure>(() => queue.StartTask(executor, new MoveTask(958, Vector3.One))));

        Assert.Same(cause, error.InnerException);
        Assert.Null(queue.CurrentTaskExecutor);
        Assert.Null(fixture.Movement.Destination);
        Assert.Equal(Format(Vector3.One), fixture.Movement.LastFailure!.Destination);
        Assert.Equal(cause.Message, fixture.Movement.LastFailure.InnerExceptionMessage);
    }

    private static string Format(Vector3 position) => position.ToString("G9", CultureInfo.InvariantCulture);

    private sealed class Fixture : NavigationTestFixture
    {
        public IGameObject Player { get; } = Substitute.For<IGameObject>();
        public ICallGateSubscriber<bool> Running { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<bool> NavReady { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public TaskCompletionSource<List<Vector3>> Pending { get; set; } = new();
        public MovementController.DestinationData Request { get; } = new(default, 42, Vector3.One, 1f,
            false, false, 1f, false, true);
        public long Now { get; set; }

        public Fixture()
        {
            Plugin.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").Returns(Running);
            Plugin.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").Returns(NavReady);
            NavReady.InvokeFunc().Returns(true);
            Pathfind.InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(_ => Pending.Task);
            Objects[0].Returns(Player);
            Client.TerritoryType.Returns(958u);
            InitializeMovement(Request, () => Now);
        }

        public void Start() => Movement.StartPathfinding(Vector3.Zero, Vector3.One, false);

        public void AssertNoNavigationFailure() => Assert.Same(QuestBoundary, Record.Exception(Movement.Update));

        // The executor only trusts a navmesh-ready signal from a loaded vnavmesh.
        public void LoadNavmesh()
        {
            var provider = Substitute.For<IExposedPlugin>();
            provider.InternalName.Returns("vnavmesh");
            provider.IsLoaded.Returns(true);
            provider.Version.Returns(new Version(1, 2, 3, 14));
            Plugin.InstalledPlugins.Returns([provider]);
        }

        public void StopWithoutNativeInput()
        {
            Knightshopper.GetBusy().Returns(true);
            Movement.Stop();
            Knightshopper.GetBusy().Returns(false);
        }

        public MoveExecutor CreateExecutor(MoveTask task, Action? start = null)
        {
            // Bypass constructor-only game sheet lookup; exercise the real Start/Update paths.
            var executor = (MoveExecutor)RuntimeHelpers.GetUninitializedObject(typeof(MoveExecutor));
            SetField("_movementController", Movement);
            SetField("_clientState", Client);
            SetField("_objectTable", Objects);
            SetField("_logger", NullLogger<MoveExecutor>.Instance);
            SetField("_startAction", start ?? (() => { }));
            typeof(TaskExecutor<MoveTask>).GetProperty("Task", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(executor, task);
            return executor;

            void SetField(string name, object value) =>
                typeof(MoveExecutor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(executor, value);
        }
    }
}
