using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Utils;

namespace Questionable.Tests.Steps;

internal static class TestQuestController
{
    public static QuestController Create(
        TaskQueue? queue = null,
        ExternalOperationFixture? operations = null,
        HighlightObject? highlight = null,
        QuestProgressTracker? tracker = null,
        Configuration? configuration = null,
        QuestAcceptanceController? acceptance = null,
        QuestRecoveryController? recovery = null,
        QuestStopPolicy? stopPolicy = null)
    {
        var controller = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
        configuration ??= new();
        // Created untyped: boxing a typed Lock warns (CS9216), and the field is set by reflection anyway.
        Set(controller, "_progressLock", Activator.CreateInstance(typeof(Lock))!);
        Set(controller, "_logger", NullLogger<QuestController>.Instance);
        Set(controller, "_configuration", configuration);
        Set(controller, "_acceptance", acceptance ?? new QuestAcceptanceController(configuration));
        Set(controller, "_selection", new QuestSelectionController(configuration));
        Set(controller, "_recovery", recovery ?? new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance));
        Set(controller, "_stopPolicy", stopPolicy ??
            new QuestStopPolicy(configuration, _ => { }, _ => { }, _ => { }, Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance));
        Set(controller, "_tracker", tracker ??
            new QuestProgressTracker(null!, null!, highlight!, NullLogger<QuestProgressTracker>.Instance));
        if (highlight != null)
            Set(controller, "_highlightObject", highlight);
        if (operations != null)
        {
            Set(controller, "_objectTable", operations.Objects);
            Set(controller, "_movementController", operations.Movement);
            Set(controller, "_combatController", operations.Combat);
            Set(controller, "_gatheringController", operations.GatheringController);
            Set(controller, "_externalOperations", operations.Coordinator);
        }

        typeof(MiniTaskController<QuestController>).GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, queue ?? new TaskQueue(NullLogger.Instance));
        return controller;
    }

    public static void Set(QuestController controller, string field, object value) =>
        typeof(QuestController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);
}
