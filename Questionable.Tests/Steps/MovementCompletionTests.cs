using System.Numerics;
using Questionable.Controller;
using Questionable.Controller.Steps.Movement;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class MovementCompletionTests
{
    [Fact]
    public void Interrupted_RetriesOnceThenFails()
    {
        MoveTask task = new(958, new Vector3(100, 0, 0));
        bool canRestart = true;
        Assert.True(MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart));
        Assert.False(canRestart);
        var error = Assert.Throws<MovementController.PathfindingException>(() =>
            MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart));
        Assert.Equal(MovementController.PathfindingFailureReason.NotArrived, error.Reason);
    }

    [Fact]
    public void NoRetry_CannotCompleteFarAway()
    {
        MoveTask task = new(958, new Vector3(100, 0, 0), RestartNavigation: false);
        bool canRestart = task.RestartNavigation;
        Assert.Throws<MovementController.PathfindingException>(() =>
            MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arrival_KeepsDistanceAllowance(bool canRestart)
    {
        MoveTask task = new(958, Vector3.Zero, StopDistance: 2f);
        Assert.False(MoveExecutor.ShouldRestartAfterStop(task, 958, new Vector3(7, 0, 0), ref canRestart));
    }

    [Fact]
    public void SuccessfulRetryCanComplete()
    {
        MoveTask task = new(958, new Vector3(100, 0, 0));
        bool canRestart = true;
        Assert.True(MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart));
        Assert.False(MoveExecutor.ShouldRestartAfterStop(task, 958, task.Destination, ref canRestart));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerritoryChange_DoesNotRestartRoute(bool canRestart)
    {
        MoveTask task = new(958, new Vector3(100, 0, 0));
        Assert.False(MoveExecutor.ShouldRestartAfterStop(task, 959, Vector3.Zero, ref canRestart));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavmeshReload_RestartsWithoutRetry(bool canRestart)
    {
        // vnav clears its route whenever its navmesh changes, e.g. while a zone's mesh is first built.
        MoveTask task = new(958, new Vector3(100, 0, 0), RestartNavigation: canRestart);
        Assert.True(MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart, navmeshReloaded: true));
        Assert.True(MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart, navmeshReloaded: true));
        Assert.Equal(task.RestartNavigation, canRestart);
    }

    [Theory]
    [InlineData(958, 100)]
    [InlineData(959, 0)]
    public void NavmeshReloadAfterArrival_Completes(uint territoryId, float x)
    {
        MoveTask task = new(958, new Vector3(100, 0, 0));
        bool canRestart = true;
        Assert.False(MoveExecutor.ShouldRestartAfterStop(task, territoryId, new Vector3(x, 0, 0), ref canRestart,
            navmeshReloaded: true));
    }

    [Fact]
    public void DistanceIncludesHeight()
    {
        MoveTask task = new(958, Vector3.Zero, StopDistance: 2f);
        bool canRestart = false;
        Assert.Throws<MovementController.PathfindingException>(() =>
            MoveExecutor.ShouldRestartAfterStop(task, 958, new Vector3(0, 8, 0), ref canRestart));
    }

    [Fact]
    public void StoppedRoute_WaitsForVnavRepath()
    {
        long? startedAt = null;
        Assert.True(MoveExecutor.ShouldWaitForSimpleMove(true, 1_000, ref startedAt));
        Assert.True(MoveExecutor.ShouldWaitForSimpleMove(true, 1_000 + MovementController.SimpleMoveWaitMilliseconds - 1, ref startedAt));
        Assert.False(MoveExecutor.ShouldWaitForSimpleMove(true, 1_000 + MovementController.SimpleMoveWaitMilliseconds, ref startedAt));

        Assert.False(MoveExecutor.ShouldWaitForSimpleMove(false, 50_000, ref startedAt));
        Assert.Null(startedAt);
        Assert.True(MoveExecutor.ShouldWaitForSimpleMove(true, 50_000, ref startedAt));
    }

    [Fact]
    public void MountCheck_AtMostEveryHalfSecond()
    {
        DateTime now = new(2026, 11, 6, 12, 0, 0);
        DateTime nextCheckAt = default;
        Assert.True(MoveExecutor.ShouldRecheckMount(now, ref nextCheckAt));
        Assert.False(MoveExecutor.ShouldRecheckMount(now.AddSeconds(0.49), ref nextCheckAt));
        Assert.True(MoveExecutor.ShouldRecheckMount(now.AddSeconds(0.5), ref nextCheckAt));
        Assert.False(MoveExecutor.ShouldRecheckMount(now.AddSeconds(0.9), ref nextCheckAt));
    }

    [Theory]
    [InlineData(11f, 50, true)]
    [InlineData(60f, 50, false)]
    [InlineData(11f, 0, false)]
    public void ShortHop_UsesSetting(float distance, int setting, bool shortHop) =>
        Assert.Equal(shortHop, MoveExecutor.IsShortHop(distance, setting));

    // Player at the origin, giver 10 y away on a 12 y high ledge.
    [Theory]
    [InlineData(10f, 12f, 50f, true)]
    [InlineData(6f, 0f, 50f, false)]
    [InlineData(10f, 0f, 50f, false)]
    [InlineData(10f, 12f, 15f, false)]
    public void GroundPath_MustEndAtTarget(float endX, float endY, float maxLength, bool walk)
    {
        Vector3 giver = new(10, 12, 0);
        Vector3[] path = [new(0, 0, 20), new(endX, endY, 0)];
        Assert.Equal(walk, MoveExecutor.ReachesOnFoot(Vector3.Zero, path, giver, 1f, maxLength));
    }
}
