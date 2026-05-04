using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Microsoft.Extensions.Logging;
using Questionable.Controller.NavigationOverrides;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Common.Converter;
using Questionable.Model.Questing;
namespace Questionable.Controller;

internal sealed class MovementController
(
    NavmeshIpc navmeshIpc,
    IClientState clientState,
    GameFunctions gameFunctions,
    ChatFunctions chatFunctions,
    ICondition condition,
    MovementOverrideController movementOverrideController,
    IObjectTable objectTable,
    AetheryteData aetheryteData,
    ICommandManager commandManager,
    ILogger<MovementController> logger) : IDisposable
{
    public ICommandManager CommandManager { get; } = commandManager;
    public const float DefaultVerticalInteractionDistance = 1.95f;

    private CancellationTokenSource? _cancellationTokenSource;
    private Task<List<Vector3>>? _pathfindTask;

    public bool IsNavmeshReady
    {
        get
        {
            try
            {
                return navmeshIpc.IsReady;
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
        }
    }

    public bool IsPathRunning
    {
        get
        {
            try
            {
                return navmeshIpc.IsPathRunning;
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
        }
    }

    public bool IsPathfinding => _pathfindTask is { IsCompleted: false };
    public DestinationData? Destination { get; set; }
    public DateTime MovementStartedAt { get; private set; } = DateTime.Now;
    public int BuiltNavmeshPercent => navmeshIpc.GetBuildProgress();

    public void Dispose() => Stop();

    public void Update()
    {
        if (_pathfindTask != null && Destination != null)
        {
            if (_pathfindTask.IsCompletedSuccessfully)
            {
                logger.LogInformation("Pathfinding complete, got {Count} points", _pathfindTask.Result.Count);
                if (_pathfindTask.Result.Count == 0)
                {
                    //_commandManager.ProcessCommand("/vnav rebuild");
                    ResetPathfinding();
                    throw new PathfindingFailedException();
                }

                List<Vector3> navPoints = _pathfindTask.Result.Skip(1).ToList();
                Vector3 start = objectTable[0]?.Position ?? navPoints[0];
                if (Destination.IsFlying && !condition[ConditionFlag.InFlight] && condition[ConditionFlag.Mounted])
                {
                    if (IsOnFlightPath(start) || navPoints.Any(IsOnFlightPath))
                    {
                        unsafe
                        {
                            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
                        }
                    }
                }

                if (!Destination.IsFlying)
                {
                    (navPoints, bool recalculateNavmesh) = movementOverrideController.AdjustPath(navPoints);
                    if (recalculateNavmesh && Destination.ShouldRecalculateNavmesh())
                    {
                        Destination.NavmeshCalculations++;
                        Destination.PartialRoute.AddRange(navPoints);
                        logger.LogInformation("Running navmesh recalculation with fudged point ({From} to {To})",
                            navPoints.Last(), Destination.Position);

                        _cancellationTokenSource = new();
                        _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));
                        _pathfindTask =
                            navmeshIpc.Pathfind(navPoints.Last(), Destination.Position, Destination.IsFlying,
                                _cancellationTokenSource.Token);
                        return;
                    }
                }

                navPoints = Destination.PartialRoute.Concat(navPoints).ToList();
                logger.LogInformation("Navigating via route: [{Route}]",
                    string.Join(" → ",
                        _pathfindTask.Result.Select(x => x.ToString("G", CultureInfo.InvariantCulture))));

                navmeshIpc.MoveTo(navPoints, Destination.IsFlying);
                MovementStartedAt = DateTime.Now;

                ResetPathfinding();
            }
            else if (_pathfindTask.IsCompleted)
            {
                logger.LogWarning("Unable to complete pathfinding task");
                //_commandManager.ProcessCommand("/vnav rebuild");
                ResetPathfinding();
                throw new PathfindingFailedException();
            }
        }

        if (IsPathRunning && Destination != null)
        {
            if (gameFunctions.IsLoadingScreenVisible())
            {
                logger.LogInformation("Stopping movement, loading screen visible");
                Stop();
                return;
            }

            if (Destination is { IsFlying: true } && condition[ConditionFlag.Swimming])
            {
                logger.LogInformation("Flying but swimming, restarting as non-flying path...");
                Restart(Destination);
                return;
            }
            else if (Destination is { IsFlying: true } && !condition[ConditionFlag.Mounted])
            {
                logger.LogInformation("Flying but not mounted, restarting as non-flying path...");
                Restart(Destination);
                return;
            }

            Vector3 localPlayerPosition = objectTable[0]?.Position ?? Vector3.Zero;
            if (Destination.MovementType == EMovementType.Landing)
            {
                if (!condition[ConditionFlag.InFlight])
                    Stop();
            }
            else if ((localPlayerPosition - Destination.Position).Length() < Destination.StopDistance)
            {
                if (localPlayerPosition.Y - Destination.Position.Y <= Destination.VerticalStopDistance)
                    Stop();
                else if (Destination.DataId != null)
                {
                    IGameObject? gameObject = gameFunctions.FindObjectByDataId(Destination.DataId.Value);
                    if (gameObject is ICharacter or IEventObj)
                    {
                        if (Math.Abs(localPlayerPosition.Y - gameObject.Position.Y) <
                            DefaultVerticalInteractionDistance)
                        {
                            Stop();
                        }
                    }
                    else if (gameObject is { ObjectKind: ObjectKind.Aetheryte })
                    {
                        if (AetheryteConverter.IsLargeAetheryte((EAetheryteLocation)Destination.DataId))
                        {
                            /*
                            if ((EAetheryteLocation) Destination.DataId is EAetheryteLocation.OldSharlayan
                                or EAetheryteLocation.UltimaThuleAbodeOfTheEa)
                                Stop();

                            // TODO verify the first part of this, is there any aetheryte like that?
                            // TODO Unsure if this is per-aetheryte or what; because e.g. old sharlayan is at -1.53;
                            //      but Elpis aetherytes fail at around -0.95
                            if (localPlayerPosition.Y - gameObject.Position.Y < 2.95f &&
                                    localPlayerPosition.Y - gameObject.Position.Y > -0.9f)
                                Stop();
                            */
                            Stop();
                        }
                        else
                        {
                            // aethernet shard
                            if (Math.Abs(localPlayerPosition.Y - gameObject.Position.Y) <
                                DefaultVerticalInteractionDistance)
                            {
                                Stop();
                            }
                        }
                    }
                    else
                        Stop();
                }
                else
                    Stop();
            }
            else
            {
                List<Vector3> navPoints = navmeshIpc.GetWaypoints();
                Vector3? start = objectTable[0]?.Position;
                if (start != null)
                {
                    if (Destination.ShouldRecalculateNavmesh() && RecalculateNavmesh(navPoints, start.Value))
                        return;

                    if (!Destination.IsFlying && !condition[ConditionFlag.Mounted] &&
                            !gameFunctions.HasStatusPreventingSprint() && Destination.CanSprint)
                        TriggerSprintIfNeeded(navPoints, start.Value);
                }
            }
        }
    }

    private void Restart(DestinationData destination)
    {
        Stop();

        if (destination.UseNavmesh)
        {
            NavigateTo(EMovementType.None, destination.DataId, destination.Position, false, false,
                destination.StopDistance, destination.VerticalStopDistance);
        }
        else
        {
            NavigateTo(EMovementType.None, destination.DataId, [destination.Position], false, false,
                destination.StopDistance, destination.VerticalStopDistance);
        }
    }

    private bool IsOnFlightPath(Vector3 p)
    {
        Vector3? pointOnFloor = navmeshIpc.GetPointOnFloor(p, true);
        return pointOnFloor != null && Math.Abs(pointOnFloor.Value.Y - p.Y) > 0.5f;
    }

    [MemberNotNull(nameof(Destination))]
    private void PrepareNavigation(EMovementType type, uint? dataId, Vector3 to, bool fly, bool sprint,
        float? stopDistance, float verticalStopDistance, bool land, bool useNavmesh)
    {
        ResetPathfinding();

        if (InputManager.IsAutoRunning())
        {
            logger.LogInformation("Turning off auto-move");
            chatFunctions.ExecuteCommand("/automove off");
        }

        Destination = new(type, dataId, to, stopDistance ?? (QuestStep.DefaultStopDistance - 0.2f), fly,
            sprint, verticalStopDistance, land, useNavmesh);
        MovementStartedAt = DateTime.MaxValue;
    }

    public void NavigateTo(EMovementType type, uint? dataId, Vector3 to, bool fly, bool sprint,
        float? stopDistance = null, float? verticalStopDistance = null, bool land = false)
    {
        fly |= condition[ConditionFlag.Diving];
        if (fly && land)
            to = to with { Y = to.Y + 2.6f };

        PrepareNavigation(type, dataId, to, fly, sprint, stopDistance, verticalStopDistance ?? DefaultVerticalInteractionDistance, land, true);
        logger.LogInformation("Pathfinding to {Destination}", Destination);

        Destination.NavmeshCalculations++;
        _cancellationTokenSource = new();
        _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));

        Vector3 startPosition = objectTable[0]!.Position;
        if (fly && aetheryteData.CalculateDistance(startPosition, clientState.TerritoryType,
                EAetheryteLocation.CoerthasCentralHighlandsCampDragonhead) < 11f)
        {
            startPosition = startPosition with { Y = startPosition.Y + 1f };
            logger.LogInformation("Using modified start position for flying pathfinding: {StartPosition}",
                startPosition.ToString("G", CultureInfo.InvariantCulture));
        }
        else if (fly)
            // other positions have a (lesser) chance of starting from underground too, in which case pathfinding takes
            // >10 seconds and gets stuck trying to go through the ground.
            // only for flying; as walking uses a different algorithm
            startPosition = startPosition with { Y = startPosition.Y + 0.2f };

        _pathfindTask =
            navmeshIpc.Pathfind(startPosition, to, fly, _cancellationTokenSource.Token);
        //      float range = stopDistance ?? 2.8f;
        //      if (!_navmeshIpc.SimplePathfindAndMoveCloseTo(to, fly, range))
        //      {
        //          _logger.LogWarning("SimpleMove rejected pathfind request (already in progress), stopping first");
        //          _navmeshIpc.Stop();
        //          if (!_navmeshIpc.SimplePathfindAndMoveCloseTo(to, fly, range))
        //          {
        //              _logger.LogWarning("SimpleMove still rejected after stop");
        //          }
        //      }
        //      MovementStartedAt = DateTime.Now;
    }

    public void NavigateTo(EMovementType type, uint? dataId, List<Vector3> to, bool fly, bool sprint,
        float? stopDistance, float? verticalStopDistance = null, bool land = false)
    {
        fly |= condition[ConditionFlag.Diving];
        if (fly && land && to.Count > 0)
            to[^1] = to[^1] with { Y = to[^1].Y + 2.6f };

        PrepareNavigation(type, dataId, to.Last(), fly, sprint, stopDistance, verticalStopDistance ?? DefaultVerticalInteractionDistance, land, false);

        logger.LogInformation("Moving to {Destination}", Destination);
        navmeshIpc.MoveTo(to, fly);
        MovementStartedAt = DateTime.Now;
    }

    public void ResetPathfinding()
    {
        if (_cancellationTokenSource != null)
        {
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _cancellationTokenSource.Dispose();
        }

        _pathfindTask = null;
    }

    private bool RecalculateNavmesh(List<Vector3> navPoints, Vector3 start)
    {
        if (Destination == null)
            throw new InvalidOperationException("Destination is null");

        if (DateTime.Now - MovementStartedAt <= TimeSpan.FromSeconds(5))
            return false;

        Vector3 nextWaypoint = navPoints.FirstOrDefault();
        if (nextWaypoint == default)
            return false;

        float distance = Vector2.Distance(new(start.X, start.Z),
            new(nextWaypoint.X, nextWaypoint.Z));
        if (Destination.LastWaypoint == null ||
            (Destination.LastWaypoint.Position - nextWaypoint).Length() > 0.1f)
        {
            Destination.LastWaypoint = new(nextWaypoint)
            {
                Distance2DAtLastUpdate = distance,
                UpdatedAt = Environment.TickCount64
            };
            return false;
        }
        else if (Environment.TickCount64 - Destination.LastWaypoint.UpdatedAt > 500)
        {
            // check whether we've made any progress of any kind
            if (Math.Abs(distance - Destination.LastWaypoint.Distance2DAtLastUpdate) < 0.5f)
            {
                int calculations = Destination.NavmeshCalculations;
                if (calculations % 6 == 1)
                {
                    logger.LogWarning("Jumping to try and resolve navmesh problem (n = {Calculations})",
                        calculations);
                    unsafe
                    {
                        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
                        Destination.NavmeshCalculations++;
                        Destination.LastWaypoint.UpdatedAt = Environment.TickCount64;
                    }
                }
                else
                {
                    logger.LogWarning("Recalculating navmesh (n = {Calculations})", calculations);
                    Restart(Destination);
                }

                Destination.NavmeshCalculations = calculations + 1;
                return true;
            }
            else
            {
                Destination.LastWaypoint.Distance2DAtLastUpdate = distance;
                Destination.LastWaypoint.UpdatedAt = Environment.TickCount64;
                return false;
            }
        }
        else
            return false;
    }

    private void TriggerSprintIfNeeded(IEnumerable<Vector3> navPoints, Vector3 start)
    {
        float actualDistance = 0;
        foreach (Vector3 end in navPoints)
        {
            actualDistance += (start - end).Length();
            start = end;
        }

        unsafe
        {
            // 70 is ~10 seconds of sprint
            float sprintDistance = 100f;

            // if we're in towns/event areas, jog is a neat fallback (if we're not already jogging,
            // if we're too close then sprinting will barely benefit us)
            if (!gameFunctions.HasStatus(EStatus.Jog) &&
                ((int)GameMain.Instance()->CurrentTerritoryIntendedUseId) is 0 or 7 or 13 or 14 or 15 or 19 or 23 or 29)
            {
                sprintDistance = 30f;
            }

            if (actualDistance > sprintDistance &&
                ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 4) == 0)
            {
                logger.LogInformation("Triggering Sprint");
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 4);
            }
        }
    }

    public void Stop()
    {
        navmeshIpc.Stop();
        ResetPathfinding();
        Destination = null;

        if (InputManager.IsAutoRunning())
        {
            logger.LogInformation("Turning off auto-move [stop]");
            chatFunctions.ExecuteCommand("/automove off");
        }
    }

    public sealed record DestinationData
    (
        EMovementType MovementType,
        uint? DataId,
        Vector3 Position,
        float StopDistance,
        bool IsFlying,
        bool CanSprint,
        float VerticalStopDistance,
        bool Land,
        bool UseNavmesh)
    {
        public int NavmeshCalculations { get; set; }
        public List<Vector3> PartialRoute { get; } = [];
        public LastWaypointData? LastWaypoint { get; set; }

        public bool ShouldRecalculateNavmesh() => NavmeshCalculations < 10;
    }

    public sealed record LastWaypointData(Vector3 Position)
    {
        public long UpdatedAt { get; set; }
        public double Distance2DAtLastUpdate { get; set; }
    }

    public sealed class PathfindingFailedException : Exception
    {
        public PathfindingFailedException()
        {
        }

        public PathfindingFailedException(string message)
            : base(message)
        {
        }

        public PathfindingFailedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
