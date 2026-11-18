using Questionable.Controller.Steps.Shared;
namespace Questionable.Controller.Steps.Movement;

internal static class MoveTo
{
    // Steps without a position in these (Waking Sands, Rising Stones, city halves) still have to be there.
    private static readonly HashSet<uint> PlaceBoundTerritories = [212, 351, 128, 131, 133, 419];

    internal sealed class Factory
    (
        IClientState clientState,
        IObjectTable objectTable,
        AetheryteData aetheryteData,
        IEnumerable<IOperationProvider> operationProviders,
        TerritoryData territoryData,
        ILogger<Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (ProviderTravel.IsHandled(sequence, step, operationProviders))
                return [];

            // A walk onto a zone line zones the player mid-route, which MappingTheRealm counts as a failed move.
            if (step is { InteractionType: EInteractionType.WalkTo, TargetTerritoryId: { } nextZone } &&
                nextZone != step.TerritoryId && Travel.CanTravel(step, clientState.TerritoryType, territoryData))
                return [new Travel.Task(nextZone, null, null)];

            if (step.Position != null)
                return CreateMoveTasks(step, step.Position.Value);
            if (step is
                {
                    InteractionType: EInteractionType.AttuneAetheryte
                            or EInteractionType.RegisterFreeOrFavoredAetheryte,
                    Aetheryte: { } aetheryteLocation
                })
            {
                return CreateMoveTasks(step, aetheryteData.Locations[aetheryteLocation]);
            }

            if (step is { InteractionType: EInteractionType.AttuneAethernetShard, AethernetShard: { } aethernetShard })
                return CreateMoveTasks(step, aetheryteData.Locations[aethernetShard]);

            List<ITask> tasks = [];
            if (clientState.TerritoryType != step.TerritoryId &&
                Travel.CanTravel(step, clientState.TerritoryType, territoryData) &&
                (Travel.InteractTarget(step) != null || PlaceBoundTerritories.Contains(step.TerritoryId)))
                tasks.Add(new Travel.Task(step.TerritoryId, null, Travel.InteractTarget(step)));
            if (step is { DataId: not null, StopDistance: not null })
                tasks.Add(new WaitForNearDataId(step.DataId.Value, step.StopDistance.Value));
            return tasks;
        }

        private IEnumerable<ITask> CreateMoveTasks(QuestStep step, Vector3 destination)
        {
            if (step.InteractionType == EInteractionType.Jump && step.JumpDestination != null &&
                (objectTable[0]!.Position - step.JumpDestination.Position).Length() <=
                (step.JumpDestination.StopDistance ?? 1f))
            {
                logger.LogInformation("We're at the jump destination, skipping movement");
                yield break;
            }

            yield return Travel.CanTravel(step, clientState.TerritoryType, territoryData)
                ? new Travel.Task(step.TerritoryId, destination, Travel.InteractTarget(step), Travel.GoesById(step),
                    Travel.StopRange(step))
                : new MoveTask(step, destination);
        }
    }
}
