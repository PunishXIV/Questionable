using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestRecoveryIntegrationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ControllerMapsEscapeThroughTheLiveSetting(bool enabled, bool pressed, bool expected)
    {
        var configuration = new Configuration();
        configuration.General.UseEscToCancelQuesting = enabled;
        var keys = Substitute.For<IKeyState>();
        keys[VirtualKey.ESCAPE].Returns(pressed);
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        typeof(QuestController).GetField("_configuration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, configuration);
        typeof(QuestController).GetField("_keyState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, keys);

        Assert.Equal(expected, ((IQuestRecoveryHost)controller).EscapeRequested);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ControllerMapsBothZoningFlags(bool betweenAreas, bool betweenAreas51, bool expected)
    {
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.BetweenAreas].Returns(betweenAreas);
        condition[ConditionFlag.BetweenAreas51].Returns(betweenAreas51);
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        typeof(QuestController).GetField("_condition", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, condition);

        Assert.Equal(expected, ((IQuestRecoveryHost)controller).IsZoning);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "UI stop")]
    [InlineData(true, "UI停止")]
    [InlineData(true, "UI 停止")]
    [InlineData(true, "{UI stop}")]
    [InlineData(true, "{UI停止}")]
    [InlineData(true, "{UI 停止}")]
    [InlineData(false, "UI停止")]
    [InlineData(false, "UI 停止")]
    public void ControllerStopResetsRecoveryBeforeCleanupEvenWhenTheQueueIsIdle(bool queued, string? uiLabel)
    {
        var clock = new TestClock();
        var recovery = new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance, clock);
        var recoveringHost = Substitute.For<IQuestRecoveryHost>();
        recoveringHost.IsLoggedIn.Returns(true);
        recoveringHost.HasQueuedTasks.Returns(true);
        recoveringHost.IsUnconscious.Returns(true);
        recoveringHost.ClearTasks().Returns(true);
        Assert.True(recovery.Update(recoveringHost));
        Assert.True(recovery.IsRecovering);

        using var operations = new ExternalOperationFixture();
        using var highlight = new HighlightObject(operations.Framework, new(), operations.Condition, operations.Objects,
            Substitute.For<IDataManager>(), NullLogger<HighlightObject>.Instance);
        var queue = new TaskQueue(NullLogger.Instance);
        if (queued)
            queue.Enqueue(new WaitAtEnd.WaitNextStepOrSequence());
        bool cleanupObserved = false;
        operations.Knightshopper.When(x => x.CancelOwned()).Do(_ =>
        {
            cleanupObserved = true;
            Assert.False(recovery.IsRecovering);
        });

        // Limited wiring check: native construction and the full framework Update remain outside this fixture.
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        SetField("_logger", NullLogger<QuestController>.Instance);
        SetField("_recovery", recovery);
        SetField("_highlightObject", highlight);
        SetField("_tracker", new QuestProgressTracker(null!, null!, highlight, NullLogger<QuestProgressTracker>.Instance));
        SetField("_objectTable", operations.Objects);
        SetField("_externalOperations", operations.Coordinator);
        var configuration = new Configuration();
        configuration.Stop.RunCommandAfterStop = true;
        configuration.Stop.CommandAfterStop = "/after-stop";
        List<string> stopEffects = [];
        SetField("_stopPolicy", new QuestStopPolicy(configuration, _ => stopEffects.Add("notify"),
            stopEffects.Add, NullLogger<QuestStopPolicy>.Instance));
        if (uiLabel != null)
            controller.AutomationType = QuestController.EAutomationType.Automatic;
        typeof(MiniTaskController<QuestController>).GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, queue);
        controller.IsQuestWindowOpenFunction = () => false;
        Assert.False(controller.IsQuestingStopped);

        if (uiLabel != null)
            controller.StopFromUi(uiLabel);
        else
            controller.Stop("UI stop");

        Assert.Empty(stopEffects);
        Assert.Equal(QuestController.EAutomationType.Manual, controller.AutomationType);

        Assert.True(cleanupObserved);
        Assert.False(recovery.IsRecovering);
        Assert.True(queue.AllTasksComplete);
        Assert.True(controller.IsQuestingStopped);
        recoveringHost.HasQueuedTasks.Returns(false);
        recoveringHost.IsUnconscious.Returns(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(recovery.Update(recoveringHost));
        recoveringHost.DidNotReceive().RetryCurrentStep();

        void SetField(string name, object value) =>
            typeof(QuestController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);
    }
}
