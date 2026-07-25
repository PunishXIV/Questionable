using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Questionable.Controller.Steps.Movement;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Gathering;

internal static class MoveToLandingLocation
{
    internal sealed record Task
    (
        uint TerritoryId,
        bool FlyBetweenNodes,
        GatheringNode GatheringNode) : ITask
    {
        public override string ToString() => $"Land/{FlyBetweenNodes}";
    }

    internal sealed class MoveToLandingLocationExecutor
    (
        MoveExecutor moveExecutor,
        IObjectTable objectTable,
        ILogger<MoveToLandingLocationExecutor> logger) : TaskExecutor<Task>, IToastAware
    {
        private ITask _moveTask = null!;

        public override ETaskResult Update() => moveExecutor.Update();
        public bool OnErrorToast(SeString message) => moveExecutor.OnErrorToast(message);
        public override bool ShouldInterruptOnDamage() => moveExecutor.ShouldInterruptOnDamage();

        protected override bool Start()
        {
            GatheringLocation location = Task.GatheringNode.Locations[0];
            if (Task.GatheringNode.Locations.Count > 1)
            {
                IGameObject? gameObject = objectTable.SingleOrDefault(x =>
                    x.ObjectKind == ObjectKind.GatheringPoint && GameFunctions.GetBaseID(x) == Task.GatheringNode.DataId &&
                    x.IsTargetable);
                if (gameObject == null)
                    return false;

                location = Task.GatheringNode.Locations.Single(x =>
                    Vector3.Distance(x.Position, gameObject.Position) < 1f);
            }

            (Vector3 target, int degrees, float range) = GatheringMath.CalculateLandingLocation(location);
            logger.LogInformation("Preliminary landing location: {Location}, with degrees = {Degrees}, range = {Range}",
                target.ToString("G", CultureInfo.InvariantCulture), degrees, range);

            bool fly = Task.FlyBetweenNodes && GameFunctions.IsFlyingUnlocked(Task.TerritoryId);
            _moveTask = new MoveTask(Task.TerritoryId, target, Mount: null, 0.25f,
                Task.GatheringNode.DataId, Fly: fly, IgnoreDistanceToObject: true,
                InteractionType: EInteractionType.Gather);
            return moveExecutor.Start(_moveTask);
        }
    }
}
