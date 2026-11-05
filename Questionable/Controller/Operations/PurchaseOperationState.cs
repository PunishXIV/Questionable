namespace Questionable.Controller.Operations;

[RegisterSingleton]
internal sealed class PurchaseOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Knightshopper", "buying quest items", timeProvider)
{
    public void ObserveStatus(Guid owner, ExternalOperationStatus status, string message) =>
        ObserveOperationStatus(owner, status, message);
}
