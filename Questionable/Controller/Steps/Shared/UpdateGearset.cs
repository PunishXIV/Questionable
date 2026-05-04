using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
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

    internal sealed class UpdateGearsetExecutor
    (
        IClientState clientState,
        ICondition condition,
        ILogger<UpdateGearsetExecutor> logger) : AbstractDelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            // Safety check: ensure player is logged in
            if (!clientState.IsLoggedIn)
            {
                logger.LogWarning("Cannot update gearset: player is not logged in");
                throw new TaskException("Player is not logged in");
            }

            // Safety check: ensure gearset module is available
            RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
            {
                logger.LogWarning("Cannot update gearset: RaptureGearsetModule is not available");
                throw new TaskException("Gearset system is not available");
            }

            // Safety check: ensure player is not in combat
            if (condition[ConditionFlag.InCombat])
            {
                logger.LogWarning("Cannot update gearset: player is in combat");
                throw new TaskException("Cannot update gearset while in combat");
            }

            // Determine which gearset to update
            int gearsetId;
            if (Task.TargetClass.HasValue)
            {
                // Find gearset by class/job
                logger.LogInformation("Looking for gearset with job {ClassJob}", Task.TargetClass.Value);

                bool found = false;
                gearsetId = -1;

                for (int i = 0; i < 100; ++i)
                {
                    RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(i);
                    if (gearset != null &&
                            gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) &&
                            gearset->ClassJob == (byte)Task.TargetClass.Value)
                    {
                        gearsetId = i;
                        found = true;
                        logger.LogInformation("Found gearset {GearsetId} for job {ClassJob}", gearsetId, Task.TargetClass.Value);
                        break;
                    }
                }

                if (!found)
                {
                    logger.LogError("No gearset found for {ClassJob}", Task.TargetClass.Value);
                    throw new TaskException($"No gearset found for {Task.TargetClass.Value}");
                }
            }
            else
            {
                // Update the currently equipped gearset
                // CurrentGearsetIndex is -1 if no gearset is equipped
                gearsetId = gearsetModule->CurrentGearsetIndex;

                if (gearsetId < 0)
                {
                    logger.LogError("No gearset is currently equipped");
                    throw new TaskException("No gearset is currently equipped");
                }

                logger.LogInformation("Updating currently equipped gearset {GearsetId}", gearsetId);
            }

            // Update the gearset with currently equipped gear
            // This will overwrite the gearset at the specified slot with current gear
            int result = gearsetModule->UpdateGearset(gearsetId);

            if (result < 0)
            {
                logger.LogError("Failed to update gearset {GearsetId}", gearsetId);
                throw new TaskException($"Failed to update gearset {gearsetId}");
            }

            logger.LogInformation("Successfully updated gearset {GearsetId}", gearsetId);
            return true;
        }

        protected override ETaskResult UpdateInternal() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
