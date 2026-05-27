using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Utils;
namespace Questionable.Controller.Steps.Shared;

internal static class AetheryteShortcut
{
    internal sealed class Factory(AetheryteData aetheryteData, TerritoryData territoryData, IClientState clientState)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.AetheryteShortcut == null)
                yield break;

            yield return new Task(step, quest.Id, step.AetheryteShortcut.Value,
                aetheryteData.TerritoryIds[step.AetheryteShortcut.Value]);
            yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(1));

            if (MoveAwayFromAetheryteExecutor.AppliesTo(step.AetheryteShortcut.Value) &&
                step.AethernetShortcut?.From != step.AetheryteShortcut.Value)
            {
                yield return new WaitCondition.Task(
                    () => clientState.TerritoryType == aetheryteData.TerritoryIds[step.AetheryteShortcut.Value],
                    $"Wait(territory: {territoryData.GetNameAndId(aetheryteData.TerritoryIds[step.AetheryteShortcut.Value])})");
                yield return new MoveAwayFromAetheryte(step.AetheryteShortcut.Value);
            }
        }
    }

    /// <param name="ExpectedTerritoryId">
    ///     If using an aethernet shortcut after, the aetheryte's territory-id and the step's
    ///     territory-id can differ, we always use the aetheryte's territory-id.
    /// </param>
    internal sealed record Task
    (
        QuestStep? Step,
        ElementId? ElementId,
        EAetheryteLocation TargetAetheryte,
        uint ExpectedTerritoryId) : ISkippableTask
    {
        internal EAetheryteLocation targetAetheryte = TargetAetheryte;
        public override string ToString() => $"UseAetheryte({targetAetheryte})";
    }

    internal sealed class UseAetheryteShortcut
    (
        ILogger<UseAetheryteShortcut> logger,
        AetheryteFunctions aetheryteFunctions,
        QuestFunctions questFunctions,
        GameFunctions gameFunctions,
        IClientState clientState,
        IObjectTable objectTable,
        IChatGui chatGui,
        ICondition condition,
        AetheryteData aetheryteData,
        ExtraConditionUtils extraConditionUtils,
        QuestRegistry questRegistry) : TaskExecutor<Task>
    {
        private DateTime _continueAt;
        private bool _teleported;
        private bool _societyPause;

        protected override bool Start() => !ShouldSkipTeleport();

        public override ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            if (!_teleported)
            {
                _teleported = DoTeleport();
                return ETaskResult.StillRunning;
            }

            if (clientState.TerritoryType == Task.ExpectedTerritoryId)
                return ETaskResult.TaskComplete;

            return ETaskResult.StillRunning;
        }

        private bool ShouldSkipTeleport()
        {
            uint territoryType = clientState.TerritoryType;
            if (Task.Step != null)
            {
                if (Task.TargetAetheryte is EAetheryteLocation.None)
                {
                    EAetheryteLocation? nearest = Task.Step.Position != null ? aetheryteData.NearestAetheryteTo(Task.Step.TerritoryId, Task.Step.Position.Value) : null;
                    EAetheryteLocation? shortcut = Task.Step.AetheryteShortcut ?? nearest ?? null;
                    if (shortcut == null)
                    {
                        logger.LogInformation("Skipping aetheryte shortcut, null result. step:{Step}, nearest:{Nearest}",
                            Task.Step.AetheryteShortcut, nearest);
                        return true;
                    }
                    if (Task.Step.Mount is { } mount)
                    {
                        logger.LogInformation("Skipping aetheryte shortcut, Mount is set as {Mount}. step:{Step}, nearest:{Nearest}",
                            mount, Task.Step.AetheryteShortcut, nearest);
                        return true;
                    }
                    if (Task.Step.Action is { } action && action.RequiresMount())
                    {
                        logger.LogInformation("Skipping aetheryte shortcut, step action requires mount. step:{Step}, nearest:{Nearest}",
                            Task.Step.AetheryteShortcut, nearest);
                        return true;
                    }
                    if (Task.Step.AethernetShortcut is { } aethernetShortcut && aetheryteData.TerritoryIds[aethernetShortcut.To] != clientState.TerritoryType)
                    {
                        logger.LogInformation("Not skipping aetheryte shortcut, aethernet destination is diff territory. step:{Step}, nearest:{Nearest}",
                            Task.Step.AetheryteShortcut, nearest);
                    }
                    Task.targetAetheryte = shortcut.Value;
                    logger.LogInformation("Aetheryte target has been changed to {TargetAetheryte}", Task.targetAetheryte);
                }
                else
                    Task.targetAetheryte = Task.TargetAetheryte;
                SkipAetheryteCondition skipConditions = Task.Step.SkipConditions?.AetheryteShortcutIf ?? new();
                if (skipConditions is { Never: false })
                {
                    if (skipConditions.InTerritory.Contains(territoryType))
                    {
                        logger.LogInformation("Skipping aetheryte teleport due to SkipCondition (InTerritory)");
                        return true;
                    }

                    if (skipConditions.QuestsCompleted.Count > 0 &&
                        skipConditions.QuestsCompleted.All(questFunctions.IsQuestComplete))
                    {
                        logger.LogInformation("Skipping aetheryte, all prequisite quests are complete");
                        return true;
                    }

                    if (skipConditions.QuestsAccepted.Count > 0 &&
                        skipConditions.QuestsAccepted.All(questFunctions.IsQuestAccepted))
                    {
                        logger.LogInformation("Skipping aetheryte, all prequisite quests are accepted");
                        return true;
                    }

                    if (skipConditions.AetheryteLocked != null &&
                        !aetheryteFunctions.IsAetheryteUnlocked(skipConditions.AetheryteLocked.Value))
                    {
                        logger.LogInformation("Skipping aetheryte teleport due to SkipCondition (AetheryteLocked)");
                        return true;
                    }

                    if (skipConditions.AetheryteUnlocked != null &&
                        aetheryteFunctions.IsAetheryteUnlocked(skipConditions.AetheryteUnlocked.Value))
                    {
                        logger.LogInformation("Skipping aetheryte teleport due to SkipCondition (AetheryteUnlocked)");
                        return true;
                    }

                    if (Task.ElementId != null)
                    {
                        QuestProgressInfo? questWork = QuestFunctions.GetQuestProgressInfo(Task.ElementId);
                        if (skipConditions.RequiredQuestVariablesNotMet &&
                            questWork != null &&
                            !QuestWorkUtils.MatchesRequiredQuestWorkConfig(Task.Step.RequiredQuestVariables, questWork,
                                logger))
                        {
                            logger.LogInformation("Skipping aetheryte teleport, as required variables do not match");
                            return true;
                        }
                    }


                    if (skipConditions.NearPosition is { } nearPosition &&
                        clientState.TerritoryType == nearPosition.TerritoryId)
                    {
                        if (Vector3.Distance(nearPosition.Position, objectTable[0]!.Position) <=
                            nearPosition.MaximumDistance)
                        {
                            logger.LogInformation("Skipping aetheryte shortcut, as we're near the position");
                            return true;
                        }
                    }

                    if (skipConditions.NotNearPosition is { } notNearPosition &&
                        clientState.TerritoryType == notNearPosition.TerritoryId)
                    {
                        if (notNearPosition.MaximumDistance <=
                            Vector3.Distance(notNearPosition.Position, objectTable[0]!.Position))
                        {
                            logger.LogInformation("Skipping aetheryte shortcut, as we're not near the position");
                            return true;
                        }
                    }

                    if (skipConditions.ExtraCondition != null && skipConditions.ExtraCondition != EExtraSkipCondition.None &&
                        extraConditionUtils.MatchesExtraCondition(skipConditions.ExtraCondition.Value))
                    {
                        logger.LogInformation("Skipping step, extra condition {} matches", skipConditions.ExtraCondition);
                        return true;
                    }
                }

                if (Task.ExpectedTerritoryId == territoryType || 
                    (Task.Step.AethernetShortcut is { } aethernet &&
                    aetheryteData.TerritoryIds[aethernet.To].Equals(territoryType)))
                {
                    if (!skipConditions.Never)
                    {
                        if (skipConditions is { InSameTerritory: true })
                        {
                            logger.LogDebug("Ignoring InSameTerritory, obsolete due to distance check");
                            //logger.LogInformation("Skipping aetheryte teleport due to SkipCondition (InSameTerritory)");
                            //return true;
                        }

                        Vector3 pos = objectTable[0]!.Position;
                        if (Task.Step.Position == null)
                        {
                            if (aetheryteData.CalculateDistance(pos, territoryType, Task.targetAetheryte) < 100)
                            {
                                logger.LogInformation("Skipping aetheryte teleport, we're already there");
                                return true;
                            }

                            if (!Task.Step.InteractionType.Equals(EInteractionType.AttuneAetheryte))
                            {
                                logger.LogInformation("No step position, teleporting to aetheryte");
                                return false;
                            }
                            else
                            {
                                logger.LogInformation("AttuneAetheryte, proceeding to destination");
                                return true;
                            }
                        }

                        float distance_target = (pos - Task.Step.Position.Value).Length();
                        float distance_aetheryte_to_target = aetheryteData.CalculateDistance(Task.Step.Position.Value, territoryType, Task.targetAetheryte);
                        if (distance_target < Task.Step.CalculateActualStopDistance())
                        {
                            logger.LogInformation("Skipping aetheryte teleport, we're near the target");
                            return true;
                        }

                        float distance_aethernet_to = 99999;
                        uint teleportTimeDistance = 90;
                        if (Task.Step.AethernetShortcut != null)
                        {
                            distance_aethernet_to = aetheryteData.CalculateDistance(Task.Step.Position.Value, territoryType, Task.Step.AethernetShortcut.To);
                            // if aetheryte route is further from the destination than just walking there, skip it
                            logger.LogDebug(
                                "target direct: {DirectDistance}. target if tp: {TpDistance} target direct XZ: {DirectXZ}. target tp XZ: {TpXZ}, target if aethernet: {AethernetDistance}",
                                distance_target, teleportTimeDistance + distance_aetheryte_to_target,
                                pos.DistanceTo_XZ(Task.Step.Position.Value),
                                Task.Step.Position.Value.DistanceTo_XZ(Task.targetAetheryte.Position(aetheryteData)),
                                distance_aethernet_to + teleportTimeDistance);
                            if (distance_target < (distance_aethernet_to + teleportTimeDistance))
                            {
                                logger.LogInformation("Skipping aethernet teleport, it's a shorter distance to walk there");
                                return true;
                            }
                        }
                        else
                        {
                            logger.LogDebug(
                                "target direct: {DirectDistance}. target if tp: {TpDistance} target direct XZ: {DirectXZ}. target tp XZ: {TpXZ}",
                                distance_target, teleportTimeDistance + distance_aetheryte_to_target,
                                pos.DistanceTo_XZ(Task.Step.Position.Value),
                                Task.Step.Position.Value.DistanceTo_XZ(Task.targetAetheryte.Position(aetheryteData)));
                            if (distance_target < (teleportTimeDistance + distance_aetheryte_to_target))
                            {
                                logger.LogInformation("Skipping aetheryte teleport, it's a shorter distance to walk there");
                                return true;
                            }
                        }
                    }
                }
            }

            if (gameFunctions.HasStatus(404) || gameFunctions.HasStatus(4376)) // Transporting
            {
                logger.LogInformation("Skipping aetheryte teleport, character is busy.");
                return true;
            }

            logger.LogInformation("Not skipping aetheryte teleport");
            return false;
        }

        private bool DoTeleport()
        {
            if (!aetheryteFunctions.CanTeleport(Task.targetAetheryte))
            {
                if (!aetheryteFunctions.IsTeleportUnlocked())
                    throw new TaskException("Teleport is not unlocked, attune to any aetheryte first.");

                _continueAt = DateTime.Now.AddSeconds(1);
                logger.LogTrace("Waiting for teleport cooldown...");
                return false;
            }

            _continueAt = DateTime.Now.AddSeconds(8);

            if (!aetheryteFunctions.IsAetheryteUnlocked(Task.targetAetheryte))
            {
                chatGui.PrintError($"Aetheryte {Task.targetAetheryte} is not unlocked.", CommandHandler.MessageTag, CommandHandler.TagColor);
                throw new TaskException("Aetheryte is not unlocked");
            }

            if (!_societyPause && Task.ElementId != null && questRegistry.TryGetQuest(Task.ElementId, out Quest? quest) && quest.Info.AlliedSociety != EAlliedSociety.None)
            {
                _societyPause = true;
                _continueAt = DateTime.Now.AddSeconds(0.5);
                logger.LogDebug("Waiting for soc teleport recalc cooldown...");
                return false;
            }
            ProgressContext =
                InteractionProgressContext.FromActionUseOrDefault(() =>
                    aetheryteFunctions.TeleportAetheryte(Task.targetAetheryte));
            if (ProgressContext != null)
            {
                logger.LogInformation("Travelling via aetheryte...");
                return true;
            }
            else
            {
                chatGui.Print("Unable to teleport to aetheryte.", CommandHandler.MessageTag, CommandHandler.TagColor);
                throw new TaskException("Unable to teleport to aetheryte");
            }
        }

        public override bool WasInterrupted() => condition[ConditionFlag.InCombat] || base.WasInterrupted();

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record MoveAwayFromAetheryte(EAetheryteLocation TargetAetheryte) : ITask
    {
        public override string ToString() => $"MoveAway({TargetAetheryte})";
    }

    internal sealed class MoveAwayFromAetheryteExecutor
    (
        MoveExecutor moveExecutor,
        AetheryteData aetheryteData,
        IClientState clientState,
        IObjectTable objectTable) : TaskExecutor<MoveAwayFromAetheryte>
    {
        private static readonly Dictionary<EAetheryteLocation, List<Vector3>> AetherytesToMoveFrom = new()
        {
            {
                EAetheryteLocation.SolutionNine,
                [
                    new(0f, 8.8f, 15.5f),
                    new(0f, 8.8f, -15.5f),
                    new(15.5f, 8.8f, 0f),
                    new(-15.5f, 8.8f, 0f)
                ]
            }
        };

        public static bool AppliesTo(EAetheryteLocation location) => AetherytesToMoveFrom.ContainsKey(location);

        protected override bool Start()
        {
            // only relevant if we're actually near the s9 aetheryte at the end
            Vector3 playerPosition = objectTable[0]!.Position;
            if (aetheryteData.CalculateDistance(playerPosition, clientState.TerritoryType, Task.TargetAetheryte) >= 20)
                return false;

            Vector3 closestPoint = AetherytesToMoveFrom[Task.TargetAetheryte]
                .MinBy(x => Vector3.Distance(x, playerPosition));
            MoveTask task = new(aetheryteData.TerritoryIds[Task.TargetAetheryte],
                closestPoint, false, 0.25f, DisableNavmesh: true,
                InteractionType: EInteractionType.None, RestartNavigation: false);
            return moveExecutor.Start(task);
        }

        public override ETaskResult Update() => moveExecutor.Update();

        public override bool ShouldInterruptOnDamage() => true;
    }
}
