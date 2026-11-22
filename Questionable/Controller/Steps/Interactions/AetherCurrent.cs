using Dalamud.Game.ClientState.Objects.Enums;
namespace Questionable.Controller.Steps.Interactions;

internal static class AetherCurrent
{
    internal sealed class Factory
    (
        AetherCurrentData aetherCurrentData,
        IChatGui chatGui) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.AttuneAetherCurrent)
                return null;
            if (!step.DataId.HasValue)
                throw new ArgumentException($"{nameof(step.DataId)} is missing", nameof(step));
            if (!step.AetherCurrentId.HasValue)
                throw new ArgumentException($"{nameof(step.AetherCurrentId)} is missing", nameof(step));

            if (!aetherCurrentData.IsValid(step.TerritoryId, step.AetherCurrentId.Value))
            {
                chatGui.PrintError(
                    $"Aether current with id {step.AetherCurrentId} is referencing an invalid aether current, will skip attunement",
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                return null;
            }

            return new Attune(step.DataId.Value, step.AetherCurrentId.Value);
        }
    }

    internal sealed record Attune(uint DataId, uint AetherCurrentId) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => $"AttuneAetherCurrent({AetherCurrentId})";
    }

    internal sealed class AttuneExecutor
    (
        GameFunctions gameFunctions,
        ILogger<AttuneExecutor> logger) : TaskExecutor<Attune>
    {
        private readonly InteractRetry _retry = new(DateTime.Now);

        protected override bool Start()
        {
            if (!gameFunctions.IsAetherCurrentUnlocked(Task.AetherCurrentId))
            {
                logger.LogInformation("Attuning to aether current {AetherCurrentId} / {DataId}", Task.AetherCurrentId,
                    Task.DataId);
                ProgressContext =
                    InteractionProgressContext.FromActionUseOrDefault(() =>
                        gameFunctions.InteractWith(Task.DataId, ObjectKind.EventObj));
                return true;
            }

            logger.LogInformation("Already attuned to aether current {AetherCurrentId} / {DataId}",
                Task.AetherCurrentId,
                Task.DataId);
            return false;
        }

        public override ETaskResult Update()
        {
            if (gameFunctions.IsAetherCurrentUnlocked(Task.AetherCurrentId))
                return ETaskResult.TaskComplete;

            if (_retry.Due(DateTime.Now, gameFunctions.IsOccupied()))
                gameFunctions.InteractWith(Task.DataId, ObjectKind.EventObj);
            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
