using Dalamud.Game.ClientState.Conditions;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Common;
using Questionable.Model.Common.Converter;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

// TODO: refactor — heavy nesting (20 lines indented ≥6 levels, max indent 11 levels).
internal static class AethernetShortcut
{
    internal sealed class Factory
    (
        AetheryteData aetheryteData,
        IClientState clientState)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.AethernetShortcut == null)
                yield break;

            yield return new WaitNavmesh.Task();
            yield return new Task(step.AethernetShortcut.From, step.AethernetShortcut.To,
                step.SkipConditions?.AethernetShortcutIf ?? new());

            if (AetheryteShortcut.MoveAwayFromAetheryteExecutor.AppliesTo(step.AethernetShortcut.To))
            {
                yield return new WaitCondition.Task(
                    () => clientState.TerritoryType == aetheryteData.TerritoryIds[step.AethernetShortcut.To],
                    $"Wait(territory: {TerritoryData.GetNameAndId(aetheryteData.TerritoryIds[step.AethernetShortcut.To])})");
                yield return new AetheryteShortcut.MoveAwayFromAetheryte(step.AethernetShortcut.To);
            }
        }
    }

    internal sealed record Task
    (
        EAetheryteLocation From,
        EAetheryteLocation To,
        SkipAetheryteCondition SkipConditions) : ISkippableTask
    {
        public Task(EAetheryteLocation from,
            EAetheryteLocation to)
            : this(from, to, new())
        {
        }

        public override string ToString() => $"UseAethernet({From} -> {To})";
    }

    internal sealed class UseAethernetShortcut
    (
        ILogger<UseAethernetShortcut> logger,
        AetheryteFunctions aetheryteFunctions,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        IClientState clientState,
        IObjectTable objectTable,
        AetheryteData aetheryteData,
        TerritoryData territoryData,
        LifestreamIpc lifestreamIpc,
        MovementController movementController,
        ICondition condition) : TaskExecutor<Task>
    {
        private DateTime _continueAt = DateTime.MinValue;
        private bool _moving;
        private bool _teleported;
        private bool _triedMounting;

        protected override bool Start()
        {
            if (!Task.SkipConditions.Never)
            {
                if (Task.SkipConditions.InSameTerritory &&
                    clientState.TerritoryType == aetheryteData.TerritoryIds[Task.To])
                {
                    logger.LogInformation("Skipping aethernet shortcut because the target is in the same territory");
                    return false;
                }

                if (Task.SkipConditions.InTerritory.Contains(clientState.TerritoryType))
                {
                    logger.LogInformation(
                        "Skipping aethernet shortcut because the target is in the specified territory");
                    return false;
                }

                if (Task.SkipConditions.QuestsCompleted.Count > 0 &&
                    Task.SkipConditions.QuestsCompleted.All(questFunctions.IsQuestComplete))
                {
                    logger.LogInformation("Skipping aethernet shortcut, all prequisite quests are complete");
                    return true;
                }

                if (Task.SkipConditions.QuestsAccepted.Count > 0 &&
                    Task.SkipConditions.QuestsAccepted.All(questFunctions.IsQuestAccepted))
                {
                    logger.LogInformation("Skipping aethernet shortcut, all prequisite quests are accepted");
                    return true;
                }

                if (Task.SkipConditions.AetheryteLocked != null &&
                    !aetheryteFunctions.IsAetheryteUnlocked(Task.SkipConditions.AetheryteLocked.Value))
                {
                    logger.LogInformation("Skipping aethernet shortcut because the target aetheryte is locked");
                    return false;
                }

                if (Task.SkipConditions.AetheryteUnlocked != null &&
                    aetheryteFunctions.IsAetheryteUnlocked(Task.SkipConditions.AetheryteUnlocked.Value))
                {
                    logger.LogInformation("Skipping aethernet shortcut because the target aetheryte is unlocked");
                    return false;
                }
                if (Task.SkipConditions.InTerritory.Contains(clientState.TerritoryType))
                {
                    logger.LogInformation("Skipping aethernet teleport due to SkipCondition (InTerritory)");
                    return false;
                }
            }

            if (aetheryteFunctions.IsAetheryteUnlocked(Task.From) &&
                aetheryteFunctions.IsAetheryteUnlocked(Task.To))
            {
                uint territoryType = clientState.TerritoryType;
                Vector3 playerPosition = objectTable[0]!.Position;

                // closer to the source
                if (aetheryteData.CalculateDistance(playerPosition, territoryType, Task.From) <
                    aetheryteData.CalculateDistance(playerPosition, territoryType, Task.To))
                {
                    if (aetheryteData.CalculateDistance(playerPosition, territoryType, Task.From) <
                        (Task.From.IsFirmamentAetheryte() ? 11f : 4f))
                    {
                        DoTeleport();
                        return true;
                    }

                    if (Task.From == EAetheryteLocation.SolutionNine)
                    {
                        logger.LogInformation("Moving to S9 aetheryte");
                        List<Vector3> nearbyPoints =
                        [
                            new(0, 8.442986f, 9),
                            new(9, 8.442986f, 0),
                            new(-9, 8.442986f, 0),
                            new(0, 8.442986f, -9)
                        ];

                        Vector3 closestPoint = nearbyPoints.MinBy(x => Vector3.Distance(playerPosition, x));
                        _moving = true;
                        movementController.NavigateTo(EMovementType.Quest, (uint)Task.From, closestPoint, new()
                        {
                            Sprint = true,
                            StopDistance = 0.25f,
                        });
                        return true;
                    }

                    if (territoryData.CanUseMount(territoryType) &&
                        aetheryteData.CalculateDistance(playerPosition, territoryType, Task.From) > 30 &&
                        !gameFunctions.HasStatusPreventingMount())
                    {
                        _triedMounting = gameFunctions.Mount();
                        if (_triedMounting)
                        {
                            _continueAt = DateTime.Now.AddSeconds(0.5);
                            return true;
                        }
                    }
                    if (Task.To is EAetheryteLocation.UldahAirship && territoryType == 130 && playerPosition.Y > 80)
                    {
                        logger.LogInformation("Skipping aethernet teleport, already in Uldah airship landing");
                        return false;
                    }
                    if (Task.To is EAetheryteLocation.LimsaAirship && territoryType == 128 && playerPosition.Y > 90)
                    {
                        logger.LogInformation("Skipping aethernet teleport, already in Limsa airship landing");
                        return false;
                    }

                    MoveTo();
                    return true;
                }
            }
            else if (clientState.TerritoryType == aetheryteData.TerritoryIds[Task.To])
            {
                logger.LogWarning(
                    "Aethernet shortcut not unlocked (from: {FromAetheryte}, to: {ToAetheryte}), skipping as we are already in the destination territory",
                    Task.From, Task.To);
            }
            else
                throw new TaskException($"Aethernet shortcut not unlocked (from: {Task.From}, to: {Task.To})");

            return false;
        }

        private void MoveTo()
        {
            logger.LogInformation("Moving to aethernet shortcut");
            _moving = true;
            float distance = Task.From switch
            {
                var _ when Task.From.IsFirmamentAetheryte() => 4.4f,
                var _ when AetheryteConverter.IsLargeAetheryte(Task.From) => 10.9f,
                var _ => 5f
            };

            bool goldSaucerAethernetShard = aetheryteData.IsGoldSaucerAetheryte(Task.From) &&
                                            !AetheryteConverter.IsLargeAetheryte(Task.From);
            movementController.NavigateTo(EMovementType.Quest, (uint)Task.From, aetheryteData.Locations[Task.From], new()
            {
                Sprint = true,
                StopDistance = distance,
                VerticalStopDistance = goldSaucerAethernetShard ? 5f : null,
            });
        }

        private void DoTeleport()
        {
            logger.LogInformation("Using lifestream to teleport to {Destination}", Task.To);
            lifestreamIpc.Teleport(Task.To);
            _teleported = true;
        }

        public override ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            if (_triedMounting)
            {
                if (condition[ConditionFlag.Mounted])
                {
                    _triedMounting = false;
                    MoveTo();
                    return ETaskResult.StillRunning;
                }

                return ETaskResult.StillRunning;
            }

            if (_moving)
            {
                DateTime movementStartedAt = movementController.MovementStartedAt;
                if (movementStartedAt == DateTime.MaxValue || movementStartedAt.AddSeconds(2) >= DateTime.Now)
                    return ETaskResult.StillRunning;

                if (!movementController.IsPathfinding && !movementController.IsPathRunning)
                    _moving = false;

                return ETaskResult.StillRunning;
            }

            if (!_teleported)
            {
                DoTeleport();
                return ETaskResult.StillRunning;
            }

            if (objectTable[0] == null)
                return ETaskResult.StillRunning;
            Vector3? position = objectTable[0]!.Position;
            if (position == null)
                return ETaskResult.StillRunning;

            if (aetheryteData.IsAirshipLanding(Task.To))
            {
                if (aetheryteData.CalculateAirshipLandingDistance(position.Value, clientState.TerritoryType, Task.To) > 5)
                    return ETaskResult.StillRunning;
            }
            else if (aetheryteData.IsCityAetheryte(Task.To) || aetheryteData.IsGoldSaucerAetheryte(Task.To))
            {
                if (aetheryteData.CalculateDistance(position.Value, clientState.TerritoryType, Task.To) > 20)
                    return ETaskResult.StillRunning;
            }
            else
            {
                // some overworld location (e.g. 'Tesselation (Lakeland)' would end up here
                if (clientState.TerritoryType != aetheryteData.TerritoryIds[Task.To])
                    return ETaskResult.StillRunning;
            }


            return ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
