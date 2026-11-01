using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class NavmeshQueryIpcTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisteredStatusValuesArePreserved(bool value)
    {
        var f = new Fixture();
        f.Movement.HasFunction.Returns(true);
        f.Simple.HasFunction.Returns(true);
        f.Pathfind.HasFunction.Returns(true);
        f.Queue.HasFunction.Returns(true);
        f.Movement.InvokeFunc().Returns(value);
        f.Simple.InvokeFunc().Returns(value);
        f.Pathfind.InvokeFunc().Returns(value);
        f.Queue.InvokeFunc().Returns(3);
        Assert.Equal(value, f.Ipc.GetMovementAllowed());
        Assert.Equal(value, f.Ipc.GetSimpleMoveInProgress());
        Assert.Equal(value, f.Ipc.GetPathfindInProgress());
        Assert.Equal(3, f.Ipc.GetQueuedPathfinds());
    }

    [Fact]
    public void MissingStatusEndpointsAreUnknownAndNeverInvoked()
    {
        var f = new Fixture();
        Assert.Null(f.Ipc.GetMovementAllowed());
        Assert.Null(f.Ipc.GetSimpleMoveInProgress());
        Assert.Null(f.Ipc.GetPathfindInProgress());
        Assert.Null(f.Ipc.GetQueuedPathfinds());
        f.Movement.DidNotReceive().InvokeFunc();
        f.Simple.DidNotReceive().InvokeFunc();
        f.Pathfind.DidNotReceive().InvokeFunc();
        f.Queue.DidNotReceive().InvokeFunc();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatusFailuresRemainUnknownWithoutLoggingEveryPoll(bool registrationFails)
    {
        var f = new Fixture();
        foreach (var gate in new[] { f.Movement, f.Simple, f.Pathfind })
        {
            gate.HasFunction.Returns(_ => registrationFails ? throw new InvalidOperationException("Gone") : true);
            gate.InvokeFunc().Returns(_ => throw new InvalidOperationException("Query failed"));
        }
        f.Queue.HasFunction.Returns(_ => registrationFails ? throw new InvalidOperationException("Gone") : true);
        f.Queue.InvokeFunc().Returns(_ => throw new InvalidOperationException("Query failed"));
        for (int i = 0; i < 3; ++i)
        {
            Assert.Null(f.Ipc.GetMovementAllowed());
            Assert.Null(f.Ipc.GetSimpleMoveInProgress());
            Assert.Null(f.Ipc.GetPathfindInProgress());
            Assert.Null(f.Ipc.GetQueuedPathfinds());
        }
        Assert.Empty(f.Log.ReceivedCalls());
    }

    [Fact]
    public void FloorQueriesForwardBoundsAndPreserveCandidate()
    {
        var f = new Fixture();
        Vector3 position = new(1, 2, 3);
        Vector3 candidate = new(1, 1, 3);
        f.Floor.InvokeFunc(position, false, 0.2f).Returns(candidate);
        f.Nearest.InvokeFunc(position, 0.2f, 2f).Returns(candidate);
        Assert.Equal(candidate, f.Ipc.GetPointOnFloor(position, unlandable: false, range: 0.2f));
        Assert.Equal(candidate, f.Ipc.GetNearestPointReachable(position, 0.2f, 2f));
        f.Floor.Received(1).InvokeFunc(position, false, 0.2f);
        f.Nearest.Received(1).InvokeFunc(position, 0.2f, 2f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloorQueriesReturnUnknownForNoCandidateOrFailure(bool throws)
    {
        var f = new Fixture();
        f.Floor.InvokeFunc(Arg.Any<Vector3>(), false, Arg.Any<float>())
            .Returns(_ => throws ? throw new InvalidOperationException("No mesh") : (Vector3?)null);
        f.Nearest.InvokeFunc(Arg.Any<Vector3>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(_ => throws ? throw new InvalidOperationException("No mesh") : (Vector3?)null);
        Assert.Null(f.Ipc.GetPointOnFloor(Vector3.One, false));
        Assert.Null(f.Ipc.GetNearestPointReachable(Vector3.One, 0.2f, 2f));
        Assert.Empty(f.Log.ReceivedCalls());
    }

    private sealed class Fixture
    {
        public ICallGateSubscriber<bool> Movement { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<bool> Simple { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<bool> Pathfind { get; } = Substitute.For<ICallGateSubscriber<bool>>();
        public ICallGateSubscriber<int> Queue { get; } = Substitute.For<ICallGateSubscriber<int>>();
        public ICallGateSubscriber<Vector3, bool, float, Vector3?> Floor { get; } = Substitute.For<ICallGateSubscriber<Vector3, bool, float, Vector3?>>();
        public ICallGateSubscriber<Vector3, float, float, Vector3?> Nearest { get; } = Substitute.For<ICallGateSubscriber<Vector3, float, float, Vector3?>>();
        public ILogger<NavmeshIpc> Log { get; } = Substitute.For<ILogger<NavmeshIpc>>();
        public NavmeshIpc Ipc { get; }

        public Fixture()
        {
            var plugin = Substitute.For<IDalamudPluginInterface>();
            plugin.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed").Returns(Movement);
            plugin.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").Returns(Simple);
            plugin.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress").Returns(Pathfind);
            plugin.GetIpcSubscriber<int>("vnavmesh.Nav.PathfindNumQueued").Returns(Queue);
            plugin.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor").Returns(Floor);
            plugin.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable").Returns(Nearest);
            Ipc = new(plugin, Log);
        }
    }
}
