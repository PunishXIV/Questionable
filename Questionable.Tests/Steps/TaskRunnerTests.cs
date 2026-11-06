using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Model.Questing;
using Xunit;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Tests.Steps;

public sealed class TaskRunnerTests
{
    [Theory]
    [InlineData(false, "missing")]
    [InlineData(false, "factory")]
    [InlineData(false, "start")]
    [InlineData(true, "missing")]
    [InlineData(true, "factory")]
    [InlineData(true, "start")]
    public void TaskCreationFailuresStopAndReportWithoutStartingReplacement(bool skipping, string failurePoint)
    {
        var expected = new InvalidOperationException("executor failed");
        var failing = Substitute.For<ITaskExecutor, IStoppableTaskExecutor>();
        failing.Start(Arg.Any<ITask>()).Returns(_ => throw expected);
        ServiceCollection registrations = [];
        if (failurePoint != "missing")
            registrations.AddKeyedTransient<ITaskExecutor>(typeof(WaitAtEnd.NextStep), (_, _) =>
                failurePoint == "factory" ? throw expected : failing);
        int replacementStarts = 0;
        registrations.AddKeyedTransient<ITaskExecutor>(typeof(WaitAtEnd.WaitDelay), (_, _) =>
        {
            replacementStarts++;
            return new WaitAtEnd.WaitDelayExecutor();
        });
        using var provider = registrations.BuildServiceProvider();
        using var fixture = new TaskRunnerFixture(provider: provider);
        var final = new WaitAtEnd.NextStep(new QuestId(123), 1);
        var previous = Prepare(fixture, skipping, final);

        fixture.Controller.Tick();

        Assert.Equal("Task failed to start", Assert.Single(fixture.Controller.Stops));
        var failure = Assert.Single(fixture.Controller.Failures);
        Assert.Same(final, failure.Task);
        if (failurePoint != "missing")
            Assert.Same(expected, failure.Error);
        Assert.Single(fixture.Log.Entries, x => x.Level == LogLevel.Error && ReferenceEquals(x.Exception, failure.Error));
        var chat = Assert.Single(fixture.Chat.ReceivedCalls(), x => x.GetMethodInfo().Name == "PrintError");
        Assert.Contains("Failed to start task", chat.GetArguments()[0]!.ToString());
        Assert.Contains("Failed to start task", Assert.Single(fixture.Notifications));
        Assert.True(fixture.Queue.AllTasksComplete);
        Assert.Equal(0, fixture.Controller.CompletedTasks);
        if (skipping)
            ((IStoppableTaskExecutor)previous!).Received(1).StopNow();
        if (failurePoint == "start")
            ((IStoppableTaskExecutor)failing).Received(1).StopNow();

        fixture.Controller.Tick();
        Assert.Equal(0, replacementStarts);
        Assert.Single(fixture.Controller.Stops);
        Assert.Single(fixture.Notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastTaskAfterSkip_KeepsStartupResult(bool starts)
    {
        var finalExecutor = Substitute.For<ITaskExecutor>();
        finalExecutor.Start(Arg.Any<ITask>()).Returns(starts);
        ServiceCollection registrations = [];
        registrations.AddKeyedTransient<ITaskExecutor>(typeof(WaitAtEnd.NextStep), (_, _) => finalExecutor);
        using var provider = registrations.BuildServiceProvider();
        using var fixture = new TaskRunnerFixture(provider: provider);
        var final = new WaitAtEnd.NextStep(new QuestId(123), 1);
        Prepare(fixture, skipping: true, final);

        fixture.Controller.Tick();

        finalExecutor.Received(1).Start(final);
        Assert.Same(starts ? finalExecutor : null, fixture.Queue.CurrentTaskExecutor);
        Assert.IsType<WaitAtEnd.WaitDelay>(Assert.Single(fixture.Queue.RemainingTasks));
        Assert.Empty(fixture.Controller.Stops);
        Assert.Empty(fixture.Controller.Failures);
        Assert.Empty(fixture.Notifications);
    }

    [Fact]
    public void UpdateFailureUsesInjectedNotificationsAndStopsQueue()
    {
        using var fixture = new TaskRunnerFixture();
        var task = new WaitAtEnd.WaitDelay();
        var executor = Substitute.For<ITaskExecutor>();
        var expected = new IOException("update failed");
        executor.Start(task).Returns(true);
        executor.CurrentTask.Returns(task);
        executor.Update().Returns(_ => throw expected);
        Assert.True(fixture.Queue.StartTask(executor, task));
        fixture.Queue.Enqueue(new WaitAtEnd.WaitDelay());

        fixture.Controller.Tick();

        Assert.Equal("Task failed to update", Assert.Single(fixture.Controller.Stops));
        Assert.Same(expected, Assert.Single(fixture.Controller.Failures).Error);
        Assert.Contains("update failed", Assert.Single(fixture.Notifications));
        Assert.Single(fixture.Chat.ReceivedCalls(), x => x.GetMethodInfo().Name == "PrintError");
        Assert.True(fixture.Queue.AllTasksComplete);
    }

    [Fact]
    public void DisposalUnsubscribesExactlyOnceAndLateDamageDoesNotMutateQueue()
    {
        using var fixture = new TaskRunnerFixture();
        Assert.Equal(1, fixture.Interruptions.SubscriberCount);
        fixture.Controller.Dispose();
        fixture.Controller.Dispose();
        var executor = new WaitAtEnd.WaitDelayExecutor();
        Assert.True(fixture.Queue.StartTask(executor, new WaitAtEnd.WaitDelay { InterruptOnDamage = true }));

        fixture.ReceiveDamage();

        Assert.Equal(0, fixture.Interruptions.SubscriberCount);
        Assert.Equal(1, fixture.Interruptions.RemoveCalls);
        Assert.Same(executor, fixture.Queue.CurrentTaskExecutor);
        Assert.Empty(fixture.Queue.RemainingTasks);
    }

    [Fact]
    public void FailedSubscriptionRollsBackPartiallyAddedHandler()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var source = new TaskRunnerFixture.TestInterruptionSource { ThrowAfterAdding = true };

        var error = Assert.Throws<InvalidOperationException>(() => new TaskRunnerFixture.Runner(
            Substitute.For<IChatGui>(), Substitute.For<ICondition>(), provider, source,
            TaskRunnerFixture.CreateToasts(), _ => { }, new TaskRunnerFixture.RecordingLogger()));

        Assert.Equal("subscription failed after adding handler", error.Message);
        Assert.Equal(0, source.SubscriberCount);
        Assert.Equal(1, source.RemoveCalls);
        source.Raise();
    }

    [Theory]
    [InlineData("Action canceled. You are under attack.", true, false, true)]
    [InlineData("Action canceled. You are under attack.", true, true, false)]
    [InlineData("Action canceled. You are under attack.", false, false, false)]
    [InlineData("Event canceled.", false, false, true)]
    [InlineData("Unable to execute command while suffering status affliction.", false, false, true)]
    [InlineData("Unrelated error", true, false, false)]
    public void ToastHandlingPreservesDamageFlightAndNonCombatRules(string text, bool damage, bool flight, bool interrupted)
    {
        using var fixture = new TaskRunnerFixture();
        fixture.Condition[ConditionFlag.InFlight].Returns(flight);
        var executor = new WaitAtEnd.WaitDelayExecutor();
        Assert.True(fixture.Queue.StartTask(executor, new WaitAtEnd.WaitDelay { InterruptOnDamage = damage }));
        SeString message = text;
        bool handled = false;

        fixture.Controller.OnErrorToast(ref message, ref handled);

        Assert.False(handled);
        Assert.Same(interrupted ? null : executor, fixture.Queue.CurrentTaskExecutor);
        Assert.Equal(interrupted && text.StartsWith("Action", StringComparison.Ordinal),
            fixture.Queue.RemainingTasks.Any(x => x is Combat.CheckAfterInterruption));
    }

    [Theory]
    [InlineData(false, "Event canceled.")]
    [InlineData(true, "Event canceled.")]
    [InlineData(false, "Unable to execute command while suffering status affliction.")]
    [InlineData(true, "Unable to execute command while suffering status affliction.")]
    public void IdleCancellationToastDoesNotReplayCompletedWork(bool completedStep, string text)
    {
        var interaction = new Interact.Task(100, null, EInteractionType.Interact);
        var executor = Substitute.For<ITaskExecutor>();
        executor.Start(interaction).Returns(true);
        executor.CurrentTask.Returns(interaction);
        executor.Update().Returns(ETaskResult.TaskComplete);
        ServiceCollection services = [];
        services.AddKeyedTransient<ITaskExecutor>(typeof(Interact.Task), (_, _) => executor);
        services.AddKeyedTransient<ITaskExecutor>(typeof(WaitAtEnd.NextStep), (_, _) => new WaitAtEnd.NextStepExecutor());
        using var provider = services.BuildServiceProvider();
        using var fixture = new TaskRunnerFixture(provider: provider);
        if (completedStep)
        {
            fixture.Queue.Enqueue(interaction);
            fixture.Queue.Enqueue(new WaitAtEnd.NextStep(new QuestId(123), 1));
            fixture.Controller.Tick(); // Start interaction.
            fixture.Controller.Tick(); // Complete interaction.
            fixture.Controller.Tick(); // Start final step marker.
            fixture.Controller.Tick(); // Complete step; no next step is enqueued.
            Assert.Equal(1, fixture.Controller.CompletedTasks);
        }
        Assert.True(fixture.Queue.AllTasksComplete);
        SeString message = text;
        bool handled = false;

        fixture.Controller.OnErrorToast(ref message, ref handled);

        Assert.Empty(fixture.Queue.RemainingTasks);
        Assert.True(fixture.Queue.AllTasksComplete);
        Assert.Empty(fixture.Controller.Stops);
        Assert.Empty(fixture.Notifications);
    }

    [Theory]
    [InlineData("Event canceled.")]
    [InlineData("Unable to execute command while suffering status affliction.")]
    public void CancellationToastBetweenTasksStillReplaysCompletedInteraction(string text)
    {
        var interaction = new Interact.Task(100, null, EInteractionType.Interact);
        var executor = Substitute.For<ITaskExecutor>();
        executor.Start(interaction).Returns(true);
        executor.CurrentTask.Returns(interaction);
        executor.Update().Returns(ETaskResult.TaskComplete);
        ServiceCollection services = [];
        services.AddKeyedTransient<ITaskExecutor>(typeof(Interact.Task), (_, _) => executor);
        using var provider = services.BuildServiceProvider();
        using var fixture = new TaskRunnerFixture(provider: provider);
        var next = new WaitAtEnd.NextStep(new QuestId(123), 1);
        fixture.Queue.Enqueue(interaction);
        fixture.Queue.Enqueue(next);
        fixture.Controller.Tick(); // Start interaction.
        fixture.Controller.Tick(); // Complete interaction; final marker remains pending.
        Assert.Equal(1, fixture.Controller.CompletedTasks);
        Assert.Null(fixture.Queue.CurrentTaskExecutor);
        Assert.Same(next, Assert.Single(fixture.Queue.RemainingTasks));
        SeString message = text;
        bool handled = false;

        fixture.Controller.OnErrorToast(ref message, ref handled);

        Assert.Null(fixture.Queue.CurrentTaskExecutor);
        Assert.Collection(fixture.Queue.RemainingTasks,
            task => Assert.IsType<WaitAtEnd.WaitDelay>(task),
            task => Assert.Same(interaction, task),
            task => Assert.Same(next, task));
        Assert.Empty(fixture.Controller.Stops);
        Assert.Empty(fixture.Notifications);
    }

    [Theory]
    [InlineData("Event canceled.")]
    [InlineData("Unable to execute command while suffering status affliction.")]
    public void IdleCancellationToastDoesNotStopBlockedProviderWork(string text)
    {
        using var fixture = new TaskRunnerFixture();
        fixture.Controller.AllowNewTasks = false;
        SeString message = text;
        bool handled = false;

        fixture.Controller.OnErrorToast(ref message, ref handled);

        Assert.True(fixture.Queue.AllTasksComplete);
        Assert.Empty(fixture.Queue.RemainingTasks);
        Assert.Empty(fixture.Controller.Stops);
        Assert.Empty(fixture.Notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandledToastDoesNotScheduleRecovery(bool alreadyHandled)
    {
        using var fixture = new TaskRunnerFixture();
        var executor = Substitute.For<ITaskExecutor, IToastAware>();
        executor.Start(Arg.Any<ITask>()).Returns(true);
        executor.ShouldInterruptOnDamage().Returns(true);
        ((IToastAware)executor).OnErrorToast(Arg.Any<SeString>()).Returns(!alreadyHandled);
        Assert.True(fixture.Queue.StartTask(executor, new WaitAtEnd.WaitDelay()));
        SeString message = "Action canceled. You are under attack.";
        bool handled = alreadyHandled;

        fixture.Controller.OnErrorToast(ref message, ref handled);

        Assert.True(handled);
        Assert.Same(executor, fixture.Queue.CurrentTaskExecutor);
        Assert.Empty(fixture.Queue.RemainingTasks);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void OverlappingToastFallsBackToNonCombatWhenDamageRecoveryIsDisallowed(bool flight, bool damage)
    {
        var toasts = new TaskToastClassifier("overlap", new("^overlap$"), "status");
        using var fixture = new TaskRunnerFixture(toasts: toasts);
        fixture.Condition[ConditionFlag.InFlight].Returns(flight);
        var original = new WaitAtEnd.WaitDelay { InterruptOnDamage = damage };
        Assert.True(fixture.Queue.StartTask(new WaitAtEnd.WaitDelayExecutor(), original));
        SeString message = "overlap";
        bool handled = false;

        fixture.Controller.OnErrorToast(ref message, ref handled);

        Assert.Null(fixture.Queue.CurrentTaskExecutor);
        Assert.Collection(fixture.Queue.RemainingTasks,
            task => Assert.IsType<WaitAtEnd.WaitDelay>(task),
            task => Assert.Same(original, task));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationToastDoesNotInterruptDutyProviders(bool solo)
    {
        using var operations = new ExternalOperationFixture();
        using var fixture = new TaskRunnerFixture();
        ITaskExecutor executor;
        ITask task;
        if (solo)
        {
            executor = new SinglePlayerDuty.WaitSinglePlayerDutyExecutor(operations.BossMod.Ipc, operations.Movement);
            task = new SinglePlayerDuty.WaitSinglePlayerDuty(AutoDutyFixture.CfcId);
        }
        else
        {
            executor = new Duty.StartAutoDutyExecutor(operations.Duty.Ipc, _ => AutoDutyFixture.Content,
                operations.Duty.Configuration, () => 100, _ => { });
            task = new Duty.StartAutoDutyTask(AutoDutyFixture.CfcId, AutoDutyIpc.DutyMode.Support);
        }
        Assert.True(fixture.Queue.StartTask(executor, task));
        SeString message = "Event canceled.";
        bool handled = false;

        fixture.Controller.OnErrorToast(ref message, ref handled);

        Assert.Same(executor, fixture.Queue.CurrentTaskExecutor);
        Assert.Empty(fixture.Queue.RemainingTasks);
        operations.Duty.Stop.DidNotReceive().InvokeAction();
        fixture.Queue.Reset();
    }

    private static ITaskExecutor? Prepare(TaskRunnerFixture fixture, bool skipping, ITask final)
    {
        ITaskExecutor? previous = null;
        if (skipping)
        {
            previous = Substitute.For<ITaskExecutor, IStoppableTaskExecutor>();
            var current = new WaitAtEnd.WaitDelay();
            previous.Start(current).Returns(true);
            previous.CurrentTask.Returns(current);
            previous.Update().Returns(ETaskResult.SkipRemainingTasksForStep);
            Assert.True(fixture.Queue.StartTask(previous, current));
            fixture.Queue.Enqueue(new WaitAtEnd.WaitDelay()); // Must be discarded before the final task.
        }
        fixture.Queue.Enqueue(final);
        fixture.Queue.Enqueue(new WaitAtEnd.WaitDelay()); // Must not start if the final task fails.
        return previous;
    }
}
