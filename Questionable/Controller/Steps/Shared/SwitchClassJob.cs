using System.Collections.Generic;
using System.Linq;
using ECommons.ExcelServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Interactions;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class SwitchClassJob
{
    internal sealed class Factory(ClassJobUtils classJobUtils) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.SwitchClass)
                yield break;

            Job classJob = classJobUtils.AsIndividualJobs(step.TargetClass, quest.Id).Single();
            Dictionary<Job, (Job, ushort)> classToJobStone = new() {
                { Job.GLA, (Job.PLD, 4542) },
                { Job.PGL, (Job.MNK, 4543) },
                { Job.MRD, (Job.WAR, 4544) },
                { Job.LNC, (Job.DRG, 4545) },
                { Job.ARC, (Job.BRD, 4546) },
                { Job.CNJ, (Job.WHM, 4547) },
                { Job.THM, (Job.BLM, 4548) },
                { Job.ACN, (Job.SMN, 4549) },
                { Job.ROG, (Job.NIN, 7886) }
            };
            if (classToJobStone.TryGetValue(classJob, out var value))
            {
                (var job, var item) = value;
                bool unlocked = false;
                unsafe {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    if (inventoryManager->GetInventoryItemCount(item) > 0)
                        unlocked = true;
                }
                if (unlocked)
                {
                    yield return new Task(job);
                    yield return new UnequipItem.Task(item);
                    yield break;
                }
            }
            yield return new Task(classJob);
        }
    }

    internal sealed record Task(Job ClassJob) : ITask
    {
        public override string ToString() => $"SwitchJob({ClassJob})";
    }

    internal sealed class SwitchClassJobExecutor : AbstractDelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            if (PlayerState.Instance()->CurrentClassJobId == (uint)Task.ClassJob)
                return false;

            RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule != null)
            {
                for (int i = 0; i < 100; ++i)
                {
                    RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(i);
                    if (gearset->ClassJob == (byte)Task.ClassJob)
                    {
                        gearsetModule->EquipGearset(gearset->Id);
                        return true;
                    }
                }
            }

            throw new TaskException($"No gearset found for {Task.ClassJob}");
        }

        protected unsafe override ETaskResult UpdateInternal()
        {
            if (PlayerState.Instance()->CurrentClassJobId == (uint)Task.ClassJob)
                return ETaskResult.TaskComplete;
            if (EzThrottler.Throttle("SwitchJob"))
                StartInternal();
            return ETaskResult.StillRunning;
        }

        // can we even take damage while switching jobs? we should be out of combat...
        public override bool ShouldInterruptOnDamage() => false;
    }
}
