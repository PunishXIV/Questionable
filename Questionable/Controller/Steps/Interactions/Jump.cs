using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Model;
using Questionable.Model.Questing;
using System;

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
                return new SingleJumpTask(step.DataId, step.JumpDestination, step.Comment);
            else
                return new RepeatedJumpTask(step.DataId, step.JumpDestination, step.Comment);
        }
    }

    internal interface IJumpTask : ITask
    {
        uint? DataId { get; }
        JumpDestination JumpDestination { get; }
        string? Comment { get; }
    }

    internal sealed record SingleJumpTask(
        uint? DataId,
        JumpDestination JumpDestination,
        string? Comment) : IJumpTask
    {
        public override string ToString() => $"Jump({Comment})";
    }

    internal abstract class JumpBase<T>(
        MovementController movementController,
        IClientState clientState,
        IFramework framework) : TaskExecutor<T>
        where T : class, IJumpTask
    {
        protected override bool Start()
        {
            float stopDistance = Task.JumpDestination.CalculateStopDistance();
            if ((clientState.LocalPlayer!.Position - Task.JumpDestination.Position).Length() <= stopDistance)
                return false;

            movementController.NavigateTo(EMovementType.Quest, Task.DataId, [Task.JumpDestination.Position], false,
                false,
                Task.JumpDestination.StopDistance ?? stopDistance);
            framework.RunOnTick(() =>
                {
                    unsafe
                    {
                        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
                    }
                },
                TimeSpan.FromSeconds(Task.JumpDestination.DelaySeconds ?? 0.5f));
            return true;
        }

        public override ETaskResult Update()
        {
            if (movementController.IsPathfinding || movementController.IsPathRunning)
                return ETaskResult.StillRunning;

            DateTime movementStartedAt = movementController.MovementStartedAt;
            if (movementStartedAt == DateTime.MaxValue || movementStartedAt.AddSeconds(1) >= DateTime.Now)
                return ETaskResult.StillRunning;

            return ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed class DoSingleJump(
        MovementController movementController,
        IClientState clientState,
        IFramework framework) : JumpBase<SingleJumpTask>(movementController, clientState, framework);

    internal sealed record RepeatedJumpTask(
        uint? DataId,
        JumpDestination JumpDestination,
        string? Comment) : IJumpTask
    {
        public override string ToString() => $"RepeatedJump({Comment})";
    }

    internal sealed class DoRepeatedJumps(
        MovementController movementController,
        IClientState clientState,
        IFramework framework,
        ICondition condition,
        ILogger<DoRepeatedJumps> logger)
        : JumpBase<RepeatedJumpTask>(movementController, clientState, framework)
    {
        private readonly IClientState _clientState = clientState;
        private DateTime _continueAt = DateTime.MinValue;
        private int _attempts;

        protected override bool Start()
        {
            _continueAt = DateTime.Now + TimeSpan.FromSeconds(2 * (Task.JumpDestination.DelaySeconds ?? 0.5f));
            return base.Start();
        }

        public override ETaskResult Update()
        {
            if (DateTime.Now < _continueAt || condition[ConditionFlag.Jumping])
                return ETaskResult.StillRunning;

            float stopDistance = Task.JumpDestination.CalculateStopDistance();
            if ((_clientState.LocalPlayer!.Position - Task.JumpDestination.Position).Length() <= stopDistance ||
                _clientState.LocalPlayer.Position.Y >= Task.JumpDestination.Position.Y - 0.5f)
                return ETaskResult.TaskComplete;

            logger.LogTrace("Y-Heights for jumps: player={A}, target={B}", _clientState.LocalPlayer.Position.Y,
                Task.JumpDestination.Position.Y - 0.5f);
            unsafe
            {
                if (ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2))
                    ++_attempts;
            }

            if (_attempts >= 50)
                throw new TaskException("Tried to jump too many times, didn't reach the target");

            _continueAt = DateTime.Now + TimeSpan.FromSeconds(Task.JumpDestination.DelaySeconds ?? 0.5f);
            return ETaskResult.StillRunning;
        }
    }
}
