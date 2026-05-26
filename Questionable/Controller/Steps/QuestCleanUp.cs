using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
namespace Questionable.Controller.Steps;

internal static class QuestCleanUp
{
    internal sealed class CheckAlliedSocietyMount(
        GameFunctions gameFunctions,
        AlliedSocietyData alliedSocietyData,
        ILogger<CheckAlliedSocietyMount> logger)
        : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (sequence.Sequence == 0)
                return null;

            ushort? mountId = gameFunctions.GetMountId();
            if (!alliedSocietyData.IsAlliedSocietyMount(mountId))
                return null;

            logger.LogInformation("On allied society mount {MountId}", mountId);

            // oh boy we love one-off hacky fixes don't we folks
            if (quest.Id.Value == 4349)
            {
                logger.LogInformation("However, this UT sidequest happens to reuse this Moogle society mount.");
                logger.LogInformation("We do not question the almighty wisdom of game devs. Let's continue with this sidequest.");
                return null;
            }

            if (alliedSocietyData.ShouldRemainMountedForStep(step, mountId!.Value))
                return null;

            logger.LogInformation(
                "Dismounting allied society mount {MountId} before {InteractionType} (sequence {Sequence})",
                mountId, step.InteractionType, sequence.Sequence);
            return new Mount.UnmountTask();
        }
    }


    internal sealed class CloseGatheringAddonFactory(IGameGuiAdapter gameGui) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (IsAddonOpen("GatheringMasterpiece"))
                yield return new CloseGatheringAddonTask("GatheringMasterpiece");

            if (IsAddonOpen("Gathering"))
                yield return new CloseGatheringAddonTask("Gathering");
        }

        private unsafe bool IsAddonOpen(string name) => gameGui.TryGetAddonByName(name, out AtkUnitBase* addon) && addon->IsVisible;
    }

    internal sealed record CloseGatheringAddonTask(string AddonName) : ITask
    {
        public override string ToString() => $"CloseAddon({AddonName})";
    }

    internal sealed class DoCloseAddon(IGameGuiAdapter gameGui) : TaskExecutor<CloseGatheringAddonTask>
    {
        protected override unsafe bool Start()
        {
            if (gameGui.TryGetAddonByName(Task.AddonName, out AtkUnitBase* addon))
            {
                addon->FireCallbackInt(-1);
                return true;
            }

            return false;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
