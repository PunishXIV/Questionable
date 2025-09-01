using Microsoft.Extensions.Logging;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using System;
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

            ArgumentNullException.ThrowIfNull(step.AethernetShard);

            return new Attune(step.AethernetShard.Value);
        }
    }

    internal sealed record Attune(EAetheryteLocation AetheryteLocation) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => $"AttuneAethernetShard({AetheryteLocation})";
    }

    internal sealed class DoAttune(
        AetheryteFunctions aetheryteFunctions,
        GameFunctions gameFunctions,
        ILogger<DoAttune> logger) : TaskExecutor<Attune>
    {
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

        public override ETaskResult Update() =>
            aetheryteFunctions.IsAetheryteUnlocked(Task.AetheryteLocation)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => true;
    }
}
