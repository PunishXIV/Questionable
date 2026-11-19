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

    public QuestAcceptanceStopTests() => _acceptance = new(_configuration);

    private static QuestController.QuestProgress Progress(ushort questId, bool repeatable = false)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(questId));
        quest.Info.IsRepeatable.Returns(repeatable);
        return new(quest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterAcceptance_IsReportedOnce(bool repeatable)
    {
        var next = Progress(2881, repeatable);
        _acceptance.Select(next, isAccepted: false);

        Assert.False(_acceptance.TryResolve(next, isAccepted: false, isComplete: repeatable, out _));
        Assert.True(_acceptance.TryResolve(next, isAccepted: true, isComplete: repeatable, out bool first));
        Assert.True(_acceptance.TryResolve(next, isAccepted: true, isComplete: repeatable, out bool second));

        Assert.True(first);
        Assert.False(second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnacceptedQuestDoesNotStop(bool configured)
    {
        QuestId nextQuest = new(2881);
        _configuration.Stop.Enabled = configured;
        _configuration.Stop.QuestsToStopWhenAccepted.Add(nextQuest);
        _acceptance.StopAfterNextAccept = !configured;

        Assert.False(_acceptance.ShouldStop(nextQuest, isAccepted: false, isManual: false, out bool configStop));
        Assert.False(configStop);
        Assert.Equal(!configured, _acceptance.StopAfterNextAccept);

        Assert.True(_acceptance.ShouldStop(nextQuest, isAccepted: true, isManual: false, out configStop));
        Assert.Equal(configured, configStop);
    }

    [Theory]
    [InlineData(2881)] // MSQ picking up a side quest.
    [InlineData(253)] // Close to Home picking up a class quest.
    [InlineData(2552)]
    public void StopAfterAcceptIncludesAnyAcceptedQuest(ushort accepted)
    {
        _acceptance.StopAfterNextAccept = true;

        Assert.True(_acceptance.ShouldStop(new QuestId(accepted), isAccepted: true, isManual: false, out bool configStop));
        Assert.False(configStop);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ConfiguredStop_NeedsEnabledMatch(bool enabled, bool matching, bool expected)
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
        _acceptance.StopAfterNextAccept = true;
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

    [Fact]
    public void StopEndsBothExpectationsAndTheToggle()
    {
        var quest = QuestTestData.CreateQuest(new QuestId(2881));
        _controller.SelectNextQuest(quest, isAccepted: false);
        var selected = _controller.NextQuest!;
        _controller.EnqueueTask(new WaitAtEnd.QuestAccepted(quest.Id), _ => false);
        _controller.StopAfterNextAccept = true;

        _controller.Stop("test");

        List<ElementId> accepted = [];
        _acceptance.ObserveQueued(_queue, _ => true, accepted.Add);
        Assert.Empty(accepted);
        Assert.False(_acceptance.TryConsumeQueued(quest.Id));
        Assert.True(_acceptance.TryResolve(selected, isAccepted: true, isComplete: false, out bool newlyAccepted));
        Assert.False(newlyAccepted);
        Assert.False(_controller.StopAfterNextAccept);
        Assert.Null(_controller.NextQuest);
    }

    public void Dispose()
    {
        _highlight.Dispose();
        _operations.Dispose();
    }
}
