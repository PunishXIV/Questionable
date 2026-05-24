using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Action = System.Action;
using Mount = Questionable.Controller.Steps.Common.Mount;

namespace Questionable.Controller.Steps.Movement;

internal sealed class MoveExecutor
(
    MovementController movementController,
    GameFunctions gameFunctions,
    ILogger<MoveExecutor> logger,
    IClientState clientState,
    IObjectTable objectTable,
    ICondition condition,
    IDataManager dataManager,
    Mount.MountEvaluator mountEvaluator,
    IServiceProvider serviceProvider) : TaskExecutor<MoveTask>, IToastAware
{
    private readonly string _cannotExecuteAtThisTime = DataManagerAdapter.GetString<LogMessage>(dataManager, 579, x => x.Text)!;
    private readonly IClientState _clientState = clientState;
    private readonly ICondition _condition = condition;
    private readonly GameFunctions _gameFunctions = gameFunctions;
    private readonly ILogger<MoveExecutor> _logger = logger;
    private readonly Mount.MountEvaluator _mountEvaluator = mountEvaluator;
    private readonly MovementController _movementController = movementController;
    private readonly IObjectTable _objectTable = objectTable;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private bool _canRestart;
    private Vector3 _destination;

    private (Mount.MountExecutor Executor, Mount.MountTask Task)? _mountBeforeMovement;
    private (Mount.MountExecutor Executor, Mount.MountTask Task)? _mountDuringMovement;

    private Action? _startAction;
    private (Mount.UnmountExecutor Executor, Mount.UnmountTask Task)? _unmountBeforeMovement;

    public override ETaskResult Update()
    {
        if (UpdateMountState() is { } mountStateResult)
            return mountStateResult;

        if (_startAction == null)
            return ETaskResult.TaskComplete;

        if (_movementController.IsPathfinding || _movementController.IsPathRunning)
            return ETaskResult.StillRunning;

        DateTime movementStartedAt = _movementController.MovementStartedAt;
        if (movementStartedAt == DateTime.MaxValue || movementStartedAt.AddSeconds(2) >= DateTime.Now)
            return ETaskResult.StillRunning;

        if (_canRestart &&
            Vector3.Distance(_objectTable[0]!.Position, _destination) >
            (Task.StopDistance ?? QuestStep.DefaultStopDistance) + 5f)
        {
            _canRestart = false;
            if (_clientState.TerritoryType == Task.TerritoryId)
            {
                _logger.LogInformation("Looks like movement was interrupted, re-attempting to move");
                _startAction();
                return ETaskResult.StillRunning;
            }
            else
            {
                _logger.LogInformation(
                    "Looks like movement was interrupted, do nothing since we're in a different territory now");
            }
        }

        return ETaskResult.TaskComplete;
    }

    public override bool WasInterrupted()
    {
        DateTime retryAt = DateTime.Now;
        if (Task.Fly && _condition[ConditionFlag.InCombat] && !_condition[ConditionFlag.Mounted] &&
            _mountBeforeMovement is { Task: { } mountTask } &&
            _mountEvaluator.EvaluateMountState(mountTask, true, ref retryAt) == Mount.MountResult.WhenOutOfCombat)
            return true;

        return base.WasInterrupted();
    }

    public override bool ShouldInterruptOnDamage()
    {
        // (a) waiting for a mount to complete, or
        // (b) want combat to be done before any other interaction?
        return _mountBeforeMovement != null || ShouldResolveCombatBeforeNextInteraction();
    }

    public bool OnErrorToast(SeString message)
    {
        if (GameFunctions.GameStringEquals(_cannotExecuteAtThisTime, message.TextValue))
            return true;

        return false;
    }

    private void PrepareMovementIfNeeded()
    {
        if (!_gameFunctions.IsFlyingUnlocked(Task.TerritoryId))
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
            Mount.MountTask mountTask = new(Task.TerritoryId, Mount.EMountIf.Always);
            _mountBeforeMovement = (_serviceProvider.GetRequiredService<Mount.MountExecutor>(), mountTask);
            if (!_mountBeforeMovement.Value.Executor.Start(mountTask))
                _mountBeforeMovement = null;
        }
        else if (Task.Mount == false)
        {
            Mount.UnmountTask unmountTask = new();
            _unmountBeforeMovement = (_serviceProvider.GetRequiredService<Mount.UnmountExecutor>(), unmountTask);
            if (!_unmountBeforeMovement.Value.Executor.Start(unmountTask))
                _unmountBeforeMovement = null;
        }
        else
        {
            if (!Task.DisableNavmesh)
            {
                Mount.EMountIf mountIf =
                    actualDistance > stopDistance && Task.Fly &&
                    _gameFunctions.IsFlyingUnlocked(Task.TerritoryId)
                        ? Mount.EMountIf.Always
                        : Mount.EMountIf.AwayFromPosition;
                Mount.MountTask mountTask = new(Task.TerritoryId, mountIf, _destination);
                DateTime retryAt = DateTime.Now;
                (Mount.MountExecutor Executor, Mount.MountTask)? move;

                if (_mountEvaluator.EvaluateMountState(mountTask, true, ref retryAt) != Mount.MountResult.DontMount)
                {
                    move = (_serviceProvider.GetRequiredService<Mount.MountExecutor>(), mountTask);
                    move.Value.Executor.Start(mountTask);
                }
                else
                    move = null;

                if (Task.Fly)
                    _mountBeforeMovement = move;
                else
                    _mountDuringMovement = move;
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
        else if (_unmountBeforeMovement is { Executor: { } unmountBeforeMoveExecutor })
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
        else if (_mountDuringMovement is { Executor: { } mountDuringMoveExecutor, Task: { } mountTask })
        {
            if (mountDuringMoveExecutor.Update() == ETaskResult.TaskComplete)
            {
                _logger.LogInformation("MountDuringMovement complete (mounted)");
                _mountDuringMovement = null;
                return null;
            }

            DateTime retryAt = DateTime.Now;
            if (_mountEvaluator.EvaluateMountState(mountTask, true, ref retryAt) == Mount.MountResult.DontMount)
            {
                _logger.LogInformation("MountDuringMovement implicitly complete (shouldn't mount anymore)");
                _mountDuringMovement = null;
                return null;
            }

            return null; // still keep moving
        }
        else
            return null;
    }

    private bool ShouldResolveCombatBeforeNextInteraction() => Task.InteractionType is EInteractionType.Jump;
}
