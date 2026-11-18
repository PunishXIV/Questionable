using static Questionable.Controller.Steps.ITaskExecutor;
using Route = System.Threading.Tasks.Task;

namespace Questionable.Controller.Steps.Movement;

internal static class Travel
{
    // Territory 1 marks steps that must not travel; duty instances are entered through the duty finder.
    internal static bool CanTravel(QuestStep step, uint territoryId, TerritoryData territoryData) =>
        step.TerritoryId != 1 && (territoryId == step.TerritoryId || !territoryData.IsDutyInstance(step.TerritoryId));

    // An aetheryte's base id is its Aetheryte row; its own position is inside the crystal.
    internal static uint? InteractTarget(QuestStep step) => step.InteractionType switch
    {
        EInteractionType.AttuneAetheryte or EInteractionType.RegisterFreeOrFavoredAetheryte => (uint?)step.Aetheryte,
        EInteractionType.AttuneAethernetShard => (uint?)step.AethernetShard,
        EInteractionType.WalkTo or EInteractionType.Jump => null,
        _ => step.DataId,
    };

    // MappingTheRealm 1.0.0.1's PathToInteractable didn't route across zones to aetherytes and shards; by id does.
    internal static bool GoesById(QuestStep step) =>
        step.InteractionType is EInteractionType.AttuneAetheryte or EInteractionType.RegisterFreeOrFavoredAetheryte
            or EInteractionType.AttuneAethernetShard;

    internal sealed record Task(uint TerritoryId, Vector3? Position, uint? DataId, bool ById = false) : ITask
    {
        public override string ToString() => $"Travel({TerritoryData.GetNameAndId(TerritoryId)})";
    }

    internal sealed class Executor(MappingTheRealmIpc mappingTheRealm, IClientState clientState)
        : TaskExecutor<Task>, IStoppable
    {
        private Route? _route;
        private bool _started;
        private bool _arrived;

        protected override bool Start()
        {
            _route = null;
            _started = false;
            _arrived = clientState.TerritoryType == Task.TerritoryId;
            if (Task.Position == null && _arrived)
                return false;

            if (!mappingTheRealm.IsReady())
                throw new NeedsPlayerException("MappingTheRealm is needed for travel between zones. Install it from the Plugins tab.");

            // A route from a stopped run can still be moving; Stop only cancels routes Questionable started.
            mappingTheRealm.Stop();
            Go();
            return true;
        }

        private void Go() =>
            _route = mappingTheRealm.Travel(Task.TerritoryId, Task.ById && Task.DataId != null ? null : Task.Position, Task.DataId);

        public override ETaskResult Update()
        {
            // 1.0.0.2 plans a cross-zone route to the zone gate in the old zone (129 -> 128 became "T129, gate 2210434")
            // and fails once the gate moves the player, so a leg ends or starts over on arrival.
            if (!_arrived && clientState.TerritoryType == Task.TerritoryId)
            {
                _arrived = true;
                mappingTheRealm.Stop();
                if (Task.Position == null && Task.DataId == null)
                    return ETaskResult.TaskComplete;

                _started = false;
                Go();
                return ETaskResult.StillRunning;
            }

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
