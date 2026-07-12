using System.Linq;
using ECommons.ExcelServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class SwitchClassJob
{
    internal sealed class Factory(ClassJobUtils classJobUtils) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.SwitchClass)
                return null;

            Job classJob = classJobUtils.AsIndividualJobs(step.TargetClass, quest.Id).Single();
            return new Task(classJob);
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
