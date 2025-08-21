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
            yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(0.5));

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

    /// <param name="ExpectedTerritoryId">If using an aethernet shortcut after, the aetheryte's territory-id and the step's territory-id can differ, we always use the aetheryte's territory-id.</param>
    internal sealed record Task(
        QuestStep? Step,
        ElementId? ElementId,
        EAetheryteLocation TargetAetheryte,
        ushort ExpectedTerritoryId) : ISkippableTask
    {
        public override string ToString() => $"UseAetheryte({TargetAetheryte})";
    }

    internal sealed class UseAetheryteShortcut(
        ILogger<UseAetheryteShortcut> logger,
        AetheryteFunctions aetheryteFunctions,
        QuestFunctions questFunctions,
        IClientState clientState,
        IChatGui chatGui,
        ICondition condition,
        AetheryteData aetheryteData,
        ExtraConditionUtils extraConditionUtils) : TaskExecutor<Task>
    {
        private bool _teleported;
        private DateTime _continueAt;

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
            ushort territoryType = clientState.TerritoryType;
            if (Task.Step != null)
            {
                var skipConditions = Task.Step.SkipConditions?.AetheryteShortcutIf ?? new();
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
                        QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(Task.ElementId);
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
                        if (Vector3.Distance(nearPosition.Position, clientState.LocalPlayer!.Position) <=
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
                            Vector3.Distance(notNearPosition.Position, clientState.LocalPlayer!.Position))
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

                if (Task.ExpectedTerritoryId == territoryType)
                {
                    if (!skipConditions.Never)
                    {
                        if (skipConditions is { InSameTerritory: true })
                        {
                            logger.LogInformation("Skipping aetheryte teleport due to SkipCondition (InSameTerritory)");
                            return true;
                        }

                        Vector3 pos = clientState.LocalPlayer!.Position;
                        if (Task.Step.Position != null &&
                            (pos - Task.Step.Position.Value).Length() < Task.Step.CalculateActualStopDistance())
                        {
                            logger.LogInformation("Skipping aetheryte teleport, we're near the target");
                            return true;
                        }

                        if (aetheryteData.CalculateDistance(pos, territoryType, Task.TargetAetheryte) < 20 ||
                            (Task.Step.AethernetShortcut != null &&
                             (aetheryteData.CalculateDistance(pos, territoryType, Task.Step.AethernetShortcut.From) <
                              20 ||
                              aetheryteData.CalculateDistance(pos, territoryType, Task.Step.AethernetShortcut.To) <
                              20)))
                        {
                            logger.LogInformation("Skipping aetheryte teleport");
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool DoTeleport()
        {
            if (!aetheryteFunctions.CanTeleport(Task.TargetAetheryte))
            {
                if (!aetheryteFunctions.IsTeleportUnlocked())
                    throw new TaskException("Teleport is not unlocked, attune to any aetheryte first.");

                _continueAt = DateTime.Now.AddSeconds(1);
                logger.LogTrace("Waiting for teleport cooldown...");
                return false;
            }

            _continueAt = DateTime.Now.AddSeconds(8);

            if (!aetheryteFunctions.IsAetheryteUnlocked(Task.TargetAetheryte))
            {
                chatGui.PrintError($"Aetheryte {Task.TargetAetheryte} is not unlocked.", CommandHandler.MessageTag, CommandHandler.TagColor);
                throw new TaskException("Aetheryte is not unlocked");
            }

            ProgressContext =
                InteractionProgressContext.FromActionUseOrDefault(() =>
                    aetheryteFunctions.TeleportAetheryte(Task.TargetAetheryte));
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

    internal sealed class MoveAwayFromAetheryteExecutor(
        MoveExecutor moveExecutor,
        AetheryteData aetheryteData,
        IClientState clientState) : TaskExecutor<MoveAwayFromAetheryte>
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
            Vector3 playerPosition = clientState.LocalPlayer!.Position;
            if (aetheryteData.CalculateDistance(playerPosition, clientState.TerritoryType, Task.TargetAetheryte) >= 20)
                return false;

            Vector3 closestPoint = AetherytesToMoveFrom[Task.TargetAetheryte]
                .MinBy(x => Vector3.Distance(x, playerPosition));
            MoveTask task = new MoveTask(aetheryteData.TerritoryIds[Task.TargetAetheryte],
                closestPoint, Mount: false, StopDistance: 0.25f, DisableNavmesh: true,
                InteractionType: EInteractionType.None, RestartNavigation: false);
            return moveExecutor.Start(task);
        }

        public override ETaskResult Update() => moveExecutor.Update();

        public override bool ShouldInterruptOnDamage() => true;
    }
}
