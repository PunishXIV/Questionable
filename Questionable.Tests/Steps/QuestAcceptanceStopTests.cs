using System.Reflection;
using System.Runtime.CompilerServices;
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

public sealed class QuestAcceptanceStopTests : IDisposable
{
    private readonly Configuration _configuration = new();
    private readonly QuestController _controller;
    private readonly QuestProgressTracker _tracker;
    private readonly TaskQueue _queue = new(NullLogger.Instance);
    private readonly HighlightObject _highlight = new(Substitute.For<IFramework>(), new(), Substitute.For<ICondition>(),
        Substitute.For<IObjectTable>(), Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);

    public QuestAcceptanceStopTests()
    {
        _tracker = new(null!, null!, _highlight, NullLogger<QuestProgressTracker>.Instance);
        // The decision uses managed state; the public caller supplies the native acceptance result.
        _controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        SetField("_configuration", _configuration);
        SetField("_tracker", _tracker);
        SetField("_logger", NullLogger<QuestController>.Instance);
        SetField("_highlightObject", _highlight);
        typeof(MiniTaskController<QuestController>).GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_controller, _queue);
        _controller.AutomationType = QuestController.EAutomationType.Automatic;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AlreadyAcceptedNextQuestDoesNotSignalAcceptance(bool singleQuest, bool repeatable)
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NextQuestAcceptedAfterSelectionSignalsOnce(bool singleQuest, bool repeatable)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        quest.Info.IsRepeatable.Returns(repeatable);
        _controller.AutomationType = singleQuest ? QuestController.EAutomationType.SingleQuestA : QuestController.EAutomationType.Automatic;
        _controller.StopAfterAcceptingNextQuest = true;
        _controller.SelectNextQuest(quest, isAccepted: false);
        List<ElementId> accepted = [];

        Assert.False(_controller.ResolveNextQuest(isAccepted: false, isComplete: repeatable, accepted.Add));
        Assert.True(_controller.ResolveNextQuest(isAccepted: true, isComplete: repeatable, id =>
        {
            accepted.Add(id);
            Assert.True(_controller.ShouldStopOnQuestAccepted(id, isAccepted: true, out _));
        }));
        Assert.False(_controller.ResolveNextQuest(isAccepted: true, isComplete: repeatable, accepted.Add));

        Assert.Equal(quest.Id, Assert.Single(accepted));
        Assert.True(_controller.StopAfterAcceptingNextQuest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedOnlyNextQuestDoesNotSignalAcceptance(bool singleQuest)
    {
        _controller.AutomationType = singleQuest ? QuestController.EAutomationType.SingleQuestA : QuestController.EAutomationType.Automatic;
        _controller.SelectNextQuest(QuestTestData.CreateQuest(new QuestId(2881)), isAccepted: false);
        List<ElementId> accepted = [];

        Assert.True(_controller.ResolveNextQuest(isAccepted: false, isComplete: true, accepted.Add));

        Assert.Empty(accepted);
        Assert.Null(_controller.NextQuest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingOrClearingNextQuestDiscardsItsAcceptanceExpectation(bool clearFirst)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.SelectNextQuest(quest, isAccepted: false);
        if (clearFirst)
        {
            _controller.SelectNextQuest(null, isAccepted: false);
            Assert.False(_controller.ResolveNextQuest(true, false, _ => Assert.Fail("Cleared selection signalled acceptance")));
        }
        _controller.SelectNextQuest(quest, isAccepted: true);
        List<ElementId> accepted = [];

        Assert.True(_controller.ResolveNextQuest(true, false, accepted.Add));

        Assert.Empty(accepted);
    }

    [Fact]
    public void NextQuestAcceptanceExpectationSurvivesDiscardedTaskPlan()
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.SelectNextQuest(quest, isAccepted: false);
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(quest.Id), _ => false);
        Assert.True(_queue.Reset());
        List<ElementId> accepted = [];
        _controller.ObserveQuestAcceptances(_ => false, accepted.Add);

        Assert.True(_controller.ResolveNextQuest(true, false, accepted.Add));

        Assert.Equal(quest.Id, Assert.Single(accepted));
    }

    [Fact]
    public void QueuedAcceptanceAndNextQuestHandoffDoNotReportTwice()
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.SelectNextQuest(quest, isAccepted: false);
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(quest.Id), _ => false);
        List<ElementId> accepted = [];

        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);
        _controller.StopAfterAcceptingNextQuest = true;
        Assert.True(_controller.ResolveNextQuest(true, false, accepted.Add));

        Assert.Equal(quest.Id, Assert.Single(accepted));
        Assert.True(_controller.StopAfterAcceptingNextQuest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingAlreadyAcceptedPriorityQuestDoesNotStop(bool configured)
    {
        _tracker.StartedQuest = new(QuestTestData.CreateQuest(new QuestId(2552)), sequence: 1);
        var selected = QuestTestData.CreateQuest(new QuestId(2881));
        _configuration.Stop.Enabled = configured;
        _configuration.Stop.QuestsToStopWhenAccepted.Add(selected.Id);
        _controller.StopAfterAcceptingNextQuest = true;
        List<ElementId> accepted = [];

        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);
        _controller.SelectStartedQuest(selected, sequence: 1);
        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);

        Assert.Empty(accepted);
        Assert.Same(selected, _controller.StartedQuest!.Quest);
        Assert.True(_controller.StopAfterAcceptingNextQuest);
        Assert.Equal(QuestController.EAutomationType.Automatic, _controller.AutomationType);
    }

    [Theory]
    [InlineData(2552, 2552, 0)] // Own MSQ acceptance before its sequence changes.
    [InlineData(2552, 2881, 1)] // Nested side quest while the parent is already active.
    [InlineData(568, 253, 1)] // Nested class quest.
    public void ExpectedAcceptanceIsObservedBeforeItsWaitRuns(ushort tracked, ushort accepting, byte sequence)
    {
        _tracker.StartedQuest = new(QuestTestData.CreateQuest(new QuestId(tracked)), sequence);
        _controller.StopAfterAcceptingNextQuest = true;
        QuestId expected = new(accepting);
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitDelay(), _ => false);
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(expected), _ => false);
        List<ElementId> accepted = [];
        _controller.ObserveQuestAcceptances(_ => false, accepted.Add);
        Assert.Empty(accepted);
        Assert.True(_controller.StopAfterAcceptingNextQuest);

        _controller.ObserveQuestAcceptances(id => id == expected, id =>
        {
            accepted.Add(id);
            Assert.True(_controller.ShouldStopOnQuestAccepted(id, isAccepted: true, out _));
        });
        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);

        Assert.Equal(expected, Assert.Single(accepted));
        Assert.Equal(sequence, _controller.StartedQuest!.Sequence);
        Assert.Null(_queue.CurrentTaskExecutor);
        Assert.Equal(2, _queue.RemainingTasks.Count());
    }

    [Fact]
    public void ActiveAcceptanceWaitIsObservedAfterItLeavesTheQueue()
    {
        QuestId expected = new(2881);
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(expected), _ => false);
        Assert.True(_queue.TryDequeue(out ITask? task));
        Assert.True(_queue.StartTask(new WaitAtEnd.WaitQuestAcceptedExecutor(null!, null!), task!));
        List<ElementId> accepted = [];

        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);

        Assert.Equal(expected, Assert.Single(accepted));
    }

    [Fact]
    public void PrioritySelectionDoesNotConfuseAnUnrelatedPendingAcceptance()
    {
        QuestId expected = new(253);
        var selected = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.StopAfterAcceptingNextQuest = true;
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(expected), _ => false);
        List<ElementId> accepted = [];

        _controller.SelectStartedQuest(selected, sequence: 1);
        _controller.ObserveQuestAcceptances(id => id == selected.Id, accepted.Add);

        Assert.Empty(accepted);
        Assert.True(_controller.StopAfterAcceptingNextQuest);
        _controller.ObserveQuestAcceptances(id => id == expected, accepted.Add);
        Assert.Equal(expected, Assert.Single(accepted));
    }

    [Fact]
    public void AlreadyAcceptedOptionalPickupIsNotANewAcceptance()
    {
        _controller.StopAfterAcceptingNextQuest = true;
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)), _ => true);
        List<ElementId> accepted = [];

        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);

        Assert.Empty(accepted);
        Assert.True(_controller.StopAfterAcceptingNextQuest);
    }

    [Fact]
    public void DiscardedAcceptanceWaitCannotStopLaterWork()
    {
        _controller.StopAfterAcceptingNextQuest = true;
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)), _ => false);
        Assert.True(_queue.Reset());
        List<ElementId> accepted = [];

        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);

        Assert.Empty(accepted);
        Assert.True(_controller.StopAfterAcceptingNextQuest);
    }

    [Fact]
    public void ObservedAcceptanceCannotBeReusedAfterArmingToggle()
    {
        _controller.EnqueueQuestTask(new WaitAtEnd.WaitQuestAccepted(new QuestId(2881)), _ => false);
        List<ElementId> accepted = [];
        _controller.ObserveQuestAcceptances(_ => true, id =>
        {
            accepted.Add(id);
            Assert.False(_controller.ShouldStopOnQuestAccepted(id, isAccepted: true, out _));
        });
        _controller.StopAfterAcceptingNextQuest = true;
        _controller.ObserveQuestAcceptances(_ => true, accepted.Add);
        _controller.TryStopOnExpectedQuestAccepted(new QuestId(2881));

        Assert.Single(accepted);
        Assert.True(_controller.StopAfterAcceptingNextQuest);
    }

    public void Dispose() => _highlight.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingAnUnacceptedQuestDoesNotStopOrConsumeTheRequest(bool configured)
    {
        QuestId nextQuest = new(2881);
        _configuration.Stop.Enabled = configured;
        _configuration.Stop.QuestsToStopWhenAccepted.Add(nextQuest);
        _controller.StopAfterAcceptingNextQuest = !configured;

        Assert.False(_controller.ShouldStopOnQuestAccepted(nextQuest, isAccepted: false, out bool configStop));
        Assert.False(configStop);
        Assert.Equal(!configured, _controller.StopAfterAcceptingNextQuest);
        Assert.Equal(QuestController.EAutomationType.Automatic, _controller.AutomationType);

        Assert.True(_controller.ShouldStopOnQuestAccepted(nextQuest, isAccepted: true, out configStop));
        Assert.Equal(configured, configStop);
    }

    [Theory]
    [InlineData(2552, 2881)] // MSQ picking up a side quest.
    [InlineData(568, 253)] // Close to Home picking up a class quest.
    [InlineData(2552, 2552)]
    public void SessionStopIncludesAnyAcceptedQuestWhileAnotherQuestIsTracked(ushort tracked, ushort accepted)
    {
        _tracker.StartedQuest = new(QuestTestData.CreateQuest(new QuestId(tracked)), sequence: 1);
        _controller.StopAfterAcceptingNextQuest = true;

        Assert.True(_controller.ShouldStopOnQuestAccepted(new QuestId(accepted), isAccepted: true, out bool configStop));
        Assert.False(configStop);
        Assert.Equal(new QuestId(tracked), _controller.StartedQuest!.Quest.Id);
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

        Assert.Equal(expected, _controller.ShouldStopOnQuestAccepted(new QuestId(matching ? (ushort)2881 : (ushort)2877),
            isAccepted: true, out bool configStop));
        Assert.Equal(expected, configStop);
    }

    [Fact]
    public void ManualModeDoesNotApplyAcceptanceStops()
    {
        _controller.AutomationType = QuestController.EAutomationType.Manual;
        _controller.StopAfterAcceptingNextQuest = true;
        _configuration.Stop.Enabled = true;
        _configuration.Stop.QuestsToStopWhenAccepted.Add(new QuestId(2881));

        Assert.False(_controller.ShouldStopOnQuestAccepted(new QuestId(2881), isAccepted: true, out bool configStop));
        Assert.False(configStop);
    }

    private void SetField(string name, object value) =>
        typeof(QuestController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_controller, value);
}
