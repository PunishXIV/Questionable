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

    internal sealed class Executor(MappingTheRealmIpc mappingTheRealm, IClientState clientState, IObjectTable objectTable)
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

            // A route from a stopped run can still be moving; Stop only cancels routes Questionable started.
            mappingTheRealm.Stop();
            _route = mappingTheRealm.Travel(Task.TerritoryId, Task.Position, Task.DataId);
            return true;
        }

        public override ETaskResult Update()
        {
            // MappingTheRealm 1.0.0.0 can stay on "Planning route" after the character has arrived.
            if (HasArrived())
            {
                mappingTheRealm.Stop();
                return ETaskResult.TaskComplete;
            }

            // The route's task can finish when the move is handed over, while the character is still walking.
            if (_route is not { IsCompleted: true } route || mappingTheRealm.GetPathing() == true)
                return ETaskResult.StillRunning;

            if (route.IsFaulted)
                throw new TaskException(
                    $"MappingTheRealm couldn't reach {TerritoryData.GetNameAndId(Task.TerritoryId)}: {route.Exception!.GetBaseException().Message}");
            if (route.IsCanceled)
                throw new TaskException("MappingTheRealm's route was cancelled");
            return ETaskResult.TaskComplete;
        }

        // An NPC can be talked to from a few yalms away.
        private bool HasArrived()
        {
            if (clientState.TerritoryType != Task.TerritoryId)
                return false;
            if (Task.Position is not { } destination)
                return true;
            return objectTable[0]?.Position is { } position &&
                   Vector3.Distance(position, destination) < (Task.DataId != null ? 5f : QuestStep.DefaultStopDistance);
        }

        public void StopNow()
        {
            if (_route != null)
                mappingTheRealm.Stop();
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
