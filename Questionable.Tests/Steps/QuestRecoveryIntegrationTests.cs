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
    [InlineData(false, null)]
    [InlineData(true, "UI stop")]
    [InlineData(true, "UI停止")]
    public void Stop_ResetsRecoveryBeforeCleanup(bool queued, string? uiLabel)
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
            queue.Enqueue(new WaitAtEnd.NextStepOrSequence());

        var configuration = new Configuration();
        configuration.Stop.RunCommandAfterStop = true;
        configuration.Stop.CommandAfterStop = "/after-stop";
        List<string> stopEffects = [];
        var controller = TestQuestController.Create(queue, operations, highlight, configuration: configuration,
            recovery: recovery, stopPolicy: new QuestStopPolicy(configuration, _ => stopEffects.Add("notify"), _ => { },
                stopEffects.Add, Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance));
        if (uiLabel != null)
            controller.AutomationType = QuestController.EAutomationType.Automatic;
        controller.IsQuestWindowOpenFunction = () => false;
        Assert.False(controller.IsQuestingStopped);

        if (uiLabel != null)
            controller.StopFromUi(uiLabel);
        else
            controller.Stop("UI stop");

        Assert.Empty(stopEffects);
        Assert.Equal(QuestController.EAutomationType.Manual, controller.AutomationType);

        Assert.False(recovery.IsRecovering);
        Assert.True(queue.AllTasksComplete);
        Assert.True(controller.IsQuestingStopped);
        recoveringHost.HasQueuedTasks.Returns(false);
        recoveringHost.IsUnconscious.Returns(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(recovery.Update(recoveringHost));
        recoveringHost.DidNotReceive().RetryCurrentStep();
    }
}
