using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class AetheryteShortcut
{
    public static HashSet<uint> Territories = [212, 351, 128, 131, 133, 419];
    // TODO: refactor the nested territory-specific routing in this factory after SmartNav coverage is established.
    internal sealed class Factory(
        AetheryteData aetheryteData,
        IClientState clientState,
        IObjectTable objectTable,
        ExtraConditionUtils extraConditionUtils,
        GilShopData gilShopData,
        ILogger<AetheryteShortcut.Factory> logger)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (Purchase.IsHandledByKnightshopper(step, gilShopData))
                yield break;

            if (step.AetheryteShortcut == null)
            {
                logger.LogTrace(step.InteractionType.ToString());
                if (step.InteractionType is EInteractionType.EquipRecommended or EInteractionType.Gather)
                    yield break;
                if (step.TerritoryId == 1) // unused territory ID, used to disable auto teleports as a gross hack
                    yield break;
                bool matchesCondition(EExtraSkipCondition condition, Vector3 position) =>
                    extraConditionUtils.MatchesExtraCondition(condition, position, step.TerritoryId);
                // Scion quest hubs
                if (step.TerritoryId == 212) // Waking Sands
                {
                    step.TargetTerritoryId = 212;
                    bool inTerritory = clientState.TerritoryType == 212;
                    if (!inTerritory)
                    {
                        yield return new Task(step, quest.Id, EAetheryteLocation.WesternThanalanHorizon, 140);
                        yield return new MoveTask(
                            TerritoryId: 140,
                            Destination: new(-492.96475f, 20.999884f, -380.82272f),
                            Fly: true);
                        yield return new MoveTask(
                            TerritoryId: 140,
                            Destination: new(-480.9181f, 18.00103f, -386.862f));
                        yield return new Interact.Task(2001711, quest, EInteractionType.Interact);
                    }
                    // if target position is in the solar, and we're either not in the territory yet or are not in the solar, interact to enter
                    if (step.Position != null && matchesCondition(EExtraSkipCondition.WakingSandsSolar, step.Position.Value) &&
                        (!inTerritory || objectTable[0] != null && !matchesCondition(EExtraSkipCondition.WakingSandsSolar, objectTable[0]!.Position)))
                    {
                        yield return new MoveTask(
                            TerritoryId: 212,
                            Destination: new(23.23944f, 2.090454f, -0.015319824f),
                            Sprint: true);
                        yield return new Interact.Task(2001715, quest, EInteractionType.Interact);
                    }
                    // if target is *not* in Solar and we are there, interact to leave
                    if (step.Position != null && !matchesCondition(EExtraSkipCondition.WakingSandsSolar, step.Position.Value) &&
                        inTerritory && objectTable[0] != null && matchesCondition(EExtraSkipCondition.WakingSandsSolar, objectTable[0]!.Position))
                    {
                        yield return new MoveTask(
                            TerritoryId: 212,
                            Destination: new(25.497803f, 2.090454f, -0.015319824f),
                            Sprint: true);
                        yield return new Interact.Task(2001717, quest, EInteractionType.Interact);
                    }
                }
                else if (step.TerritoryId == 351) // Rising Stones
                {
                    step.TargetTerritoryId = 351;
                    bool inTerritory = clientState.TerritoryType == 351;
                    if (!inTerritory)
                    {
                        yield return new Task(step, quest.Id, EAetheryteLocation.MorDhona, 156);
                        yield return new MoveTask(
                            TerritoryId: 156,
                            Destination: new(21.133728f, 22.323914f, -631.281f),
                            Sprint: true);
                        yield return new Interact.Task(2002881, quest, EInteractionType.Interact);
                        yield return new WaitAtEnd.WaitForTerritory(351);
                    }
                    // if target is in Solar and we are not currently there, interact to get there
                    if (step.Position != null && matchesCondition(EExtraSkipCondition.RisingStonesSolar, step.Position.Value) &&
                        (!inTerritory || objectTable[0] != null && !matchesCondition(EExtraSkipCondition.RisingStonesSolar, objectTable[0]!.Position)))
                    {
                        yield return new MoveTask(
                         TerritoryId: 351,
                         Destination: new(-0.015319824f, -1.0223389f, -26.779602f),
                            Sprint: true);
                        yield return new Interact.Task(2002878, quest, EInteractionType.Interact);
                    }
                    // if target is *not* in Solar and we are there, interact to leave
                    if (step.Position != null && !matchesCondition(EExtraSkipCondition.RisingStonesSolar, step.Position.Value) &&
                        inTerritory && objectTable[0] != null && matchesCondition(EExtraSkipCondition.RisingStonesSolar, objectTable[0]!.Position))
                    {
                        yield return new MoveTask(
                            TerritoryId: 351,
                            Destination: new(-0.015319824f, -1.0223389f, -29.251587f),
                            Sprint: true);
                        yield return new Interact.Task(2002880, quest, EInteractionType.Interact);
                    }
                }

                // Citystates
                else if (Territories.Skip(2).Take(4).Contains(step.TerritoryId))
                {
                    // of course there's an exception
                    var tId = step.TerritoryId != 128 ? step.TerritoryId - 1 : step.TerritoryId + 1;
                    EAetheryteLocation? tp = aetheryteData.NearestAetheryteTo(tId, new());
                    EAetheryteLocation? anD = new List<EAetheryteLocation?>() {
                        EAetheryteLocation.LimsaAftcastle,
                        EAetheryteLocation.UldahGoldsmith,
                        EAetheryteLocation.GridaniaAmphitheatre,
                        EAetheryteLocation.IshgardLastVigil
                    }.First(location => location?.Territory(aetheryteData) == step.TerritoryId) ?? null;
                    if (tp is { } teleportDest && anD is { } aethernetDest &&
                        !sequence.Steps.Any(step => step.TerritoryId != 128 ? step.TerritoryId == step.TerritoryId - 1 : step.TerritoryId == step.TerritoryId + 1) &&
                        clientState.TerritoryType != step.TerritoryId &&
                        step.AethernetShortcut == null)
                    {
                        yield return new Task(step, quest.Id, teleportDest, tId);
                        yield return new AethernetShortcut.Task(teleportDest, aethernetDest);
                    }
                }
                yield return new WaitAtEnd.WaitDelay();
            }
            else
            {
                yield return new Task(step, quest.Id, step.AetheryteShortcut.Value,
                    aetheryteData.TerritoryIds[step.AetheryteShortcut.Value]);
                yield return new WaitAtEnd.WaitDelay();

                if (MoveAwayFromAetheryteExecutor.AppliesTo(step.AetheryteShortcut.Value) &&
                    step.AethernetShortcut?.From != step.AetheryteShortcut.Value)
                {
                    yield return new WaitCondition.Task(
                        () => clientState.TerritoryType == aetheryteData.TerritoryIds[step.AetheryteShortcut.Value],
                        $"Wait(territory: {TerritoryData.GetNameAndId(aetheryteData.TerritoryIds[step.AetheryteShortcut.Value])})");
                    yield return new MoveAwayFromAetheryte(step.AetheryteShortcut.Value);
                }
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
        private uint _overrideExpectedTerritoryId;

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

            if (clientState.TerritoryType == _overrideExpectedTerritoryId || clientState.TerritoryType == Task.ExpectedTerritoryId)
                return ETaskResult.TaskComplete;

            return ETaskResult.StillRunning;
        }

        private bool ShouldSkipTeleport()
        {
            uint territoryType = clientState.TerritoryType;
            if (Task.Step is { } step)
            {
                if (Task.TargetAetheryte is EAetheryteLocation.None)
                {
                    if (ShouldSkipForMissingAetheryte(step, out EAetheryteLocation? shortcut))
                        return true;

                    Task.targetAetheryte = shortcut!.Value;
                }
                else
                    Task.targetAetheryte = Task.TargetAetheryte;

                SkipAetheryteCondition skipConditions = step.SkipConditions?.AetheryteShortcutIf ?? new();
                if (ShouldSkipForConditions(step, skipConditions, territoryType))
                    return true;

                if (IsDestinationTerritory(step, territoryType))
                {
                    bool? decision = ShouldSkipWithinDestinationTerritory(step, skipConditions, territoryType);
                    if (decision is { } shouldSkip)
                        return shouldSkip;
                }
            }

            if (gameFunctions.HasCharacterStatusPreventingMountOrSprint()) // Transporting
            {
                logger.LogInformation("Skipping aetheryte teleport, character is busy.");
                return true;
            }

            // quick hack, pls fix better
            if (Task.TargetAetheryte == EAetheryteLocation.MorDhona && Task.Step?.TerritoryId == 351 && clientState.TerritoryType == 351)
            {
                logger.LogInformation("Skipping aetheryte teleport, it shouldn't be doing that anyway");
                return true;
            }

            logger.LogInformation("Not skipping aetheryte teleport");
            return false;
        }

        private bool ShouldSkipForMissingAetheryte(QuestStep step, out EAetheryteLocation? shortcut)
        {
            EAetheryteLocation? nearest = step.Position != null
                ? aetheryteData.NearestAetheryteTo(step.TerritoryId, step.Position)
                : null;
            if (nearest == null && step.Position != null && step.AethernetShortcut is { } aethernetShortcut)
                nearest = aetheryteData.NearestAetheryteTo(
                    aetheryteData.TerritoryIds[aethernetShortcut.From], step.Position);
            nearest ??= aetheryteData.NearestAetheryteTo(step.TerritoryId, position: null);
            shortcut = step.AetheryteShortcut ?? nearest;

            if (shortcut == null)
            {
                logger.LogInformation("Skipping aetheryte shortcut, null result. step:{Step}, nearest:{Nearest}",
                    step.AetheryteShortcut, nearest);
                if (step.AethernetShortcut == null && clientState.TerritoryType != step.TerritoryId)
                    chatGui.PrintError("Questionable could not automatically find an unlocked aetheryte destination in " +
                        $"{TerritoryData.GetNameAndId(step.TerritoryId)}, waiting until you manually navigate there.",
                        CommandHandler.MessageTag, CommandHandler.TagColor);
                return true;
            }

            _overrideExpectedTerritoryId = aetheryteData.TerritoryIds[shortcut.Value];
            if (step.Mount is { } mount)
            {
                logger.LogInformation("Skipping aetheryte shortcut, Mount is set as {Mount}. step:{Step}, nearest:{Nearest}",
                    mount, step.AetheryteShortcut, nearest);
                return true;
            }

            if (step.Action is { } action && action.RequiresMount())
            {
                logger.LogInformation("Skipping aetheryte shortcut, step action requires mount. step:{Step}, nearest:{Nearest}",
                    step.AetheryteShortcut, nearest);
                return true;
            }

            if (step.AethernetShortcut is { } aethernet &&
                aetheryteData.TerritoryIds[aethernet.To] != clientState.TerritoryType)
            {
                logger.LogInformation("Not skipping aetheryte shortcut, aethernet destination is diff territory. step:{Step}, nearest:{Nearest}",
                    step.AetheryteShortcut, nearest);
            }

            logger.LogInformation("Aetheryte target has been changed to {TargetAetheryte}", shortcut.Value);
            return false;
        }

        private bool ShouldSkipForConditions(QuestStep step, SkipAetheryteCondition skipConditions, uint territoryType)
        {
            if (skipConditions.Never)
                return false;

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
                logger.LogInformation("Skipping aetheryte shortcut, all prequisite quests are accepted");
                return true;
            }

            if (skipConditions.AetheryteLocked is { } locked && !aetheryteFunctions.IsAetheryteUnlocked(locked))
            {
                logger.LogInformation("Skipping aetheryte teleport due to SkipCondition (AetheryteLocked)");
                return true;
            }

            if (skipConditions.AetheryteUnlocked is { } unlocked && aetheryteFunctions.IsAetheryteUnlocked(unlocked))
            {
                logger.LogInformation("Skipping aetheryte teleport due to SkipCondition (AetheryteUnlocked)");
                return true;
            }

            if (skipConditions.RequiredQuestVariablesNotMet && Task.ElementId is { } elementId)
            {
                QuestProgressInfo? questWork = QuestFunctions.GetQuestProgressInfo(elementId);
                if (questWork != null &&
                    !QuestWorkUtils.MatchesRequiredQuestWorkConfig(step.RequiredQuestVariables, questWork, logger))
                {
                    logger.LogInformation("Skipping aetheryte teleport, as required variables do not match");
                    return true;
                }
            }

            if (IsNearPosition(skipConditions.NearPosition, maxDistance: true))
            {
                logger.LogInformation("Skipping aetheryte shortcut, as we're near the position");
                return true;
            }

            if (IsNearPosition(skipConditions.NotNearPosition, maxDistance: false))
            {
                logger.LogInformation("Skipping aetheryte shortcut, as we're not near the position");
                return true;
            }

            if (ShouldSkipForItemCondition(step, skipConditions))
                return true;

            if (skipConditions.ExtraCondition is { } extraCondition && extraCondition != EExtraSkipCondition.None &&
                extraConditionUtils.MatchesExtraCondition(extraCondition))
            {
                logger.LogInformation("Skipping aetheryte shortcut, extra condition {ExtraCondition} matches", extraCondition);
                return true;
            }

            return false;
        }

        private bool IsNearPosition(NearPositionCondition? positionCondition, bool maxDistance)
        {
            if (positionCondition is not { } condition || clientState.TerritoryType != condition.TerritoryId)
                return false;

            float distance = Vector3.Distance(condition.Position, objectTable[0]!.Position);
            return maxDistance ? distance <= condition.MaximumDistance : condition.MaximumDistance <= distance;
        }

        private unsafe bool ShouldSkipForItemCondition(QuestStep step, SkipAetheryteCondition skipConditions)
        {
            if (skipConditions.Item is not { } itemCondition || step.ItemId is not { } itemId)
                return false;

            InventoryManager* inventoryManager = InventoryManager.Instance();
            int itemCount = inventoryManager->GetInventoryItemCount(itemId, isHq: false, checkEquipped: false)
                            + inventoryManager->GetInventoryItemCount(itemId, isHq: true, checkEquipped: false);
            if (itemCount == 0 && itemCondition.NotInInventory)
            {
                logger.LogInformation("Skipping aetheryte shortcut, no item with itemId {ItemId} in inventory", itemId);
                return true;
            }

            if (itemCount > 0 && !itemCondition.NotInInventory)
            {
                logger.LogInformation("Skipping aetheryte shortcut, item with itemId {ItemId} in inventory", itemId);
                return true;
            }

            return false;
        }

        private bool IsDestinationTerritory(QuestStep step, uint territoryType) =>
            Task.ExpectedTerritoryId == territoryType ||
            (step.AethernetShortcut is { } aethernet && aetheryteData.TerritoryIds[aethernet.To] == territoryType);

        // Null leaves the decision to the remaining checks; false explicitly requests teleporting.
        private bool? ShouldSkipWithinDestinationTerritory(
            QuestStep step,
            SkipAetheryteCondition skipConditions,
            uint territoryType)
        {
            if (!aetheryteFunctions.IsTeleportUnlocked())
            {
                logger.LogInformation("Skipping aetheryte teleport, teleport isn't unlocked yet and we're already in the zone");
                return true;
            }

            if (skipConditions.Never)
                return null;

            if (skipConditions.InSameTerritory)
                logger.LogDebug("Ignoring InSameTerritory, obsolete due to distance check");

            Vector3 playerPosition = objectTable[0]!.Position;
            if (step.Position is not { } targetPosition)
                return ShouldSkipWhenStepHasNoPosition(step, playerPosition, territoryType);

            return ShouldSkipWhenStepHasPosition(step, playerPosition, targetPosition, territoryType) ? true : null;
        }

        private bool ShouldSkipWhenStepHasNoPosition(QuestStep step, Vector3 playerPosition, uint territoryType)
        {
            if (aetheryteData.CalculateDistance(playerPosition, territoryType, Task.targetAetheryte) < 100)
            {
                logger.LogInformation("Skipping aetheryte teleport, we're already there");
                return true;
            }

            if (step.InteractionType == EInteractionType.AttuneAetheryte ||
                !aetheryteFunctions.IsAetheryteUnlocked(Task.targetAetheryte))
            {
                logger.LogInformation("AttuneAetheryte, proceeding to destination");
                return true;
            }

            logger.LogInformation("No step position, teleporting to aetheryte");
            return false;
        }

        private bool ShouldSkipWhenStepHasPosition(
            QuestStep step,
            Vector3 playerPosition,
            Vector3 targetPosition,
            uint territoryType)
        {
            float directDistance = Vector3.Distance(playerPosition, targetPosition);
            float aetheryteDistance = aetheryteData.CalculateDistance(targetPosition, territoryType, Task.targetAetheryte);
            if (directDistance < step.CalculateActualStopDistance())
            {
                logger.LogInformation("Skipping aetheryte shortcut, we're near the target");
                return true;
            }

            const uint teleportTimeDistance = 90;
            float aethernetDistance = step.AethernetShortcut is { } aethernet
                ? aetheryteData.CalculateDistance(targetPosition, territoryType, aethernet.To)
                : float.PositiveInfinity;

            if (step.AethernetShortcut != null)
            {
                logger.LogDebug(
                    "target direct: {DirectDistance}. target if tp: {TpDistance} target direct XZ: {DirectXZ}. target tp XZ: {TpXZ}, target if aethernet: {AethernetDistance}",
                    directDistance, teleportTimeDistance + aetheryteDistance,
                    playerPosition.DistanceTo_XZ(targetPosition),
                    targetPosition.DistanceTo_XZ(Task.targetAetheryte.Position(aetheryteData)),
                    aethernetDistance + teleportTimeDistance);
                if (directDistance < aethernetDistance + teleportTimeDistance)
                {
                    logger.LogInformation("Skipping aethernet teleport, it's a shorter distance to walk there");
                    return true;
                }
            }
            else
            {
                logger.LogDebug(
                    "target direct: {DirectDistance}. target if tp: {TpDistance} target direct XZ: {DirectXZ}. target tp XZ: {TpXZ}",
                    directDistance, teleportTimeDistance + aetheryteDistance,
                    playerPosition.DistanceTo_XZ(targetPosition),
                    targetPosition.DistanceTo_XZ(Task.targetAetheryte.Position(aetheryteData)));
                if (directDistance < teleportTimeDistance + aetheryteDistance)
                {
                    logger.LogInformation("Skipping aetheryte shortcut, it's a shorter distance to walk there");
                    return true;
                }
            }

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
                //chatGui.PrintError($"Aetheryte {Task.targetAetheryte} is not unlocked.", CommandHandler.MessageTag, CommandHandler.TagColor);
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

            //chatGui.Print("Unable to teleport to aetheryte.", CommandHandler.MessageTag, CommandHandler.TagColor);
            throw new TaskException("Unable to teleport to aetheryte");
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
                closestPoint, Mount: false, 0.25f, DisableNavmesh: true,
                InteractionType: EInteractionType.None, RestartNavigation: false);
            return moveExecutor.Start(task);
        }

        public override ETaskResult Update() => moveExecutor.Update();

        public override bool ShouldInterruptOnDamage() => true;
    }
}
