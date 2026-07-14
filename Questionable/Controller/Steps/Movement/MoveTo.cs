using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Movement;

internal static class MoveTo
{
    internal sealed class Factory
    (
        IClientState clientState,
        IObjectTable objectTable,
        AetheryteData aetheryteData,
        ILogger<Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.Position != null)
                return CreateMoveTasks(quest, step, step.Position.Value);
            if (step is { DataId: not null, StopDistance: not null })
                return [new WaitForNearDataId(step.DataId.Value, step.StopDistance.Value)];
            if (step is
                {
                    InteractionType: EInteractionType.AttuneAetheryte
                            or EInteractionType.RegisterFreeOrFavoredAetheryte,
                    Aetheryte: { } aetheryteLocation
                })
            {
                return CreateMoveTasks(quest, step, aetheryteData.Locations[aetheryteLocation]);
            }

            if (step is { InteractionType: EInteractionType.AttuneAethernetShard, AethernetShard: { } aethernetShard })
                return CreateMoveTasks(quest, step, aetheryteData.Locations[aethernetShard]);

            return [];
        }

        private IEnumerable<ITask> CreateMoveTasks(Quest quest, QuestStep step, Vector3 destination)
        {
            if (step.InteractionType == EInteractionType.Jump && step.JumpDestination != null &&
                (objectTable[0]!.Position - step.JumpDestination.Position).Length() <=
                (step.JumpDestination.StopDistance ?? 1f))
            {
                logger.LogInformation("We're at the jump destination, skipping movement");
                yield break;
            }

            if (clientState.TerritoryType != step.TerritoryId)
            {
                yield return new AetheryteShortcut.Task(step, quest.Id, EAetheryteLocation.None, step.TerritoryId);
                if (step.AethernetShortcut is { })
                {
                    var fromTerritory = aetheryteData.TerritoryIds[step.AethernetShortcut.From];
                    var toTerritory = aetheryteData.TerritoryIds[step.AethernetShortcut.To];
                    yield return new WaitCondition.Task(() => clientState.TerritoryType == fromTerritory || clientState.TerritoryType == toTerritory,
                    $"Wait(territori: {TerritoryData.GetNameAndId(fromTerritory)}|{(fromTerritory != toTerritory ? TerritoryData.GetNameAndId(toTerritory) : '*')})");
                    yield return new Shared.AethernetShortcut.Task(step.AethernetShortcut.From, step.AethernetShortcut.To);
                }
            }
            else
                yield return new WaitCondition.Task(() => clientState.TerritoryType == step.TerritoryId,
                    $"Wait(territoro: {TerritoryData.GetNameAndId(step.TerritoryId)})");

            if (!step.DisableNavmesh)
                yield return new WaitNavmesh.Task();

            yield return new MoveTask(step, destination);

            if (step is { Fly: true, Land: true })
                yield return new LandTask();
        }
    }
}
