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
using Questionable.Controller.Utils;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestTaskQueueTests
{
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

        // Use the real step-transition method with managed quest data, without starting
        // native game hooks or constructing unrelated UI services.
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
            new(), duty.Ipc, null!, NullLogger<MovementController>.Instance);

        // Bypass unrelated game-sheet and UI construction; exercise the real replan,
        // Stop, provider cleanup and TaskQueue cancellation paths with no native player.
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
