namespace Questionable.Controller.Operations;

[RegisterSingleton]
internal sealed class GatheringOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Gather n Chill", "gathering quest items", timeProvider)
{
    public void ObserveStatus(Guid owner, ExternalOperationStatus status, string message) =>
        ObserveOperationStatus(owner, status, message);
}
