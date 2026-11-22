using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
namespace Questionable.Controller.Steps.Interactions;

internal static class FillGauge
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.FillGauge)
                return null;
            if (!step.DataId.HasValue)
                throw new ArgumentException($"{nameof(step.DataId)} is missing", nameof(step));

            return new Task(step.DataId.Value);
        }
    }

    internal sealed record Task(uint DataId) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"FillGauge({DataId})";
    }

    internal sealed class Executor(GameFunctions gameFunctions, ICondition condition, ILogger<Task> logger)
        : TaskExecutor<Task>
    {
        private const byte Full = 100;
        private DateTime _nextTry = DateTime.MinValue;

        protected override unsafe bool Start()
        {
            if ((Job)PlayerState.Instance()->CurrentClassJobId != Job.BST)
                throw new TaskException("Switch to Beastmaster first.");
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            BeastmasterGauge* gauge = (BeastmasterGauge*)JobGaugeManager.Instance()->CurrentGauge;
            if (IsFull(gauge->TPGauge, gauge->PetTPGauge))
            {
                logger.LogInformation("Beastmaster gauges full ({Gauge}/{PetGauge})", gauge->TPGauge, gauge->PetTPGauge);
                return ETaskResult.TaskComplete;
            }

            if (DateTime.Now < _nextTry || condition[ConditionFlag.Casting])
                return ETaskResult.StillRunning;

            _nextTry = DateTime.Now.AddSeconds(0.5);
            if (gameFunctions.FindObjectByDataId(Task.DataId) is { IsTargetable: true } target)
                gameFunctions.UseAction(target, EAction.SmashAxe);
            return ETaskResult.StillRunning;
        }

        internal static bool IsFull(byte gauge, byte petGauge) => gauge >= Full && petGauge >= Full;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
