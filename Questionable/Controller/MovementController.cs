using System.Diagnostics.CodeAnalysis;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc.Exceptions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Questionable.Controller.Steps.Movement;
using Questionable.Model.Common;
using Questionable.Model.Common.Converter;
using Questionable.Model.Questing;
namespace Questionable.Controller;

[RegisterSingleton]
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
    IChatGui chatGui,
    IFramework framework,
    Lazy<ExternalOperationCoordinator> externalOperations,
    Lazy<QuestController> questController,
    ILogger<MovementController> logger) : IDisposable
{
    public const float DefaultVerticalInteractionDistance = 1.95f;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Dispose dispatches Stop to the framework thread. ResetPathfinding detaches the CTS and CancelAndDisposeAsync disposes it after cancellation callbacks finish.")]
    private CancellationTokenSource? _cancellationTokenSource;
    private Task<List<Vector3>>? _pathfindTask;
    private bool _hasSubmittedRoute;
    private long? _pathfindingStartedAt;
    private long? _providerDiagnosticsUpdatedAt;
    private bool? _movementAllowed;
    private bool? _simpleMoveInProgress;
    private bool? _pathfindInProgress;
    private int? _queuedPathfinds;
    private long? _simpleMoveSeenAt;

    // How long a pending SimpleMove may pause a running route before it counts as blocking.
    internal const long SimpleMoveWaitMilliseconds = 15_000;

    internal Func<long> PathfindingClock { get; set; } = static () => Environment.TickCount64;

    public bool IsNavmeshReady
    {
        get
        {
            try
            {
                return navmeshIpc.IsReady();
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
        }
    }

    public bool? GetPathRunning() => navmeshIpc.GetPathRunning();

    public bool IsPathRunning => GetPathRunning() ?? Destination != null;

    // Waiting out a pending SimpleMove counts as pathfinding, so callers don't mistake the pause for a stop.
    public bool IsPathfinding => _pathfindTask is { IsCompleted: false } || _simpleMoveSeenAt != null;
    public DestinationData? Destination { get; set; }
    public NavigationFailureSnapshot? LastFailure { get; private set; }
    public DateTime MovementStartedAt { get; private set; } = DateTime.Now;
    public int BuiltNavmeshPercent => navmeshIpc.GetBuildProgress();
    private DateTime? _landAndRetryTimeout;
    private DestinationData? _previousDestinationData;
    private bool HasExternalMovementOperation => externalOperations.Value.HasMovingOperation;

    public void Dispose() => IpcInvoke.TryOnFrameworkThread(framework, Stop, logger);

    public void Update()
    {
        if (!CanSubmitMovement(Destination != null || _pathfindTask != null || _previousDestinationData != null,
            refreshProviderDiagnostics: false, waitForSimpleMove: true))
            return;

        ThrowIfPathfindingFailed();

        if (Destination != null && GetPathRunning() == null)
        {
            var failure = CreateFailure(PathfindingFailureReason.StatusUnavailable,
                "Unable to determine navmesh movement status");
            ClearLocalMovement();
            throw failure;
        }

        if (_landAndRetryTimeout != null && _previousDestinationData != null)
        {
            if (condition[ConditionFlag.InFlight])
            {
                if (DateTime.Now > _landAndRetryTimeout)
                {
                    var failure = CreateFailure(PathfindingFailureReason.LandingFailed,
                        "Land-and-retry timed out, still InFlight");
                    Stop();
                    throw failure;
                }
                return;
            }

            var retry = _previousDestinationData;
            _previousDestinationData = null;
            _landAndRetryTimeout = null;
            NavigateTo(retry);
            return;
        }
        if (_pathfindTask != null && Destination != null)
        {
            if (_pathfindTask.IsCompletedSuccessfully)
            {
                List<Vector3> pathfindResult = _pathfindTask.Result;
                logger.LogInformation("Pathfinding complete, got {Count} points", pathfindResult.Count);
                if (pathfindResult.Count == 0)
                {
                    if (!Destination.IsFlying && condition[ConditionFlag.InFlight])
                    {
                        chatGui.Print(_L("vnavmesh was not able to find a path. Attempting to land, then trying again."),
                            CommandHandler.MessageTag, CommandHandler.TagColor);
                        _previousDestinationData = Destination;
                        _landAndRetryTimeout = DateTime.Now.AddSeconds(10);
                        ResetPathfinding();
                        LandExecutor.TryLanding();
                        return;
                    }
                    var failure = CreateFailure(PathfindingFailureReason.NoRoute,
                        "Pathfinding complete, got 0 points");
                    Stop();
                    throw failure;
                }

                List<Vector3> navPoints = pathfindResult.Skip(1).ToList();

                if (navPoints.Count > 0 && !Destination.IsFlying)
                {
                    (navPoints, bool recalculateNavmesh) = movementOverrideController.AdjustPath(navPoints);
                    if (navPoints.Count > 0 && recalculateNavmesh && Destination.ShouldRecalculateNavmesh())
                    {
                        Destination.NavmeshCalculations++;
                        Destination.PartialRoute.AddRange(navPoints);
                        logger.LogInformation("Running navmesh recalculation with fudged point ({From} to {To})",
                            navPoints[^1], Destination.Position);

                        StartPathfinding(navPoints[^1], Destination.Position, Destination.IsFlying);
                        return;
                    }
                }

                navPoints = Destination.PartialRoute.Concat(navPoints).ToList();
                if (navPoints.Count == 0)
                {
                    Vector3? playerPosition = objectTable[0]?.Position;
                    bool arrived = playerPosition is Vector3 position &&
                        Vector3.Distance(position, Destination.Position) < Destination.StopDistance &&
                        position.Y - Destination.Position.Y <= Destination.VerticalStopDistance;
                    if (!arrived)
                    {
                        var failure = CreateFailure(PathfindingFailureReason.NoRoute,
                            "Pathfinding returned no movement waypoints before reaching the destination");
                        ClearLocalMovement();
                        throw failure;
                    }
                    ClearLocalMovement();
                    MovementStartedAt = DateTime.Now;
                    return;
                }
                logger.LogInformation("Navigating via route (XZ:{Distance}) [{Route}]",
                    navPoints[0].DistanceTo_XZ(navPoints[^1]),
                    string.Join(" → ", pathfindResult.Select(x => x.ToString("G5", CultureInfo.InvariantCulture))));

                ThrowIfPathfindingFailed();
                if (!SubmitMovement(navPoints, Destination.IsFlying))
                    return;
            }
        }

        if (questController.Value.IsQuestingStopped)
        {
            return;
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

            if (Destination is { IsFlying: true } && !condition[ConditionFlag.Mounted])
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
                            Stop();
                        }
                        else
                        {
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
        if (!CanSubmitMovement())
            return;

        Stop();

        NavigationOptions options = new()
        {
            StopDistance = destination.StopDistance,
            VerticalStopDistance = destination.VerticalStopDistance,
        };
        if (destination.UseNavmesh)
            NavigateTo(EMovementType.None, destination.DataId, destination.Position, options);
        else
            NavigateTo(EMovementType.None, destination.DataId, [destination.Position], options);
    }

    [MemberNotNull(nameof(Destination))]
    private void PrepareNavigation(EMovementType type, uint? dataId, Vector3 to, NavigationOptions options, bool useNavmesh)
    {
        ResetPathfinding();

        if (InputManager.IsAutoRunning())
        {
            logger.LogInformation("Turning off auto-move");
            chatFunctions.ExecuteCommand("/automove off");
        }

        Destination = new(type, dataId, to,
            options.StopDistance ?? (QuestStep.DefaultStopDistance - 0.2f),
            options.Fly, options.Sprint,
            options.VerticalStopDistance ?? DefaultVerticalInteractionDistance,
            options.Land, useNavmesh);
        MovementStartedAt = DateTime.MaxValue;
    }

    public void NavigateTo(DestinationData destination) =>
        NavigateTo(destination.MovementType, destination.DataId, destination.Position, NavigationOptions.FromDestinationData(destination));

    public void NavigateTo(EMovementType type, uint? dataId, Vector3 to, NavigationOptions options)
    {
        if (!CanSubmitMovement(isNewRequest: true))
            return;

        bool fly = options.Fly || condition[ConditionFlag.Diving];
        if (fly && options.Land)
            to = to with { Y = to.Y + 2.6f };

        NavigationOptions effective = options with { Fly = fly };
        PrepareNavigation(type, dataId, to, effective, useNavmesh: true);
        logger.LogInformation("Pathfinding to {Destination}", Destination);

        Destination.NavmeshCalculations++;

        Vector3? playerPosition = objectTable[0]?.Position;
        if (playerPosition == null)
        {
            logger.LogWarning("Cannot pathfind: local player object not available");
            ResetPathfinding();
            return;
        }

        Vector3 startPosition = playerPosition.Value;
        if (fly && aetheryteData.CalculateDistance(startPosition, clientState.TerritoryType,
            EAetheryteLocation.CoerthasCentralHighlandsCampDragonhead) < 11f)
        {
            startPosition = startPosition with { Y = startPosition.Y + 1f };
            logger.LogInformation("Using modified start position for flying pathfinding: {StartPosition}",
                startPosition.ToString("G5", CultureInfo.InvariantCulture));
        }
        else if (fly)
            // other positions have a (lesser) chance of starting from underground too, in which case pathfinding takes
            // >10 seconds and gets stuck trying to go through the ground.
            // only for flying; as walking uses a different algorithm
            startPosition = startPosition with { Y = startPosition.Y + 0.2f };

        StartPathfinding(startPosition, to, fly);
    }

    internal void StartPathfinding(Vector3 from, Vector3 to, bool fly)
    {
        if (!CanSubmitMovement())
            return;

        ResetPathfinding();
        _pathfindingStartedAt = PathfindingClock();
        _cancellationTokenSource = new();
        _pathfindTask = navmeshIpc.Pathfind(from, to, fly, _cancellationTokenSource.Token);
    }

    private void ThrowIfPathfindingFailed()
    {
        Task<List<Vector3>>? task = _pathfindTask;
        if (task == null)
            return;

        Exception? failure = null;
        if (task.IsCompleted && !task.IsCompletedSuccessfully)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                // Awaiting preserves the provider's original exception instead of its task wrapper.
                failure = e;
            }
        }

        // Check even completed tasks: a provider may ignore cancellation and return a late route.
        if (_pathfindingStartedAt is long startedAt && PathfindingClock() - startedAt >= 30_000)
        {
            var exception = CreateFailure(PathfindingFailureReason.TimedOut,
                "Pathfinding timed out after 30 seconds", failure);
            ClearLocalMovement();
            throw exception;
        }

        if (failure != null)
        {
            PathfindingFailureReason reason = failure switch
            {
                IpcNotReadyError => PathfindingFailureReason.Unavailable,
                OperationCanceledException => PathfindingFailureReason.Cancelled,
                _ => PathfindingFailureReason.ProviderFault,
            };
            var exception = CreateFailure(reason, "Unable to complete pathfinding task", failure);
            ClearLocalMovement();
            throw exception;
        }
    }

    public void NavigateTo(EMovementType type, uint? dataId, List<Vector3> to, NavigationOptions options)
    {
        if (!CanSubmitMovement(isNewRequest: true))
            return;

        if (to.Count == 0)
        {
            var failure = CreateFailure(PathfindingFailureReason.NoRoute,
                "Cannot navigate without movement waypoints", isNewRequest: true);
            ClearLocalMovement();
            throw failure;
        }

        bool fly = options.Fly || condition[ConditionFlag.Diving];
        if (fly && options.Land && to.Count > 0)
            to[^1] = to[^1] with { Y = to[^1].Y + 2.6f };

        NavigationOptions effective = options with { Fly = fly };
        PrepareNavigation(type, dataId, to[^1], effective, useNavmesh: false);

        logger.LogInformation("Moving to {Destination}", Destination);
        SubmitMovement(to, fly);
    }

    private void RefreshProviderDiagnostics(bool force)
    {
        long now = PathfindingClock();
        if (!force && _providerDiagnosticsUpdatedAt is long updatedAt && now - updatedAt < 250)
            return;

        _movementAllowed = navmeshIpc.GetMovementAllowed();
        _simpleMoveInProgress = navmeshIpc.GetSimpleMoveInProgress();
        _pathfindInProgress = navmeshIpc.GetPathfindInProgress();
        _queuedPathfinds = navmeshIpc.GetQueuedPathfinds();
        _providerDiagnosticsUpdatedAt = now;
    }

    private bool CanSubmitMovement(bool hasRequest = true, bool isNewRequest = false,
        bool refreshProviderDiagnostics = true, bool waitForSimpleMove = false)
    {
        if (HasExternalMovementOperation)
        {
            ClearLocalMovement();
            return false;
        }

        if (!hasRequest)
            return true;

        bool providerHasWork = HasExternalMovementProviderWork();
        // A provider query may have reconciled a retained operation in the meantime.
        if (HasExternalMovementOperation)
        {
            ClearLocalMovement();
            return false;
        }

        // Updates use a short-lived cache; missing optional IPC stays unknown and does not disable navigation.
        RefreshProviderDiagnostics(refreshProviderDiagnostics);
        if (_movementAllowed == false)
        {
            var disabled = CreateFailure(PathfindingFailureReason.MovementDisabled,
                "Movement is disabled in vnavmesh", isNewRequest: isNewRequest);
            // Disabling movement only pauses vnav's route; clear it so re-enabling can't resume it after cleanup.
            if (_hasSubmittedRoute && !providerHasWork && _simpleMoveInProgress != true)
                navmeshIpc.Stop();
            ClearLocalMovement();
            throw disabled;
        }

        if (!providerHasWork && _simpleMoveInProgress != true)
        {
            _simpleMoveSeenAt = null;
            return true;
        }

        // vnav re-paths a stuck route through SimpleMove by itself; give our running route time for that to finish.
        if (waitForSimpleMove && !providerHasWork && _hasSubmittedRoute)
        {
            long now = PathfindingClock();
            _simpleMoveSeenAt ??= now;
            if (now - _simpleMoveSeenAt.Value < SimpleMoveWaitMilliseconds)
                return false;
        }

        var failure = CreateFailure(PathfindingFailureReason.MovementBlocked,
            _simpleMoveInProgress == true
                ? "Navigation blocked by pending vnavmesh SimpleMove work"
                : "Navigation blocked by an external movement provider", isNewRequest: isNewRequest);
        ClearLocalMovement();
        throw failure;
    }

    internal bool SubmitMovement(List<Vector3> points, bool fly)
    {
        if (!CanSubmitMovement())
            return false;

        try
        {
            navmeshIpc.MoveTo(points, fly);
        }
        catch (Exception e)
        {
            var failure = CreateFailure(PathfindingFailureReason.StartFailed,
                "Unable to start navmesh movement", e);
            ClearLocalMovement();
            throw failure;
        }

        _hasSubmittedRoute = true;
        MovementStartedAt = DateTime.Now;
        ResetPathfinding();
        return true;
    }

    public void RecordFailure(PathfindingFailedException exception, string? quest = null, string? task = null)
    {
        NavigationFailureSnapshot snapshot = exception.Snapshot ?? CaptureFailure(exception, Destination);
        snapshot = snapshot with { Quest = quest ?? snapshot.Quest, Task = task ?? snapshot.Task };
        exception.Snapshot = snapshot;
        LastFailure = snapshot;
    }

    internal void RecordFailureWithDestination(PathfindingFailedException exception, DestinationData fallbackDestination)
    {
        exception.Snapshot ??= CaptureFailure(exception, Destination ?? fallbackDestination);
        RecordFailure(exception);
    }

    private PathfindingFailedException CreateFailure(PathfindingFailureReason reason, string message,
        Exception? innerException = null, bool isNewRequest = false)
    {
        var exception = new PathfindingFailedException(reason, message, innerException);
        // A rejected initial submission has not replaced the previous request yet.
        exception.Snapshot = CaptureFailure(exception, isNewRequest ? null : Destination ?? _previousDestinationData,
            includeElapsed: !isNewRequest);
        RecordFailure(exception);
        return exception;
    }

    private NavigationFailureSnapshot CaptureFailure(PathfindingFailedException exception,
        DestinationData? destination, bool includeElapsed = true) => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        Reason = exception.Reason.ToString(),
        Message = exception.Message,
        ExceptionType = exception.GetType().FullName,
        InnerExceptionType = exception.InnerException?.GetType().FullName,
        InnerExceptionMessage = ReadFailureContext(() => exception.InnerException?.Message, null),
        TerritoryId = ReadFailureContext(() => (uint)clientState.TerritoryType, 0u),
        PlayerPosition = ReadFailureContext(() => objectTable[0]?.Position.ToString("G9", CultureInfo.InvariantCulture), null),
        Destination = destination?.Position.ToString("G9", CultureInfo.InvariantCulture),
        DataId = destination?.DataId,
        Fly = destination?.IsFlying,
        StopDistance = destination is { StopDistance: var distance } && float.IsFinite(distance) ? distance : null,
        ElapsedMilliseconds = includeElapsed && _pathfindingStartedAt is long startedAt
            ? ReadFailureContext<double?>(() => PathfindingClock() - startedAt, null)
            : null,
        // Reports use the cached vnav status and never query it afresh.
        NavmeshVersion = ReadFailureContext(() => navmeshIpc.GetVersion()?.ToString(), null),
        MovementAllowed = _movementAllowed,
        SimpleMoveInProgress = _simpleMoveInProgress,
        PathfindInProgress = _pathfindInProgress,
        QueuedPathfinds = _queuedPathfinds,
        FloorPoint = destination == null ? null : ReadFailureContext(() =>
            navmeshIpc.GetPointOnFloor(destination.Position, unlandable: false, range: 0.2f)
                ?.ToString("G9", CultureInfo.InvariantCulture), null),
        NearestReachablePoint = destination == null ? null : ReadFailureContext(() =>
            navmeshIpc.GetNearestPointReachable(destination.Position, halfExtentXZ: 0.2f, halfExtentY: 2f)
                ?.ToString("G9", CultureInfo.InvariantCulture), null),
    };

    private static T ReadFailureContext<T>(Func<T> read, T unknown)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return unknown;
        }
    }

    public void ResetPathfinding()
    {
        CancellationTokenSource? cancellationTokenSource = _cancellationTokenSource;
        Task<List<Vector3>>? task = _pathfindTask;
        _cancellationTokenSource = null;
        _pathfindTask = null;
        _pathfindingStartedAt = null;

        // Observe late faults from detached work without touching controller state.
        if (task != null)
            _ = task.ContinueWith(static completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        if (cancellationTokenSource != null)
            _ = CancelAndDisposeAsync(cancellationTokenSource);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            // Cancel asynchronously so provider callbacks can't block the framework thread.
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        }
        catch (AggregateException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
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
            (Destination.LastWaypoint.Position - nextWaypoint).Length() > 0.1f ||
            gameFunctions.IsOccupied())
        {
            Destination.LastWaypoint = new(nextWaypoint)
            {
                Distance2DAtLastUpdate = distance,
                UpdatedAt = Environment.TickCount64
            };
            return false;
        }

        if (Environment.TickCount64 - Destination.LastWaypoint.UpdatedAt > 500)
        {
            if (Math.Abs(distance - Destination.LastWaypoint.Distance2DAtLastUpdate) < 0.5f && !condition[ConditionFlag.WatchingCutscene])
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

                if (Destination != null)
                    Destination.NavmeshCalculations = calculations + 1;
                return true;
            }

            Destination.LastWaypoint.Distance2DAtLastUpdate = distance;
            Destination.LastWaypoint.UpdatedAt = Environment.TickCount64;
            return false;
        }

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

    private bool HasExternalMovementProviderWork() => externalOperations.Value.MayBeMovingCharacter();

    public void Stop()
    {
        bool providerHasWork = HasExternalMovementOperation || HasExternalMovementProviderWork() ||
            HasExternalMovementOperation || navmeshIpc.GetSimpleMoveInProgress() == true;
        ClearLocalMovement();
        // Leave provider-owned travel alone, including a pending SimpleMove that can't be cancelled safely.
        if (providerHasWork || HasExternalMovementOperation)
            return;

        navmeshIpc.Stop();

        if (InputManager.IsAutoRunning())
        {
            logger.LogInformation("Turning off auto-move [stop]");
            chatFunctions.ExecuteCommand("/automove off");
        }
    }

    private void ClearLocalMovement()
    {
        _hasSubmittedRoute = false;
        Destination = null;
        _previousDestinationData = null;
        _landAndRetryTimeout = null;
        _simpleMoveSeenAt = null;
        ResetPathfinding();
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

    public sealed record NavigationOptions
    {
        public bool Fly { get; init; }
        public bool Sprint { get; init; }
        public float? StopDistance { get; init; }
        public float? VerticalStopDistance { get; init; }
        public bool Land { get; init; }
        public static NavigationOptions FromDestinationData(DestinationData destData) =>
            new()
            {
                Fly = destData.IsFlying,
                Sprint = destData.CanSprint,
                StopDistance = destData.StopDistance,
                VerticalStopDistance = destData.VerticalStopDistance,
                Land = destData.Land
            };
    }

    public enum PathfindingFailureReason
    {
        Unknown,
        Unavailable,
        TimedOut,
        Cancelled,
        ProviderFault,
        NoRoute,
        LandingFailed,
        StatusUnavailable,
        StartFailed,
        NotArrived,
        MovementBlocked,
        MovementDisabled,
    }

    public sealed class PathfindingFailedException : Exception
    {
        public PathfindingFailureReason Reason { get; }
        internal NavigationFailureSnapshot? Snapshot { get; set; }

        public PathfindingFailedException(PathfindingFailureReason reason, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Reason = reason;
        }

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
