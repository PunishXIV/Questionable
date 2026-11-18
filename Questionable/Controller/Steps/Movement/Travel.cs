using static Questionable.Controller.Steps.ITaskExecutor;
using Route = System.Threading.Tasks.Task;

namespace Questionable.Controller.Steps.Movement;

internal static class Travel
{
    // Territory 1 marks steps that must not travel; duty instances are entered through the duty finder.
    internal static bool CanTravel(QuestStep step, uint territoryId, TerritoryData territoryData) =>
        step.TerritoryId != 1 && (territoryId == step.TerritoryId || !territoryData.IsDutyInstance(step.TerritoryId));

    // An aetheryte's base id is its Aetheryte row; its own position is inside the crystal.
    internal static uint? TalkTarget(QuestStep step) => step.InteractionType switch
    {
        EInteractionType.Interact or EInteractionType.AcceptQuest or EInteractionType.CompleteQuest => step.DataId,
        EInteractionType.AttuneAetheryte or EInteractionType.RegisterFreeOrFavoredAetheryte => (uint?)step.Aetheryte,
        EInteractionType.AttuneAethernetShard => (uint?)step.AethernetShard,
        _ => null,
    };

    internal sealed record Task(uint TerritoryId, Vector3? Position, uint? DataId) : ITask
    {
        public override string ToString() => $"Travel({TerritoryData.GetNameAndId(TerritoryId)})";
    }

    internal sealed class Executor(MappingTheRealmIpc mappingTheRealm, IClientState clientState)
        : TaskExecutor<Task>, IStoppable
    {
        // Event objects (2000000 and up) need you closer than MappingTheRealm 1.0.0.1's "in range", e.g. 2001563.
        private const uint FirstEventObject = 2_000_000;

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
            uint? npc = Task.DataId is < FirstEventObject ? Task.DataId : null;
            _route = mappingTheRealm.Travel(Task.TerritoryId, Task.Position, npc);
            return true;
        }

        public override ETaskResult Update()
        {
            if (mappingTheRealm.GetPathing() == true)
            {
                _started = true;
                return ETaskResult.StillRunning;
            }

            if (_route is { IsFaulted: true } faulted)
                throw new TaskException(
                    $"MappingTheRealm couldn't reach {TerritoryData.GetNameAndId(Task.TerritoryId)}: {faulted.Exception!.GetBaseException().Message}");
            if (_route is { IsCanceled: true })
                throw new TaskException("MappingTheRealm's route was cancelled");

            // IsPathing reads false for a moment before the route starts.
            return _started || _route is { IsCompleted: true } ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        public void StopNow()
        {
            if (_route != null)
                mappingTheRealm.Stop();
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
