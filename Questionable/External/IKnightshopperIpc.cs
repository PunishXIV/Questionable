namespace Questionable.External;

internal interface IKnightshopperIpc
{
    bool IsReady();
    bool? GetBusy();
    KnightshopperIpc.EStartResult BuyWithGil(uint itemId, uint quantity, out Guid operationId, out string message);
    void PollOperation();
    void CancelOwned();
    void ObserveIdle();
}
