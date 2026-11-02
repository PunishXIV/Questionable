// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @eternalwaitt

using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
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
    public void WaitDelay_UsesConfiguredDamageInterruption(bool interruptOnDamage)
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
    public void QuestWaitDamageStartsCombatBeforeReplayingConversationUnlessInFlight(bool turnIn, bool inFlight, bool inCombat)
    {
        QuestId questId = new(123);
        ITask wait = turnIn
            ? new WaitAtEnd.WaitQuestCompleted(questId)
            : new WaitAtEnd.WaitQuestAccepted(questId);
        ITaskExecutor executor = turnIn
            ? new WaitAtEnd.WaitQuestCompletedExecutor(null!)
            : new WaitAtEnd.WaitQuestAcceptedExecutor(null!, null!);
        Interact.Task interaction = new(100, null,
            turnIn ? EInteractionType.CompleteQuest : EInteractionType.AcceptQuest);
        TaskQueue queue = new(NullLogger.Instance);
        queue.Enqueue(interaction);
        Assert.True(queue.TryDequeue(out _)); // Interaction completed before the quest-state wait.
        queue.Enqueue(wait);
        Assert.True(queue.TryDequeue(out ITask? current));
        Assert.True(queue.StartTask(executor, current));
        WaitAtEnd.WaitDelay pendingDelay = new(TimeSpan.FromSeconds(1));
        WaitAtEnd.NextStep next = new(questId, 0);
        queue.Enqueue(pendingDelay);
        queue.Enqueue(next);

        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.InCombat].Returns(inCombat);
        condition[ConditionFlag.InFlight].Returns(inFlight);
        var controller = CreateController(queue, condition);

        controller.ReceiveDamage();

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
    public void EarlyDamageChecksCombatAfterRecoveryBeforeReplayingQuestWait(bool turnIn, bool mounted, bool entersCombat)
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.Mounted].Returns(mounted);
        TaskQueue queue = new(NullLogger.Instance);
        Interact.Task interaction = new(100, null,
            turnIn ? EInteractionType.CompleteQuest : EInteractionType.AcceptQuest);
        queue.Enqueue(interaction);
        Assert.True(queue.TryDequeue(out _));
        ITask wait = turnIn
            ? new WaitAtEnd.WaitQuestCompleted(new QuestId(123))
            : new WaitAtEnd.WaitQuestAccepted(new QuestId(123));
        ITaskExecutor executor = turnIn
            ? new WaitAtEnd.WaitQuestCompletedExecutor(null!)
            : new WaitAtEnd.WaitQuestAcceptedExecutor(null!, null!);
        queue.Enqueue(wait);
        Assert.True(queue.TryDequeue(out ITask? current));
        Assert.True(queue.StartTask(executor, current));
        WaitAtEnd.NextStep next = new(new QuestId(123), 0);
        queue.Enqueue(next);

        // Use the production keyed registrations and controller update path to exercise
        // task insertion ahead of replay, without touching native interaction executors.
        IServiceCollection services = new ServiceCollection();
        ServiceCollectionExtensions.AddTaskRegistrations(services);
        services.AddSingleton(condition);
        using ServiceProvider provider = services.BuildServiceProvider();
        var controller = CreateController(queue, condition, provider);
        controller.ReceiveDamage();
        Assert.Collection(queue.RemainingTasks,
            task => Assert.Equal(TimeSpan.FromMilliseconds(750), Assert.IsType<WaitAtEnd.WaitDelay>(task).Delay),
            task => Assert.IsType<Combat.CheckAfterInterruption>(task),
            task => Assert.Same(interaction, task),
            task => Assert.Same(wait, task),
            task => Assert.Same(next, task));

        controller.ReceiveDamage(); // No executor between tasks: do not duplicate replay.
        controller.Tick(); // Start the existing recovery delay.
        var delay = Assert.IsType<WaitAtEnd.WaitDelayExecutor>(queue.CurrentTaskExecutor);
        condition[ConditionFlag.InCombat].Returns(entersCombat);
        controller.ReceiveDamage(); // Recovery itself is not damage-interruptible.
        typeof(AbstractDelayedTaskExecutor<WaitAtEnd.WaitDelay>)
            .GetField("_continueAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(delay, DateTime.MinValue);
        controller.Tick(); // Complete recovery without a wall-clock sleep.
        controller.Tick(); // Start the late check.
        controller.ReceiveDamage();
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
    public void LateCombatCheckSkipsWhenCombatEndedBeforeDecision()
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
    public void AlreadyInCombatMountedInterruptionKeepsImmediateUnmountAndCombatOrder()
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.InCombat].Returns(true);
        condition[ConditionFlag.Mounted].Returns(true);
        TaskQueue queue = new(NullLogger.Instance);
        WaitAtEnd.WaitDelay task = new() { InterruptOnDamage = true };
        Assert.True(queue.StartTask(new WaitAtEnd.WaitDelayExecutor(), task));

        CreateController(queue, condition).ReceiveDamage();

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
        TaskQueue queue = new(NullLogger.Instance);
        Assert.True(queue.StartTask(new WaitAtEnd.WaitDelayExecutor(),
            new WaitAtEnd.WaitDelay { InterruptOnDamage = true }));
        var controller = CreateController(queue, condition);
        controller.ReceiveDamage();
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

    private static InterruptionController CreateController(TaskQueue queue, ICondition condition,
        IServiceProvider? provider = null)
    {
        // Bypass native hooks and game-sheet construction, exercising the real damage
        // handler, wait executors and replay queue without invoking native quest reads.
        var controller = (InterruptionController)RuntimeHelpers.GetUninitializedObject(typeof(InterruptionController));
        SetField("_taskQueue", queue);
        SetField("_condition", condition);
        SetField("_logger", NullLogger<InterruptionController>.Instance);
        if (provider != null)
            SetField("_serviceProvider", provider);
        return controller;

        void SetField(string name, object value) =>
            typeof(MiniTaskController<InterruptionController>)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);
    }

    private sealed class InterruptionController()
        : MiniTaskController<InterruptionController>(null!, null!, null!, null!, null!, NullLogger<InterruptionController>.Instance)
    {
        public void ReceiveDamage() => HandleInterruption(this, EventArgs.Empty);

        public void Tick() => UpdateCurrentTask();

        public override void Stop(string label)
        {
            Assert.Equal("test stop", label);
            Assert.True(_taskQueue.Reset());
        }
    }
}
