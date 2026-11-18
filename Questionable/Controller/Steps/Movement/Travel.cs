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
        private bool _started;

        protected override bool Start()
        {
            _route = null;
            _started = false;
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
            if (mappingTheRealm.GetPathing() == true)
            {
                _started = true;
                if (!HasArrived())
                    return ETaskResult.StillRunning;

                mappingTheRealm.Stop();
                return ETaskResult.TaskComplete;
            }

            if (_route is { IsFaulted: true } faulted)
                throw new TaskException(
                    $"MappingTheRealm couldn't reach {TerritoryData.GetNameAndId(Task.TerritoryId)}: {faulted.Exception!.GetBaseException().Message}");
            if (_route is { IsCanceled: true })
                throw new TaskException("MappingTheRealm's route was cancelled");

            // IsPathing reads false for a moment before the route starts.
            return _started || _route is { IsCompleted: true } ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        // shortcut: MappingTheRealm 1.0.0.0 can stay on "Planning route" after arriving; delete once it ends the route itself.
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
