using System.Numerics;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using NSubstitute;
using Questionable.Controller;
using Xunit;
using Reason = Questionable.Controller.MovementController.PathfindingFailureReason;

namespace Questionable.Tests.Steps;

public sealed class PathfindingFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProviderFault_KeepsOriginalCause(bool synchronous)
    {
        using var fixture = new Fixture();
        var cause = new InvalidOperationException("Provider diagnostic");
        fixture.Pathfind.InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), false, Arg.Any<CancellationToken>())
            .Returns(_ => synchronous ? throw cause : Task.FromException<List<Vector3>>(cause));
        fixture.Start();
        var error = Assert.Throws<MovementController.PathfindingFailedException>(fixture.Movement.Update);
        Assert.Equal(Reason.ProviderFault, error.Reason);
        Assert.Same(cause, error.InnerException);
        Assert.False(fixture.Movement.IsPathfinding);
        fixture.AssertNoDispatch();
    }

    [Fact]
    public void UncooperativeRequestExpiresAndLateResultCannotMove()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Now = 29_999;
        fixture.AssertNoPathfindingFailure();
        Assert.True(fixture.Movement.IsPathfinding);
        fixture.Now = 30_000;
        var error = Assert.Throws<MovementController.PathfindingFailedException>(fixture.Movement.Update);
        Assert.Equal(Reason.TimedOut, error.Reason);
        Assert.False(fixture.Movement.IsPathfinding);
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        fixture.AssertNoPathfindingFailure();
        fixture.AssertNoDispatch();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalCancellationIsSilentAndLateResultCannotMove(bool fault)
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Movement.ResetPathfinding();
        if (fault)
            fixture.Pending.SetException(new InvalidOperationException("Late fault"));
        else
            fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        fixture.AssertNoPathfindingFailure();
        Assert.False(fixture.Movement.IsPathfinding);
        fixture.AssertNoDispatch();
    }

    [Fact]
    public void LateRouteResult_IsNotSent()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Now = 30_000;
        fixture.Pending.SetResult([Vector3.Zero, Vector3.One]);
        var error = Assert.Throws<MovementController.PathfindingFailedException>(fixture.Movement.Update);
        Assert.Equal(Reason.TimedOut, error.Reason);
        fixture.AssertNoDispatch();
    }

    [Fact]
    public void UnexpectedProviderCancellationIsNotNoRoute()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Pending.SetCanceled();
        var error = Assert.Throws<MovementController.PathfindingFailedException>(fixture.Movement.Update);
        Assert.Equal(Reason.Cancelled, error.Reason);
        fixture.AssertNoDispatch();
    }

    [Fact]
    public void MissingIpcIsNotNoRoute()
    {
        using var fixture = new Fixture();
        var cause = new IpcNotReadyError("vnavmesh.Nav.PathfindCancelable");
        fixture.Pending.SetException(cause);
        fixture.Start();
        var error = Assert.Throws<MovementController.PathfindingFailedException>(fixture.Movement.Update);
        Assert.Equal(Reason.Unavailable, error.Reason);
        Assert.Same(cause, error.InnerException);
    }

    [Theory]
    [InlineData((int)Reason.Unavailable, "IPC")]
    [InlineData((int)Reason.TimedOut, "30 seconds")]
    [InlineData((int)Reason.Cancelled, "cancelled")]
    [InlineData((int)Reason.NoRoute, "no route")]
    [InlineData((int)Reason.LandingFailed, "landing")]
    [InlineData((int)Reason.ProviderFault, "/xllog")]
    [InlineData((int)Reason.StatusUnavailable, "movement status")]
    [InlineData((int)Reason.StartFailed, "could not be started")]
    [InlineData((int)Reason.NotArrived, "before reaching")]
    [InlineData((int)Reason.MovementBlocked, "another plugin")]
    [InlineData((int)Reason.MovementDisabled, "movement is disabled")]
    public void MessageDescribesCause(int reason, string expected)
    {
        string message = DalamudInitializer.GetPathfindingFailureMessage((Reason)reason, false);
        Assert.Contains(expected, message);
        Assert.DoesNotContain("Please report this to", message);
    }

    [Fact]
    public void DisabledFlyingAdviceOnlyAppliesToNoRoute()
    {
        Assert.Contains("Flying is disabled", DalamudInitializer.GetPathfindingFailureMessage(Reason.NoRoute, true));
        Assert.Equal(DalamudInitializer.GetPathfindingFailureMessage(Reason.Unavailable, false),
            DalamudInitializer.GetPathfindingFailureMessage(Reason.Unavailable, true));
    }

    private sealed class Fixture : NavigationTestFixture
    {
        public TaskCompletionSource<List<Vector3>> Pending { get; } = new();
        private readonly ICallGateSubscriber<List<Vector3>, bool, object> _move = Substitute.For<ICallGateSubscriber<List<Vector3>, bool, object>>();
        public long Now { get; set; }

        public Fixture()
        {
            Plugin.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").Returns(_move);
            Pathfind.InvokeFunc(Arg.Any<Vector3>(), Arg.Any<Vector3>(), false, Arg.Any<CancellationToken>()).Returns(Pending.Task);
            InitializeMovement(new(default, null, Vector3.One, 1f, false, false, 1f, false, true), () => Now);
        }

        public void Start() => Movement.StartPathfinding(Vector3.Zero, Vector3.One, false);
        public void AssertNoPathfindingFailure() => Assert.Same(QuestBoundary, Record.Exception(Movement.Update));
        public void AssertNoDispatch() => _move.DidNotReceive().InvokeAction(Arg.Any<List<Vector3>>(), Arg.Any<bool>());
    }
}
