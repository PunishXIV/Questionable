using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

internal static class CreateGearset
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.CreateGearset)
                return null;

            return new Task();
        }
    }

    internal sealed record Task : ITask
    {
        public override string ToString() => "CreateGearset";
    }

    internal sealed class CreateGearsetExecutor(
        IClientState clientState,
        ICondition condition,
        ILogger<CreateGearsetExecutor> logger) : AbstractDelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            // Safety check: ensure player is logged in
            if (!clientState.IsLoggedIn)
            {
                logger.LogWarning("Cannot create gearset: player is not logged in");
                throw new TaskException("Player is not logged in");
            }

            // Safety check: ensure gearset module is available
            var gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
            {
                logger.LogWarning("Cannot create gearset: RaptureGearsetModule is not available");
                throw new TaskException("Gearset system is not available");
            }

            // Safety check: ensure player is not in combat
            if (condition[ConditionFlag.InCombat])
            {
                logger.LogWarning("Cannot create gearset: player is in combat");
                throw new TaskException("Cannot create gearset while in combat");
            }

            // Create the gearset with currently equipped gear
            // This will automatically:
            // - Use the player's current job for the gearset
            // - Name it based on the current job (e.g., "Culinarian", "Paladin")
            // - Find the next available slot (0-99)
            int gearsetId = gearsetModule->CreateGearset();

            if (gearsetId < 0)
            {
                logger.LogError("Failed to create gearset (all slots may be full)");
                throw new TaskException("Failed to create gearset - all slots may be full");
            }

            logger.LogInformation("Successfully created gearset in slot {GearsetId}", gearsetId);
            return true;
        }

        protected override ETaskResult UpdateInternal() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
