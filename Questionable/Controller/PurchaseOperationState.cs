namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class PurchaseOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Knightshopper", "buying quest items", timeProvider)
{
    public ExternalOperationStatus Status => CurrentStatus;

    public void ObserveStatus(Guid owner, ExternalOperationStatus status, string message) =>
        ObserveOperationStatus(owner, status, message);
}
