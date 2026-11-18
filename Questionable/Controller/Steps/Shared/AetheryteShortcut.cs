using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller.Steps.Movement;
namespace Questionable.Controller.Steps.Shared;

internal static class AetheryteShortcut
{
    public static HashSet<uint> Territories = [212, 351, 128, 131, 133, 419];

    private const uint WesternThanalan = 140;
    private const uint WakingSands = 212;
    internal static readonly Vector3 VesperBayLanding = new(-408.92343f, 23.167036f, -351.16223f);

    // Vesper Bay has no aetheryte and Horizon is a long walk from it; inside the zone the usual checks decide.
    internal static bool TakesVesperBayTicket(EAetheryteLocation aetheryte, uint playerTerritory,
        uint stepTerritory, Vector3? target, float horizonDistance, int tickets) =>
        aetheryte == EAetheryteLocation.WesternThanalanHorizon && tickets > 0 &&
        playerTerritory is not (WesternThanalan or WakingSands) &&
        (stepTerritory == WakingSands ||
         stepTerritory == WesternThanalan && target is { } position &&
         Vector3.Distance(position, VesperBayLanding) < horizonDistance);

    // Limsa Upper Decks (128) pairs with Lower Decks (129); the other city parts pair with the zone before them.
    // Teleporting would leave the zone the step happens in, e.g. Mor Dhona from inside The Rising Stones.
    internal static bool IsAlreadyInStepTerritory(uint playerTerritory, uint stepTerritory, uint teleportTerritory) =>
        playerTerritory == stepTerritory && teleportTerritory != stepTerritory;

    internal sealed class Factory(
        AetheryteData aetheryteData,
        IClientState clientState,
        IEnumerable<IOperationProvider> operationProviders,
        TerritoryData territoryData)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (ProviderTravel.IsHandled(sequence, step, operationProviders))
                yield break;

            if (Travel.CanTravel(step, clientState.TerritoryType, territoryData) &&
                (step.Position != null || clientState.TerritoryType != step.TerritoryId))
            {
                if (step.Position == null && (step.AetheryteShortcut != null || Territories.Contains(step.TerritoryId)))
                    yield return new Travel.Task(step.TerritoryId, null, null);
                yield break;
            }

            if (step.AetheryteShortcut == null)
                yield break;

            yield return new Task(step, quest.Id, step.AetheryteShortcut.Value,
                aetheryteData.TerritoryIds[step.AetheryteShortcut.Value]);
            yield return new WaitAfterTeleport();

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

    internal sealed record WaitAfterTeleport : ITask
    {
        public override string ToString() => "Wait(after teleport)";
    }

    internal sealed class WaitAfterTeleportExecutor : DelayedTaskExecutor<WaitAfterTeleport>
    {
        internal static bool LastTeleported { get; set; }

        protected override bool StartInternal()
        {
            Delay = TimeSpan.FromMilliseconds(750);
            return LastTeleported;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    // A following aethernet shortcut can use a territory other than the step's.
    internal sealed record Task
    (
        QuestStep? Step,
        ElementId? ElementId,
        EAetheryteLocation TargetAetheryte,
        uint ExpectedTerritoryId) : ISkippableTask
    {
        public override string ToString() => $"UseAetheryte({TargetAetheryte})";
    }

    internal sealed class Executor
    (
        ILogger<Executor> logger,
        AetheryteFunctions aetheryteFunctions,
        QuestFunctions questFunctions,
        GameFunctions gameFunctions,
        IClientState clientState,
        IObjectTable objectTable,
        ICondition condition,
        AetheryteData aetheryteData,
        ExtraConditionUtils extraConditionUtils,
        QuestRegistry questRegistry) : TaskExecutor<Task>, ITaskExecutor.IExtraTaskCreator
    {
        private const int TicketTries = 3;
        private DateTime _continueAt;
        private bool _teleported;
        private bool _byTicket;
        private int _ticketTries;
        private bool _societyPause;

        internal bool HasTeleported => _teleported;

        protected override bool Start()
        {
            WaitAfterTeleportExecutor.LastTeleported = false;
            if (ShouldSkipTeleport())
                return false;

            _byTicket = TakesTicket();
            _ticketTries = 0;
            return true;
        }

        private unsafe bool TakesTicket()
        {
            if (Task.Step is not { } step)
                return false;

            float horizonDistance = step.Position is { } position
                ? aetheryteData.CalculateDistance(position, step.TerritoryId, EAetheryteLocation.WesternThanalanHorizon)
                : float.MaxValue;
            int tickets = InventoryManager.Instance()->GetInventoryItemCount(QuestStep.VesperBayAetheryteTicket);
            return TakesVesperBayTicket(Task.TargetAetheryte, clientState.TerritoryType, step.TerritoryId,
                step.Position, horizonDistance, tickets);
        }

        public IEnumerable<ITask> CreateExtraTasks() =>
        [
            new MoveTask(
                TerritoryId: WesternThanalan,
                VesperBayLanding,
                StopDistance: 0.25f,
                DataId: null,
                InteractionType: EInteractionType.WalkTo)
        ];

        public override ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            if (!_teleported)
            {
                _teleported = DoTeleport();
                WaitAfterTeleportExecutor.LastTeleported = _teleported;
                return ETaskResult.StillRunning;
            }

            if (clientState.TerritoryType == Task.ExpectedTerritoryId)
                return _byTicket ? ETaskResult.CreateNewTasks : ETaskResult.TaskComplete;

            return ETaskResult.StillRunning;
        }

        private bool ShouldSkipTeleport()
        {
            uint territoryType = clientState.TerritoryType;
            if (Task.Step is { } step)
            {
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
            if (aetheryteData.CalculateDistance(playerPosition, territoryType, Task.TargetAetheryte) < 100)
            {
                logger.LogInformation("Skipping aetheryte teleport, we're already there");
                return true;
            }

            if (step.InteractionType == EInteractionType.AttuneAetheryte ||
                !aetheryteFunctions.IsAetheryteUnlocked(Task.TargetAetheryte))
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
            float aetheryteDistance = aetheryteData.CalculateDistance(targetPosition, territoryType, Task.TargetAetheryte);
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
                    targetPosition.DistanceToXZ(Task.TargetAetheryte.Position(aetheryteData)),
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
                    targetPosition.DistanceToXZ(Task.TargetAetheryte.Position(aetheryteData)));
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
            if (_byTicket && UseTicket() is { } used)
                return used;

            if (!aetheryteFunctions.CanTeleport(Task.TargetAetheryte))
            {
                if (!aetheryteFunctions.IsTeleportUnlocked())
                    throw new NeedsPlayerException("Teleport is not unlocked, attune to any aetheryte first.");

                _continueAt = DateTime.Now.AddSeconds(1);
                logger.LogTrace("Waiting for teleport cooldown...");
                return false;
            }

            _continueAt = DateTime.Now.AddSeconds(8);

            if (!aetheryteFunctions.IsAetheryteUnlocked(Task.TargetAetheryte))
            {
                throw new NeedsPlayerException("Aetheryte is not unlocked");
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
                    aetheryteFunctions.TeleportAetheryte(Task.TargetAetheryte));
            if (ProgressContext != null)
            {
                logger.LogInformation("Travelling via aetheryte...");
                return true;
            }

            throw new TaskException("Unable to teleport to aetheryte");
        }

        // Null falls back to teleporting to Horizon.
        private bool? UseTicket()
        {
            ProgressContext = InteractionProgressContext.FromActionUseOrDefault(() =>
                gameFunctions.UseItem(QuestStep.VesperBayAetheryteTicket));
            if (ProgressContext != null)
            {
                _continueAt = DateTime.Now.AddSeconds(8);
                logger.LogInformation("Travelling via Vesper Bay ticket...");
                return true;
            }

            if (++_ticketTries < TicketTries)
            {
                _continueAt = DateTime.Now.AddSeconds(1);
                return false;
            }

            logger.LogInformation("Vesper Bay ticket couldn't be used, teleporting to Horizon instead");
            _byTicket = false;
            return null;
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
                closestPoint, 0.25f);
            return moveExecutor.Start(task);
        }

        public override ETaskResult Update() => moveExecutor.Update();

        public override bool ShouldInterruptOnDamage() => true;
    }
}
