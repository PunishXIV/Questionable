using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Questionable.Windows.ConfigComponents;
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
    }

    private sealed class SkipTestContext : IDisposable
    {
        private static readonly string[] SkipCommandExceptions = ["Skip", "SkipNx"];
        private readonly AutoDutyFixture _duty = new();
        private readonly HighlightObject _highlight;
        private readonly CombatController _combat;
        private readonly MovementController _movement;

        public QuestController Controller { get; }
        public QuestController.QuestProgress Progress { get; }
        public TaskQueue Queue { get; } = new(NullLogger.Instance);

        public SkipTestContext(bool automatic, int stepCount)
        {
            var framework = Substitute.For<IFramework>();
            framework.IsFrameworkUnloading.Returns(true);
            var objects = Substitute.For<IObjectTable>();
            var condition = Substitute.For<ICondition>();
            var client = Substitute.For<IClientState>();
            var purchase = Substitute.For<IKnightshopperIpc>();
            var gather = Substitute.For<IGatherChillIpc>();
            var fishing = Substitute.For<IFeeshIpc>();
            var gatheringOperation = new GatheringOperationState();
            _highlight = new HighlightObject(framework, new(), condition, objects,
                Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);
            _combat = new CombatController([], null!, Substitute.For<ITargetManager>(), objects,
                condition, client, null!, null!, framework, NullLogger<CombatController>.Instance);
            _movement = new MovementController(null!, client, null!, null!, condition, null!, objects,
                null!, Substitute.For<IChatGui>(), framework, new(), purchase, gatheringOperation, gather,
                new(), fishing, _duty.Ipc, null!, NullLogger<MovementController>.Instance);
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
            SetField("_highlightObject", _highlight);
            SetField("_tracker", tracker);
            SetField("_objectTable", objects);
            SetField("_movementController", _movement);
            SetField("_combatController", _combat);
            SetField("_bossModIpc", new BossModIpc(Substitute.For<IDalamudPluginInterface>(), new(), null!, null!));
            SetField("_autoDutyIpc", _duty.Ipc);
            SetField("_craftingOperation", new CraftingOperationState());
            SetField("_purchaseOperation", new PurchaseOperationState());
            SetField("_gatheringOperation", gatheringOperation);
            SetField("_fishingOperation", new FishingOperationState());
            SetField("_knightshopperIpc", purchase);
            SetField("_gatherChillIpc", gather);
            SetField("_feeshIpc", fishing);
            SetField("_gatheringController", new GatheringController(gather, gatheringOperation, _ => 0, () => { }));
            var stopConditions = (StopConditionComponent)RuntimeHelpers.GetUninitializedObject(typeof(StopConditionComponent));
            typeof(StopConditionComponent).GetField("commandExceptions", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(stopConditions, SkipCommandExceptions);
            SetField("_stopConditionComponent", stopConditions);
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
            _movement.Dispose();
            _combat.Dispose();
            _highlight.Dispose();
            _duty.Dispose();
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
        var framework = Substitute.For<IFramework>();
        framework.IsFrameworkUnloading.Returns(true);
        using var duty = new AutoDutyFixture();
        using var combat = new CombatController([], null!, Substitute.For<ITargetManager>(),
            Substitute.For<IObjectTable>(), Substitute.For<ICondition>(), Substitute.For<IClientState>(),
            null!, null!, framework, NullLogger<CombatController>.Instance);

        // Skip the constructor's game hooks and UI services; exercise the real step transition.
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        SetField("_progressLock", Activator.CreateInstance(typeof(System.Threading.Lock))!);
        SetField("_logger", NullLogger<QuestController>.Instance);
        SetField("_tracker", tracker);
        SetField("_craftingOperation", new CraftingOperationState());
        SetField("_purchaseOperation", new PurchaseOperationState());
        SetField("_gatheringOperation", new GatheringOperationState());
        SetField("_fishingOperation", new FishingOperationState());
        SetField("_autoDutyIpc", duty.Ipc);
        SetField("_combatController", combat);
        SetField("_bossModIpc", new BossModIpc(Substitute.For<IDalamudPluginInterface>(), new(), null!, null!));

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
        var framework = Substitute.For<IFramework>();
        framework.IsFrameworkUnloading.Returns(true);
        var objects = Substitute.For<IObjectTable>();
        var condition = Substitute.For<ICondition>();
        var client = Substitute.For<IClientState>();
        using var duty = new AutoDutyFixture();
        using var highlight = new HighlightObject(framework, new(), condition, objects,
            Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);
        using var combat = new CombatController([], null!, Substitute.For<ITargetManager>(), objects,
            condition, client, null!, null!, framework, NullLogger<CombatController>.Instance);
        var purchase = Substitute.For<IKnightshopperIpc>();
        var gather = Substitute.For<IGatherChillIpc>();
        var fishing = Substitute.For<IFeeshIpc>();
        List<string> cleanupOrder = [];
        purchase.When(x => x.CancelOwned()).Do(_ => cleanupOrder.Add("purchase"));
        fishing.When(x => x.CancelOwned()).Do(_ => cleanupOrder.Add("fishing"));
        gather.When(x => x.CancelOwned()).Do(_ => cleanupOrder.Add("gathering"));
        purchase.IsReady().Returns(provider == "purchase");
        gather.IsAvailable().Returns(provider == "gather");
        bool? busy = unknown ? null : true;
        purchase.GetBusy().Returns(provider == "purchase" ? busy : false);
        gather.GetBusy().Returns(provider == "gather" ? busy : false);
        var gatheringOperation = new GatheringOperationState();
        using var movement = new MovementController(null!, client, null!, null!, condition, null!, objects,
            null!, Substitute.For<IChatGui>(), framework, new(), purchase, gatheringOperation, gather,
            new(), fishing, duty.Ipc, null!, NullLogger<MovementController>.Instance);

        // Skip game-sheet and UI construction; exercise the real replan, Stop and cleanup paths.
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        SetField("_logger", NullLogger<QuestController>.Instance);
        SetField("_highlightObject", highlight);
        SetField("_tracker", new QuestProgressTracker(null!, null!, highlight, NullLogger<QuestProgressTracker>.Instance));
        SetField("_objectTable", objects);
        SetField("_movementController", movement);
        SetField("_combatController", combat);
        SetField("_bossModIpc", new BossModIpc(Substitute.For<IDalamudPluginInterface>(), new(), null!, null!));
        SetField("_autoDutyIpc", duty.Ipc);
        SetField("_craftingOperation", new CraftingOperationState());
        SetField("_purchaseOperation", new PurchaseOperationState());
        SetField("_gatheringOperation", gatheringOperation);
        SetField("_fishingOperation", new FishingOperationState());
        SetField("_knightshopperIpc", purchase);
        SetField("_gatherChillIpc", gather);
        SetField("_feeshIpc", fishing);
        SetField("_gatheringController", new GatheringController(gather, gatheringOperation, _ => 0, () => { }));
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
        duty.Stop.DidNotReceive().InvokeAction();

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
