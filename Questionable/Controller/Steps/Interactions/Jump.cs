using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Interactions;

internal static class Jump
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Jump)
                return null;

            ArgumentNullException.ThrowIfNull(step.JumpDestination);

            if (step.JumpDestination.Type == EJumpType.SingleJump)
                return new SingleTask(step.DataId, step.JumpDestination, step.Comment);

            return new RepeatedTask(step.DataId, step.JumpDestination, step.Comment);
        }
    }

    internal interface IJumpTask : ITask
    {
        uint? DataId { get; }
        JumpDestination Destination { get; }
        string? Comment { get; }
    }

    internal sealed record SingleTask
    (
        uint? DataId,
        JumpDestination Destination,
        string? Comment) : IJumpTask
    {
        public override string ToString() => $"Jump({Comment})";
    }

    internal abstract class ExecutorBase<T>
    (
        MovementController movementController,
        IObjectTable objectTable,
        IFramework framework) : TaskExecutor<T>
    where T : class, IJumpTask
    {
        protected override bool Start()
        {
            float stopDistance = Task.Destination.CalculateStopDistance();
            if ((objectTable[0]!.Position - Task.Destination.Position).Length() <= stopDistance)
                return false;

            movementController.NavigateTo(EMovementType.Quest, Task.DataId, [Task.Destination.Position], new()
            {
                StopDistance = Task.Destination.StopDistance ?? stopDistance,
            });
            framework.RunOnTick(() =>
                {
                    unsafe
                    {
                        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
                    }
                },
                TimeSpan.FromSeconds(Task.Destination.DelaySeconds ?? 0.5f));
            return true;
        }

        public override ETaskResult Update()
        {
            if (movementController.IsPathfinding || movementController.IsPathRunning)
                return ETaskResult.StillRunning;

            DateTime movementStartedAt = movementController.StartedAt;
            if (movementStartedAt == DateTime.MaxValue || movementStartedAt.AddSeconds(1) >= DateTime.Now)
                return ETaskResult.StillRunning;

            return ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed class SingleExecutor
    (
        MovementController movementController,
        IObjectTable objectTable,
        IFramework framework) : ExecutorBase<SingleTask>(movementController, objectTable, framework);

    internal sealed record RepeatedTask
    (
        uint? DataId,
        JumpDestination Destination,
        string? Comment) : IJumpTask
    {
        public override string ToString() => $"RepeatedJump({Comment})";
    }

    internal sealed class RepeatedExecutor
    (
        MovementController movementController,
        IObjectTable objectTable,
        IFramework framework,
        ICondition condition,
        ILogger<RepeatedExecutor> logger)
        : ExecutorBase<RepeatedTask>(movementController, objectTable, framework)
    {
        private readonly IObjectTable _objectTable = objectTable;
        private int _attempts;
        private DateTime _continueAt = DateTime.MinValue;

        protected override bool Start()
        {
            _continueAt = DateTime.Now + TimeSpan.FromSeconds(2 * (Task.Destination.DelaySeconds ?? 0.5f));
            return base.Start();
        }

        public override ETaskResult Update()
        {
            if (DateTime.Now < _continueAt || condition[ConditionFlag.Jumping])
                return ETaskResult.StillRunning;

            float stopDistance = Task.Destination.CalculateStopDistance();
            if (_objectTable[0] == null)
                return ETaskResult.StillRunning;
            if ((_objectTable[0]!.Position - Task.Destination.Position).Length() <= stopDistance ||
                _objectTable[0]?.Position.Y >= Task.Destination.Position.Y - 0.5f)
            {
                return ETaskResult.TaskComplete;
            }

            logger.LogTrace("Y-Heights for jumps: player={A}, target={B}", _objectTable[0]?.Position.Y,
                Task.Destination.Position.Y - 0.5f);
            unsafe
            {
                if (ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2))
                    ++_attempts;
            }

            if (_attempts >= 50)
                throw new TaskException("Tried to jump too many times, didn't reach the target");

            _continueAt = DateTime.Now + TimeSpan.FromSeconds(Task.Destination.DelaySeconds ?? 0.5f);
            return ETaskResult.StillRunning;
        }
    }
}
