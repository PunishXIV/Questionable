// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @eternalwaitt

using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class WaitAtEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaitDelay_UsesDamageSetting(bool interruptOnDamage)
    {
        WaitAtEnd.WaitDelay task = new() { InterruptOnDamage = interruptOnDamage };
        WaitAtEnd.WaitDelayExecutor executor = new();

        Assert.True(executor.Start(task));
        Assert.Equal(interruptOnDamage, executor.ShouldInterruptOnDamage());
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void QuestWaitDamage_CombatBeforeReplay(bool turnIn, bool inFlight, bool inCombat)
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.InCombat].Returns(inCombat);
        condition[ConditionFlag.InFlight].Returns(inFlight);
        using var fixture = new TaskRunnerFixture(condition);
        QuestId questId = new(123);
        ITask wait = turnIn
            ? new WaitAtEnd.QuestCompleted(questId)
            : new WaitAtEnd.QuestAccepted(questId);
        ITaskExecutor executor = turnIn
            ? new WaitAtEnd.QuestCompletedExecutor(null!)
            : new WaitAtEnd.QuestAcceptedExecutor(null!, null!);
        Interact.Task interaction = new(100, null,
            turnIn ? EInteractionType.CompleteQuest : EInteractionType.AcceptQuest);
        TaskQueue queue = fixture.Queue;
        queue.Enqueue(interaction);
        Assert.True(queue.TryDequeue(out _)); // Interaction completed before the quest-state wait.
        queue.Enqueue(wait);
        Assert.True(queue.TryDequeue(out ITask? current));
        Assert.True(queue.StartTask(executor, current));
        WaitAtEnd.WaitDelay pendingDelay = new(TimeSpan.FromSeconds(1));
        WaitAtEnd.NextStep next = new(questId, 0);
        queue.Enqueue(pendingDelay);
        queue.Enqueue(next);

        fixture.ReceiveDamage();

        if (inFlight)
        {
            Assert.Same(executor, queue.CurrentTaskExecutor);
            Assert.Equal(new ITask[] { pendingDelay, next }, queue.RemainingTasks);
            return;
        }

        Assert.Null(queue.CurrentTaskExecutor);
        Assert.Collection(queue.RemainingTasks,
            task => Assert.Equal(EEnemySpawnType.QuestInterruption,
                Assert.IsType<Combat.Task>(task).CombatData.SpawnType),
            task => Assert.IsType<WaitAtEnd.WaitDelay>(task),
            task => Assert.Same(interaction, task),
            task => Assert.Same(wait, task),
            task => Assert.Same(pendingDelay, task),
            task => Assert.Same(next, task));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void EarlyDamage_ChecksCombatBeforeReplay(bool turnIn, bool mounted, bool entersCombat)
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.Mounted].Returns(mounted);

        IServiceCollection services = new ServiceCollection();
        ServiceCollectionExtensions.AddTaskRegistrations(services);
        services.AddSingleton(condition);
        using ServiceProvider provider = services.BuildServiceProvider();
        using var fixture = new TaskRunnerFixture(condition, provider);
        var controller = fixture.Controller;
        TaskQueue queue = fixture.Queue;
        Interact.Task interaction = new(100, null,
            turnIn ? EInteractionType.CompleteQuest : EInteractionType.AcceptQuest);
        queue.Enqueue(interaction);
        Assert.True(queue.TryDequeue(out _));
        ITask wait = turnIn
            ? new WaitAtEnd.QuestCompleted(new QuestId(123))
            : new WaitAtEnd.QuestAccepted(new QuestId(123));
        ITaskExecutor executor = turnIn
            ? new WaitAtEnd.QuestCompletedExecutor(null!)
            : new WaitAtEnd.QuestAcceptedExecutor(null!, null!);
        queue.Enqueue(wait);
        Assert.True(queue.TryDequeue(out ITask? current));
        Assert.True(queue.StartTask(executor, current));
        WaitAtEnd.NextStep next = new(new QuestId(123), 0);
        queue.Enqueue(next);

        fixture.ReceiveDamage();
        Assert.Collection(queue.RemainingTasks,
            task => Assert.Equal(TimeSpan.FromMilliseconds(750), Assert.IsType<WaitAtEnd.WaitDelay>(task).Delay),
            task => Assert.IsType<Combat.CheckAfterInterruption>(task),
            task => Assert.Same(interaction, task),
            task => Assert.Same(wait, task),
            task => Assert.Same(next, task));

        fixture.ReceiveDamage(); // No executor between tasks: do not duplicate replay.
        controller.Tick(); // Start the existing recovery delay.
        var delay = Assert.IsType<WaitAtEnd.WaitDelayExecutor>(queue.CurrentTaskExecutor);
        condition[ConditionFlag.InCombat].Returns(entersCombat);
        fixture.ReceiveDamage(); // Recovery itself is not damage-interruptible.
        typeof(DelayedTaskExecutor<WaitAtEnd.WaitDelay>)
            .GetField("_continueAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(delay, DateTime.MinValue);
        controller.Tick(); // Complete recovery without a wall-clock sleep.
        controller.Tick(); // Start the late check.
        fixture.ReceiveDamage();
        controller.Tick(); // Insert combat ahead of the original replay, if now needed.

        ITask[] remaining = queue.RemainingTasks.ToArray();
        int index = 0;
        if (entersCombat)
        {
            if (mounted)
                Assert.IsType<MountStep.UnmountTask>(remaining[index++]);
            Combat.Task combat = Assert.IsType<Combat.Task>(remaining[index++]);
            Assert.Equal(EEnemySpawnType.FinishCombatIfAny, combat.CombatData.SpawnType);
            Assert.False(combat.IsLastStep);
        }
        Assert.Equal(new ITask[] { interaction, wait, next }, remaining[index..]);
        Assert.Null(queue.CurrentTaskExecutor);
    }

    [Fact]
    public void LateCombatCheck_SkipsIfCombatEnded()
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.InCombat].Returns(true);
        condition[ConditionFlag.Mounted].Returns(true);
        Combat.CheckAfterInterruptionExecutor executor = new(condition);
        Assert.True(executor.Start(new Combat.CheckAfterInterruption()));

        condition[ConditionFlag.InCombat].Returns(false);

        Assert.Equal(ETaskResult.CreateNewTasks, executor.Update());
        Assert.Empty(executor.CreateExtraTasks());
    }

    [Fact]
    public void MountedInCombat_UnmountsThenFights()
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.InCombat].Returns(true);
        condition[ConditionFlag.Mounted].Returns(true);
        using var fixture = new TaskRunnerFixture(condition);
        TaskQueue queue = fixture.Queue;
        WaitAtEnd.WaitDelay task = new() { InterruptOnDamage = true };
        Assert.True(queue.StartTask(new WaitAtEnd.WaitDelayExecutor(), task));

        fixture.ReceiveDamage();

        Assert.Collection(queue.RemainingTasks,
            item => Assert.IsType<MountStep.UnmountTask>(item),
            item => Assert.Equal(EEnemySpawnType.QuestInterruption,
                Assert.IsType<Combat.Task>(item).CombatData.SpawnType),
            item => Assert.IsType<WaitAtEnd.WaitDelay>(item),
            item => Assert.Same(task, item));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopDuringRecoveryClearsLateCombatCheck(bool checkStarted)
    {
        var condition = Substitute.For<ICondition>();
        using var fixture = new TaskRunnerFixture(condition);
        var controller = fixture.Controller;
        TaskQueue queue = fixture.Queue;
        Assert.True(queue.StartTask(new WaitAtEnd.WaitDelayExecutor(),
            new WaitAtEnd.WaitDelay { InterruptOnDamage = true }));
        fixture.ReceiveDamage();
        Assert.True(queue.TryDequeue(out ITask? delay));
        if (checkStarted)
        {
            Assert.True(queue.TryDequeue(out ITask? check));
            Assert.True(queue.StartTask(new Combat.CheckAfterInterruptionExecutor(condition), check));
        }
        else
            Assert.True(queue.StartTask(new WaitAtEnd.WaitDelayExecutor(), delay));

        controller.Stop("test stop");
        condition[ConditionFlag.InCombat].Returns(true);
        controller.Tick();

        Assert.True(queue.AllTasksComplete);
    }

    [Theory]
    [InlineData(nameof(ETaskResult.TaskComplete), false)]
    [InlineData(nameof(ETaskResult.TaskComplete), true)]
    [InlineData(nameof(ETaskResult.CreateNewTasks), false)]
    [InlineData(nameof(ETaskResult.CreateNewTasks), true)]
    [InlineData(nameof(ETaskResult.NextStep), false)]
    [InlineData(nameof(ETaskResult.NextStep), true)]
    [InlineData(nameof(ETaskResult.RetryStep), false)]
    [InlineData(nameof(ETaskResult.RetryStep), true)]
    [InlineData(nameof(ETaskResult.End), false)]
    [InlineData(nameof(ETaskResult.End), true)]
    public void QueueChangedInUpdate_DropsStaleResult(string result, bool replace)
    {
        using var fixture = new TaskRunnerFixture();
        var controller = fixture.Controller;
        TaskQueue queue = fixture.Queue;
        var executor = Substitute.For<ITaskExecutor>();
        var replacement = Substitute.For<ITaskExecutor>();
        executor.Start(Arg.Any<ITask>()).Returns(true);
        replacement.Start(Arg.Any<ITask>()).Returns(true);
        WaitAtEnd.QuestAccepted accepted = new(new QuestId(2881));
        Assert.True(queue.StartTask(executor, accepted));
        queue.Enqueue(new WaitAtEnd.NextStep(new QuestId(2552), 1));
        executor.Update().Returns(_ =>
        {
            controller.Stop("test stop");
            if (replace)
                Assert.True(queue.StartTask(replacement, new WaitAtEnd.WaitDelay()));
            return Enum.Parse<ETaskResult>(result);
        });

        controller.Tick();

        Assert.Empty(queue.RemainingTasks);
        Assert.Same(replace ? replacement : null, queue.CurrentTaskExecutor);
        Assert.Equal(0, controller.CompletedTasks);
        replacement.DidNotReceive().Update();
    }

}
