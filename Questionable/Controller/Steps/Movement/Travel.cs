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

    internal sealed record Task(uint TerritoryId, Vector3? Position, uint? DataId, float Range = 0, bool Walk = false) : ITask
    {
        public override string ToString() => $"Travel({TerritoryData.GetNameAndId(TerritoryId)})";
    }

    internal sealed class Executor(MappingTheRealmIpc mappingTheRealm, NavmeshIpc navmesh, IClientState clientState,
        ICondition condition, IObjectTable objectTable, GameFunctions gameFunctions, ILogger<Executor> logger)
        : TaskExecutor<Task>, IStoppable
    {
        private const float VnavArrival = 3f;

        private Route? _route;
        private bool _started;
        private uint _zone;
        private bool _byPosition;
        private bool _escort;
        private bool _waiting;
        private DateTime _readyAfter;
        private int _retries;
        private DateTime? _vnavSince;

        protected override bool Start()
        {
            _route = null;
            _started = false;
            _retries = 0;
            _vnavSince = null;
            _zone = clientState.TerritoryType;
            if (Task.Position == null && _zone == Task.TerritoryId)
                return false;

            _byPosition = false;
            _escort = FollowsPlayer();
            if (_escort)
                logger.LogInformation("{DataId} walks with the player, travelling to the step's position", Task.DataId);

            if (!mappingTheRealm.IsReady())
                throw new NeedsPlayerException("MappingTheRealm is needed for travel between zones. Install it from the Plugins page in settings.");

            // A route from a stopped run can still be moving; Stop only cancels routes Questionable started.
            mappingTheRealm.Stop();
            Restart(TimeSpan.Zero);
            return true;
        }

        private bool UseId => Task.DataId != null && !_byPosition;

        // An escorted NPC walks with the player, so a route to it ends where the player stands (FF16 4797, Clive).
        private bool FollowsPlayer() =>
            Task is { DataId: { } id, Position: { } spot } && _zone == Task.TerritoryId &&
            gameFunctions.FindObjectByDataId(id) is { } target && objectTable[0] is { } player &&
            Vector3.Distance(target.Position, player.Position) < 5 && Vector3.Distance(target.Position, spot) > 10;

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
            // MappingTheRealm can't route underwater; its fallback teleports the player out of the water.
            if (condition[ConditionFlag.Diving] && clientState.TerritoryType == Task.TerritoryId && Task.Position is { } position)
            {
                if (!navmesh.FlyTo(position))
                    throw new TaskException("vnavmesh couldn't find an underwater path");
                _vnavSince = DateTime.Now;
                return;
            }

            // MappingTheRealm mounts and flies whenever it can; follow and stealth steps fail when you outrun the NPC.
            if (Task.Walk && clientState.TerritoryType == Task.TerritoryId && Task.Position is { } walkTo)
            {
                if (condition[ConditionFlag.Mounted])
                {
                    gameFunctions.Unmount();
                    _waiting = true;
                    _readyAfter = DateTime.Now.AddSeconds(1);
                    return;
                }

                if (!navmesh.WalkTo(walkTo))
                    throw new TaskException("vnavmesh couldn't find a path on foot");
                _vnavSince = DateTime.Now;
                return;
            }

            if (Task.Walk)
            {
                _route = mappingTheRealm.Travel(Task.TerritoryId, null, null);
                return;
            }

            // MappingTheRealm routes to the object whenever it gets an id, even with a position.
            _route = _escort
                ? mappingTheRealm.Travel(Task.TerritoryId, Task.Position, null)
                : mappingTheRealm.Travel(Task.TerritoryId, UseId ? null : Task.Position, Task.DataId, Task.Range);
        }

        private ETaskResult FollowVnav(DateTime since)
        {
            if (objectTable[0]?.Position is { } here && Vector3.Distance(here, Task.Position!.Value) <= VnavArrival)
            {
                navmesh.Stop();
                return ETaskResult.TaskComplete;
            }

            bool moving = navmesh.GetPathfindInProgress() == true || navmesh.GetPathRunning() == true;
            return moving || DateTime.Now - since < TimeSpan.FromSeconds(1) ? ETaskResult.StillRunning : ETaskResult.TaskComplete;
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

            if (_vnavSince is { } since)
                return FollowVnav(since);

            if (mappingTheRealm.GetPathing() == true)
            {
                _started = true;
                return ETaskResult.StillRunning;
            }

            // By-id lookups fail for objects MappingTheRealm hasn't indexed (rock 2001481); each retry can cost a teleport.
            if (_route is { IsFaulted: true } && UseId && Task.Position != null && _retries++ < 1)
            {
                _byPosition = true;
                Restart(TimeSpan.Zero);
                return ETaskResult.StillRunning;
            }

            if (_route is { IsFaulted: true } failed && _retries++ < 1)
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
            if (_vnavSince != null)
                navmesh.Stop();
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
