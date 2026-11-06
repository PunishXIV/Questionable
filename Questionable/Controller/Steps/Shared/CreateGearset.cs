using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Questionable.Controller.Steps.Common;
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

    internal sealed class CreateGearsetExecutor
    (
        IClientState clientState,
        ICondition condition,
        ILogger<CreateGearsetExecutor> logger) : AbstractDelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            if (!clientState.IsLoggedIn)
            {
                logger.LogWarning("Cannot create gearset: player is not logged in");
                throw new TaskException("Player is not logged in");
            }

            RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
            {
                logger.LogWarning("Cannot create gearset: RaptureGearsetModule is not available");
                throw new TaskException("Gearset system is not available");
            }

            if (condition[ConditionFlag.InCombat])
            {
                logger.LogWarning("Cannot create gearset: player is in combat");
                throw new TaskException("Cannot create gearset while in combat");
            }

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
