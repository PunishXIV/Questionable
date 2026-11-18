using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;

namespace Questionable.Controller.Steps;

internal static class TeleportTaskDetector
{
    public static ITask? GetUpcoming(ITaskExecutor? current, ITask? next, uint territoryId) => current switch
    {
        AetheryteShortcut.Executor { HasTeleported: false } => current.CurrentTask,
        // MappingTheRealm picks teleport, ferry or walking itself, so any trip to another zone counts.
        null when next is Travel.Task travel && travel.TerritoryId != territoryId => next,
        // The ticket is the only item quest paths use to change zone; other items are quest items.
        null when next is UseItem.IUseTask { ItemId: QuestStep.VesperBayAetheryteTicket } => next,
        _ => null,
    };
}
