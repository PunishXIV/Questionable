using static Questionable.Controller.Steps.ITaskExecutor;
using Route = System.Threading.Tasks.Task;

namespace Questionable.Controller.Steps.Movement;

internal static class Travel
{
    // Territory 1 marks steps that must not travel.
    internal static bool CanTravel(QuestStep step) => step.TerritoryId != 1;

    // Older paths say Mount: false for a short hop too; only following an NPC needs the vnav walk.
    internal static bool Follows(QuestStep step) =>
        step is { Mount: false, InteractionType: EInteractionType.WalkTo or EInteractionType.WaitForNpcAtPosition };

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

    internal sealed record Task(uint TerritoryId, Vector3? Position, uint? DataId, float Range = 0, bool Walk = false,
        uint? Leader = null) : ITask
    {
        public override string ToString() => $"Travel({TerritoryData.GetNameAndId(TerritoryId)})";
    }

    internal sealed class Executor(MappingTheRealmIpc mappingTheRealm, NavmeshIpc navmesh, IClientState clientState,
        ICondition condition, IObjectTable objectTable, GameFunctions gameFunctions, AlliedSocietyData alliedSocietyData,
        ILogger<Executor> logger)
        : TaskExecutor<Task>, IStoppable
    {
        private const float VnavArrival = 3f;
        private static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(15);

        private Route? _route;
        private bool _started;
        private uint _zone;
        private bool _byPosition;
        private bool _escort;
        private bool _escortTried;
        private DateTime? _catchUpSince;
        private bool _pace;
        private bool _paused;
        private Vector3? _heading;
        private Vector3? _leaderAt;
        private DateTime _leaderMoved;
        private bool _waiting;
        private DateTime _readyAfter;
        private int _retries;
        private DateTime? _vnavSince;
        private DateTime? _idleSince;

        protected override bool Start()
        {
            _route = null;
            _started = false;
            _retries = 0;
            _vnavSince = null;
            _paused = false;
            _heading = null;
            _leaderAt = null;
            _zone = clientState.TerritoryType;
            if (Task.Position == null && _zone == Task.TerritoryId)
                return false;

            _byPosition = false;
            _escort = Task.DataId is { } escorted && NearPlayer(escorted);
            _escortTried = _escort;
            _catchUpSince = null;
            if (_escort)
                logger.LogInformation("{DataId} walks with the player, travelling to the step's position", Task.DataId);
            // The leader waits ahead of the player, often further than an escort walks (FF16 4798, Torgal).
            _pace = Task is { Walk: true, Leader: { } leader, Position: { } goal } && _zone == Task.TerritoryId &&
                gameFunctions.FindObjectByDataId(leader) is { } npc && Vector3.Distance(npc.Position, goal) > 8;
            if (_pace)
                logger.LogInformation("Walking behind {Leader}", Task.Leader);

            if (!mappingTheRealm.IsReady())
                throw new NeedsPlayerException("MappingTheRealm is needed for travel between zones. Install it from the Plugins page in settings.");

            // A route from a stopped run can still be moving; Stop only cancels routes Questionable started.
            mappingTheRealm.Stop();
            Restart(TimeSpan.Zero);
            return true;
        }

        private bool UseId => Task.DataId != null && !_byPosition;

        // An escorted NPC walks with the player, so a route to it ends where the player stands (FF16 4797, Clive).
        private bool NearPlayer(uint id) =>
            Task is { Position: { } spot } && _zone == Task.TerritoryId &&
            gameFunctions.FindObjectByDataId(id) is { } target && objectTable[0] is { } player &&
            Vector3.Distance(target.Position, player.Position) < 10 && Vector3.Distance(target.Position, spot) > 10;

        private bool Followed(uint id) =>
            gameFunctions.FindObjectByDataId(id) is not { } target || objectTable[0] is not { } player ||
            Vector3.Distance(target.Position, player.Position) < 10;

        private void Restart(TimeSpan settle)
        {
            _route = null;
            _started = false;
            _idleSince = null;
            _waiting = true;
            _readyAfter = DateTime.Now + settle;
        }

        // A route started while vnavmesh (re)loads its mesh fails.
        private void TryGo()
        {
            if (DateTime.Now < _readyAfter || !navmesh.IsReady() ||
                WaitsForOldRoute(mappingTheRealm.GetPathing() == true, DateTime.Now - _readyAfter))
                return;

            _waiting = false;
            // MappingTheRealm teleports when that's quicker, and a teleport takes away a society's quest mount (2178).
            if (GameFunctions.GetMountId() is { } mount && alliedSocietyData.Mounts.ContainsKey(mount) &&
                clientState.TerritoryType == Task.TerritoryId && Task.Position is { } mounted)
            {
                if (!navmesh.MoveTo(mounted, condition[ConditionFlag.InFlight]))
                    throw new TaskException("vnavmesh couldn't find a path on the quest mount");
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

                if (!_pace && !navmesh.WalkTo(walkTo))
                    throw new TaskException("vnavmesh couldn't find a path on foot");
                _vnavSince = DateTime.Now;
                return;
            }

            if (Task.Walk)
            {
                _route = mappingTheRealm.Travel(Task.TerritoryId, null, null);
                return;
            }

            // By id, MappingTheRealm plans to its cached spot and ends at the live object; with a position it plans
            // to that position and only looks for the id on the way.
            _route = _escort
                ? mappingTheRealm.Travel(Task.TerritoryId, Task.Position, null)
                : mappingTheRealm.Travel(Task.TerritoryId, UseId ? null : Task.Position, Task.DataId, Task.Range);
        }

        // Follow steps fail when the player passes the NPC, and a resting NPC waits until the player is close (FF16 4798, Torgal).
        internal static bool KeepWalking(bool walking, float gap, bool resting)
        {
            float stop = resting ? 2 : 3;
            return walking ? gap > stop : gap > (resting ? stop : 5);
        }

        private ETaskResult Pace(Vector3 goal, Vector3 player, Vector3 npc)
        {
            // The leader's last stop is near the step's position, not on it.
            if (Vector3.Distance(npc, goal) <= 8)
            {
                navmesh.Stop();
                return ETaskResult.TaskComplete;
            }

            if (_leaderAt is not { } last || Vector3.Distance(last, npc) > 0.5f)
            {
                _leaderAt = npc;
                _leaderMoved = DateTime.Now;
            }

            bool resting = DateTime.Now - _leaderMoved > TimeSpan.FromSeconds(2);
            _paused = !KeepWalking(!_paused, Vector3.Distance(player, npc), resting);
            if (!_paused)
            {
                // vnavmesh rejects a new route while it is still pathfinding the last one.
                if (navmesh.GetSimpleMoveInProgress() == true)
                    return ETaskResult.StillRunning;
                if (_heading is not { } heading || Vector3.Distance(heading, npc) > 2)
                {
                    if (!navmesh.WalkTo(npc))
                        throw new TaskException("vnavmesh couldn't find a path on foot");
                    _heading = npc;
                }
            }
            else
            {
                _heading = null;
                // A stop sent while vnavmesh is still pathfinding is ignored, and the route starts anyway.
                if (navmesh.GetSimpleMoveInProgress() == true || navmesh.GetPathRunning() == true)
                    navmesh.Stop();
            }

            return ETaskResult.StillRunning;
        }

        private ETaskResult FollowVnav(DateTime since)
        {
            // A leader missing for a moment must not end the follow; the stuck retry covers one that's gone.
            if (_pace)
                return objectTable[0] is { } player && gameFunctions.FindObjectByDataId(Task.Leader!.Value) is { } npc
                    ? Pace(Task.Position!.Value, player.Position, npc.Position)
                    : ETaskResult.StillRunning;

            if (objectTable[0]?.Position is { } here && Vector3.Distance(here, Task.Position!.Value) <= VnavArrival)
            {
                navmesh.Stop();
                return ETaskResult.TaskComplete;
            }

            bool moving = navmesh.GetPathfindInProgress() == true || navmesh.GetPathRunning() == true;
            return moving || DateTime.Now - since < TimeSpan.FromSeconds(1) ? ETaskResult.StillRunning : ETaskResult.TaskComplete;
        }

        internal static bool KeepsRoute(bool routeOpen, bool pathing) => routeOpen && pathing;

        // A stopped route runs on until its teleport or aethernet step ends, and MappingTheRealm refuses a new one until then.
        internal static bool WaitsForOldRoute(bool pathing, TimeSpan waited) => pathing && waited < BusyWait;

        public override ETaskResult Update()
        {
            // MappingTheRealm fails a route once a zone gate moves the player (129 -> 128 gate 2210434, 135 -> 134 gate 2453662).
            if (clientState.TerritoryType != _zone)
            {
                _zone = clientState.TerritoryType;
                // A restart after MappingTheRealm's own teleport cancelled its next aethernet step.
                if (KeepsRoute(_route is { IsCompleted: false }, mappingTheRealm.GetPathing() == true))
                    return ETaskResult.StillRunning;

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
                _idleSince = null;
                return ETaskResult.StillRunning;
            }

            // IsPathing drops before a failed route's task faults (portal 2013565 in 132).
            if (_started && _route is { IsCompleted: false })
            {
                _idleSince ??= DateTime.Now;
                if (DateTime.Now - _idleSince < TimeSpan.FromSeconds(3))
                    return ETaskResult.StillRunning;
            }

            // By-id lookups fail before moving for objects MappingTheRealm hasn't indexed (rock 2001481); each retry can cost a teleport.
            // A route that moved and then failed stays failed, and a retry from on top of the NPC hides why (Il Mheg 1030271).
            if (_route is { IsFaulted: true } byId && !_started && UseId && Task.Position != null && _retries++ < 1)
            {
                logger.LogInformation("MappingTheRealm couldn't route to {DataId} ({Reason}), trying the step's position",
                    Task.DataId, byId.Exception!.GetBaseException().Message);
                _byPosition = true;
                Restart(TimeSpan.Zero);
                return ETaskResult.StillRunning;
            }

            if (_route is { IsFaulted: true } failed && !_started && _retries++ < 1)
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
            if (!_started && _route is not { IsCompleted: true })
                return ETaskResult.StillRunning;

            // An escort waiting away from the step only starts walking once the player reaches it (FF16 4797, Clive).
            if (!_escortTried && Task.DataId is { } escorted && NearPlayer(escorted))
            {
                _escort = _escortTried = true;
                logger.LogInformation("{DataId} walks with the player, travelling on to the step's position", escorted);
                Restart(TimeSpan.Zero);
                return ETaskResult.StillRunning;
            }

            // A step position that is just out of date leaves the NPC behind.
            if (_escort && Task.DataId is { } left && !Followed(left))
            {
                _catchUpSince ??= DateTime.Now;
                if (DateTime.Now - _catchUpSince < TimeSpan.FromSeconds(3))
                    return ETaskResult.StillRunning;

                _escort = false;
                logger.LogInformation("{DataId} didn't follow, going back to it", left);
                Restart(TimeSpan.Zero);
                return ETaskResult.StillRunning;
            }

            LogArrival();
            return ETaskResult.TaskComplete;
        }

        private void LogArrival()
        {
            Vector3? target = Task.DataId is { } id && gameFunctions.FindObjectByDataId(id) is { } found
                ? found.Position
                : Task.Position;
            if (target is not { } goal || objectTable[0]?.Position is not { } here || clientState.TerritoryType != Task.TerritoryId)
                return;

            float across = Vector2.Distance(new(here.X, here.Z), new(goal.X, goal.Z));
            float height = MathF.Abs(here.Y - goal.Y);
            if (height > 5 || across > 10)
                logger.LogWarning("MappingTheRealm finished {Across:F1} y across and {Height:F1} y above/below the target", across, height);
            else
                logger.LogDebug("MappingTheRealm finished {Across:F1} y across and {Height:F1} y above/below the target", across, height);
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
