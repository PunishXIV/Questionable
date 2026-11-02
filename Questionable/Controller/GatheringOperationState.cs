namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class GatheringOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Gather n Chill", "gathering quest items", timeProvider)
{
    public ExternalOperationStatus Status => CurrentStatus;

    public void ObserveStatus(Guid owner, ExternalOperationStatus status, string message) =>
        ObserveOperationStatus(owner, status, message);
}
