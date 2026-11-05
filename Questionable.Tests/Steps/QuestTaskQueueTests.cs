using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public void SkipPreservesNormalQuestAndAdvancesWhileStopping(bool automatic, bool hasExecutor, int stepCount)
    {
        using var context = new SkipTestContext(automatic, stepCount);
        if (hasExecutor)
            context.Queue.StartTask(new WaitAtEnd.WaitNextStepOrSequenceExecutor(), new WaitAtEnd.WaitNextStepOrSequence());
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
        context.Queue.StartTask(new WaitAtEnd.WaitNextStepOrSequenceExecutor(), new WaitAtEnd.WaitNextStepOrSequence());
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

            // Exercise real Skip/Stop with a normal quest, without game hooks or native objects.
            Controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
            SetField("_progressLock", Activator.CreateInstance(typeof(System.Threading.Lock))!);
            SetField("_logger", NullLogger<QuestController>.Instance);
            SetField("_recovery", new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance));
            SetField("_highlightObject", _highlight);
            SetField("_tracker", tracker);
            SetField("_objectTable", _operations.Objects);
            SetField("_movementController", _operations.Movement);
            SetField("_combatController", _operations.Combat);
            SetField("_gatheringController", _operations.GatheringController);
            SetField("_externalOperations", _operations.Coordinator);
            var configuration = new Configuration();
            configuration.Stop.RunCommandAfterStop = true;
            configuration.Stop.CommandAfterStop = "/after-stop";
            SetField("_stopPolicy", new QuestStopPolicy(configuration, _ => StopEffects.Add("notify"),
                StopEffects.Add, NullLogger<QuestStopPolicy>.Instance));
            typeof(MiniTaskController<QuestController>).GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Controller, Queue);
            Controller.AutomationType = automatic
                ? QuestController.EAutomationType.Automatic
                : QuestController.EAutomationType.Manual;
        }

        private void SetField(string name, object value) =>
            typeof(QuestController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Controller, value);

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
    public void StepCompletionRejectsOldSequenceWithoutChangingCurrentProgress(int stepCount, int? completedSequence)
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

        // Skip the constructor's game hooks and UI services; exercise the real step transition.
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        SetField("_progressLock", Activator.CreateInstance(typeof(System.Threading.Lock))!);
        SetField("_logger", NullLogger<QuestController>.Instance);
        SetField("_recovery", new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance));
        SetField("_tracker", tracker);
        SetField("_externalOperations", operations.Coordinator);
        SetField("_stopPolicy", new QuestStopPolicy(new Configuration(), _ => { }, _ => { }, NullLogger<QuestStopPolicy>.Instance));

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

        void SetField(string name, object value) =>
            typeof(QuestController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);
    }

    [Theory]
    [InlineData("CheckNextTasks", "purchase", false)]
    [InlineData("CheckNextTasks", "purchase", true)]
    [InlineData("CheckNextTasks", "gather", false)]
    [InlineData("CheckNextTasks", "gather", true)]
    [InlineData("ExecuteNextStep", "purchase", false)]
    [InlineData("ExecuteNextStep", "purchase", true)]
    [InlineData("ExecuteNextStep", "gather", false)]
    [InlineData("ExecuteNextStep", "gather", true)]
    public void BlockedReplanCancelsObsoleteQueueAndDoesNotResumeWhenProviderBecomesIdle(
        string entryPoint, string provider, bool unknown)
    {
        using var operations = new ExternalOperationFixture();
        using var highlight = new HighlightObject(operations.Framework, new(), operations.Condition, operations.Objects,
            Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);
        var purchase = operations.Knightshopper;
        var gather = operations.Gather;
        var fishing = operations.Feesh;
        List<string> cleanupOrder = [];
        purchase.When(x => x.CancelOwned()).Do(_ => cleanupOrder.Add("purchase"));
        fishing.When(x => x.CancelOwned()).Do(_ => cleanupOrder.Add("fishing"));
        gather.When(x => x.CancelOwned()).Do(_ => cleanupOrder.Add("gathering"));
        purchase.IsReady().Returns(provider == "purchase");
        gather.IsAvailable().Returns(provider == "gather");
        bool? busy = unknown ? null : true;
        purchase.GetBusy().Returns(provider == "purchase" ? busy : false);
        gather.GetBusy().Returns(provider == "gather" ? busy : false);
        // Skip game-sheet and UI construction; exercise the real replan, Stop and cleanup paths.
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        SetField("_logger", NullLogger<QuestController>.Instance);
        SetField("_recovery", new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance));
        SetField("_highlightObject", highlight);
        SetField("_tracker", new QuestProgressTracker(null!, null!, highlight, NullLogger<QuestProgressTracker>.Instance));
        SetField("_objectTable", operations.Objects);
        SetField("_movementController", operations.Movement);
        SetField("_combatController", operations.Combat);
        SetField("_externalOperations", operations.Coordinator);
        SetField("_stopPolicy", new QuestStopPolicy(new Configuration(), _ => { }, _ => { }, NullLogger<QuestStopPolicy>.Instance));
        SetField("_gatheringController", operations.GatheringController);
        var queue = new TaskQueue(NullLogger.Instance);
        typeof(MiniTaskController<QuestController>).GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, queue);
        var executor = Substitute.For<ITaskExecutor.IStoppableTaskExecutor>();
        var current = new MoveTask(958, Vector3.One);
        executor.Start(current).Returns(true);
        executor.CurrentTask.Returns(current);
        executor.When(x => x.StopNow()).Do(_ =>
        {
            Assert.Null(queue.CurrentTaskExecutor);
            Assert.Empty(queue.RemainingTasks);
            cleanupOrder.Add("executor");
        });
        Assert.True(queue.StartTask(executor, current));
        queue.Enqueue(new MoveTask(958, new Vector3(20, 0, 0)));
        controller.AutomationType = QuestController.EAutomationType.SingleQuestA;
        controller.AutomationTypeChanged += (_, mode) =>
        {
            Assert.Equal(QuestController.EAutomationType.Manual, mode);
            cleanupOrder.Add("manual");
        };

        Replan();

        Assert.False(controller.IsRunning);
        Assert.Equal(QuestController.EAutomationType.Manual, controller.AutomationType);
        Assert.Empty(queue.RemainingTasks);
        Assert.Null(queue.CurrentTaskExecutor);
        executor.Received(1).StopNow();
        Assert.Equal(["executor", "purchase", "fishing", "gathering", "manual"], cleanupOrder);
        Assert.False(controller.HasExternalOperation);
        operations.Duty.Stop.DidNotReceive().InvokeAction();

        purchase.GetBusy().Returns(false);
        gather.GetBusy().Returns(false);
        cleanupOrder.Clear();
        Replan();
        Assert.False(controller.IsRunning);
        Assert.Equal(QuestController.EAutomationType.Manual, controller.AutomationType);
        executor.Received(1).StopNow();
        executor.DidNotReceive().Update();
        Assert.Equal(["purchase", "fishing", "gathering"], cleanupOrder);

        void SetField(string name, object value) =>
            typeof(QuestController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);

        void Replan()
        {
            var method = typeof(QuestController).GetMethod(entryPoint, BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (entryPoint == "CheckNextTasks")
                method.CreateDelegate<Action<string>>(controller)("New sequence");
            else
                method.CreateDelegate<Action>(controller)();
        }
    }
}
