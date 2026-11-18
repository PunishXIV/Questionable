using Questionable.Controller.Steps.Shared;
namespace Questionable.Controller.Steps.Movement;

internal static class MoveTo
{
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

            if (step.Position != null)
                return CreateMoveTasks(step, step.Position.Value);
            if (step is { DataId: not null, StopDistance: not null })
                return [new WaitForNearDataId(step.DataId.Value, step.StopDistance.Value)];
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

            return [];
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
                ? new Travel.Task(step.TerritoryId, destination, Travel.TalkTarget(step))
                : new MoveTask(step, destination);
            if (step is { Fly: true, Land: true })
                yield return new LandTask();
        }
    }
}
