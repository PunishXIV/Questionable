using Dalamud.Game.ClientState.Objects.Enums;
using Microsoft.Extensions.Logging;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using System;

namespace Questionable.Controller.Steps.Interactions;

internal static class AetheryteFreeOrFavored
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.RegisterFreeOrFavoredAetheryte)
                return null;

            ArgumentNullException.ThrowIfNull(step.Aetheryte);

            return new Register(step.Aetheryte.Value);
        }
    }

    internal sealed record Register(EAetheryteLocation AetheryteLocation) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => $"RegisterFreeOrFavoredAetheryte({AetheryteLocation})";
    }

    internal sealed class DoRegister(
        AetheryteFunctions aetheryteFunctions,
        GameFunctions gameFunctions,
        ILogger<DoRegister> logger) : TaskExecutor<Register>
    {
        protected override bool Start()
        {
            if (!aetheryteFunctions.IsAetheryteUnlocked(Task.AetheryteLocation))
                throw new TaskException($"Aetheryte {Task.AetheryteLocation} is not attuned");

            if (aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(Task.AetheryteLocation) ==
                AetheryteRegistrationResult.NotPossible)
            {
                logger.LogInformation("Could not register aetheryte {AetheryteLocation} as free or favored",
                    Task.AetheryteLocation);
                return false;
            }

            ProgressContext = InteractionProgressContext.FromActionUseOrDefault(() =>
                gameFunctions.InteractWith((uint)Task.AetheryteLocation, ObjectKind.Aetheryte));
            return true;
        }

        public override ETaskResult Update()
        {
            return aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(Task.AetheryteLocation) ==
                   AetheryteRegistrationResult.NotPossible
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
