using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LLib.GameData;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
using System.Linq;

namespace Questionable.Controller.Steps.Shared;

internal static class SwitchClassJob
{
    internal sealed class Factory(ClassJobUtils classJobUtils) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.SwitchClass)
                return null;

            EClassJob classJob = classJobUtils.AsIndividualJobs(step.TargetClass, quest.Id).Single();
            return new Task(classJob);
        }
    }
    internal sealed record Task(EClassJob ClassJob) : ITask
    {
        public override string ToString() => $"SwitchJob({ClassJob})";
    }

    internal sealed class SwitchClassJobExecutor(IClientState clientState) : AbstractDelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            if (clientState.LocalPlayer!.ClassJob.RowId == (uint)Task.ClassJob)
                return false;

            var gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule != null)
            {
                for (int i = 0; i < 100; ++i)
                {
                    var gearset = gearsetModule->GetGearset(i);
                    if (gearset->ClassJob == (byte)Task.ClassJob)
                    {
                        gearsetModule->EquipGearset(gearset->Id);
                        return true;
                    }
                }
            }

            throw new TaskException($"No gearset found for {Task.ClassJob}");
        }

        protected override ETaskResult UpdateInternal() => ETaskResult.TaskComplete;

        // can we even take damage while switching jobs? we should be out of combat...
        public override bool ShouldInterruptOnDamage() => false;
    }
}
