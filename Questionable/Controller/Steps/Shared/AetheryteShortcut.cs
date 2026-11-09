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

    // Limsa Upper Decks (128) pairs with Lower Decks (129); the other city parts pair with the zone before them.
    // Teleporting would leave the zone the step happens in, e.g. Mor Dhona from inside The Rising Stones.
    internal static bool IsAlreadyInStepTerritory(uint playerTerritory, uint stepTerritory, uint teleportTerritory) =>
        playerTerritory == stepTerritory && teleportTerritory != stepTerritory;

    internal static uint CityNeighbourTerritory(uint territoryId) => territoryId != 128 ? territoryId - 1 : territoryId + 1;

    // A sequence that also visits the neighbouring part walks between them instead.
    internal static bool ShouldEnterByAethernet(QuestSequence sequence, QuestStep step, uint currentTerritory) =>
        !sequence.Steps.Any(x => x.TerritoryId == CityNeighbourTerritory(step.TerritoryId)) &&
        currentTerritory != step.TerritoryId &&
        step.AethernetShortcut == null;
    // TODO: refactor the nested territory-specific routing in this factory after SmartNav coverage is established.
    internal sealed class Factory(
        AetheryteData aetheryteData,
        IClientState clientState,
        IObjectTable objectTable,
        ExtraConditionUtils extraConditionUtils,
        IEnumerable<IOperationProvider> operationProviders,
        ILogger<AetheryteShortcut.Factory> logger)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (ProviderTravel.IsHandled(sequence, step, operationProviders))
                yield break;

            if (step.AetheryteShortcut == null)
            {
                logger.LogTrace(step.InteractionType.ToString());
                if (step.InteractionType is EInteractionType.EquipRecommended or EInteractionType.Gather)
                    yield break;
                if (step.TerritoryId == 1) // unused territory ID, used to disable auto teleports as a gross hack
                    yield break;
                bool matches(EExtraSkipCondition condition, Vector3 position) =>
                    extraConditionUtils.Matches(condition, position, step.TerritoryId);
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
                    if (step.Position != null && matches(EExtraSkipCondition.WakingSandsSolar, step.Position.Value) &&
                        (!inTerritory || objectTable[0] != null && !matches(EExtraSkipCondition.WakingSandsSolar, objectTable[0]!.Position)))
                    {
                        yield return new MoveTask(
                            TerritoryId: 212,
                            Destination: new(23.23944f, 2.090454f, -0.015319824f),
                            Sprint: true);
                        yield return new Interact.Task(2001715, quest, EInteractionType.Interact);
                    }
                    if (step.Position != null && !matches(EExtraSkipCondition.WakingSandsSolar, step.Position.Value) &&
                        inTerritory && objectTable[0] != null && matches(EExtraSkipCondition.WakingSandsSolar, objectTable[0]!.Position))
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
                        yield return new WaitAtEnd.TerritoryEntered(351);
                    }
                    if (step.Position != null && matches(EExtraSkipCondition.RisingStonesSolar, step.Position.Value) &&
                        (!inTerritory || objectTable[0] != null && !matches(EExtraSkipCondition.RisingStonesSolar, objectTable[0]!.Position)))
                    {
                        yield return new MoveTask(
                         TerritoryId: 351,
                         Destination: new(-0.015319824f, -1.0223389f, -26.779602f),
                            Sprint: true);
                        yield return new Interact.Task(2002878, quest, EInteractionType.Interact);
                    }
                    if (step.Position != null && !matches(EExtraSkipCondition.RisingStonesSolar, step.Position.Value) &&
                        inTerritory && objectTable[0] != null && matches(EExtraSkipCondition.RisingStonesSolar, objectTable[0]!.Position))
                    {
                        yield return new MoveTask(
                            TerritoryId: 351,
                            Destination: new(-0.015319824f, -1.0223389f, -29.251587f),
                            Sprint: true);
                        yield return new Interact.Task(2002880, quest, EInteractionType.Interact);
                    }
                }

                else if (Territories.Skip(2).Take(4).Contains(step.TerritoryId))
                {
                    var neighbourId = CityNeighbourTerritory(step.TerritoryId);
                    EAetheryteLocation? nearest = aetheryteData.NearestTo(neighbourId, new());
                    EAetheryteLocation? aethernetTarget = new List<EAetheryteLocation?>() {
                        EAetheryteLocation.LimsaAftcastle,
                        EAetheryteLocation.UldahGoldsmith,
                        EAetheryteLocation.GridaniaAmphitheatre,
                        EAetheryteLocation.IshgardLastVigil
                    }.First(location => location?.Territory(aetheryteData) == step.TerritoryId) ?? null;
                    if (nearest is { } teleportDest && aethernetTarget is { } aethernetDest &&
                        ShouldEnterByAethernet(sequence, step, clientState.TerritoryType))
                    {
                        yield return new Task(step, quest.Id, teleportDest, neighbourId);
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

                if (MoveAwayExecutor.AppliesTo(step.AetheryteShortcut.Value) &&
                    step.AethernetShortcut?.From != step.AetheryteShortcut.Value)
                {
                    yield return new WaitCondition.Task(
                        () => clientState.TerritoryType == aetheryteData.TerritoryIds[step.AetheryteShortcut.Value],
                        $"Wait(territory: {TerritoryData.GetNameAndId(aetheryteData.TerritoryIds[step.AetheryteShortcut.Value])})");
                    yield return new MoveAway(step.AetheryteShortcut.Value);
                }
            }
        }
    }

    // A following aethernet shortcut can use a territory other than the step's.
    internal sealed record Task
    (
        QuestStep? Step,
        ElementId? ElementId,
        EAetheryteLocation TargetAetheryte,
        uint ExpectedTerritoryId) : ISkippableTask
    {
        internal EAetheryteLocation ResolvedAetheryte = TargetAetheryte;
        public override string ToString() => $"UseAetheryte({ResolvedAetheryte})";
    }

    internal sealed class Executor
    (
        ILogger<Executor> logger,
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
        private uint _overrideTerritoryId;

        internal bool HasTeleported => _teleported;

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

            if (clientState.TerritoryType == _overrideTerritoryId || clientState.TerritoryType == Task.ExpectedTerritoryId)
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
                    if (ShouldSkipNoTarget(step, out EAetheryteLocation? shortcut))
                        return true;

                    Task.ResolvedAetheryte = shortcut!.Value;
                }
                else
                    Task.ResolvedAetheryte = Task.TargetAetheryte;

                SkipAetheryteCondition skipConditions = step.SkipConditions?.AetheryteShortcutIf ?? new();
                if (ShouldSkipForConditions(step, skipConditions, territoryType))
                    return true;

                if (IsDestinationTerritory(step, territoryType))
                {
                    bool? decision = ShouldSkipInDestination(step, skipConditions, territoryType);
                    if (decision is { } shouldSkip)
                        return shouldSkip;
                }

                if (!skipConditions.Never && IsAlreadyInStepTerritory(territoryType, step.TerritoryId, Task.ExpectedTerritoryId))
                {
                    logger.LogInformation("Skipping aetheryte teleport, we're already in the step's zone and the aetheryte is in another");
                    return true;
                }
            }

            if (gameFunctions.IsMountOrSprintBlocked()) // Transporting
            {
                logger.LogInformation("Skipping aetheryte teleport, character is busy.");
                return true;
            }

            logger.LogInformation("Not skipping aetheryte teleport");
            return false;
        }

        private bool ShouldSkipNoTarget(QuestStep step, out EAetheryteLocation? shortcut)
        {
            EAetheryteLocation? nearest = step.Position != null
                ? aetheryteData.NearestTo(step.TerritoryId, step.Position)
                : null;
            if (nearest == null && step.Position != null && step.AethernetShortcut is { } aethernetShortcut)
                nearest = aetheryteData.NearestTo(
                    aetheryteData.TerritoryIds[aethernetShortcut.From], step.Position);
            nearest ??= aetheryteData.NearestTo(step.TerritoryId, position: null);
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

            _overrideTerritoryId = aetheryteData.TerritoryIds[shortcut.Value];
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
                QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(elementId);
                if (questWork != null &&
                    !QuestWorkUtils.MatchesRequired(step.RequiredQuestVariables, questWork, logger))
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

            if (ShouldSkipForItem(step, skipConditions))
                return true;

            if (skipConditions.ExtraCondition is { } extraCondition && extraCondition != EExtraSkipCondition.None &&
                extraConditionUtils.Matches(extraCondition))
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

        private unsafe bool ShouldSkipForItem(QuestStep step, SkipAetheryteCondition skipConditions)
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
        private bool? ShouldSkipInDestination(
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
                return ShouldSkipNoPosition(step, playerPosition, territoryType);

            return ShouldSkipWithPosition(step, playerPosition, targetPosition, territoryType) ? true : null;
        }

        private bool ShouldSkipNoPosition(QuestStep step, Vector3 playerPosition, uint territoryType)
        {
            if (aetheryteData.CalculateDistance(playerPosition, territoryType, Task.ResolvedAetheryte) < 100)
            {
                logger.LogInformation("Skipping aetheryte teleport, we're already there");
                return true;
            }

            if (step.InteractionType == EInteractionType.AttuneAetheryte ||
                !aetheryteFunctions.IsAetheryteUnlocked(Task.ResolvedAetheryte))
            {
                logger.LogInformation("AttuneAetheryte, proceeding to destination");
                return true;
            }

            logger.LogInformation("No step position, teleporting to aetheryte");
            return false;
        }

        private bool ShouldSkipWithPosition(
            QuestStep step,
            Vector3 playerPosition,
            Vector3 targetPosition,
            uint territoryType)
        {
            float directDistance = Vector3.Distance(playerPosition, targetPosition);
            float aetheryteDistance = aetheryteData.CalculateDistance(targetPosition, territoryType, Task.ResolvedAetheryte);
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
                    playerPosition.DistanceToXZ(targetPosition),
                    targetPosition.DistanceToXZ(Task.ResolvedAetheryte.Position(aetheryteData)),
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
                    playerPosition.DistanceToXZ(targetPosition),
                    targetPosition.DistanceToXZ(Task.ResolvedAetheryte.Position(aetheryteData)));
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
            if (!aetheryteFunctions.CanTeleport(Task.ResolvedAetheryte))
            {
                if (!aetheryteFunctions.IsTeleportUnlocked())
                    throw new TaskException("Teleport is not unlocked, attune to any aetheryte first.");

                _continueAt = DateTime.Now.AddSeconds(1);
                logger.LogTrace("Waiting for teleport cooldown...");
                return false;
            }

            _continueAt = DateTime.Now.AddSeconds(8);

            if (!aetheryteFunctions.IsAetheryteUnlocked(Task.ResolvedAetheryte))
            {
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
                    aetheryteFunctions.TeleportAetheryte(Task.ResolvedAetheryte));
            if (ProgressContext != null)
            {
                logger.LogInformation("Travelling via aetheryte...");
                return true;
            }

            throw new TaskException("Unable to teleport to aetheryte");
        }

        public override bool WasInterrupted() => condition[ConditionFlag.InCombat] || base.WasInterrupted();

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record MoveAway(EAetheryteLocation TargetAetheryte) : ITask
    {
        public override string ToString() => $"MoveAway({TargetAetheryte})";
    }

    internal sealed class MoveAwayExecutor
    (
        MoveExecutor moveExecutor,
        AetheryteData aetheryteData,
        IClientState clientState,
        IObjectTable objectTable) : TaskExecutor<MoveAway>
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
