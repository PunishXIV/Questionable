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

    // Skills need attack range; MappingTheRealm otherwise stops at interaction range (rock 2001481).
    internal static float StopRange(QuestStep step) => step.InteractionType == EInteractionType.Action ? 3f : 0;

    internal sealed record Task(uint TerritoryId, Vector3? Position, uint? DataId, float Range = 0) : ITask
    {
        public override string ToString() => $"Travel({TerritoryData.GetNameAndId(TerritoryId)})";
    }

    internal sealed class Executor(MappingTheRealmIpc mappingTheRealm, NavmeshIpc navmesh, IClientState clientState,
        ILogger<Executor> logger)
        : TaskExecutor<Task>, IStoppable
    {
        private Route? _route;
        private bool _started;
        private uint _zone;
        private bool _byPosition;
        private bool _waiting;
        private DateTime _readyAfter;
        private int _retries;

        protected override bool Start()
        {
            _route = null;
            _started = false;
            _byPosition = false;
            _retries = 0;
            _zone = clientState.TerritoryType;
            if (Task.Position == null && _zone == Task.TerritoryId)
                return false;

            if (!mappingTheRealm.IsReady())
                throw new NeedsPlayerException("MappingTheRealm is needed for travel between zones. Install it from the Helper plugins page in settings.");

            // A route from a stopped run can still be moving; Stop only cancels routes Questionable started.
            mappingTheRealm.Stop();
            Restart(TimeSpan.Zero);
            return true;
        }

        private bool UseId => Task.DataId != null && !_byPosition;

        private void Restart(TimeSpan settle)
        {
            _route = null;
            _started = false;
            _waiting = true;
            _readyAfter = DateTime.Now + settle;
        }

        // A route started while vnavmesh (re)loads its mesh fails.
        private void TryGo()
        {
            if (DateTime.Now < _readyAfter || !navmesh.IsReady())
                return;

            _waiting = false;
            _route = mappingTheRealm.Travel(Task.TerritoryId, UseId ? null : Task.Position, Task.DataId, Task.Range);
        }

        public override ETaskResult Update()
        {
            // MappingTheRealm fails a route once a zone gate moves the player (129 -> 128 gate 2210434, 135 -> 134 gate 2453662).
            if (clientState.TerritoryType != _zone)
            {
                _zone = clientState.TerritoryType;
                mappingTheRealm.Stop();
                if (_zone == Task.TerritoryId && Task.Position == null && Task.DataId == null)
                    return ETaskResult.TaskComplete;

                // vnavmesh briefly reports the old zone's mesh as ready.
                Restart(TimeSpan.FromSeconds(1));
                return ETaskResult.StillRunning;
            }

            if (_waiting)
            {
                TryGo();
                return ETaskResult.StillRunning;
            }

            if (mappingTheRealm.GetPathing() == true)
            {
                _started = true;
                return ETaskResult.StillRunning;
            }

            // By-id lookups fail for objects MappingTheRealm hasn't indexed, e.g. rock 2001481.
            if (_route is { IsFaulted: true } && UseId && Task.Position != null)
            {
                _byPosition = true;
                Restart(TimeSpan.Zero);
                return ETaskResult.StillRunning;
            }

            if (_route is { IsFaulted: true } failed && _retries++ < 2)
            {
                logger.LogWarning("MappingTheRealm route failed ({Reason}), waiting for the navmesh and trying again",
                    failed.Exception!.GetBaseException().Message);
                Restart(TimeSpan.FromSeconds(1));
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

        public override bool ShouldInterruptOnDamage() => false;
    }
}
