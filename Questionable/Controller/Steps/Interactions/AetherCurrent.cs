using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Interactions;

internal static class AetherCurrent
{
    internal sealed class Factory(
        AetherCurrentData aetherCurrentData,
        IChatGui chatGui) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.AttuneAetherCurrent)
                return null;

            ArgumentNullException.ThrowIfNull(step.DataId);
            ArgumentNullException.ThrowIfNull(step.AetherCurrentId);

            if (!aetherCurrentData.IsValidAetherCurrent(step.TerritoryId, step.AetherCurrentId.Value))
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

    internal sealed class DoAttune(
        GameFunctions gameFunctions,
        ILogger<DoAttune> logger) : TaskExecutor<Attune>
    {
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

        public override ETaskResult Update() =>
            gameFunctions.IsAetherCurrentUnlocked(Task.AetherCurrentId)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => true;
    }
}
