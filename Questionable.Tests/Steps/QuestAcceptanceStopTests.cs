using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestAcceptanceStopTests
{
    private readonly Configuration _configuration = new();
    private readonly QuestAcceptanceController _acceptance;
    private readonly TaskQueue _queue = new(NullLogger.Instance);

    public QuestAcceptanceStopTests() => _acceptance = new(_configuration);

    private static QuestController.QuestProgress Progress(ushort questId, bool repeatable = false)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(questId));
        quest.Info.IsRepeatable.Returns(repeatable);
        return new(quest);
    }

    private void Queue(ITask task, bool accepted = false)
    {
        _acceptance.TrackQueued(task, _ => accepted);
        _queue.Enqueue(task);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlreadyAcceptedSelectionIsNotNewlyAccepted(bool repeatable)
    {
        var next = Progress(2881, repeatable);
        _acceptance.Select(next, isAccepted: true);

        Assert.True(_acceptance.TryResolve(next, isAccepted: true, isComplete: repeatable, out bool newlyAccepted));
        Assert.False(newlyAccepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionAcceptedAfterItWasMadeIsReportedOnce(bool repeatable)
    {
        var next = Progress(2881, repeatable);
        _acceptance.Select(next, isAccepted: false);

        Assert.False(_acceptance.TryResolve(next, isAccepted: false, isComplete: repeatable, out _));
        Assert.True(_acceptance.TryResolve(next, isAccepted: true, isComplete: repeatable, out bool first));
        Assert.True(_acceptance.TryResolve(next, isAccepted: true, isComplete: repeatable, out bool second));

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void CompletedOnlySelectionIsNotAnAcceptance()
    {
        var next = Progress(2881);
        _acceptance.Select(next, isAccepted: false);

        Assert.True(_acceptance.TryResolve(next, isAccepted: false, isComplete: true, out bool newlyAccepted));
        Assert.False(newlyAccepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingOrClearingTheSelectionDiscardsItsExpectation(bool clearFirst)
    {
        var first = Progress(2881);
        _acceptance.Select(first, isAccepted: false);
        if (clearFirst)
        {
            _acceptance.Select(null, isAccepted: false);
            Assert.True(_acceptance.TryResolve(first, isAccepted: true, isComplete: false, out bool cleared));
            Assert.False(cleared);
        }

        var replacement = new QuestController.QuestProgress(first.Quest);
        _acceptance.Select(replacement, isAccepted: true);

        Assert.True(_acceptance.TryResolve(replacement, isAccepted: true, isComplete: false, out bool newlyAccepted));
        Assert.False(newlyAccepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingTheSameQuestAgainIsANewSelection(bool resolveStale)
    {
        var first = Progress(2881);
        var second = new QuestController.QuestProgress(first.Quest);
        _acceptance.Select(first, isAccepted: false);
        _acceptance.Select(second, isAccepted: false);

        Assert.True(_acceptance.TryResolve(resolveStale ? first : second, isAccepted: true, isComplete: false,
            out bool newlyAccepted));

        Assert.Equal(!resolveStale, newlyAccepted);
    }

    [Fact]
    public void SelectionSurvivesADiscardedTaskPlan()
    {
        var next = Progress(2881);
        _acceptance.Select(next, isAccepted: false);
        Queue(new WaitAtEnd.WaitQuestAccepted(next.Quest.Id));
        Assert.True(_queue.Reset());
        _acceptance.ClearQueued();
        List<ElementId> accepted = [];
        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.True(_acceptance.TryResolve(next, isAccepted: true, isComplete: false, out bool newlyAccepted));

        Assert.Empty(accepted);
        Assert.True(newlyAccepted);
    }

    // Acceptance is reported once whichever detection point observes it first.
    [Theory]
    [InlineData("observer")]
    [InlineData("executor")]
    [InlineData("handoff")]
    public void AcceptanceIsReportedOnceWhicheverDetectionPointSeesItFirst(string first)
    {
        var next = Progress(2881);
        _acceptance.Select(next, isAccepted: false);
        Queue(new WaitAtEnd.WaitQuestAccepted(next.Quest.Id));
        int reports = 0;

        void Observer() => _acceptance.ObserveQueued(_queue, _ => true, _ => reports++);
        void Executor() => reports += _acceptance.TryConsumeQueued(next.Quest.Id) ? 1 : 0;
        void Handoff()
        {
            Assert.True(_acceptance.TryResolve(next, isAccepted: true, isComplete: false, out bool newlyAccepted));
            reports += newlyAccepted ? 1 : 0;
        }

        Action[] order = first switch
        {
            "observer" => [Observer, Executor, Handoff],
            "executor" => [Executor, Observer, Handoff],
            _ => [Handoff, Observer, Executor],
        };
        foreach (Action detect in order)
            detect();

        Assert.Equal(1, reports);
    }

    [Theory]
    [InlineData(2552)] // Own MSQ acceptance before its sequence changes.
    [InlineData(2881)] // Side quest picked up while an MSQ is active.
    [InlineData(253)] // Class quest picked up while another quest is active.
    public void ExpectedAcceptanceIsObservedBeforeItsWaitRuns(ushort accepting)
    {
        _acceptance.StopAfterAcceptingNextQuest = true;
        QuestId expected = new(accepting);
        Queue(new WaitAtEnd.WaitDelay());
        Queue(new WaitAtEnd.WaitQuestAccepted(expected));
        List<ElementId> accepted = [];
        _acceptance.ObserveQueued(_queue, _ => false, accepted.Add);
        Assert.Empty(accepted);

        _acceptance.ObserveQueued(_queue, id => id == expected, id =>
        {
            accepted.Add(id);
            Assert.True(_acceptance.ShouldStop(id, isAccepted: true, isManual: false, out _));
        });
        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.Equal(expected, Assert.Single(accepted));
        Assert.Null(_queue.CurrentTaskExecutor);
        Assert.Equal(2, _queue.RemainingTasks.Count());
    }

    [Fact]
    public void ActiveAcceptanceWaitIsObservedAfterItLeavesTheQueue()
    {
        QuestId expected = new(2881);
        Queue(new WaitAtEnd.WaitQuestAccepted(expected));
        Assert.True(_queue.TryDequeue(out ITask? task));
        Assert.True(_queue.StartTask(new WaitAtEnd.WaitQuestAcceptedExecutor(null!, null!), task!));
        List<ElementId> accepted = [];

        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.Equal(expected, Assert.Single(accepted));
    }

    [Fact]
    public void AnUnrelatedAcceptanceDoesNotConsumeAPendingOne()
    {
        QuestId expected = new(253);
        Queue(new WaitAtEnd.WaitQuestAccepted(expected));
        List<ElementId> accepted = [];

        _acceptance.ObserveQueued(_queue, id => id == new QuestId(2881), accepted.Add);
        Assert.Empty(accepted);

        _acceptance.ObserveQueued(_queue, id => id == expected, accepted.Add);
        Assert.Equal(expected, Assert.Single(accepted));
    }

    [Fact]
    public void AlreadyAcceptedOptionalPickupIsNotANewAcceptance()
    {
        Queue(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)), accepted: true);
        List<ElementId> accepted = [];

        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.Empty(accepted);
        Assert.False(_acceptance.TryConsumeQueued(new QuestId(2881)));
    }

    [Fact]
    public void DiscardedAcceptanceWaitCannotReportLater()
    {
        Queue(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)));
        Assert.True(_queue.Reset());
        List<ElementId> accepted = [];

        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.Empty(accepted);
        Assert.False(_acceptance.TryConsumeQueued(new QuestId(2881)));
    }

    [Fact]
    public void AStopDuringObservationEndsTheRestOfThePlan()
    {
        Queue(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)));
        Queue(new WaitAtEnd.WaitQuestAccepted(new QuestId(253)));
        List<ElementId> accepted = [];

        _acceptance.ObserveQueued(_queue, _ => true, id =>
        {
            accepted.Add(id);
            _acceptance.ClearQueued(); // What the controller's stop does.
        });

        Assert.Single(accepted);
    }

    [Fact]
    public void ObservedAcceptanceCannotBeReusedAfterArmingTheToggle()
    {
        Queue(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)));
        List<ElementId> accepted = [];
        _acceptance.ObserveQueued(_queue, _ => true, id =>
        {
            accepted.Add(id);
            Assert.False(_acceptance.ShouldStop(id, isAccepted: true, isManual: false, out _));
        });

        _acceptance.StopAfterAcceptingNextQuest = true;
        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.False(_acceptance.TryConsumeQueued(new QuestId(2881)));
        Assert.Single(accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnacceptedQuestDoesNotStop(bool configured)
    {
        QuestId nextQuest = new(2881);
        _configuration.Stop.Enabled = configured;
        _configuration.Stop.QuestsToStopWhenAccepted.Add(nextQuest);
        _acceptance.StopAfterAcceptingNextQuest = !configured;

        Assert.False(_acceptance.ShouldStop(nextQuest, isAccepted: false, isManual: false, out bool configStop));
        Assert.False(configStop);
        Assert.Equal(!configured, _acceptance.StopAfterAcceptingNextQuest);

        Assert.True(_acceptance.ShouldStop(nextQuest, isAccepted: true, isManual: false, out configStop));
        Assert.Equal(configured, configStop);
    }

    [Theory]
    [InlineData(2881)] // MSQ picking up a side quest.
    [InlineData(253)] // Close to Home picking up a class quest.
    [InlineData(2552)]
    public void StopAfterAcceptIncludesAnyAcceptedQuest(ushort accepted)
    {
        _acceptance.StopAfterAcceptingNextQuest = true;

        Assert.True(_acceptance.ShouldStop(new QuestId(accepted), isAccepted: true, isManual: false, out bool configStop));
        Assert.False(configStop);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ConfiguredStopRequiresEnabledMatchingQuest(bool enabled, bool matching, bool expected)
    {
        _configuration.Stop.Enabled = enabled;
        _configuration.Stop.QuestsToStopWhenAccepted.Add(new QuestId(2881));

        Assert.Equal(expected, _acceptance.ShouldStop(new QuestId(matching ? (ushort)2881 : (ushort)2877),
            isAccepted: true, isManual: false, out bool configStop));
        Assert.Equal(expected, configStop);
    }

    [Fact]
    public void ManualModeDoesNotApplyAcceptanceStops()
    {
        _acceptance.StopAfterAcceptingNextQuest = true;
        _configuration.Stop.Enabled = true;
        _configuration.Stop.QuestsToStopWhenAccepted.Add(new QuestId(2881));

        Assert.False(_acceptance.ShouldStop(new QuestId(2881), isAccepted: true, isManual: true, out bool configStop));
        Assert.False(configStop);
    }
}

// Covers the controller wiring around the acceptance component.
public sealed class QuestAcceptanceWiringTests : IDisposable
{
    private readonly ExternalOperationFixture _operations = new();
    private readonly Configuration _configuration = new();
    private readonly QuestAcceptanceController _acceptance;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed",
        Justification = "Built without its constructor, so it holds no game subscriptions to release.")]
    private readonly QuestController _controller;
    private readonly TaskQueue _queue = new(NullLogger.Instance);
    private readonly HighlightObject _highlight;

    public QuestAcceptanceWiringTests()
    {
        _acceptance = new(_configuration);
        _highlight = new(_operations.Framework, new(), _operations.Condition, _operations.Objects,
            Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);
        _controller = TestQuestController.Create(_queue, _operations, _highlight, configuration: _configuration,
            acceptance: _acceptance);
        _controller.AutomationType = QuestController.EAutomationType.Automatic;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AlreadyAcceptedNextQuestHandsOffWithoutReporting(bool singleQuest, bool repeatable)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        quest.Info.IsRepeatable.Returns(repeatable);
        _controller.AutomationType = singleQuest ? QuestController.EAutomationType.SingleQuestA : QuestController.EAutomationType.Automatic;
        _controller.StopAfterAcceptingNextQuest = true;
        _controller.SelectNextQuest(quest, isAccepted: true);
        var selected = _controller.NextQuest;
        List<ElementId> accepted = [];

        Assert.True(_controller.ResolveNextQuest(isAccepted: true, isComplete: repeatable, accepted.Add));

        Assert.Empty(accepted);
        Assert.Null(_controller.NextQuest);
        Assert.True(_controller.StopAfterAcceptingNextQuest);
        Assert.Equal(singleQuest ? QuestController.EAutomationType.SingleQuestB : QuestController.EAutomationType.Automatic,
            _controller.AutomationType);
        if (singleQuest)
            Assert.Same(selected, _controller.StartedQuest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NextQuestAcceptedAfterSelectionHandsOffAndReportsOnce(bool singleQuest)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.AutomationType = singleQuest ? QuestController.EAutomationType.SingleQuestA : QuestController.EAutomationType.Automatic;
        _controller.SelectNextQuest(quest, isAccepted: false);
        var selected = _controller.NextQuest;
        List<ElementId> accepted = [];

        Assert.False(_controller.ResolveNextQuest(isAccepted: false, isComplete: false, accepted.Add));
        Assert.True(_controller.ResolveNextQuest(isAccepted: true, isComplete: false, accepted.Add));
        Assert.False(_controller.ResolveNextQuest(isAccepted: true, isComplete: false, accepted.Add));

        Assert.Equal(quest.Id, Assert.Single(accepted));
        Assert.Null(_controller.NextQuest);
        if (singleQuest)
            Assert.Same(selected, _controller.StartedQuest);
    }

    [Fact]
    public void SelectingAStartedQuestRegistersNoAcceptance()
    {
        _controller.StopAfterAcceptingNextQuest = true;
        _controller.SelectStartedQuest(QuestTestData.CreateQuest(new QuestId(2881)), sequence: 1);
        List<ElementId> accepted = [];

        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.Empty(accepted);
        Assert.True(_controller.StopAfterAcceptingNextQuest);
        Assert.Equal(QuestController.EAutomationType.Automatic, _controller.AutomationType);
    }

    [Fact]
    public void QueuedWaitsAreTrackedAsTheyAreEnqueued()
    {
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)), _ => false);
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(new QuestId(253)), _ => true);
        List<ElementId> accepted = [];

        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);

        Assert.Equal(new QuestId(2881), Assert.Single(accepted));
        Assert.Equal(2, _queue.RemainingTasks.Count());
    }

    [Fact]
    public void StopEndsBothExpectationsAndTheToggle()
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.SelectNextQuest(quest, isAccepted: false);
        var selected = _controller.NextQuest!;
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(quest.Id), _ => false);
        _controller.StopAfterAcceptingNextQuest = true;

        _controller.Stop("test");

        List<ElementId> accepted = [];
        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);
        Assert.Empty(accepted);
        Assert.False(_acceptance.TryConsumeQueued(quest.Id));
        Assert.True(_acceptance.TryResolve(selected, isAccepted: true, isComplete: false, out bool newlyAccepted));
        Assert.False(newlyAccepted);
        Assert.False(_controller.StopAfterAcceptingNextQuest);
        Assert.Null(_controller.NextQuest);
    }

    [Fact]
    public void WaitExecutorReportConsumesTheNextQuestExpectation()
    {
        _controller.AutomationType = QuestController.EAutomationType.Manual;
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.SelectNextQuest(quest, isAccepted: false);
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(quest.Id), _ => false);

        _controller.TryStopOnExpectedQuestAccepted(quest.Id);

        Assert.True(_controller.ResolveNextQuest(isAccepted: true, isComplete: false,
            _ => Assert.Fail("Acceptance was reported twice")));
        Assert.False(_acceptance.TryConsumeQueued(quest.Id));
    }

    public void Dispose()
    {
        _highlight.Dispose();
        _operations.Dispose();
    }
}
