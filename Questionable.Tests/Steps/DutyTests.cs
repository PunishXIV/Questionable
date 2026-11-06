using NSubstitute;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class DutyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(357)]
    [InlineData(4646)]
    public void Factory_BlacklistedDutyNeverEmitsAutoDutyForAnyMode(ushort questId)
    {
        using var f = new AutoDutyFixture();
        f.Configuration.Duties.RunUnsynced = true;
        f.Configuration.Duties.BlacklistedDutyCfcIds.Add(AutoDutyFixture.CfcId);
        var (quest, sequence, step) = QuestTestData.FactoryContext(new QuestId(questId), 1,
            new QuestStep { InteractionType = EInteractionType.Duty,
                DutyOptions = new DutyOptions { ContentFinderConditionId = AutoDutyFixture.CfcId, Enabled = true } });
        var tasks = new Duty.Factory(f.Ipc, null!, f.Configuration).CreateAllTasks(quest, sequence, step).ToList();
        Assert.DoesNotContain(tasks, x => x is Duty.StartAutoDutyTask);
        Assert.Single(tasks);
    }

    [Theory]
    [InlineData(1, (int)AutoDutyIpc.DutyMode.Support)]
    [InlineData(357, (int)AutoDutyIpc.DutyMode.Regular)]
    [InlineData(4646, (int)AutoDutyIpc.DutyMode.Variant)]
    public void Factory_UsesOneExecutorAcrossEntryRunAndExit(ushort questId, int mode)
    {
        using var f = new AutoDutyFixture();
        f.Configuration.Duties.RunUnsynced = questId == 357;
        var (quest, sequence, step) = QuestTestData.FactoryContext(new QuestId(questId), 1,
            new QuestStep { InteractionType = EInteractionType.Duty,
                DutyOptions = new DutyOptions { ContentFinderConditionId = AutoDutyFixture.CfcId, Enabled = true } });
        var tasks = new Duty.Factory(f.Ipc, null!, f.Configuration).CreateAllTasks(quest, sequence, step).ToList();
        var duty = Assert.IsType<Duty.StartAutoDutyTask>(tasks[0]);
        Assert.Equal((AutoDutyIpc.DutyMode)mode, duty.DutyMode);
        Assert.Equal(2, tasks.Count); // combined operation, then the existing quest-sequence wait
    }

    [Theory]
    [InlineData(1)]
    [InlineData(357)]
    [InlineData(4646)]
    public void Factory_DisabledPathUsesSamePredicateAsNotifications(ushort questId)
    {
        using var f = new AutoDutyFixture();
        var (quest, sequence, step) = QuestTestData.FactoryContext(new QuestId(questId), 1,
            new QuestStep { InteractionType = EInteractionType.Duty,
                DutyOptions = new DutyOptions { ContentFinderConditionId = AutoDutyFixture.CfcId, Enabled = false } });
        Assert.False(f.Ipc.IsConfiguredToRunContent(step.DutyOptions));
        Assert.DoesNotContain(new Duty.Factory(f.Ipc, null!, f.Configuration).CreateAllTasks(quest, sequence, step),
            x => x is Duty.StartAutoDutyTask);
    }

    [Fact]
    public void Executor_DoesNotCompleteAtEntryOrProviderStopWithoutNativeObjective()
    {
        using var f = new AutoDutyFixture();
        var executor = Executor(f);
        Assert.True(executor.Start(Task()));
        f.Enter();
        Assert.Equal(ETaskResult.StillRunning, executor.Update());
        f.Stopped = true;
        f.Exit();
        f.PollFor(3);
        Assert.Throws<TaskException>(() => executor.Update());
        Assert.False(f.State.HasOperation);
    }

    [Fact]
    public void Executor_CompletesOnlyAfterNativeCompletionExitAndSettingsCleanup()
    {
        using var f = new AutoDutyFixture();
        var executor = Executor(f);
        executor.Start(Task());
        f.Enter();
        f.RaiseCompleted();
        Assert.Equal(ETaskResult.StillRunning, executor.Update());
        f.Exit();
        Assert.Equal(ETaskResult.StillRunning, executor.Update());
        f.Stopped = true;
        f.PollFor(3);
        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
        Assert.False(f.State.HasOperation);
        f.AssertUserProfileRestored();
    }

    [Fact]
    public void Executor_InsufficientGearFailsInsteadOfCompletingTheCombinedTask()
    {
        using var f = new AutoDutyFixture();
        string? notification = null;
        var executor = new Duty.StartAutoDutyExecutor(f.Ipc, _ => AutoDutyFixture.Content, f.Configuration,
            () => 1, text => notification = text);
        Assert.Throws<TaskException>(() => executor.Start(Task()));
        Assert.NotNull(notification);
        Assert.False(f.State.HasOperation);
        f.Run.DidNotReceiveWithAnyArgs().InvokeAction(default, default, default);
    }

    [Fact]
    public void Executor_RechecksConfigurationAtStart()
    {
        using var f = new AutoDutyFixture();
        var executor = Executor(f);
        var task = Task();
        f.Configuration.Duties.BlacklistedDutyCfcIds.Add(AutoDutyFixture.CfcId);
        Assert.Throws<TaskException>(() => executor.Start(task));
        Assert.False(f.State.HasOperation);
    }

    [Fact]
    public void Executor_CannotAdoptOrCancelAnExistingOperationAfterRejectedStart()
    {
        using var f = new AutoDutyFixture();
        f.Start();
        Guid? owner = f.State.OperationId;
        var executor = Executor(f);
        Assert.Throws<TaskException>(() => executor.Start(Task()));
        executor.StopNow();
        Assert.Equal(owner, f.State.OperationId);
        Assert.False(f.State.IsAbandoned);
        f.Stop.DidNotReceive().InvokeAction();
    }

    [Fact]
    public void Executor_StaleStopCannotCancelNewOperation()
    {
        using var f = new AutoDutyFixture();
        var executor = Executor(f);
        executor.Start(Task());
        executor.StopNow();
        f.Stopped = true;
        f.PollFor(3);
        Assert.False(f.State.HasOperation);
        f.Start();
        executor.StopNow();
        Assert.False(f.State.IsAbandoned);
        f.Stop.Received(1).InvokeAction();
    }

    private static Duty.StartAutoDutyExecutor Executor(AutoDutyFixture f) =>
        new(f.Ipc, _ => AutoDutyFixture.Content, f.Configuration, () => 150, _ => { });

    private static Duty.StartAutoDutyTask Task() => new(AutoDutyFixture.CfcId, AutoDutyIpc.DutyMode.Support);
}
