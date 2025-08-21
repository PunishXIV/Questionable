using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Movement;

internal static class MoveTo
{
    internal sealed class Factory(
        IClientState clientState,
        AetheryteData aetheryteData,
        TerritoryData territoryData,
        ILogger<Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.Position != null)
            {
                return CreateMoveTasks(step, step.Position.Value);
            }
            else if (step is { DataId: not null, StopDistance: not null })
            {
                return [new WaitForNearDataId(step.DataId.Value, step.StopDistance.Value)];
            }
            else if (step is
                     {
                         InteractionType: EInteractionType.AttuneAetheryte
                             or EInteractionType.RegisterFreeOrFavoredAetheryte,
                         Aetheryte: {} aetheryteLocation
                     })
            {
                return CreateMoveTasks(step, aetheryteData.Locations[aetheryteLocation]);
            }
            else if (step is { InteractionType: EInteractionType.AttuneAethernetShard, AethernetShard: {} aethernetShard })
            {
                return CreateMoveTasks(step, aetheryteData.Locations[aethernetShard]);
            }

            return [];
        }

        private IEnumerable<ITask> CreateMoveTasks(QuestStep step, Vector3 destination)
        {
            if (step.InteractionType == EInteractionType.Jump && step.JumpDestination != null &&
                (clientState.LocalPlayer!.Position - step.JumpDestination.Position).Length() <=
                (step.JumpDestination.StopDistance ?? 1f))
            {
                logger.LogInformation("We're at the jump destination, skipping movement");
                yield break;
            }

            yield return new WaitCondition.Task(() => clientState.TerritoryType == step.TerritoryId,
                $"Wait(territory: {territoryData.GetNameAndId(step.TerritoryId)})");

            if (!step.DisableNavmesh)
                yield return new WaitNavmesh.Task();

            yield return new MoveTask(step, destination);

            if (step is { Fly: true, Land: true })
                yield return new LandTask();
        }
    }
}
