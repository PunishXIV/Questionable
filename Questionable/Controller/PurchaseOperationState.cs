using static Questionable.External.KnightshopperIpc;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class PurchaseOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Knightshopper", "buying quest items", timeProvider)
{
    public EPurchaseState Status => (EPurchaseState)CurrentStatus;

    public void ObserveStatus(Guid owner, EPurchaseState status, string message) =>
        ObserveOperationStatus(owner, (ExternalOperationStatus)status, message);
}
