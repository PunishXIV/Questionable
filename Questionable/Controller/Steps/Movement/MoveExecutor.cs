using Dalamud.Game.Text.SeStringHandling;
using Lumina.Excel.Sheets;
using Action = System.Action;

namespace Questionable.Controller.Steps.Movement;

internal sealed class MoveExecutor
(
    MovementController movementController,
    ILogger<MoveExecutor> logger,
    IClientState clientState,
    IObjectTable objectTable,
    ICondition condition,
    IDataManager dataManager,
    MountStep.MountEvaluator mountEvaluator,
    IServiceProvider serviceProvider) : TaskExecutor<MoveTask>, IToastAware, ITaskExecutor.IStoppable
{
    public void StopNow()
    {
        if (_movementController.Destination != null)
            _movementController.Stop();
    }

    private readonly string _cannotExecuteAtThisTime = dataManager.GetString<LogMessage>(579, x => x.Text)!;
    private readonly IClientState _clientState = clientState;
    private readonly ICondition _condition = condition;
    private readonly ILogger<MoveExecutor> _logger = logger;
    private readonly MountStep.MountEvaluator _mountEvaluator = mountEvaluator;
    private readonly MovementController _movementController = movementController;
    private readonly IObjectTable _objectTable = objectTable;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private bool _canRestart;
    private Vector3 _destination;
    private bool _navmeshReloaded;
    private DateTime? _navmeshWaitStartedAt;
    private long? _simpleMoveWaitStartedAt;
    private DateTime _nextMountCheckAt;

    // First builds of a large zone can take a while; WaitNavmesh itself has no limit.
    private static readonly TimeSpan NavmeshReloadTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MountRecheckInterval = TimeSpan.FromSeconds(0.5);

    private (MountStep.MountExecutor Executor, MountStep.MountTask Task)? _mountBeforeMovement;
    private (MountStep.MountExecutor Executor, MountStep.MountTask Task)? _mountDuringMovement;

    private Action? _startAction;
    private (MountStep.UnmountExecutor Executor, MountStep.UnmountTask Task)? _unmountBeforeMovement;

    public override ETaskResult Update()
    {
        try
        {
            return UpdateMovement();
        }
        catch (MovementController.PathfindingException e)
        {
            RecordFailure(e);
            throw;
        }
    }

    private void RecordFailure(MovementController.PathfindingException exception) =>
        _movementController.RecordFailureWithDestination(exception, new(
            EMovementType.Quest, Task.DataId, Task.Destination,
            Task.StopDistance ?? QuestStep.DefaultStopDistance, Task.Fly, Task.Sprint ?? false,
            MovementController.DefaultVerticalDistance, Task.Land, !Task.DisableNavmesh));

    private ETaskResult UpdateMovement()
    {
        if (UpdateMountState() is { } mountStateResult)
            return mountStateResult;

        if (_startAction == null)
            return ETaskResult.TaskComplete;

        // vnav drops its current route whenever its navmesh changes, even for routes that don't use the mesh.
        bool navmeshReady = _movementController.IsNavmeshReady;
        if (!navmeshReady)
            _navmeshReloaded = true;

        if (_movementController.IsPathfinding)
            return ETaskResult.StillRunning;

        bool? running = _movementController.GetPathRunning();
        if (running == null)
            throw new MovementController.PathfindingException(
                MovementController.PathfindingFailureReason.StatusUnavailable,
                "Unable to verify whether navigation is still running; movement will not be retried");
        if (running == true)
            return ETaskResult.StillRunning;

        DateTime movementStartedAt = _movementController.StartedAt;
        if (movementStartedAt == DateTime.MaxValue || movementStartedAt.AddSeconds(2) >= DateTime.Now)
            return ETaskResult.StillRunning;

        if (_objectTable[0] is not { } player)
            return ETaskResult.StillRunning;

        if (!navmeshReady && _clientState.TerritoryType == Task.TerritoryId)
        {
            _navmeshWaitStartedAt ??= DateTime.Now;
            if (DateTime.Now - _navmeshWaitStartedAt < NavmeshReloadTimeout)
                return ETaskResult.StillRunning;

            throw new MovementController.PathfindingException(
                MovementController.PathfindingFailureReason.NotArrived,
                "Movement stopped and vnavmesh did not finish loading its navmesh");
        }

        _navmeshWaitStartedAt = null;
        if (ShouldWaitForSimpleMove(_movementController.IsSimpleMoveInProgress(), Environment.TickCount64,
                ref _simpleMoveWaitStartedAt))
            return ETaskResult.StillRunning;

        bool navmeshReloaded = _navmeshReloaded;
        _navmeshReloaded = false;
        if (ShouldRestartAfterStop(Task, _clientState.TerritoryType, player.Position, ref _canRestart, navmeshReloaded))
        {
            _logger.LogInformation(navmeshReloaded
                ? "Movement was interrupted by a navmesh reload, moving again"
                : "Looks like movement was interrupted, re-attempting to move");
            _startAction();
            return ETaskResult.StillRunning;
        }

        return ETaskResult.TaskComplete;
    }

    // The controller's status cache can miss vnav starting to re-path a stuck route; a retry would then be refused.
    internal static bool ShouldWaitForSimpleMove(bool simpleMoveInProgress, long now, ref long? waitStartedAt)
    {
        if (!simpleMoveInProgress)
        {
            waitStartedAt = null;
            return false;
        }

        waitStartedAt ??= now;
        return now - waitStartedAt.Value < MovementController.SimpleMoveWaitMilliseconds;
    }

    internal static bool ShouldRestartAfterStop(MoveTask task, uint territoryId, Vector3 position, ref bool canRestart,
        bool navmeshReloaded = false)
    {
        // Territory transitions can complete movement before the original endpoint is reached.
        if (territoryId != task.TerritoryId ||
            Vector3.Distance(position, task.Destination) <= (task.StopDistance ?? QuestStep.DefaultStopDistance) + 5f)
            return false;

        // A navmesh reload cleared the route; that doesn't use up the one retry for a real interruption.
        if (navmeshReloaded)
            return true;

        if (!canRestart)
            throw new MovementController.PathfindingException(
                MovementController.PathfindingFailureReason.NotArrived,
                "Movement stopped before reaching the destination");

        canRestart = false;
        return true;
    }

    public override bool WasInterrupted()
    {
        DateTime retryAt = DateTime.Now;
        if (Task.Fly && _condition[ConditionFlag.InCombat] && !_condition[ConditionFlag.Mounted] &&
            _mountBeforeMovement is { Task: { } mountTask } &&
            _mountEvaluator.Evaluate(mountTask, dryRun: true, ref retryAt) == MountStep.MountResult.WhenOutOfCombat)
            return true;

        return base.WasInterrupted();
    }

    public override bool ShouldInterruptOnDamage()
    {
        return _mountBeforeMovement != null || ShouldClearCombatFirst();
    }

    public bool OnErrorToast(SeString message)
    {
        if (GameFunctions.GameStringEquals(_cannotExecuteAtThisTime, message.TextValue))
            return true;

        return false;
    }

    private void PrepareMovementIfNeeded()
    {
        if (!GameFunctions.IsFlyingUnlocked(Task.TerritoryId))
            Task = Task with { Fly = false, Land = false };

        MovementController.NavigationOptions options = new()
        {
            Fly = Task.Fly,
            Sprint = Task.Sprint ?? _mountDuringMovement == null,
            StopDistance = Task.StopDistance,
            VerticalStopDistance = Task.IgnoreDistanceToObject ? float.MaxValue : null,
            Land = Task.Land,
        };
        if (!Task.DisableNavmesh)
            _startAction = () => _movementController.NavigateTo(EMovementType.Quest, Task.DataId, _destination, options);
        else
            _startAction = () => _movementController.NavigateTo(EMovementType.Quest, Task.DataId, [_destination], options);
    }

    protected override bool Start()
    {
        try
        {
            return StartMovement();
        }
        catch (MovementController.PathfindingException e)
        {
            RecordFailure(e);
            throw;
        }
    }

    private bool StartMovement()
    {
        _canRestart = Task.RestartNavigation;
        _destination = Task.Destination;


        float stopDistance = Task.StopDistance ?? QuestStep.DefaultStopDistance;
        Vector3? position = _objectTable[0]?.Position;
        float actualDistance = position == null ? float.MaxValue : Vector3.Distance(position.Value, _destination);
        bool requiresMovement = actualDistance > stopDistance;
        if (requiresMovement)
            PrepareMovementIfNeeded();

        if (Task.Mount == true)
        {
            MountStep.MountTask mountTask = new(Task.TerritoryId, MountStep.EMountIf.Always);
            _mountBeforeMovement = (_serviceProvider.GetRequiredService<MountStep.MountExecutor>(), mountTask);
            if (!_mountBeforeMovement.Value.Executor.Start(mountTask))
                _mountBeforeMovement = null;
        }
        else if (Task.Mount == false)
        {
            MountStep.UnmountTask unmountTask = new();
            _unmountBeforeMovement = (_serviceProvider.GetRequiredService<MountStep.UnmountExecutor>(), unmountTask);
            if (!_unmountBeforeMovement.Value.Executor.Start(unmountTask))
                _unmountBeforeMovement = null;
        }
        else
        {
            if (!Task.DisableNavmesh)
            {
                MountStep.EMountIf mountIf =
                    actualDistance > stopDistance && Task.Fly &&
                    GameFunctions.IsFlyingUnlocked(Task.TerritoryId)
                        ? MountStep.EMountIf.Always
                        : MountStep.EMountIf.AwayFromPosition;
                MountStep.MountTask mountTask = new(Task.TerritoryId, mountIf, _destination);
                DateTime retryAt = DateTime.Now;
                (MountStep.MountExecutor Executor, MountStep.MountTask)? mount;

                if (_mountEvaluator.Evaluate(mountTask, dryRun: true, ref retryAt) != MountStep.MountResult.DontMount)
                {
                    mount = (_serviceProvider.GetRequiredService<MountStep.MountExecutor>(), mountTask);
                    mount.Value.Executor.Start(mountTask);
                }
                else
                    mount = null;

                if (Task.Fly)
                    _mountBeforeMovement = mount;
                else
                    _mountDuringMovement = mount;
            }
        }

        if (_mountBeforeMovement == null &&
            _unmountBeforeMovement == null &&
            _startAction != null)
            _startAction();
        return true;
    }

    private ETaskResult? UpdateMountState()
    {
        if (_mountBeforeMovement is { Executor: { } mountBeforeMoveExecutor })
        {
            if (mountBeforeMoveExecutor.Update() == ETaskResult.TaskComplete)
            {
                _logger.LogInformation("MountBeforeMovement complete");
                _mountBeforeMovement = null;
                _startAction?.Invoke();
                return null;
            }

            return ETaskResult.StillRunning;
        }

        if (_unmountBeforeMovement is { Executor: { } unmountBeforeMoveExecutor })
        {
            if (unmountBeforeMoveExecutor.Update() == ETaskResult.TaskComplete)
            {
                _logger.LogInformation("UnmountBeforeMovement complete");
                _unmountBeforeMovement = null;
                _startAction?.Invoke();
                return null;
            }

            return ETaskResult.StillRunning;
        }

        if (_mountDuringMovement is { Executor: { } mountDuringMoveExecutor, Task: { } mountTask })
        {
            if (mountDuringMoveExecutor.Update() == ETaskResult.TaskComplete)
            {
                _logger.LogInformation("MountDuringMovement complete (mounted)");
                _mountDuringMovement = null;
                return null;
            }

            DateTime retryAt = DateTime.Now;
            if (ShouldRecheckMount(DateTime.Now, ref _nextMountCheckAt) &&
                _mountEvaluator.Evaluate(mountTask, dryRun: true, ref retryAt) == MountStep.MountResult.DontMount)
            {
                _logger.LogInformation("MountDuringMovement implicitly complete (shouldn't mount anymore)");
                _mountDuringMovement = null;
                return null;
            }

            return null; // still keep moving
        }

        return null;
    }

    private bool ShouldClearCombatFirst() => Task.InteractionType is EInteractionType.Jump;

    // The evaluator logs on every call; in combat it keeps answering 'WhenOutOfCombat' for the whole walk.
    internal static bool ShouldRecheckMount(DateTime now, ref DateTime nextCheckAt)
    {
        if (now < nextCheckAt)
            return false;

        nextCheckAt = now + MountRecheckInterval;
        return true;
    }
}
