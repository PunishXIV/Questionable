using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Movement;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Gathering;

internal static class MoveToLandingLocation
{
    internal sealed record Task(
        ushort TerritoryId,
        bool FlyBetweenNodes,
        GatheringNode GatheringNode) : ITask
    {
        public override string ToString() => $"Land/{FlyBetweenNodes}";
    }

    internal sealed class MoveToLandingLocationExecutor(
        MoveExecutor moveExecutor,
        GameFunctions gameFunctions,
        IObjectTable objectTable,
        ILogger<MoveToLandingLocationExecutor> logger) : TaskExecutor<Task>, IToastAware
    {
        private ITask _moveTask = null!;

        protected override bool Start()
        {
            var location = Task.GatheringNode.Locations.First();
            if (Task.GatheringNode.Locations.Count > 1)
            {
                var gameObject = objectTable.SingleOrDefault(x =>
                    x.ObjectKind == ObjectKind.GatheringPoint && x.DataId == Task.GatheringNode.DataId &&
                    x.IsTargetable);
                if (gameObject == null)
                    return false;

                location = Task.GatheringNode.Locations.Single(x =>
                    Vector3.Distance(x.Position, gameObject.Position) < 0.1f);
            }

            var (target, degrees, range) = GatheringMath.CalculateLandingLocation(location);
            logger.LogInformation("Preliminary landing location: {Location}, with degrees = {Degrees}, range = {Range}",
                target.ToString("G", CultureInfo.InvariantCulture), degrees, range);

            bool fly = Task.FlyBetweenNodes && gameFunctions.IsFlyingUnlocked(Task.TerritoryId);
            _moveTask = new MoveTask(Task.TerritoryId, target, null, 0.25f,
                DataId: Task.GatheringNode.DataId, Fly: fly, IgnoreDistanceToObject: true,
                InteractionType: EInteractionType.Gather);
            return moveExecutor.Start(_moveTask);
        }

        public override ETaskResult Update() => moveExecutor.Update();
        public bool OnErrorToast(SeString message) => moveExecutor.OnErrorToast(message);
        public override bool ShouldInterruptOnDamage() => moveExecutor.ShouldInterruptOnDamage();
    }
}
