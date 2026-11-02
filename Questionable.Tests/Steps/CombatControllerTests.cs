using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using Questionable.Controller;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CombatControllerTests : IDisposable
{
    private readonly ICombatModule _module = Substitute.For<ICombatModule>();
    private readonly CombatController _controller;

    public CombatControllerTests()
    {
        _module.CanHandleFight(Arg.Any<CombatController.CombatData>()).Returns(true);
        _module.Start(Arg.Any<CombatController.CombatData>()).Returns(true);
        _module.Stop().Returns(true);
        var framework = Substitute.For<IFramework>();
        framework.IsInFrameworkUpdateThread.Returns(true);
        _controller = new CombatController([_module], null!, Substitute.For<ITargetManager>(),
            Substitute.For<IObjectTable>(), Substitute.For<ICondition>(), Substitute.For<IClientState>(),
            null!, null!, framework, NullLogger<CombatController>.Instance);
    }

    public void Dispose() => _controller.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedOrThrowingStart_RollsBackAndCannotBeSkipped(bool throws)
    {
        _module.Start(Arg.Any<CombatController.CombatData>()).Returns(_ =>
            throws ? throw new InvalidOperationException("partially started") : false);
        TaskQueue queue = new(NullLogger.Instance);
        Assert.ThrowsAny<Exception>(() => queue.StartTask(new Combat.HandleCombat(_controller),
            new Combat.Task(CombatTestData.Create(), [], false)));
        _module.Received(1).Stop();
        Assert.False(_controller.IsRunning);
        Assert.False(_controller.HasPendingCleanup);
    }

    [Fact]
    public void MissingProvider_StopsRatherThanSkippingCombatTask()
    {
        _module.CanHandleFight(Arg.Any<CombatController.CombatData>()).Returns(false);
        Assert.Throws<TaskException>(() => _controller.Start(CombatTestData.Create()));
        _module.DidNotReceive().Start(Arg.Any<CombatController.CombatData>());
    }

    [Fact]
    public void OptionalCombatWithNoFight_SkipsWithoutProvider()
    {
        _module.CanHandleFight(Arg.Any<CombatController.CombatData>()).Returns(false);
        Assert.False(_controller.Start(CombatTestData.Create(EEnemySpawnType.FinishCombatIfAny)));
        _module.DidNotReceive().Start(Arg.Any<CombatController.CombatData>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCleanup_BlocksReplacementAndRetainsModuleUntilRetry(bool throws)
    {
        Assert.True(_controller.Start(CombatTestData.Create()));
        _module.Stop().Returns(_ => throws ? throw new InvalidOperationException("unavailable") : false);
        _controller.Stop("cancelled");
        Assert.False(_controller.IsRunning);
        Assert.True(_controller.HasPendingCleanup);
        Assert.NotNull(_controller.BlockReason);
        Assert.Throws<TaskException>(() => _controller.Start(CombatTestData.Create()));
        _module.Received(1).Start(Arg.Any<CombatController.CombatData>());
        _module.Configure().Stop().Returns(true);
        _controller.TryRetryCleanup();
        Assert.False(_controller.HasPendingCleanup);
        Assert.Null(_controller.BlockReason);
        Assert.True(_controller.Start(CombatTestData.Create()));
    }

    [Fact]
    public void FailedStartAndRollback_RetainCleanup()
    {
        _module.Start(Arg.Any<CombatController.CombatData>()).Returns(false);
        _module.Stop().Returns(false);
        Assert.Throws<TaskException>(() => _controller.Start(CombatTestData.Create()));
        Assert.True(_controller.HasPendingCleanup);
        Assert.False(_controller.IsRunning);
        _module.Stop().Returns(true);
        _controller.TryRetryCleanup();
        Assert.False(_controller.HasPendingCleanup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostOwnership_StopsBeforeAccessingMovementOrTargets(bool throwsTaskException)
    {
        Assert.True(_controller.Start(CombatTestData.Create()));
        Exception failure = throwsTaskException
            ? new TaskException("lease cancelled")
            : new InvalidOperationException("provider disappeared");
        _module.When(x => x.VerifyActive()).Do(_ => throw failure);
        TaskException reported = Assert.Throws<TaskException>(() => _controller.Update());
        Assert.Same(failure, throwsTaskException ? reported : reported.InnerException);
        _module.Received(1).Stop();
        Assert.False(_controller.IsRunning);
    }
}
