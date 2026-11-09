using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;

namespace Questionable.Controller.Steps;

internal static class TeleportTaskDetector
{
    public static ITask? GetUpcoming(ITaskExecutor? current, ITask? next) => current switch
    {
        AetheryteShortcut.Executor { HasTeleported: false } => current.CurrentTask,
        null when next is UseItem.IUseTask => next,
        _ => null,
    };
}
