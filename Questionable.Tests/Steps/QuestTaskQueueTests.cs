using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestTaskQueueTests
{
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public void Skip_KeepsQuestAndAdvancesOnStop(bool automatic, bool hasExecutor, int stepCount)
    {
        using var context = new SkipTestContext(automatic, stepCount);
        if (hasExecutor)
            context.Queue.StartTask(new WaitAtEnd.NextStepOrSequenceExecutor(), new WaitAtEnd.NextStepOrSequence());
        context.Queue.Enqueue(new WaitAtEnd.NextStep(context.Progress.Quest.Id, context.Progress.Sequence));

        context.Controller.Skip(context.Progress.Quest.Id, context.Progress.Sequence);

        Assert.Same(context.Progress, context.Controller.CurrentQuest);
        Assert.Equal(stepCount == 1 ? 255 : 1, context.Progress.Step);
        Assert.Equal(QuestController.EAutomationType.Manual, context.Controller.AutomationType);
        Assert.True(context.Queue.AllTasksComplete);
        Assert.Equal(automatic ? ["notify", "/after-stop"] : Array.Empty<string>(), context.StopEffects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkipPreservesAlreadyIdleNormalQuest(bool automatic)
    {
        using var context = new SkipTestContext(automatic, 2);

        context.Controller.Skip(context.Progress.Quest.Id, context.Progress.Sequence);

        Assert.Same(context.Progress, context.Controller.CurrentQuest);
        Assert.Equal(1, context.Progress.Step);
        Assert.Equal(QuestController.EAutomationType.Manual, context.Controller.AutomationType);
        Assert.True(context.Queue.AllTasksComplete);
        Assert.Equal(automatic ? ["notify", "/after-stop"] : Array.Empty<string>(), context.StopEffects);
    }

    [Theory]
    [InlineData(124, 2)]
    [InlineData(123, 1)]
    public void SkipRejectsStaleQuestOrSequence(ushort questId, byte sequence)
    {
        using var context = new SkipTestContext(automatic: true, stepCount: 2);
        context.Queue.StartTask(new WaitAtEnd.NextStepOrSequenceExecutor(), new WaitAtEnd.NextStepOrSequence());
        var originalStepProgress = context.Progress.StepProgress;

        context.Controller.Skip(new QuestId(questId), sequence);

        Assert.Same(context.Progress, context.Controller.CurrentQuest);
        Assert.Equal(0, context.Progress.Step);
        Assert.Same(originalStepProgress, context.Progress.StepProgress);
        Assert.Equal(QuestController.EAutomationType.Manual, context.Controller.AutomationType);
        Assert.True(context.Queue.AllTasksComplete);
        Assert.Equal(["notify", "/after-stop"], context.StopEffects);
    }

    private sealed class SkipTestContext : IDisposable
    {
        private readonly ExternalOperationFixture _operations = new();
        private readonly HighlightObject _highlight;

        public QuestController Controller { get; }
        public QuestController.QuestProgress Progress { get; }
        public TaskQueue Queue { get; } = new(NullLogger.Instance);
        public List<string> StopEffects { get; } = [];

        public SkipTestContext(bool automatic, int stepCount)
        {
            _highlight = new HighlightObject(_operations.Framework, new(), _operations.Condition, _operations.Objects,
                Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);
            var quest = QuestTestData.CreateQuest(new QuestId(123),
                QuestTestData.Seq(2, Enumerable.Range(0, stepCount).Select(_ => new QuestStep()).ToArray()));
            Progress = new(quest, sequence: 2);
            var tracker = new QuestProgressTracker(null!, null!, _highlight, NullLogger<QuestProgressTracker>.Instance)
            {
                StartedQuest = Progress
            };

            var configuration = new Configuration();
            configuration.Stop.RunCommandAfterStop = true;
            configuration.Stop.CommandAfterStop = "/after-stop";
            Controller = TestQuestController.Create(Queue, _operations, _highlight, tracker, configuration,
                stopPolicy: new QuestStopPolicy(configuration, _ => StopEffects.Add("notify"),
                    StopEffects.Add, NullLogger<QuestStopPolicy>.Instance));
            Controller.AutomationType = automatic
                ? QuestController.EAutomationType.Automatic
                : QuestController.EAutomationType.Manual;
        }

        public void Dispose()
        {
            _highlight.Dispose();
            _operations.Dispose();
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    public void StepDone_RejectsOldSequence(int stepCount, int? completedSequence)
    {
        QuestId questId = new(123);
        var quest = QuestTestData.CreateQuest(questId,
            QuestTestData.Seq(1, new QuestStep()),
            QuestTestData.Seq(2, Enumerable.Range(0, stepCount).Select(_ => new QuestStep()).ToArray()));
        var progress = new QuestController.QuestProgress(quest, sequence: 2);
        progress.IncreasePointMenuCounter();
        var originalStepProgress = progress.StepProgress;
        var tracker = new QuestProgressTracker(null!, null!, null!, NullLogger<QuestProgressTracker>.Instance)
        {
            SimulatedQuest = progress
        };
        using var operations = new ExternalOperationFixture();

        var controller = TestQuestController.Create(operations: operations, tracker: tracker);

        controller.IncreaseStepCount(questId, completedSequence);

        Assert.Same(progress, controller.CurrentQuest);
        Assert.Equal((byte)2, progress.Sequence);
        if (completedSequence == 1)
        {
            Assert.Equal(0, progress.Step);
            Assert.Same(originalStepProgress, progress.StepProgress);
            Assert.Equal(1, progress.StepProgress.PointMenuCounter);
        }
        else
        {
            Assert.Equal(stepCount == 1 ? 255 : 1, progress.Step);
            Assert.NotSame(originalStepProgress, progress.StepProgress);
            Assert.Equal(0, progress.StepProgress.PointMenuCounter);
        }
    }
}
