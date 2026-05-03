using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Movement;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using System.Globalization;
using System.Linq;
using System.Numerics;
namespace Questionable.Controller.Steps.Gathering;

internal static class MoveToLandingLocation
{
    internal sealed record Task
    (
        uint TerritoryId,
        bool FlyBetweenNodes,
        GatheringNode GatheringNode) : ITask
    {
        public override string ToString()
        {
            return $"Land/{FlyBetweenNodes}";
        }
    }

    internal sealed class MoveToLandingLocationExecutor
    (
        MoveExecutor moveExecutor,
        GameFunctions gameFunctions,
        IObjectTable objectTable,
        ILogger<MoveToLandingLocationExecutor> logger) : TaskExecutor<Task>, IToastAware
    {
        private ITask _moveTask = null!;

        public override ETaskResult Update()
        {
            return moveExecutor.Update();
        }
        public bool OnErrorToast(SeString message)
        {
            return moveExecutor.OnErrorToast(message);
        }
        public override bool ShouldInterruptOnDamage()
        {
            return moveExecutor.ShouldInterruptOnDamage();
        }

        protected override bool Start()
        {
            GatheringLocation location = Task.GatheringNode.Locations.First();
            if (Task.GatheringNode.Locations.Count > 1)
            {
                IGameObject? gameObject = objectTable.SingleOrDefault(x =>
                    x.ObjectKind == ObjectKind.GatheringPoint && GameFunctions.GetBaseID(x) == Task.GatheringNode.DataId &&
                    x.IsTargetable);
                if (gameObject == null)
                {
                    return false;
                }

                location = Task.GatheringNode.Locations.Single(x =>
                    Vector3.Distance(x.Position, gameObject.Position) < 0.1f);
            }

            (Vector3 target, int degrees, float range) = GatheringMath.CalculateLandingLocation(location);
            logger.LogInformation("Preliminary landing location: {Location}, with degrees = {Degrees}, range = {Range}",
                target.ToString("G", CultureInfo.InvariantCulture), degrees, range);

            bool fly = Task.FlyBetweenNodes && gameFunctions.IsFlyingUnlocked(Task.TerritoryId);
            _moveTask = new MoveTask(Task.TerritoryId, target, null, 0.25f,
                Task.GatheringNode.DataId, Fly: fly, IgnoreDistanceToObject: true,
                InteractionType: EInteractionType.Gather);
            return moveExecutor.Start(_moveTask);
        }
    }
}
