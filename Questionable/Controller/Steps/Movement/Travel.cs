using static Questionable.Controller.Steps.ITaskExecutor;
using Route = System.Threading.Tasks.Task;

namespace Questionable.Controller.Steps.Movement;

internal static class Travel
{
    // Territory 1 marks steps that must not travel; duty instances are entered through the duty finder.
    internal static bool CanTravel(QuestStep step, uint territoryId, TerritoryData territoryData) =>
        step.TerritoryId != 1 && (territoryId == step.TerritoryId || !territoryData.IsDutyInstance(step.TerritoryId));

    internal static uint? TalkTarget(QuestStep step) =>
        step.InteractionType is EInteractionType.Interact or EInteractionType.AcceptQuest or EInteractionType.CompleteQuest
            ? step.DataId
            : null;

    internal sealed record Task(uint TerritoryId, Vector3? Position, uint? DataId) : ITask
    {
        public override string ToString() => $"Travel({TerritoryData.GetNameAndId(TerritoryId)})";
    }

    internal sealed class Executor(MappingTheRealmIpc mappingTheRealm, IClientState clientState)
        : TaskExecutor<Task>, IStoppable
    {
        private Route? _route;

        protected override bool Start()
        {
            _route = null;
            if (Task.Position == null && clientState.TerritoryType == Task.TerritoryId)
                return false;

            if (!mappingTheRealm.IsReady())
                throw new NeedsPlayerException("MappingTheRealm is needed for travel between zones. Install it from the Plugins tab.");

            if (mappingTheRealm.GetPathing() == true)
                throw new NeedsPlayerException("MappingTheRealm is busy with another plugin's route. Restart Questionable when it's done.");

            _route = mappingTheRealm.Travel(Task.TerritoryId, Task.Position, Task.DataId);
            return true;
        }

        public override ETaskResult Update()
        {
            if (_route is not { IsCompleted: true } route)
                return ETaskResult.StillRunning;

            if (route.IsFaulted)
                throw new TaskException(
                    $"MappingTheRealm couldn't reach {TerritoryData.GetNameAndId(Task.TerritoryId)}: {route.Exception!.GetBaseException().Message}");
            if (route.IsCanceled)
                throw new TaskException("MappingTheRealm's route was cancelled");
            return ETaskResult.TaskComplete;
        }

        public void StopNow()
        {
            if (_route is { IsCompleted: false })
                mappingTheRealm.Stop();
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
