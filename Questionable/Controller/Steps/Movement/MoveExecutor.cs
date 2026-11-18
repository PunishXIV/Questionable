namespace Questionable.Controller.Steps.Movement;

internal sealed class MoveExecutor(MovementController movementController, IObjectTable objectTable)
    : TaskExecutor<MoveTask>, ITaskExecutor.IStoppable
{
    public void StopNow()
    {
        if (movementController.Destination != null)
            movementController.Stop();
    }

    protected override bool Start()
    {
        try
        {
            if (objectTable[0] is { } player &&
                Vector3.Distance(player.Position, Task.Destination) <= (Task.StopDistance ?? QuestStep.DefaultStopDistance))
                return false;

            movementController.NavigateTo(EMovementType.Quest, Task.DataId, Task.Destination, new()
            {
                StopDistance = Task.StopDistance,
                VerticalStopDistance = Task.IgnoreDistanceToObject ? float.MaxValue : null,
            });
            return true;
        }
        catch (MovementController.PathfindingException e)
        {
            movementController.RecordFailure(e);
            throw;
        }
    }

    public override ETaskResult Update() =>
        movementController.IsPathRunning ? ETaskResult.StillRunning : ETaskResult.TaskComplete;

    public override bool ShouldInterruptOnDamage() => Task.InteractionType is EInteractionType.Jump;
}
