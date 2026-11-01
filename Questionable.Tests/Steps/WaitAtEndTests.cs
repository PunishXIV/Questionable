// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @eternalwaitt

using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void QuestWaitDamageStartsCombatBeforeReplayingConversationUnlessInFlight(bool turnIn, bool inFlight)
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
        condition[ConditionFlag.InCombat].Returns(true);
        condition[ConditionFlag.InFlight].Returns(inFlight);
        // Bypass native hooks and game-sheet construction, exercising the real damage
        // handler, wait executors and replay queue without invoking native quest reads.
        var controller = (InterruptionController)RuntimeHelpers.GetUninitializedObject(typeof(InterruptionController));
        SetField("_taskQueue", queue);
        SetField("_condition", condition);
        SetField("_logger", NullLogger<InterruptionController>.Instance);

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

        void SetField(string name, object value) =>
            typeof(MiniTaskController<InterruptionController>)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);
    }

    private sealed class InterruptionController()
        : MiniTaskController<InterruptionController>(null!, null!, null!, null!, null!, NullLogger<InterruptionController>.Instance)
    {
        public void ReceiveDamage() => HandleInterruption(this, EventArgs.Empty);

        public override void Stop(string label) => throw new InvalidOperationException($"Unexpected stop: {label}");
    }
}
