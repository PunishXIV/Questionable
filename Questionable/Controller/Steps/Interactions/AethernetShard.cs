using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Questionable.Controller.Steps.Interactions;

internal static class AethernetShard
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.AttuneAethernetShard)
                return null;
            if (!step.AethernetShard.HasValue)
                throw new ArgumentException($"{nameof(step.AethernetShard)} is missing", nameof(step));

            return new Attune(step.AethernetShard.Value);
        }
    }

    internal sealed record Attune(EAetheryteLocation AetheryteLocation) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => $"AttuneAethernetShard({AetheryteLocation})";
    }

    internal sealed class AttuneExecutor
    (
        AetheryteFunctions aetheryteFunctions,
        GameFunctions gameFunctions,
        ILogger<AttuneExecutor> logger) : TaskExecutor<Attune>
    {
        private readonly InteractRetry _retry = new(DateTime.Now);

        protected override bool Start()
        {
            if (!aetheryteFunctions.IsAetheryteUnlocked(Task.AetheryteLocation))
            {
                logger.LogInformation("Attuning to aethernet shard {AethernetShard}", Task.AetheryteLocation);
                ProgressContext = InteractionProgressContext.FromActionUseOrDefault(() =>
                    gameFunctions.InteractWith((uint)Task.AetheryteLocation, ObjectKind.Aetheryte));
                return true;
            }

            logger.LogInformation("Already attuned to aethernet shard {AethernetShard}", Task.AetheryteLocation);
            return false;
        }

        public override ETaskResult Update()
        {
            if (aetheryteFunctions.IsAetheryteUnlocked(Task.AetheryteLocation))
                return ETaskResult.TaskComplete;

            if (_retry.Due(DateTime.Now, gameFunctions.IsOccupied()))
                gameFunctions.InteractWith((uint)Task.AetheryteLocation, ObjectKind.Aetheryte);
            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
