using FFXIVClientStructs.FFXIV.Client.UI.Misc;
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

    internal sealed class Executor
    (
        IClientState clientState,
        ICondition condition,
        ILogger<Executor> logger) : DelayedTaskExecutor<Task>(TimeSpan.FromSeconds(1))
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

            byte job = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance()->CurrentClassJobId;
            for (int i = 0; i < 100; ++i)
            {
                RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(i);
                if (gearset != null && gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) &&
                    gearset->ClassJob == job)
                {
                    // Equipping a job's weapon leaves another job's set marked as current; switch to this job's set.
                    if (gearsetModule->CurrentGearsetIndex == i)
                    {
                        logger.LogInformation("Gearset {GearsetId} already exists for the current job", i);
                        return false;
                    }

                    logger.LogInformation("Switching to gearset {GearsetId}, which already exists for the current job", i);
                    gearsetModule->EquipGearset(i);
                    return true;
                }
            }

            int gearsetId = gearsetModule->CreateGearset();

            if (gearsetId < 0)
            {
                logger.LogError("Failed to create gearset (all slots may be full)");
                throw new NeedsPlayerException("Failed to create gearset - all slots may be full");
            }

            logger.LogInformation("Successfully created gearset in slot {GearsetId}", gearsetId);
            return true;
        }

        protected override ETaskResult UpdateInternal() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
