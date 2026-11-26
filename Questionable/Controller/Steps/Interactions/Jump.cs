using FFXIVClientStructs.FFXIV.Client.Game;
namespace Questionable.Controller.Steps.Interactions;

internal static class Jump
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Jump)
                return null;

            if (step.JumpDestination == null)
                throw new ArgumentException($"{nameof(step.JumpDestination)} is missing", nameof(step));

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
        NavmeshIpc navmesh,
        IObjectTable objectTable,
        IFramework framework) : TaskExecutor<T>, ITaskExecutor.IStoppable
    where T : class, IJumpTask
    {
        private DateTime _startedAt = DateTime.MaxValue;

        protected NavmeshIpc Navmesh => navmesh;

        protected override bool Start()
        {
            float stopDistance = Task.Destination.CalculateStopDistance();
            if ((objectTable[0]!.Position - Task.Destination.Position).Length() <= stopDistance)
                return false;

            // MappingTheRealm only follows the navmesh and can't jump; the run-up has to be a straight line.
            if (!navmesh.MoveStraight(Task.Destination.Position))
                throw new TaskException("vnavmesh couldn't start the run-up for the jump");
            _startedAt = DateTime.Now;
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
            if (objectTable[0] is { } player &&
                (player.Position - Task.Destination.Position).Length() <= Task.Destination.CalculateStopDistance())
            {
                navmesh.Stop();
                return ETaskResult.TaskComplete;
            }

            if (navmesh.GetPathRunning() == true || _startedAt.AddSeconds(1) >= DateTime.Now)
                return ETaskResult.StillRunning;

            return ETaskResult.TaskComplete;
        }

        public void StopNow() => navmesh.Stop();

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed class SingleExecutor
    (
        NavmeshIpc navmesh,
        IObjectTable objectTable,
        IFramework framework) : ExecutorBase<SingleTask>(navmesh, objectTable, framework);

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
        NavmeshIpc navmesh,
        IObjectTable objectTable,
        IFramework framework,
        ICondition condition,
        ILogger<RepeatedExecutor> logger)
        : ExecutorBase<RepeatedTask>(navmesh, objectTable, framework)
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
            if (_objectTable[0] is not { } player)
                return ETaskResult.StillRunning;
            if ((player.Position - Task.Destination.Position).Length() <= stopDistance ||
                player.Position.Y >= Task.Destination.Position.Y - 0.5f)
            {
                Navmesh.Stop();
                return ETaskResult.TaskComplete;
            }

            logger.LogTrace("Y-Heights for jumps: player={A}, target={B}", player.Position.Y,
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
