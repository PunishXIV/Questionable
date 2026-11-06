using System.Numerics;
using Questionable.Controller;
using Questionable.Controller.Steps.Movement;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class MovementCompletionTests
{
    [Fact]
    public void InterruptedMovementGetsOneRetryThenFailsInsteadOfCompleting()
    {
        MoveTask task = new(958, new Vector3(100, 0, 0));
        bool canRestart = true;
        Assert.True(MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart));
        Assert.False(canRestart);
        var error = Assert.Throws<MovementController.PathfindingFailedException>(() =>
            MoveExecutor.ShouldRestartAfterStop(task, 958, Vector3.Zero, ref canRestart));
        Assert.Equal(MovementController.PathfindingFailureReason.NotArrived, error.Reason);
    }

    [Fact]
    public void RetryDisabledCannotCompleteFarFromDestination()
    {
        MoveTask task = new(958, new Vector3(100, 0, 0), RestartNavigation: false);
        bool canRestart = task.RestartNavigation;
        Assert.Throws<MovementController.PathfindingFailedException>(() =>
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
    public void TerritoryTransitionDoesNotRestartOldRoute(bool canRestart)
    {
        MoveTask task = new(958, new Vector3(100, 0, 0));
        Assert.False(MoveExecutor.ShouldRestartAfterStop(task, 959, Vector3.Zero, ref canRestart));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavmeshReloadRestartsWithoutUsingUpTheRetry(bool canRestart)
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
    public void NavmeshReloadAfterArrivalOrTransitionCompletes(uint territoryId, float x)
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
        Assert.Throws<MovementController.PathfindingFailedException>(() =>
            MoveExecutor.ShouldRestartAfterStop(task, 958, new Vector3(0, 8, 0), ref canRestart));
    }

    [Fact]
    public void StoppedRouteWaitsOutVnavRepathingBeforeRetrying()
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
    public void MountDuringMovementIsRecheckedAtMostEveryHalfSecond()
    {
        DateTime now = new(2026, 11, 6, 12, 0, 0);
        DateTime nextCheckAt = default;
        Assert.True(MoveExecutor.ShouldRecheckMount(now, ref nextCheckAt));
        Assert.False(MoveExecutor.ShouldRecheckMount(now.AddSeconds(0.49), ref nextCheckAt));
        Assert.True(MoveExecutor.ShouldRecheckMount(now.AddSeconds(0.5), ref nextCheckAt));
        Assert.False(MoveExecutor.ShouldRecheckMount(now.AddSeconds(0.9), ref nextCheckAt));
    }
}
