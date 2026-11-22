using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
namespace Questionable.Controller.Steps.Shared;

internal static class UpdateGearset
{
    internal sealed class Factory(ClassJobUtils classJobUtils) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.UpdateGearset)
                return null;

            Job? classJob = null;
            if (step.TargetClass != EExtendedClassJob.None)
                classJob = classJobUtils.AsIndividualJobs(step.TargetClass, quest.Id).Single();

            return new Task(classJob);
        }
    }

    internal sealed record Task(Job? TargetClass) : ITask
    {
        public override string ToString() => $"UpdateGearset({TargetClass?.ToString() ?? "current"})";
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
                logger.LogWarning("Cannot update gearset: player is not logged in");
                throw new TaskException("Player is not logged in");
            }

            RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
            {
                logger.LogWarning("Cannot update gearset: RaptureGearsetModule is not available");
                throw new TaskException("Gearset system is not available");
            }

            if (condition[ConditionFlag.InCombat])
            {
                logger.LogWarning("Cannot update gearset: player is in combat");
                throw new TaskException("Cannot update gearset while in combat");
            }

            // The game's current set can belong to another job after an EquipItem job change; never update that one.
            byte job = Task.TargetClass is { } target
                ? (byte)target
                : FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance()->CurrentClassJobId;
            byte?[] jobsBySlot = new byte?[100];
            for (int i = 0; i < jobsBySlot.Length; ++i)
            {
                RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(i);
                if (gearset != null && gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                    jobsBySlot[i] = gearset->ClassJob;
            }

            int gearsetId = PickGearset(jobsBySlot, job, gearsetModule->CurrentGearsetIndex);
            if (gearsetId < 0)
            {
                logger.LogError("No gearset found for job {ClassJob}", job);
                throw new NeedsPlayerException($"No gear set found for {(Job)job}");
            }

            logger.LogInformation("Updating gearset {GearsetId} for job {ClassJob}", gearsetId, (Job)job);
            int result = gearsetModule->UpdateGearset(gearsetId);

            if (result < 0)
            {
                logger.LogError("Failed to update gearset {GearsetId}", gearsetId);
                throw new TaskException($"Failed to update gearset {gearsetId}");
            }

            logger.LogInformation("Successfully updated gearset {GearsetId}", gearsetId);
            return true;
        }

        internal static int PickGearset(IReadOnlyList<byte?> jobsBySlot, byte job, int current)
        {
            if (current >= 0 && current < jobsBySlot.Count && jobsBySlot[current] == job)
                return current;
            for (int i = 0; i < jobsBySlot.Count; ++i)
                if (jobsBySlot[i] == job)
                    return i;
            return -1;
        }

        protected override ETaskResult UpdateInternal() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
