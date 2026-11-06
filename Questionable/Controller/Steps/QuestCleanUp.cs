using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps;

internal static class QuestCleanUp
{
    internal sealed class CheckAlliedSocietyMount(AetheryteData aetheryteData, AlliedSocietyData alliedSocietyData, ILogger<CheckAlliedSocietyMount> logger) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (sequence.Sequence == 0)
                return null;

            if (GameFunctions.GetMountId() is { } mountId &&
                alliedSocietyData.Mounts.TryGetValue(mountId, out AlliedSocietyMountConfiguration? mountConfiguration))
            {
                logger.LogInformation("We are on a known allied society mount with id = {MountId}", mountId);

                // This UT sidequest reuses a Moogle society mount.
                if (quest.Id.Value == 4349)
                {
                    logger.LogInformation("However, this UT sidequest happens to reuse this Moogle society mount.");
                    logger.LogInformation("We do not question the almighty wisdom of game devs. Let's continue with this sidequest.");
                    return null;
                }

                EAetheryteLocation targetAetheryte = step.AetheryteShortcut ?? mountConfiguration.ClosestAetheryte;
                AetheryteShortcut.Task teleportTask = new(Step: null, quest.Id, targetAetheryte, aetheryteData.TerritoryIds[targetAetheryte]);

                // turn-in step can never be done while mounted on an allied society mount
                if (sequence.Sequence == 255)
                {
                    logger.LogInformation("Mount can't be used to finish quest, teleporting to {Aetheryte}", mountConfiguration.ClosestAetheryte);
                    return teleportTask;
                }

                if (!quest.AllSteps().Any(x => (x.Step.Action is { } action && action.RequiresMount()) ||
                    (x.Step.InteractionType == EInteractionType.Combat && x.Step.KillEnemyDataIds.Contains(8593))))
                {
                    logger.LogInformation("Quest doesn't use any mount actions, unmounting");
                    return new MountStep.UnmountTask();
                }

                List<QuestStep> previousSteps =
                    quest.AllSequences()
                        .Where(x => x.Sequence > 0 // quest accept doesn't ever put us into a mount
                                    && x.Sequence < sequence.Sequence)
                        .SelectMany(x => x.Steps)
                        .ToList();
                if (!previousSteps.Any(x => x.DataId != null && mountConfiguration.IssuerDataIds.Contains(x.DataId.Value)))
                {
                    logger.LogInformation("Haven't talked to mount NPC for this allied society quest; {Aetheryte}", mountConfiguration.ClosestAetheryte);
                    return teleportTask;
                }
            }

            return null;
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
