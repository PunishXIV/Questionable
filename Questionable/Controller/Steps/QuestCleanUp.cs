using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps;

internal static class QuestCleanUp
{
    internal sealed class CheckAlliedSocietyMount(GameFunctions gameFunctions, AetheryteData aetheryteData, AlliedSocietyData alliedSocietyData, ILogger<CheckAlliedSocietyMount> logger) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (sequence.Sequence == 0)
                return null;

            // if you are on a allied society mount
            if (gameFunctions.GetMountId() is { } mountId &&
                alliedSocietyData.Mounts.TryGetValue(mountId, out var mountConfiguration))
            {
                logger.LogInformation("We are on a known allied society mount with id = {MountId}", mountId);

                // it doesn't particularly matter if we teleport to the same aetheryte twice in the same quest step, as
                // the second (normal) teleport instance should detect that we're within range and not do anything
                var targetAetheryte = step.AetheryteShortcut ?? mountConfiguration.ClosestAetheryte;
                var teleportTask = new AetheryteShortcut.Task(null, quest.Id, targetAetheryte, aetheryteData.TerritoryIds[targetAetheryte]);

                // turn-in step can never be done while mounted on an allied society mount
                if (sequence.Sequence == 255)
                {
                    logger.LogInformation("Mount can't be used to finish quest, teleporting to {Aetheryte}", mountConfiguration.ClosestAetheryte);
                    return teleportTask;
                }

                // if the quest uses no mount actions, that's not a mount quest
                if (!quest.AllSteps().Any(x => (x.Step.Action is { } action && action.RequiresMount()) || (x.Step.InteractionType == EInteractionType.Combat && x.Step.KillEnemyDataIds.Contains(8593))))
                {
                    logger.LogInformation("Quest doesn't use any mount actions, teleporting to {Aetheryte}", mountConfiguration.ClosestAetheryte);
                    return teleportTask;
                }

                // have any of the previous sequences interacted with the issuer?
                var previousSteps =
                    quest.AllSequences()
                        .Where(x => x.Sequence > 0 // quest accept doesn't ever put us into a mount
                                    && x.Sequence < sequence.Sequence)
                        .SelectMany(x => x.Steps)
                        .ToList();
                if (!previousSteps.Any(x => x.DataId != null && mountConfiguration.IssuerDataIds.Contains(x.DataId.Value)))
                {
                    // this quest hasn't given us a mount yet
                    logger.LogInformation("Haven't talked to mount NPC for this allied society quest; {Aetheryte}", mountConfiguration.ClosestAetheryte);
                    return teleportTask;
                }
            }

            return null;
        }
    }


    internal sealed class CloseGatheringAddonFactory(IGameGui gameGui) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (IsAddonOpen("GatheringMasterpiece"))
                yield return new CloseGatheringAddonTask("GatheringMasterpiece");

            if (IsAddonOpen("Gathering"))
                yield return new CloseGatheringAddonTask("Gathering");
        }

        private unsafe bool IsAddonOpen(string name)
        {
            return gameGui.TryGetAddonByName(name, out AtkUnitBase* addon) && addon->IsVisible;
        }
    }

    internal sealed record CloseGatheringAddonTask(string AddonName) : ITask
    {
        public override string ToString() => $"CloseAddon({AddonName})";
    }

    internal sealed class DoCloseAddon(IGameGui gameGui) : TaskExecutor<CloseGatheringAddonTask>
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
