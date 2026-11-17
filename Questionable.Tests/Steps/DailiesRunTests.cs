// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Reflection;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Tests.Steps;

public sealed class DailiesRunTests
{
    [Fact]
    public void MidSequence_DoesNotChangeDaily()
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.State.Accepted.Add(123);
        context.State.Sequences[123] = 1;
        context.Controller.CurrentQuest!.SetStep(1);
        context.Run.Choice = QuestTestData.CreateQuest(new QuestId(124));

        Assert.True(context.Invoke("ChooseDaily"));

        Assert.Equal(new QuestId(123), context.Controller.CurrentQuest!.Quest.Id);
        Assert.Equal(1, context.Controller.CurrentQuest.Step);
        Assert.Equal(0, context.Run.Choices);
    }

    [Fact]
    public void SequenceChange_AllowsRouteChoice()
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.State.Accepted.Add(123);
        context.State.Sequences[123] = 2;
        context.Controller.CurrentQuest!.SetStep(1);
        context.Run.Choice = QuestTestData.CreateQuest(new QuestId(124));

        Assert.True(context.Invoke("ChooseDaily"));

        Assert.Equal(new QuestId(124), context.Controller.CurrentQuest!.Quest.Id);
        Assert.Equal(1, context.Run.Choices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyEnd_UsesTodaysCompletion(bool today)
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.State.Accepted.Add(123);
        context.State.Complete.Add(123);
        Assert.True(context.Invoke("ObserveDaily"));
        context.State.Accepted.Clear();
        if (today)
            context.State.DailyCompleted.Add(123);
        else
            TestQuestController.Set(context.Controller, "_dailyMissingSince", DateTime.UtcNow.AddSeconds(-3));

        Assert.True(context.Invoke("ObserveDaily"));

        Assert.Equal(today ? 1 : 0, context.Run.CompletedIds.Count);
        Assert.Equal(today, context.Controller.IsDailiesRun);
        Assert.Equal(today ? Array.Empty<bool>() : [false], context.Run.Finished);
    }

    [Fact]
    public void DelayedCompletion_HoldsThenCompletes()
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.State.Accepted.Add(123);
        Assert.True(context.Invoke("ObserveDaily"));
        context.State.Accepted.Clear();

        Assert.False(context.Invoke("ChooseDaily"));
        Assert.True(context.Controller.IsDailiesRun);
        Assert.Empty(context.Run.CompletedIds);
        Assert.Empty(context.Run.Finished);
        Assert.Equal(0, context.Run.Choices);

        context.State.DailyCompleted.Add(123);
        Assert.True(context.Invoke("ObserveDaily"));
        Assert.Equal([new QuestId(123)], context.Run.CompletedIds);
        Assert.True(context.Controller.IsDailiesRun);
        Assert.Empty(context.Run.Finished);
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, true)]
    public void CompletionWait_UsesScopedState(bool scoped, bool accepted, bool today, bool done)
    {
        using var context = new Context();
        context.Priority.IsDailiesRun = scoped;
        context.State.Complete.Add(123);
        if (accepted)
            context.State.Accepted.Add(123);
        if (today)
            context.State.DailyCompleted.Add(123);
        var executor = new WaitAtEnd.QuestCompletedExecutor(context.Functions, context.Priority);
        Assert.True(executor.Start(new WaitAtEnd.QuestCompleted(new QuestId(123))));

        Assert.Equal(done ? ETaskResult.TaskComplete : ETaskResult.StillRunning,
            executor.Update());
    }

    [Fact]
    public void StaleCompletion_KeepsCurrentDaily()
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.State.Accepted.Add(123);
        context.State.Complete.Add(123);
        context.Invoke("ObserveDaily");

        typeof(QuestController).GetMethod("OnTaskComplete", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context.Controller, [new WaitAtEnd.QuestCompleted(new QuestId(123))]);

        Assert.Equal(new QuestId(123), context.Controller.CurrentQuest!.Quest.Id);
        Assert.Empty(context.Run.CompletedIds);
        Assert.True(context.Controller.IsDailiesRun);
    }

    [Fact]
    public void UserStop_EndsRunAndClearsMarker()
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.Controller.AutomationType = QuestController.EAutomationType.Automatic;
        context.Controller.StopFromUi("UI stop");
        Assert.False(context.Controller.IsDailiesRun);
        Assert.False(context.Priority.IsDailiesRun);
        Assert.Equal([false], context.Run.Finished);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void ModeChange_EndsRunOnce(int mode)
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.Controller.AutomationType = (QuestController.EAutomationType)mode;

        Assert.False(context.Controller.IsDailiesRun);
        Assert.False(context.Priority.IsDailiesRun);
        Assert.Equal((QuestController.EAutomationType)mode, context.Controller.AutomationType);
        Assert.NotNull(context.Controller.CurrentQuest);
        Assert.Equal([false], context.Run.Finished);
        context.Controller.AutomationType = QuestController.EAutomationType.Manual;
        Assert.Equal([false], context.Run.Finished);
    }

    [Fact]
    public void Shutdown_DetachesWithoutSummary()
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.Controller.Shutdown();
        Assert.False(context.Controller.IsDailiesRun);
        Assert.False(context.Priority.IsDailiesRun);
        Assert.Empty(context.Run.Finished);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Unsupported rank-up battle", false)]
    public void EmptyPlan_ReportsCorrectEnd(string? reason, bool completed)
    {
        using var context = new Context();
        context.Run.StopReason = reason;
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.Controller.AutomationType = QuestController.EAutomationType.Automatic;
        Assert.False(context.Invoke("ChooseDaily"));
        Assert.Equal([completed], context.Run.Finished);
        Assert.False(context.Controller.IsDailiesRun);
    }

    [Fact]
    public void LoggedOutStart_DoesNotAttach()
    {
        using var context = new Context();
        context.Operations.Client.IsLoggedIn.Returns(false);
        Assert.False(context.Controller.BeginDailies(context.Run));
        Assert.False(context.Controller.IsDailiesRun);
        Assert.False(context.Priority.IsDailiesRun);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Failure_ClearsQueueAndKeepsChoice(int action, bool keep)
    {
        using var context = new Context();
        context.Run.Action = (EDailiesFailure)action;
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.Queue.Enqueue(new WaitAtEnd.WaitDelay());
        Assert.True(context.Invoke("TryDailyFailure", "path failed"));
        Assert.True(context.Queue.AllTasksComplete);
        Assert.Equal(keep, context.Controller.CurrentQuest != null);
        Assert.Single(context.Run.Failed);
        Assert.Empty(context.Run.Finished);
        Assert.Equal(QuestController.EAutomationType.Automatic, context.Controller.AutomationType);
    }

    [Fact]
    public void ExternalWork_PreventsDailyRetry()
    {
        using var context = new Context();
        Assert.True(context.Controller.BeginDailies(context.Run));
        context.SetCurrent();
        context.Operations.Crafting.BeginRequest();
        Assert.False(context.Invoke("TryDailyFailure", "craft failed"));
        Assert.Empty(context.Run.Failed);
        Assert.NotNull(context.Controller.CurrentQuest);
    }

    [Fact]
    public void FailureKinds_KeepManualStops()
    {
        Assert.True(QuestController.CanRetryDaily(new TaskException("quest task failed")));
        Assert.True(QuestController.CanRetryDaily(new MovementController.PathfindingException()));
        Assert.False(QuestController.CanRetryDaily(new NeedsPlayerException("provider paused")));
        Assert.False(QuestController.CanRetryDaily(new InvalidOperationException("unexpected bug")));
    }

    private sealed class Run : IDailiesRun
    {
        public string? StopReason { get; set; }
        public EDailiesFailure Action { get; set; } = EDailiesFailure.Retry;
        public List<bool> Finished { get; } = [];
        public List<ElementId> Failed { get; } = [];
        public List<ElementId> CompletedIds { get; } = [];
        public Quest? Choice { get; set; }
        public int Choices { get; private set; }
        public Quest? Choose(QuestController.QuestProgress? current) { Choices++; return Choice; }
        public void Accepted(ElementId id) { }
        public void Completed(ElementId id) => CompletedIds.Add(id);
        public EDailiesFailure Fail(ElementId id, string reason) { Failed.Add(id); return Action; }
        public void Finish(bool completed) => Finished.Add(completed);
    }

    private sealed class Context : IDisposable
    {
        public ExternalOperationFixture Operations { get; } = new();
        public TaskQueue Queue { get; } = new(NullLogger.Instance);
        public QuestPriorityManager Priority { get; } = new(null!, NullLogger<QuestPriorityManager>.Instance, Substitute.For<IChatGui>());
        public Run Run { get; } = new();
        public FakeQuestGameState State { get; } = new();
        public QuestController Controller { get; }
        public QuestFunctions Functions { get; }
        private readonly HighlightObject _highlight;
        private readonly QuestProgressTracker _tracker;
        private readonly Quest _quest;

        public Context()
        {
            Operations.Client.IsLoggedIn.Returns(true);
            _highlight = new(Operations.Framework, new(), Operations.Condition, Operations.Objects,
                Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);
            _tracker = new(null!, null!, _highlight, NullLogger<QuestProgressTracker>.Instance);
            var catalog = new FakeQuestCatalog();
            _quest = catalog.Add(TestQuestInfo.Create(123)
                .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Ixal)
                .With(nameof(QuestInfo.IsRepeatable), true), QuestTestData.Seq(1, new QuestStep(), new QuestStep()));
            Controller = TestQuestController.Create(Queue, Operations, _highlight, _tracker);
            TestQuestController.Set(Controller, "_clientState", Operations.Client);
            TestQuestController.Set(Controller, "_priorityManager", Priority);
            Functions = new QuestFunctions(catalog, State, new AlliedSocietyData(), new(), Substitute.For<IChatGui>());
            TestQuestController.Set(Controller, "_questFunctions", Functions);
            var selection = Substitute.For<IQuestSelectionState>();
            selection.GetSequence(Arg.Any<ElementId>()).Returns(x => State.GetQuestSequence((ushort)((ElementId)x[0]).Value));
            TestQuestController.Set(Controller, "_selectionState", selection);
        }

        public void SetCurrent()
        {
            _tracker.StartedQuest = new(_quest, 1);
            Controller.AutomationType = QuestController.EAutomationType.Automatic;
        }

        public bool Invoke(string method, params object[] arguments) =>
            (bool)typeof(QuestController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Controller, arguments)!;

        public void Dispose()
        {
            _highlight.Dispose();
            Operations.Dispose();
        }
    }
}
