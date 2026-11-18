namespace Questionable.Controller.Steps.Movement;

internal sealed record MoveTask
(
    uint TerritoryId,
    Vector3 Destination,
    float? StopDistance = null,
    uint? DataId = null,
    bool IgnoreDistanceToObject = false,
    EInteractionType InteractionType = EInteractionType.None) : ITask
{
    public MoveTask(QuestStep step, Vector3 destination)
        : this(step.TerritoryId,
            destination,
            step.CalculateActualStopDistance(),
            step.DataId,
            step.IgnoreDistanceToObject == true,
            step.InteractionType)
    {
    }

    public bool ShouldRedoOnInterrupt() => true;

    public override string ToString() => $"MoveTo({Destination.ToString("G5", CultureInfo.InvariantCulture)})";
}
